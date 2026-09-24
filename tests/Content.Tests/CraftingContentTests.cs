using UNNAMED.Domain.Crafting;
using UNNAMED.World.Runtime;

namespace UNNAMED.Content.Tests;

/// <summary>Gathering and crafting from content (M3f): two nodes and two recipes build, and the CRF lint refuses what Phase 1 cannot run.</summary>
public class CraftingContentTests
{
    private static ContentLoader Load(string root)
    {
        var loader = new ContentLoader();
        loader.LoadAll(root);
        return loader;
    }

    /// <summary>A copy of the game's content with one file edited, in a directory removed afterwards.</summary>
    private sealed class EditedContent : IDisposable
    {
        public EditedContent(string relativePath, string find, string replace)
        {
            string source = Path.Combine(RepoPaths.Root(), "content");
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            string path = Path.Combine(Root, relativePath);
            string text = File.ReadAllText(path).Replace("\r\n", "\n");
            Assert.Contains(find, text);
            File.WriteAllText(path, text.Replace(find, replace));
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-crf-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static void AssertRefused(string relativePath, string find, string replace, string mentions)
    {
        using var content = new EditedContent(relativePath, find, replace);
        var loader = Load(content.Root);
        Assert.Contains(loader.Errors, e => e.Code == "CRF001" && e.Message.Contains(mentions, StringComparison.Ordinal));
    }

    [Fact]
    public void TheGamesCrafting_IsTwoNodesAndTwoRecipes()
    {
        var crafting = CraftingContent.Build(Load(Path.Combine(RepoPaths.Root(), "content")));

        Assert.Equal(new[] { "node.ore.iron_seam", "node.wood.ash_stand" }, crafting.Nodes.Keys);
        var seam = crafting.Nodes["node.ore.iron_seam"];
        Assert.Equal(("item.material.iron_ore", 1, 2, 3, Respawn.None, "skill.survival", 5),
            (seam.ItemId, seam.YieldMin, seam.YieldMax, seam.Charges, seam.Respawn, seam.SkillId, seam.Difficulty));
        var stand = crafting.Nodes["node.wood.ash_stand"];
        Assert.Equal(("item.material.ash_haft", 1, 1, 1, Respawn.Daily), (stand.ItemId, stand.YieldMin, stand.YieldMax, stand.Charges, stand.Respawn));

        Assert.Equal(new[] { "recipe.smithing.iron_billet", "recipe.smithing.march_spear" }, crafting.Recipes.Keys);
        var billet = crafting.Recipes["recipe.smithing.iron_billet"];
        Assert.Equal(("forge", "skill.smithing", 0, "item.material.iron_ingot", 1, true, 15L),
            (billet.StationKind, billet.SkillId, billet.Complexity, billet.OutputItemId, billet.OutputCount, billet.QualityRoll, billet.FirstTimeXp));
        Assert.Equal(new[] { new RecipeInput("item.material.iron_ore", 1) }, billet.Inputs.ToList());
        var spear = crafting.Recipes["recipe.smithing.march_spear"];
        Assert.Equal(("anvil", 10, "item.weapon.march_spear", true, 40L), (spear.StationKind, spear.Complexity, spear.OutputItemId, spear.QualityRoll, spear.FirstTimeXp));
        Assert.Equal(new[] { new RecipeInput("item.material.iron_ingot", 1), new RecipeInput("item.material.ash_haft", 1) }, spear.Inputs.ToList());

        Assert.Equal(new YieldBonus("skill.survival", 3, 1), Assert.Single(crafting.YieldBonuses));   // PROTOTYPE.md §4.1
        var c = crafting.Constants;
        Assert.Equal((10, 2, 60, 3, 40, 2),
            (c.FinePercentAtComplexity, c.FinePercentPerPoint, c.FinePercentMax, c.CrudePercentPerPoint, c.CrudePercentMax, c.WeaponDamagePerStep));
    }

    [Fact]
    public void ADailyNodeOfManyCharges_IsRefused() =>
        AssertRefused("nodes/wood/ash_stand.yaml", "charges: 1", "charges: 3", "a daily node holds one charge a day");

    [Fact]
    public void ANodeNeedingATool_IsRefused() =>
        AssertRefused("nodes/ore/iron_seam.yaml", "difficulty: 5", "difficulty: 5\ntool_tier_min: 1", "tool_tier_min is not built in Phase 1");

    [Fact]
    public void ARecipeOfTwoOutputs_IsRefused() =>
        AssertRefused("recipes/smithing/iron_billet.yaml", "outputs:\n", "outputs:\n  - { item_ref: item.material.iron_ore, count: 1 }\n",
            "exactly one output");

    [Fact]
    public void ARecipeOfAProfession_IsRefused() =>
        AssertRefused("recipes/smithing/march_spear.yaml", "complexity: 10", "complexity: 10\nprofession: smithing", "profession is not built in Phase 1");

    [Fact]
    public void ARecipeOfNoInputs_IsRefused() =>
        AssertRefused("recipes/smithing/iron_billet.yaml", "  - { item_ref: item.material.iron_ore, count: 1 }\n", "", "at least one input");

    [Fact]
    public void QualityTuningPastAHundredPercent_IsRefused() =>
        AssertRefused("config/crafting.yaml", "crude_percent_max: 40", "crude_percent_max: 60", "fine and crude together at most 100");

    [Fact]
    public void AYieldPassiveThatIsNotAnAddition_IsRefused() =>
        AssertRefused("skills/survival.yaml", "target: stat.gather_yield, op: add", "target: stat.gather_yield, op: mul", "is raised with op add");
}
