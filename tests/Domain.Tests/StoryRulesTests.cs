using System.Collections.Immutable;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Quests;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// The story framework's rules (WP4): the new objective types, a competitive bounty's claim, where an objective points, and the journal's
/// Recent list - all pure functions of the definitions and the records.
/// </summary>
public class StoryRulesTests
{
    private const string Wolf = "creature.beast.wolf_grey";
    private const string Tavar = "npc.ashen_hollow.tavar_orr";

    private sealed class Facts : IQuestFacts
    {
        public Dictionary<(string, string), long> Flags { get; } = new();
        public HashSet<string> Stones { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, CompanionOrder> Party { get; } = new(StringComparer.Ordinal);

        public bool Heard(string dialogueId, string nodeId) => false;
        public bool Discovered(string locationId) => false;
        public long DistanceMm(string locationId) => long.MaxValue;
        public int Carried(string itemId, int qualityMin) => 0;
        public long WorldFlag(string flagId, string locationId) => Flags.GetValueOrDefault((flagId, locationId));
        public int Relationship(string npcId, string dimension) => 0;
        public bool KnowsStone(string stoneKey) => Stones.Contains(stoneKey);
        public CompanionOrder? CompanionPresent(string npcId) => Party.TryGetValue(npcId, out var order) ? order : null;
    }

    private static ObjectiveDefinition O(string id, ObjectiveCondition condition, params string[] next) => new(id, id, condition, next.ToImmutableArray());

    private static QuestDefinition Quest(params ObjectiveDefinition[] objectives) =>
        new("quest.test.story", "Story", "A story.", null, objectives[0].Id, objectives.ToImmutableArray(), ImmutableArray<ObjectiveCondition>.Empty,
            ImmutableArray<QuestReward>.Empty);

    private static bool Holds(ObjectiveCondition condition, Facts facts, int progress = 0) => QuestRules.Evaluate(condition, facts, progress, 0, 10).Holds;

    [Fact]
    public void ADefeat_IsReadFromItsFlag_AsTheDeathWroteIt()
    {
        var defeat = new DefeatCreature(Wolf, "world.test.wolf", "location.outpost", Boss: true);
        var facts = new Facts();
        Assert.Equal("defeat_boss", defeat.Type);
        Assert.Equal("defeat_creature", (defeat with { Boss = false }).Type);
        Assert.False(defeat.CountsDeeds);
        Assert.False(Holds(defeat, facts));
        facts.Flags[("world.test.wolf", "location.outpost")] = 1;
        Assert.True(Holds(defeat, facts));
    }

    [Fact]
    public void ACompanion_IsPresent_OnTheirFeet_UnderTheOrderNamed()
    {
        var facts = new Facts();
        var any = new CompanionPresent(Tavar, null);
        var following = new CompanionPresent(Tavar, CompanionOrder.Follow);
        Assert.False(Holds(any, facts));
        facts.Party[Tavar] = CompanionOrder.Wait;
        Assert.True(Holds(any, facts));
        Assert.False(Holds(following, facts));
        Assert.Equal(("wait", "follow"), (QuestRules.Evaluate(following, facts, 0, 0, 0).Terms[0].Value, QuestRules.Evaluate(following, facts, 0, 0, 0).Terms[0].Wanted));
        facts.Party[Tavar] = CompanionOrder.Follow;
        Assert.True(Holds(following, facts));
    }

