// UNNAMED Domain tests - progression's caps, masteries and respec (queue item 7).

using System.Collections.Immutable;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// PROGRESSION.md section 4.3 as a mechanic: every skill stops at the common ceiling, the one discipline a character designates may pass it to
/// the mastery level, reaching a cap earns a mastery, and a respec gives every spent attribute point back for a price that rises with the level
/// that earned them.
/// </summary>
public class ProgressionMasteryTests
{
    private const string Blade = "skill.one_hand_blade";
    private const string Bow = "skill.bow";

    private static DerivedFormula Formula() => new(0, ImmutableSortedDictionary<CharacterAttribute, long>.Empty);

    private static ProgressionRules Rules(int commonCeiling = 60, int masteryLevel = 100) => new()
    {
        Curve = new XpCurve(100, 1.8, 5, 10, 49, 8.0),
        LevelCap = 50,
        SoftCap = 50,
        AttributeBase = 10,
        AttributePointsPerLevel = 1,
        AttributeGrantCapFraction = 0.08,
        Derived = new DerivedFormulas(Formula(), Formula(), Formula(), Formula(), Formula()),
        Skills = new SkillModel(15, commonCeiling, 20, 4, 10, 0.5, 2.0, 25, masteryLevel),
        Guards = new XpGuards(ImmutableArray<LevelBandRow>.Empty, 0.05, 0.9, 0.1, 25, 36000, 0.1, 0.1, 1.0),
        TicksPerWorldDay = 48000,
        StartingPackage = StartingPackage.None,
        TierTargets = ImmutableArray<TierTarget>.Empty,
        DesignationCost = 500,
        RespecBaseCost = 100,
        RespecCostPerLevel = 50,
    };

    /// <summary>Practice one use that is worth XP at the current level.</summary>
    private static SkillResult Practice(CharacterProgression progression, ProgressionRules rules, string skill, int difficulty = 40) =>
        ProgressionEngine.Practice(progression, new SkillPractice(skill, difficulty, PracticeOutcome.Success, 1), rules);

    [Fact]
    public void ASkillStopsAtTheCommonCeiling_AndEarnsItsMasteryThere()
    {
        var rules = Rules();
        var progression = CharacterProgression.Empty with
        {
            Skills = ImmutableSortedDictionary<string, SkillState>.Empty.Add(Blade, new SkillState(59, 250)),
        };

        // one use past the ceiling's edge: the level stops at 60 and the mastery is recorded
        var result = Practice(progression, rules, Blade, difficulty: 90);
        Assert.Equal(60, ProgressionEngine.SkillLevel(result.Progression, Blade));
        Assert.Contains(Blade, result.Progression.Masteries);
        Assert.DoesNotContain(Bow, result.Progression.Masteries);
    }

    [Fact]
    public void TheDesignatedDiscipline_PassesTheCommonCeiling()
    {
        var rules = Rules();
        var progression = CharacterProgression.Empty with
        {
            Skills = ImmutableSortedDictionary<string, SkillState>.Empty.Add(Blade, new SkillState(59, 250)),
            DesignatedSkill = Blade,
        };

        var result = Practice(progression, rules, Blade, difficulty: 90);
        Assert.Equal(60, ProgressionEngine.SkillLevel(result.Progression, Blade));   // it may pass, not does in one use

        var far = progression with { Skills = progression.Skills.SetItem(Blade, new SkillState(80, 330)) };
        var past = Practice(far, rules, Blade, difficulty: 90);
        Assert.True(ProgressionEngine.SkillLevel(past.Progression, Blade) > 80, "a designated discipline keeps climbing past 60");
        Assert.Equal(80, ProgressionEngine.SkillLevel(Practice(progression with
        {
            Skills = progression.Skills.SetItem(Blade, new SkillState(80, 330)),
            DesignatedSkill = Bow,
        }, rules, Blade, difficulty: 90).Progression, Blade));
    }

    [Fact]
    public void DesignatingIsPure_AndTheSkillMustExist()
    {
        var chosen = Designation.Choose(CharacterProgression.Empty, Blade);
        Assert.Equal(Blade, chosen.DesignatedSkill);
        Assert.Equal(CharacterProgression.Empty with { DesignatedSkill = null },
            Designation.Choose(chosen, null));
        Assert.Throws<ArgumentException>(() => Designation.Choose(CharacterProgression.Empty, "not a skill"));
    }

    [Fact]
    public void ARespec_ReturnsEverySpentPoint_AndKeepsGrantsAndLevel()
    {
        var progression = CharacterProgression.Empty with
        {
            Level = 7,
            UnspentAttributePoints = 2,
            Allocation = ImmutableSortedDictionary<CharacterAttribute, int>.Empty
                .Add(CharacterAttribute.Might, 3)
                .Add(CharacterAttribute.Endurance, 2),
            Grants = ImmutableArray.Create(new AttributeGrant(CharacterAttribute.Will, 1, GrantSource.Quest, "quest.test")),
        };

        var refunded = Designation.Refund(progression);
        Assert.Empty(refunded.Allocation);
        Assert.Equal(7, refunded.UnspentAttributePoints);           // 2 unspent + 3 + 2 spent
        Assert.Equal(7, refunded.Level);                            // the level does not move
        Assert.Single(refunded.Grants);                             // a grant was never bought with a point
        Assert.Equal(0, refunded.Allocation.Values.Sum());
    }

    [Fact]
    public void ARespecCostsMoreTheHigherTheLevel()
    {
        var rules = Rules();
        Assert.Equal(100, rules.RespecCost(1));
        Assert.Equal(150, rules.RespecCost(2));
        Assert.Equal(600, rules.RespecCost(11));
        Assert.Equal(100, rules.RespecCost(0));                     // a level that cannot happen pays the base
    }

    [Fact]
    public void TheLevelCap_StopsLevelGainAtTheCanonNumber()
    {
        var rules = Rules();
        var atCap = CharacterProgression.Empty with
        {
            Level = 50,
            LevelProgressXp = 0,
        };

        var award = new XpAward(XpSource.QuestObjective, 1_000_000, 1);
        var result = ProgressionEngine.Award(atCap, award, rules);
        Assert.Equal(50, result.Progression.Level);
        Assert.Equal(50, rules.LevelCap);
    }
}
