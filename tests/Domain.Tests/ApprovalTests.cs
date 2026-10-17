using System.Collections.Immutable;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using static UNNAMED.Domain.Tests.Progression.TestRules;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// WP15b's pure rules: what a party member does when asked, read dimension by dimension (no single approval score); how they regard the
/// character, in words; what a value of theirs is touched by; a member's health on the character's rules; and banter's recent-deed and
/// place conditions.
/// </summary>
public class ApprovalTests
{
    private static readonly ApprovalRules Rules = new(1, 1, -5,
        ImmutableArray.Create(new DimensionBound("loyalty", -20, null), new DimensionBound("grudge", null, 40)),
        ImmutableArray.Create(new DimensionBound("loyalty", -50, null)),
        ImmutableArray.Create(new DimensionBound("loyalty", -80, null), new DimensionBound("grudge", null, 90)));

    private static Disposition Of(params (string Dimension, int Value)[] values) =>
        Approval.Of(d => values.FirstOrDefault(v => v.Dimension == d).Value, Rules);

    [Fact]
    public void AMember_Complies_Hesitates_Refuses_OrLeaves_AsAnyOneBoundIsPassed()
    {
        Assert.Equal(Disposition.Comply, Of());
        Assert.Equal(Disposition.Comply, Of(("loyalty", -20), ("grudge", 40)));   // at a bound is not past it
        Assert.Equal(Disposition.Hesitate, Of(("loyalty", -21)));
        Assert.Equal(Disposition.Hesitate, Of(("grudge", 41), ("loyalty", 100)));   // loyal, and still a grudge is enough
        Assert.Equal(Disposition.Refuse, Of(("loyalty", -51)));
        Assert.Equal(Disposition.Leave, Of(("loyalty", -81)));
        Assert.Equal(Disposition.Leave, Of(("grudge", 91), ("trust", 100), ("affection", 100)));   // leaving is read first
        Assert.Equal(Disposition.Comply, Approval.Of(_ => -100, ApprovalRules.None));
    }

    [Fact]
    public void ApprovalRules_ThatMakeNoSense_AreNamed()
    {
        Assert.Null(Rules.Problem());
        Assert.NotNull((Rules with { LoyaltyWageUnpaid = 1 }).Problem());
        Assert.NotNull((Rules with { LoyaltyPerDay = -1 }).Problem());
        Assert.NotNull((Rules with { Leave = ImmutableArray.Create(new DimensionBound("grudge", 10, 90)) }).Problem());
    }

    /// <summary>§2.15: no approval bar - a short sentence of what they feel strongly, the warm side first and the cold after a "but".</summary>
    [Fact]
    public void TheRegardSentence_SaysWhatTheyFeelStrongly_WarmThenCold()
    {
        string Regard(params (string Dimension, int Value)[] values) => Approval.Regard("Tavar", d => values.FirstOrDefault(v => v.Dimension == d).Value);

        Assert.Equal("Tavar has no strong view of you yet.", Regard(("trust", 39), ("grudge", 10)));
        Assert.Equal("Tavar trusts you.", Regard(("trust", 40)));
        Assert.Equal("Tavar trusts you, is loyal to you and respects you.", Regard(("trust", 50), ("loyalty", 60), ("respect", 45)));
        Assert.Equal("Tavar respects you, but does not trust you and holds a grudge against you.", Regard(("trust", -40), ("respect", 50), ("grudge", 70)));
        Assert.Equal("Tavar is afraid of you.", Regard(("fear", 80)));
    }

    [Fact]
    public void AValue_IsTouchedByItsDeed_OnItsSubjects_AndToItsNpc()
    {
        var value = new ValueReaction(DeedKind.Delivered, ImmutableSortedSet.Create(StringComparer.Ordinal, "item.test.herb"), "npc.test.sel",
            ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[] { KeyValuePair.Create("trust", 2) }), null);

        Assert.True(value.Matches(DeedKind.Delivered, "item.test.herb", "npc.test.sel"));
        Assert.False(value.Matches(DeedKind.Delivered, "item.test.herb", "npc.test.kera"));
        Assert.False(value.Matches(DeedKind.Crafted, "item.test.herb", null));
        Assert.False(value.Matches(DeedKind.Delivered, "item.test.ore", "npc.test.sel"));
        Assert.True((value with { NpcId = null }).Matches(DeedKind.Delivered, "item.test.herb", "npc.test.kera"));
    }

    /// <summary>A member stands as their content says until they spend points; what endurance adds on the character's rules, it adds to them.</summary>
    [Fact]
    public void AMembersHealth_IsTheirOwn_PlusWhatTheirAttributesAdd()
    {
        var rules = Default();
        Assert.Equal(100, Approval.MaxHealth(100, CharacterProgression.Empty, rules));
        var trained = ProgressionEngine.Allocate(CharacterProgression.Empty with { UnspentAttributePoints = 2 },
            new AttributeAllocation(CharacterAttribute.Endurance, 2));
        long added = ProgressionEngine.Derive(trained, rules).HealthMax - ProgressionEngine.Derive(CharacterProgression.Empty, rules).HealthMax;
        Assert.True(added > 0);
        Assert.Equal(100 + added, Approval.MaxHealth(100, trained, rules));
    }

    private sealed class Road : IDialogueFacts
    {
        public long? Last { get; set; }
        public int Count { get; set; }
        public long Now { get; set; }
        public HashSet<string> Places { get; } = new(StringComparer.Ordinal);

        public bool Visited(string dialogueId, string nodeId) => false;
        public long WorldFlag(string flagId, string? locationId) => 0;
        public int Carried(string itemId, int qualityMin) => 0;
        public int Relationship(string npcId, string dimension) => 0;
        public int SkillLevel(string skillId) => 0;
        public int Level => 1;
        public string QuestState(string questId, string? objectiveId) => "not_started";
        public CompanionOrder? CompanionOrderOf(string npcId) => null;
        public int Deeds(DeedKind kind, string subjectId, string? npcId) => Count;
        public long? DeedLastTick(DeedKind kind, string subjectId, string? npcId) => Last;
        public bool Near(string locationId, long? radiusMm) => Places.Contains(locationId);
    }

    /// <summary>Banter's conditions (WP15b): a deed done within so long, and the character near a place.</summary>
    [Fact]
    public void ARecentDeed_HoldsOnlyWithinItsWindow_AndNear_WhereTheCharacterIs()
    {
        var road = new Road();
        var recent = new DeedCondition(DeedKind.Killed, "creature.test.husk", 1, false) { WithinTicks = 1_200 };
        bool Holds(DialogueCondition c) => DialogueRules.Holds(c, "dialogue.test.road", road);

        Assert.False(Holds(recent));   // never done
        (road.Count, road.Last, road.Now) = (1, 1_000, 2_200);
        Assert.True(Holds(recent));
        road.Now = 2_201;
        Assert.False(Holds(recent));
        Assert.True(Holds(recent with { Negated = true }));
        Assert.True(Holds(recent with { WithinTicks = null }));   // whenever

        var near = new NearCondition("location.test.scar", null);
        Assert.False(Holds(near));
        road.Places.Add("location.test.scar");
        Assert.True(Holds(near));
    }
}