    [Fact]
    public void AReplyAndAnArrival_AreDeeds_CountedOnlyForTheObjectiveWaitingForExactlyThem()
    {
        var reply = new ChoiceGiven("npc.a", "dialogue.a", "node", "yes");
        var travel = new TravelTo("waystone.rim");
        var quest = Quest(O("o_reply", reply), O("o_travel", travel));
        var state = QuestRules.Start(quest, 0) with
        {
            Objectives = ImmutableArray.Create(new ObjectiveState("o_reply", ObjectiveStatus.Active, 0, null, 0),
                new ObjectiveState("o_travel", ObjectiveStatus.Active, 0, null, 0)),
        };
        Deed Chose(string subject, string npc) => new(DeedKind.Chose, subject, 1, Quality.Standard, npc, 1);

        state = QuestRules.Record(quest, state, Chose("dialogue.a/node/no", "npc.a"));
        state = QuestRules.Record(quest, state, Chose("dialogue.a/node/yes", "npc.b"));
        state = QuestRules.Record(quest, state, new Deed(DeedKind.Travelled, "waystone.ashen", 1, Quality.Standard, null, 1));
        Assert.All(state.Objectives, o => Assert.Equal(0, o.Progress));

        state = QuestRules.Record(quest, state, Chose(ChoiceGiven.ChoiceSubject("dialogue.a", "node", "yes"), "npc.a"));
        state = QuestRules.Record(quest, state, new Deed(DeedKind.Travelled, "waystone.rim", 1, Quality.Standard, null, 1));
        Assert.All(state.Objectives, o => Assert.Equal(1, o.Progress));
        Assert.True(Holds(reply, new Facts(), 1) && Holds(travel, new Facts(), 1));
        Assert.True(reply.CountsDeeds && travel.CountsDeeds);
    }

    [Fact]
    public void AStone_IsKnown_OnceTouched()
    {
        var facts = new Facts();
        var known = new WaystoneKnown("waystone.ashen");
        Assert.False(Holds(known, facts));
        facts.Stones.Add("waystone.ashen");
        Assert.True(Holds(known, facts));
    }

    [Fact]
    public void AnObjective_PointsAtItsPlacePersonOrStone_AndAPredicateOverTimeOrThingsPointsNowhere()
    {
        Assert.Equal(new QuestTarget(TargetKind.Location, "location.x"), QuestRules.Target(new ExploreLocation("location.x", 10)));
        Assert.Equal(new QuestTarget(TargetKind.Location, "location.y"), QuestRules.Target(new DefeatCreature(Wolf, "world.f", "location.y", false)));
        Assert.Equal(new QuestTarget(TargetKind.Npc, "npc.a"), QuestRules.Target(new ChoiceGiven("npc.a", "dialogue.a", "n", "c")));
        Assert.Equal(new QuestTarget(TargetKind.Stone, "waystone.rim"), QuestRules.Target(new TravelTo("waystone.rim")));
        Assert.Null(QuestRules.Target(new WaitUntil(10)));
        Assert.Null(QuestRules.Target(new CraftItem("item.x", 1, Quality.Crude)));
    }

    // ── competitive bounties ────────────────────────────────────────────────

    private static readonly QuestDefinition Bounty = Quest(O("o_cull", new KillCreature(Wolf, 3)), O("o_more", new WaitUntil(0))) with
    {
        Track = QuestTrack.Bounty, Competitive = true, RepeatAfterTicks = 100,
    };

    [Fact]
    public void AClaimOnAHeldBounty_FailsTheCharactersHold_ClosingWhatWasActive()
    {
        var held = QuestRules.Start(Bounty, 10);
        Assert.True(QuestRules.CanClaim(Bounty, held, 20));
        var lost = QuestRules.Claim(Bounty, held, "npc_01", 20);

        Assert.Equal((QuestStatus.Failed, (long?)20, "lost_to:npc_01"), (lost.Status, lost.EndedTick, lost.EndedBy));
        Assert.Equal(ObjectiveStatus.Closed, lost.Objective("o_cull")!.Status);
        Assert.Equal("npc_01", QuestRules.Claimant(lost));
        Assert.True(QuestRules.Held(lost));
        Assert.False(QuestRules.CanClaim(Bounty, lost, 119));
        Assert.False(QuestRules.CanStart(Bounty, lost, 119));
        Assert.True(QuestRules.CanStart(Bounty, lost, 120));
    }

