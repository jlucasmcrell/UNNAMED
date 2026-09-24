using UNNAMED.Domain.Crafting;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// The rules of making (M3f): the weakest material caps the work and the smith's hand moves it a step, up more often the
/// further skill is past the recipe's complexity and down more often the further the recipe is past the skill. No rank gates a recipe.
/// </summary>
public class CraftingRulesTests
{
    private static readonly CraftingConstants Rules = new();

    [Fact]
    public void SkillPastTheComplexity_MakesFineMoreOften_AndShortOfItCrude()
    {
        Assert.Equal((10, 0), (CraftingRules.FinePercent(10, 10, Rules), CraftingRules.CrudePercent(10, 10, Rules)));   // at the complexity
        Assert.Equal((30, 0), (CraftingRules.FinePercent(20, 10, Rules), CraftingRules.CrudePercent(20, 10, Rules)));   // 10 points past: +2% a point
        Assert.Equal((60, 0), (CraftingRules.FinePercent(60, 10, Rules), CraftingRules.CrudePercent(60, 10, Rules)));   // capped
        Assert.Equal((0, 30), (CraftingRules.FinePercent(0, 10, Rules), CraftingRules.CrudePercent(0, 10, Rules)));     // 10 short: +3% crude a point, never fine
        Assert.Equal((0, 50), (CraftingRules.FinePercent(0, 40, Rules), CraftingRules.CrudePercent(0, 40, Rules)));     // capped: a novice still makes something
    }

    [Fact]
    public void TheRoll_MovesTheWeakestMaterial_OneStepAtMost()
    {
        // A novice at a complexity-10 recipe: the lowest 30% of rolls come out a step down, the rest as the materials were.
        Assert.Equal(Quality.Crude, CraftingRules.Roll(Quality.Standard, 0, 10, Rules, 0.25));
        Assert.Equal(Quality.Standard, CraftingRules.Roll(Quality.Standard, 0, 10, Rules, 0.35));
        Assert.Equal(Quality.Standard, CraftingRules.Roll(Quality.Standard, 0, 10, Rules, 0.99));

        // Ten points past it: the top 30% a step up.
        Assert.Equal(Quality.Fine, CraftingRules.Roll(Quality.Standard, 20, 10, Rules, 0.75));
        Assert.Equal(Quality.Standard, CraftingRules.Roll(Quality.Standard, 20, 10, Rules, 0.65));

        // Crude material holds a master to standard; nothing is made finer than fine or worse than crude.
        Assert.Equal(Quality.Standard, CraftingRules.Roll(Quality.Crude, 60, 0, Rules, 0.99));
        Assert.Equal(Quality.Fine, CraftingRules.Roll(Quality.Fine, 60, 0, Rules, 0.99));
        Assert.Equal(Quality.Crude, CraftingRules.Roll(Quality.Crude, 0, 40, Rules, 0.01));
    }

    [Fact]
    public void AFiniteNode_HoldsItsCharges_AndADailyOne_RefillsAtTheDayBoundary()
    {
        const long day = 100;
        var seam = new NodeDefinition("node.ore.test", "item.material.iron_ore", 1, 2, 3, Respawn.None, "skill.survival", 5);
        Assert.True(CraftingRules.Ready(seam, null, 0, 0, day));
        Assert.True(CraftingRules.Ready(seam, 50, 2, 60, day));
        Assert.False(CraftingRules.Ready(seam, 50, 3, 60, day));
        Assert.False(CraftingRules.Ready(seam, 50, 3, 10_000, day));   // never refills

        var stand = seam with { Charges = 1, Respawn = Respawn.Daily };
        Assert.True(CraftingRules.Ready(stand, null, 0, 0, day));
        Assert.False(CraftingRules.Ready(stand, 150, 1, 199, day));   // the same day
        Assert.True(CraftingRules.Ready(stand, 150, 1, 200, day));    // the next
        Assert.True(CraftingRules.Ready(stand, 199, 7, 200, day));    // a tick later, once the day has turned
    }

    [Fact]
    public void Quality_IsCrudeStandardOrFine()
    {
        Assert.Equal(new[] { "crude", "standard", "fine" }, new[] { Quality.Crude, Quality.Standard, Quality.Fine }.Select(Quality.Key));
        Assert.False(Quality.IsValid(2));
        Assert.False(Quality.IsValid(-2));
    }
}
