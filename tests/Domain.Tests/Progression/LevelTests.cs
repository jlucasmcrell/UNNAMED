using UNNAMED.Domain.Progression;
using static UNNAMED.Domain.Tests.Progression.TestRules;

namespace UNNAMED.Domain.Tests.Progression;

/// <summary>AX-LVL (PROGRESSION.md §3): the curve, level-ups, the cap, first-time production, and the AG-8 debt.</summary>
public class LevelTests
{
    private static readonly ProgressionRules Rules = Default();

    [Fact]
    public void TheCurve_SumsToThePublishedTotal()
    {
        // PROGRESSION.md §3.2: the total is derived from the formula, and this test holds the two together.
        Assert.Equal(2_448_025, Rules.Curve.TotalTo(50));
        Assert.Equal(100, Rules.Curve.ToReach(2));
        Assert.Equal(350, Rules.Curve.ToReach(3));      // 100 * 2^1.85 = 360.5, to the nearest 25
        Assert.Equal(2_525, Rules.Curve.TotalTo(5));    // the Phase-1 cap
        Assert.Equal(21_400, Rules.Curve.TotalTo(10));  // the Novice tier
    }

    [Fact]
    public void ALevelUp_GrantsExactlyTheConfiguredAttributePoints_AndNothingElse()
    {
        // ROADMAP M2c exit (e): skill points, technique points and designations are not level rewards.
        var result = ProgressionEngine.Award(CharacterProgression.Empty, new XpAward(XpSource.Discovery, 450, 10), Rules);

        var p = result.Progression;
        Assert.Equal((3, 0L, 2), (p.Level, p.LevelProgressXp, p.UnspentAttributePoints));
        Assert.Equal(2, result.LevelsGained);
        Assert.Empty(p.Skills);
        Assert.Empty(p.Known);
        Assert.Empty(p.Allocation);
        Assert.Empty(p.Grants);
        Assert.Equal<Advancement>(
            new[]
            {
                new Advancement(Axis.Level, "xp", 450, "discovery", 10),
                new Advancement(Axis.Attributes, "unspent_points", 2, "level_up", 10),
            },
            result.Advancements);
    }

    [Fact]
    public void TheLevelCap_StopsLevelling_AndXpPastItIsKeptOnlyInTheLifetimeTotals()
    {
        var p = ProgressionEngine.Award(CharacterProgression.Empty, new XpAward(XpSource.QuestObjective, 10_000, 0), Rules).Progression;

        Assert.Equal((5, 0L, 4), (p.Level, p.LevelProgressXp, p.UnspentAttributePoints));
        Assert.Equal(10_000, p.LifetimeXp[XpSource.QuestObjective]);
    }

    [Fact]
    public void LifetimeXp_IsKeptPerSourceKind()
    {
        var p = CharacterProgression.Empty;
        p = ProgressionEngine.Award(p, new XpAward(XpSource.Discovery, 30, 0), Rules).Progression;
        p = ProgressionEngine.Award(p, new XpAward(XpSource.Social, 20, 0), Rules).Progression;
        p = ProgressionEngine.Award(p, new XpAward(XpSource.Discovery, 5, 0), Rules).Progression;

        Assert.Equal(35, p.LifetimeXp[XpSource.Discovery]);
        Assert.Equal(20, p.LifetimeXp[XpSource.Social]);
        Assert.Equal(55, p.LevelProgressXp);
    }

    [Fact]
    public void Production_EarnsXpTheFirstTimeOnly()
    {
        // AG-7: no conversion of materials into XP beyond the first-time production credit.
        var award = new XpAward(XpSource.Production, 50, 0) { FirstKey = "item.potion.salve_minor" };
        var first = ProgressionEngine.Award(CharacterProgression.Empty, award, Rules);
        var second = ProgressionEngine.Award(first.Progression, award with { Tick = 100 }, Rules);

        Assert.Equal(50, first.Awarded);
        Assert.Equal(0, second.Awarded);
        Assert.Empty(second.Advancements);
        Assert.Contains("item.potion.salve_minor", second.Progression.ProductionFirsts);
    }

    [Fact]
    public void AnAwardMissingWhatItsGuardsNeed_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => ProgressionEngine.Award(CharacterProgression.Empty, new XpAward(XpSource.Combat, 10, 0), Rules));
        Assert.Throws<ArgumentException>(() => ProgressionEngine.Award(CharacterProgression.Empty, new XpAward(XpSource.Production, 10, 0), Rules));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProgressionEngine.Award(CharacterProgression.Empty, new XpAward(XpSource.Social, -1, 0), Rules));
    }

    [Fact]
    public void Death_OwesAFractionOfTheLevelSpan_ExactlyOncePerDeath()
    {
        // PROTOTYPE.md C17: the penalty is applied exactly once, and it is debt, not lost XP.
        var start = ProgressionEngine.Award(CharacterProgression.Empty, new XpAward(XpSource.Discovery, 550, 0), Rules).Progression;
        Assert.Equal((3, 100L), (start.Level, start.LevelProgressXp));

        var died = ProgressionEngine.Die(start, Rules);

        Assert.Equal(78, died.DebtAdded);       // 10% of the level-3 -> 4 span, 775
        Assert.Equal(78, died.Progression.XpDebt);
        Assert.Equal((3, 100L), (died.Progression.Level, died.Progression.LevelProgressXp));   // a level is never lost
    }

    [Fact]
    public void Debt_IsRepaidBeforeXpCountsAsProgress()
    {
        var died = ProgressionEngine.Die(CharacterProgression.Empty, Rules).Progression;   // 10% of 100
        Assert.Equal(10, died.XpDebt);

        var partly = ProgressionEngine.Award(died, new XpAward(XpSource.Discovery, 4, 0), Rules);
        Assert.Equal((4L, 6L, 0L), (partly.Repaid, partly.Progression.XpDebt, partly.Progression.LevelProgressXp));

        var cleared = ProgressionEngine.Award(partly.Progression, new XpAward(XpSource.Discovery, 20, 0), Rules);
        Assert.Equal((6L, 0L, 14L), (cleared.Repaid, cleared.Progression.XpDebt, cleared.Progression.LevelProgressXp));
    }

    [Fact]
    public void Debt_NeverExceedsTheConfiguredSpans()
    {
        var p = CharacterProgression.Empty;
        for (int death = 0; death < 15; death++)
            p = ProgressionEngine.Die(p, Rules).Progression;

        Assert.Equal(100, p.XpDebt);   // one full level-1 span
        Assert.Equal(0, ProgressionEngine.Die(p, Rules).DebtAdded);
    }
}
