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
    public void EveryRecipeMeetsTheVendorMarginIncludingComposedInputs()
    {
        var loader = Load(Path.Combine(RepoPaths.Root(), "content"));
        Assert.True(CraftingContent.CheckRecipeMargins(ItemContent.BuildCatalog(loader), ItemContent.BuildPricing(loader), CraftingContent.BuildRecipes(loader)));
        Assert.DoesNotContain(loader.Errors, e => e.Code == "CRF001");
    }

    [Fact]
    public void AnOverpricedOutputIsRefused() =>
        AssertRefused("items/material/iron_ingot.yaml", "value_base: 9", "value_base: 9999", "recipe.smithing.iron_billet");

    [Fact]
    public void CheapRawInputsAlsoExposeAnUnsafeComposedRecipe() =>
        AssertRefused("items/material/iron_ore.yaml", "value_base: 5", "value_base: 1", "recipe.smithing.march_spear");

    [Fact]
    public void TheGamesCrafting_IsItsNodesAndRecipes()
    {
        var crafting = CraftingContent.Build(Load(Path.Combine(RepoPaths.Root(), "content")));

        Assert.Equal(new[]
        {
            "node.carcass.boar", "node.carcass.deer", "node.carcass.grey_wolf", "node.fibre.bog_cotton", "node.firstplay.ashbloom",
            "node.herb.boneleaf", "node.herb.woundmoss", "node.ore.bog_iron", "node.ore.grey_iron", "node.ore.hidden_bog_iron",
            "node.ore.hidden_grey_iron", "node.ore.iron_seam", "node.ore.loose_iron", "node.sedge.tidewashed_spar",
            "node.trap.larder_snare", "node.trap.pelt_snare", "node.wood.ash_stand", "node.wood.deadfall", "node.wood.pine_resin",
        }, crafting.Nodes.Keys);
        var spoil = crafting.Nodes["node.ore.loose_iron"];
        Assert.Equal(("item.material.iron_ore", 1, 1, Respawn.Daily), (spoil.ItemId, spoil.Charges, spoil.YieldMin, spoil.Respawn));
        var seam = crafting.Nodes["node.ore.iron_seam"];
        Assert.Equal(("item.material.iron_ore", 1, 2, 3, Respawn.None, "skill.mining", 5),
            (seam.ItemId, seam.YieldMin, seam.YieldMax, seam.Charges, seam.Respawn, seam.SkillId, seam.Difficulty));
        var stand = crafting.Nodes["node.wood.ash_stand"];
        Assert.Equal(("item.material.ash_haft", 1, 1, 1, Respawn.Daily), (stand.ItemId, stand.YieldMin, stand.YieldMax, stand.Charges, stand.Respawn));
        // Halda's trapline (Halda's Tally): a pelt snare and a larder snare, each a catch a day.
        var pelt = crafting.Nodes["node.trap.pelt_snare"];
        Assert.Equal(("item.material.small_hide", 1, 1, 1, Respawn.Daily), (pelt.ItemId, pelt.YieldMin, pelt.YieldMax, pelt.Charges, pelt.Respawn));
        var larder = crafting.Nodes["node.trap.larder_snare"];
        Assert.Equal(("item.material.raw_meat", 1, 1, 1, Respawn.Daily), (larder.ItemId, larder.YieldMin, larder.YieldMax, larder.Charges, larder.Respawn));
        var bloom = crafting.Nodes["node.firstplay.ashbloom"];
        Assert.Equal(("item.material.herb_ashbloom", 1, 1, 1, Respawn.Daily), (bloom.ItemId, bloom.YieldMin, bloom.YieldMax, bloom.Charges, bloom.Respawn));

        Assert.Equal(new[]
        {
            "recipe.cooking.boil_water", "recipe.cooking.herb_tea", "recipe.cooking.roast_meat", "recipe.cooking.torch",
            "recipe.fletching.arrows", "recipe.fletching.hunting_bow", "recipe.fletching.hunting_bow_silk",
            "recipe.herbal.salve_ember", "recipe.herbal.salve_ember_boneleaf", "recipe.herbal.salve_minor", "recipe.herbal.salve_minor_ashbloom",
            "recipe.herbal.salve_minor_boiled",
            "recipe.leatherwork.cloak", "recipe.leatherwork.hide_boots", "recipe.leatherwork.hide_cap", "recipe.leatherwork.hide_cap_fellhorn",
            "recipe.leatherwork.hide_vest", "recipe.leatherwork.hide_vest_barrowback", "recipe.leatherwork.smith_gloves", "recipe.leatherwork.timber_shield",
            "recipe.leatherwork.wool_trousers", "recipe.outfitting.herb_pouch", "recipe.outfitting.pack_large", "recipe.outfitting.quiver",
            "recipe.outfitting.reagent_satchel", "recipe.outfitting.satchel", "recipe.smithing.assembled_sword", "recipe.smithing.fitting_wedge",
            "recipe.smithing.iron_billet", "recipe.smithing.lame_coif", "recipe.smithing.lame_sabatons", "recipe.smithing.lamework_jack",
            "recipe.smithing.march_spear", "recipe.smithing.torch", "recipe.tanning.wolf_hide",
        }, crafting.Recipes.Keys);
        Assert.Equal(("workbench", 12), (crafting.Recipes["recipe.fletching.arrows"].StationKind, crafting.Recipes["recipe.fletching.arrows"].OutputCount));
        var billet = crafting.Recipes["recipe.smithing.iron_billet"];
        Assert.Equal(("forge", "skill.smithing", 0, "item.material.iron_ingot", 1, true, 15L),
            (billet.StationKind, billet.SkillId, billet.Complexity, billet.OutputItemId, billet.OutputCount, billet.QualityRoll, billet.FirstTimeXp));
        Assert.Equal(new[] { new RecipeInput("item.material.iron_ore", 1) }, billet.Inputs.ToList());
        var spear = crafting.Recipes["recipe.smithing.march_spear"];
        Assert.Equal(("anvil", 10, "item.weapon.march_spear", true, 40L), (spear.StationKind, spear.Complexity, spear.OutputItemId, spear.QualityRoll, spear.FirstTimeXp));
        Assert.Equal(new[] { new RecipeInput("item.material.iron_ingot", 1), new RecipeInput("item.material.ash_haft", 1) }, spear.Inputs.ToList());

        // PROTOTYPE.md §4.1's one more of whatever a harvest gives, carried by the skill its node practises (D13 §2.7).
        Assert.Equal(new[] { "skill.foraging", "skill.herbalism", "skill.logging", "skill.mining", "skill.skinning" },
            crafting.YieldBonuses.Select(b => b.SkillId));
        Assert.All(crafting.YieldBonuses, bonus => Assert.Equal((3, 1), (bonus.FromLevel, bonus.Amount)));
        var c = crafting.Constants;
        Assert.Equal((10, 2, 60, 3, 40, 2),
            (c.FinePercentAtComplexity, c.FinePercentPerPoint, c.FinePercentMax, c.CrudePercentPerPoint, c.CrudePercentMax, c.WeaponDamagePerStep));
    }

    [Fact]
    public void ADailyNodeOfManyCharges_IsRefused() =>
        AssertRefused("nodes/wood/ash_stand.yaml", "charges: 1", "charges: 3", "a daily node holds one charge a day");

    /// <summary>
    /// The swings one harvest takes are content: three at a seam, one at a stand, and one at every node that names none - the default that
    /// keeps every node authored before the swing working at a touch.
    /// </summary>
    [Fact]
    public void ASwingCountIsContent_AndOneIsTheDefault()
    {
        var crafting = CraftingContent.Build(Load(Path.Combine(RepoPaths.Root(), "content")));

        Assert.Equal(3, crafting.Nodes["node.ore.iron_seam"].Swings);
        Assert.Equal(1, crafting.Nodes["node.wood.ash_stand"].Swings);
        Assert.Equal(1, crafting.Nodes["node.herb.boneleaf"].Swings);
        Assert.Equal(1, crafting.Nodes["node.fibre.bog_cotton"].Swings);   // names no swings: one, as every node did before
    }

    [Fact]
    public void MoreThanTenSwings_IsRefused() =>
        AssertRefused("nodes/ore/iron_seam.yaml", "swings: 3", "swings: 11", "swings must be 0..10");

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
        AssertRefused("skills/mining.yaml", "target: stat.gather_yield, op: add", "target: stat.gather_yield, op: mul", "is raised with op add");
}