    [Fact]
    public void AClaimOnAnOpenBounty_RecordsThePosting_AsNotTheCharacters()
    {
        var taken = QuestRules.Claim(Bounty, null, "npc_01", 5);
        Assert.Equal((QuestStatus.Failed, 5L, (long?)5, "claimed_by:npc_01"), (taken.Status, taken.StartedTick, taken.EndedTick, taken.EndedBy));
        Assert.Equal(ObjectiveStatus.Closed, Assert.Single(taken.Objectives).Status);
        Assert.False(QuestRules.Held(taken));
        Assert.Equal("npc_01", QuestRules.Claimant(taken));
        Assert.Empty(QuestJournal.Entries(Bounty, taken, id => id));

        // Not competitive, or claimed already: refused.
        Assert.False(QuestRules.CanClaim(Bounty with { Competitive = false }, null, 5));
        Assert.Throws<InvalidOperationException>(() => QuestRules.Claim(Bounty, taken, "npc_02", 6));
        // A bounty the character finished is theirs until it is posted again; then it may be claimed afresh.
        var done = QuestRules.Start(Bounty, 0) with { Status = QuestStatus.Completed, EndedTick = 50, EndedBy = "o_more" };
        Assert.Null(QuestRules.Claimant(done));
        Assert.False(QuestRules.CanClaim(Bounty, done, 149));
        Assert.True(QuestRules.CanClaim(Bounty, done, 150));
    }

    // ── the journal ─────────────────────────────────────────────────────────

    [Fact]
    public void TheRecentList_IsTheRecordsOwnLines_NewestFirst_WithTheChoiceAfterTheObjectiveThatMadeIt()
    {
        var quest = Quest(
            O("o_fork", new WaitUntil(0), "o_left", "o_right") with { FirstBranch = true },
            O("o_left", new WaitUntil(0)) with { Effects = ImmutableArray.Create<QuestEffect>(new ChoiceEffect("world.main.way", "location.x", 1, "You went left.")) },
            O("o_right", new WaitUntil(0)));
        var state = new QuestState(quest.Id, QuestStatus.Completed, 2, 9, "o_left", ImmutableArray.Create(
            new ObjectiveState("o_fork", ObjectiveStatus.Satisfied, 2, 5, 0), new ObjectiveState("o_left", ObjectiveStatus.Satisfied, 5, 9, 0),
            new ObjectiveState("o_right", ObjectiveStatus.Closed, 5, 9, 0)));

        var entries = QuestJournal.Entries(quest, state, id => id).ToList();
        Assert.Equal(new[] { "Took up Story.", "o_fork", "o_left", "You went left.", "Finished Story." }, entries.Select(e => e.Text));
        Assert.All(entries, e => Assert.Equal(quest.Id, e.QuestId));

        var away = new JournalEntry(7, JournalEntryKind.Away, "Two days passed.");
        var recent = QuestJournal.Recent(entries.Append(away), length: 4);
        Assert.Equal(new[] { "Finished Story.", "You went left.", "o_left", "Two days passed." }, recent.Select(e => e.Text));
    }

    [Fact]
    public void ALostBounty_SaysWhoFinishedItFirst_AndATimedOutObjectiveSaysSo()
    {
        var lost = QuestRules.Claim(Bounty, QuestRules.Start(Bounty, 0), "npc_01", 4);
        Assert.Equal("Aldric finished Story first.", QuestJournal.Entries(Bounty, lost, _ => "Aldric").Last().Text);

        var timed = Quest(O("o_hurry", new WaitUntil(100)) with { TimeLimitTicks = 5, OnFail = QuestRules.FailQuest });
        var failed = new QuestState(timed.Id, QuestStatus.Failed, 0, 5, "o_hurry",
            ImmutableArray.Create(new ObjectiveState("o_hurry", ObjectiveStatus.Failed, 0, 5, 0)));
        Assert.Equal(new[] { "Took up Story.", "Too late: o_hurry", "Failed Story." }, QuestJournal.Entries(timed, failed, id => id).Select(e => e.Text));
    }

    [Fact]
    public void Tracks_HaveStableKeys()
    {
        foreach (var track in Enum.GetValues<QuestTrack>())
            Assert.Equal(track, QuestTracks.Parse(QuestTracks.Key(track)));
        Assert.Null(QuestTracks.Parse("story"));
        Assert.DoesNotContain("defeat_boss", ObjectiveTypes.NotBuilt.Keys);
        Assert.Contains("travel_to", ObjectiveTypes.Built);
    }
}
