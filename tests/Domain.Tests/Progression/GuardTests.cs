using UNNAMED.Domain.Progression;
using static UNNAMED.Domain.Tests.Progression.TestRules;

namespace UNNAMED.Domain.Tests.Progression;

/// <summary>The anti-farm guards AG-1..AG-4 (PROGRESSION.md §3.4).</summary>
public class GuardTests
{
    private static readonly ProgressionRules Rules = Default(levelCap: 50);

    // A level-10 character at a cap of 10 cannot level up mid-test, so AG-1 stays at an even fight.
    private static readonly ProgressionRules Capped = Default(levelCap: 10);

    private static CharacterProgression AtLevel(int level) => CharacterProgression.Empty with { Level = level };

    [Theory]
    [InlineData(0, 1_000)]     // an even fight
    [InlineData(-3, 600)]
    [InlineData(-8, 250)]
    [InlineData(-13, 50)]
    [InlineData(3, 1_150)]
    [InlineData(8, 1_300)]
    [InlineData(12, 1_150)]    // capped, to discourage suicidal farming
    public void AG1_ScalesKillXpByTheLevelDifference(int difference, long expected)
    {
        var result = ProgressionEngine.Award(AtLevel(20), Kill(Wolf, 20 + difference, "cluster.a", 0), Rules);

        Assert.Equal(expected, result.Awarded);
    }

    [Fact]
    public void AG1_NeverZeroesASpeciesTheCharacterHasNeverKilled_ButDoesZeroItAfterward()
    {
        var first = ProgressionEngine.Award(AtLevel(30), Kill("creature.beast.rat", 5, "cluster.a", 0), Rules);
        var second = ProgressionEngine.Award(first.Progression, Kill("creature.beast.rat", 5, "cluster.b", 60_000), Rules);

        Assert.Equal(50, first.Awarded);    // the new-species floor, not zero
        Assert.Equal(0, second.Awarded);
    }

    [Fact]
    public void AG2_DecaysRepeatKillsOfASpecies_WithinOneWorldDay_ToItsFloor_AndResetsTheNextDay()
    {
        var p = AtLevel(10);
        var awarded = new List<long>();
        for (int k = 0; k < 30; k++)
        {
            // A different cluster each time, so only AG-2 acts.
            var result = ProgressionEngine.Award(p, Kill(Wolf, 10, $"cluster.{k}", k * 10), Capped);
            awarded.Add(result.Awarded);
            p = result.Progression;
        }

        Assert.Equal(new long[] { 1_000, 900, 810, 729 }, awarded.Take(4));
        Assert.Equal(100, awarded[^1]);     // 0.9^29 is far below the 0.10 floor
        var nextDay = ProgressionEngine.Award(p, Kill(Wolf, 10, "cluster.next", Capped.TicksPerWorldDay), Capped);
        Assert.Equal(1_000, nextDay.Awarded);
    }

    [Fact]
    public void AG2_KeysOnTheSpecies()
    {
        var p = ProgressionEngine.Award(AtLevel(10), Kill(Wolf, 10, "cluster.a", 0), Capped).Progression;

        Assert.Equal(1_000, ProgressionEngine.Award(p, Kill("creature.beast.deer", 10, "cluster.b", 10), Capped).Awarded);
    }

    [Fact]
    public void AG3_SaturatesOneCluster_AfterItsThreshold_UntilTheWindowEmpties()
    {
        var p = AtLevel(10);
        var awarded = new List<long>();
        for (int k = 0; k < 26; k++)
        {
            // A different species each time, so only AG-3 acts.
            var result = ProgressionEngine.Award(p, Kill($"creature.test.species_{k}", 10, "cluster.den", k * 100), Capped);
            awarded.Add(result.Awarded);
            p = result.Progression;
        }

        Assert.All(awarded.Take(25), xp => Assert.Equal(1_000, xp));
        Assert.Equal(100, awarded[25]);
        long later = 25 * 100 + Capped.Guards.ClusterWindowTicks;   // every earlier kill has left the window
        Assert.Equal(1_000, ProgressionEngine.Award(p, Kill("creature.test.fresh", 10, "cluster.den", later), Capped).Awarded);
    }

    [Fact]
    public void AG2AndAG3_ShareOneFloor_AndNeverCompoundBelowIt()
    {
        var p = AtLevel(10);
        long last = 0;
        for (int k = 0; k < 60; k++)
        {
            var result = ProgressionEngine.Award(p, Kill(Wolf, 10, "cluster.den", k * 10), Capped);
            last = result.Awarded;
            p = result.Progression;
        }

        Assert.Equal(100, last);   // 0.10, not 0.10 x 0.10
    }

    [Fact]
    public void AG4_TheGuardsReadNothingButLevelXp_SoPracticeIsTheSameAfterAHeavyFarm()
    {
        var fresh = AtLevel(10);
        var farmed = fresh;
        for (int k = 0; k < 60; k++)
            farmed = ProgressionEngine.Award(farmed, Kill(Wolf, 10, "cluster.den", k * 10), Capped).Progression;

        var practice = new SkillPractice(Blade, 20, PracticeOutcome.Success, 1_000);
        var afterFresh = ProgressionEngine.Practice(fresh, practice, Capped);
        var afterFarm = ProgressionEngine.Practice(farmed, practice, Capped);

        Assert.Equal(afterFresh.XpGained, afterFarm.XpGained);
        Assert.Equal(afterFresh.Progression.Skills[Blade], afterFarm.Progression.Skills[Blade]);
    }
}
