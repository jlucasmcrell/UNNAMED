using System.Collections.Immutable;
using UNNAMED.Domain.Crafting;

namespace UNNAMED.Domain.Tests;

/// <summary>What an experiment comes to (UI progression F): found when every input is carried, a hint when some are, otherwise nothing.</summary>
public class FindingRulesTests
{
    private static RecipeDefinition Recipe(string id, params (string Item, int Count)[] inputs) =>
        new(id, "station.bench", "skill.crafting", 5, inputs.Select(i => new RecipeInput(i.Item, i.Count)).ToImmutableArray(), "item.out", 1, true, 10);

    private static readonly RecipeDefinition Cloak = Recipe("recipe.cloak", ("hide", 1), ("cotton", 2));
    private static readonly RecipeDefinition Shield = Recipe("recipe.shield", ("timber", 2), ("scrap", 1));

    private static Dictionary<string, int> Carry(params (string Item, int Count)[] items) => items.ToDictionary(i => i.Item, i => i.Count);

    [Fact]
    public void EveryInputCarried_FindsTheRecipe_NamingTheMaterialsThatWentIn()
    {
        var outcome = FindingRules.Assess(new[] { Cloak, Shield }, Carry(("hide", 1), ("cotton", 3)));
        Assert.Equal(FindingKind.Found, outcome.Kind);
        Assert.Same(Cloak, outcome.Recipe);
        Assert.Equal(new[] { "cotton", "hide" }, outcome.Materials);
    }

    [Fact]
    public void SomeInputsCarried_IsAHint_AtTheFirstRecipeTheyBearOn()
    {
        var outcome = FindingRules.Assess(new[] { Cloak, Shield }, Carry(("timber", 1)));
        Assert.Equal(FindingKind.Hint, outcome.Kind);
        Assert.Same(Shield, outcome.Recipe);
        Assert.Equal(new[] { "timber" }, outcome.Materials);
    }

    [Fact]
    public void ACarriedInputShort_ButNotAbsent_StillHints()
    {
        var outcome = FindingRules.Assess(new[] { Cloak }, Carry(("hide", 1), ("cotton", 1)));
        Assert.Equal(FindingKind.Hint, outcome.Kind);
        Assert.Equal(new[] { "cotton", "hide" }, outcome.Materials);
    }

    [Fact]
    public void NothingThatBearsOnAnyRecipe_IsNothing()
    {
        var outcome = FindingRules.Assess(new[] { Cloak, Shield }, Carry(("pebble", 4)));
        Assert.Equal(FindingKind.Nothing, outcome.Kind);
        Assert.Null(outcome.Recipe);
        Assert.Empty(outcome.Materials);
        Assert.Equal(FindingKind.Nothing, FindingRules.Assess(new[] { Cloak }, Carry()).Kind);
    }

    [Fact]
    public void AFoundRecipeWinsOverAHint_EvenWhenAnEarlierRecipeOnlyHints()
    {
        var outcome = FindingRules.Assess(new[] { Cloak, Shield }, Carry(("hide", 1), ("timber", 2), ("scrap", 1)));
        Assert.Equal(FindingKind.Found, outcome.Kind);
        Assert.Same(Shield, outcome.Recipe);
    }

    [Fact]
    public void AQuestionIsClosedOncePerStationOutcomeAndRecipe()
    {
        Assert.Equal("station.a|hint|recipe.cloak", FindingKeys.Of("station.a", FindingKind.Hint, "recipe.cloak"));
        Assert.Equal("station.a|nothing|-", FindingKeys.Of("station.a", FindingKind.Nothing, null));
        var a = new Finding(FindingKind.Hint, "station.a", "bench", "recipe.cloak", ImmutableArray.Create("hide"), 1);
        var later = a with { Tick = 99, Materials = ImmutableArray.Create("hide", "cotton") };
        Assert.Equal(a.Key, later.Key);
    }
}
