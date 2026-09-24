using UNNAMED.Domain.Magic;

namespace UNNAMED.Domain.Tests;

/// <summary>The rules of casting (M3e): skill makes a working steadier and cheaper; past tolerance costs health, never permission.</summary>
public class MagicRulesTests
{
    private static readonly MagicConstants Rules = new();

    private static FormulaDefinition Formula(int complexity, int strain) =>
        new("spell.force.test", "skill.force", complexity, 10, strain, 12, 6, Targeting.Projectile);

    [Fact]
    public void AboveOnesSkill_AWorkingCostsMoreStrain_AndBelowItLess_DownToTheFloor()
    {
        var formula = Formula(complexity: 10, strain: 20);
        Assert.Equal(26, MagicRules.StrainCost(formula, skill: 0, Rules));    // 10 points above skill: +30%
        Assert.Equal(20, MagicRules.StrainCost(formula, skill: 10, Rules));   // at skill: as authored
        Assert.Equal(14, MagicRules.StrainCost(formula, skill: 20, Rules));   // 10 points below: -30%
        Assert.Equal(10, MagicRules.StrainCost(formula, skill: 60, Rules));   // however skilled, half
    }

    [Fact]
    public void AWorkingFizzlesOnlyAboveOnesSkill()
    {
        var formula = Formula(complexity: 5, strain: 8);
        Assert.Equal(15, MagicRules.FizzlePercent(formula, skill: 0, Rules));
        Assert.Equal(3, MagicRules.FizzlePercent(formula, skill: 4, Rules));
        Assert.Equal(0, MagicRules.FizzlePercent(formula, skill: 5, Rules));
        Assert.Equal(0, MagicRules.FizzlePercent(formula, skill: 40, Rules));
        Assert.Equal(100, MagicRules.FizzlePercent(Formula(complexity: 90, strain: 8), skill: 0, Rules));
    }

    [Fact]
    public void PastTolerance_TheExcessIsPaidInHealth_AndStrainStaysAtTheLimit()
    {
        Assert.Equal((30, 0), MagicRules.Settle(strain: 20, cost: 10, tolerance: 40, Rules));
        Assert.Equal((40, 0), MagicRules.Settle(strain: 30, cost: 10, tolerance: 40, Rules));
        Assert.Equal((40, 12), MagicRules.Settle(strain: 36, cost: 10, tolerance: 40, Rules));   // 6 points past: 2 health each
        Assert.Equal((40, 20), MagicRules.Settle(strain: 40, cost: 10, tolerance: 40, Rules));
    }

    [Fact]
    public void StrainedFromThreeQuartersOfTolerance()
    {
        Assert.False(MagicRules.Strained(29, 40, Rules));
        Assert.True(MagicRules.Strained(30, 40, Rules));
        Assert.False(MagicRules.Strained(0, 0, Rules));
    }

    [Fact]
    public void Resonance_StrengthensAWorking_AroundItsReference()
    {
        Assert.Equal(1.0, MagicRules.ResonanceMultiplier(20, Rules), 6);
        Assert.Equal(1.2, MagicRules.ResonanceMultiplier(30, Rules), 6);
        Assert.Equal(0.8, MagicRules.ResonanceMultiplier(10, Rules), 6);
        Assert.Equal(0.1, MagicRules.ResonanceMultiplier(-1_000, Rules), 6);
    }
}
