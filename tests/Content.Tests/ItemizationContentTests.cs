using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;

namespace UNNAMED.Content.Tests;

/// <summary>
/// WP3a from content: item classes with rarity derived and linted, recipe books, experiment candidates, masterwork-possible recipes, drop
/// quality bands, and the new slot content - gloves, trousers, boots, a cloak, a torch and a shield.
/// </summary>
public class ItemizationContentTests
{
    private static ContentLoader Load(string root)
    {
        var loader = new ContentLoader();
        loader.LoadAll(root);
        return loader;
    }

    private static ContentLoader Game() => Load(Path.Combine(RepoPaths.Root(), "content"));

    [Fact]
    public void AllFourControlAndPoisonDeliveries_AreGuaranteedNormalLoot_NotStartingItems()
    {
        var content = Game();
        var loot = ItemContent.BuildLootTables(content);
        var starting = ItemContent.BuildStartingKit(content);
        foreach (string item in new[] { "poison_flask", "snarebind_resin", "sleep_coating", "paralyse_coating" })
        {
            string id = "item.consumable." + item;
            Assert.Contains(loot["loot.merchant_cart"].Guaranteed, e => e.ItemId == id);
            Assert.Contains(loot["loot.barrow_cache"].Guaranteed, e => e.ItemId == id);
            Assert.DoesNotContain(starting, e => e.ItemId == id);
        }
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

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-wp3a-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static void AssertRefused(string relativePath, string find, string replace, string code, string mentions)
    {
        using var content = new EditedContent(relativePath, find, replace);
        var loader = Load(content.Root);
        Assert.Contains(loader.Errors, e => e.Code == code && e.Message.Contains(mentions, StringComparison.Ordinal));
    }

    [Fact]
    public void TheGamesContent_LoadsClean()
    {
        var loader = Game();
        Assert.Empty(loader.Errors);
    }

    [Fact]
    public void RarityIsDerivedFromTheClass_AndHaldasBowIsANotableFind()
    {
        var catalog = ItemContent.BuildCatalog(Game());
        var bow = catalog.Get("item.weapon.haldas_bow");
        Assert.Equal((ItemClass.Notable, "rare", "skill.bow"), (bow.Class, bow.Rarity, bow.Weapon!.SkillId));
        Assert.All(catalog.Definitions.Values, d => Assert.Equal(ItemClasses.Rarity(d.Class), d.Rarity));
    }

    [Fact]
    public void ARarityThatDisagreesWithTheClass_IsRefused() =>
        AssertRefused("items/weapon/haldas_bow.yaml", "rarity: rare", "rarity: common", "ITM001", "does not match item_class notable");

    [Fact]
    public void ANotableThatStacks_IsRefused() =>
        AssertRefused("items/material/boneleaf.yaml", "rarity: common", "item_class: notable", "ITM001", "one of a kind");

    [Fact]
    public void AMasterworkInALootTable_IsRefused() =>
        AssertRefused("items/armor/smith_gloves.yaml", "item_class: ordinary", "item_class: masterwork", "ITM001", "masterwork is craft-only");

    [Fact]
    public void TheNewSlots_AreFilled()
    {
        var catalog = ItemContent.BuildCatalog(Game());
        Assert.Equal(EquipSlot.Hands, catalog.Get("item.armor.smith_gloves").Slot);
        Assert.Equal(EquipSlot.Legs, catalog.Get("item.armor.wool_trousers").Slot);
        Assert.Equal(EquipSlot.Feet, catalog.Get("item.armor.hide_boots").Slot);
        Assert.Equal(EquipSlot.Cloak, catalog.Get("item.armor.cloak").Slot);
        Assert.Equal(EquipSlot.OffHand, catalog.Get("item.armor.timber_shield").Slot);
        var torch = catalog.Get("item.tool.torch");
        Assert.Equal((EquipSlot.OffHand, 8_000L, 0.5), (torch.Slot!.Value, torch.LightRadiusMm, torch.StealthPenalty));
    }

    [Fact]
    public void AMainHandPlainItem_IsRefused() =>
        AssertRefused("items/tool/torch.yaml", "equip_slot: off_hand", "equip_slot: main_hand", "ITM001", "is for weapons");

    [Fact]
    public void RecipeBooks_TeachRecipes()
    {
        var teaches = MagicContent.Build(Game()).Teaches;
        Assert.Equal(new[] { "recipe.leatherwork.smith_gloves", "recipe.leatherwork.wool_trousers", "recipe.leatherwork.hide_boots" },
            teaches["item.tome.bench_patterns"]);
        Assert.Equal(new[] { "recipe.herbal.salve_minor_ashbloom", "recipe.herbal.salve_ember_boneleaf" }, teaches["item.tome.herbal_remedies"]);
    }

    [Fact]
    public void ABookTeachingWhatIsNoRecipe_IsRefused() =>
        AssertRefused("items/tome/bench_patterns.yaml", "ref: recipe.leatherwork.hide_boots", "ref: recipe.leatherwork.nothing", "MAG001", "is not a recipe");

    [Fact]
    public void Crafting_HoldsItsExperiments_MasterworkFlags_AndDropBands()
    {
        var crafting = CraftingContent.Build(Game());
        Assert.Equal(new[] { "recipe.leatherwork.cloak", "recipe.leatherwork.timber_shield", "recipe.smithing.assembled_sword", "recipe.smithing.torch", "recipe.tanning.wolf_hide" },
            crafting.Recipes.Values.Where(r => r.Experiment).Select(r => r.Id));
        Assert.Equal((2, 1), (crafting.Constants.ExperimentsPerStation["workbench"], crafting.Constants.ExperimentsPerStation["forge"]));
        Assert.True(crafting.Recipes["recipe.leatherwork.smith_gloves"].MasterworkPossible);
        Assert.False(crafting.Recipes["recipe.smithing.torch"].MasterworkPossible);
        Assert.Equal((25, 1, 1), (crafting.Constants.MasterworkPercentOfFine, crafting.Constants.MasterworkStationGrade, crafting.Constants.ArmorPerStep));
        Assert.Equal(new[] { 1, 3, 5 }, crafting.DropQuality.Select(b => b.LevelMin));
    }

    [Fact]
    public void MoreExperimentsThanCandidates_AreRefused() =>
        AssertRefused("config/crafting.yaml", "  forge: 1", "  forge: 3", "CRF001", "experiments.forge is 3");

    [Fact]
    public void MasterworkOnAStackingOutput_IsRefused() =>
        AssertRefused("recipes/herbal/salve_minor_ashbloom.yaml", "quality_roll: false }", "quality_roll: true, masterwork_possible: true }", "CRF001",
            "a masterwork is one of a kind");

    [Fact]
    public void ADropBandOverAHundred_IsRefused() =>
        AssertRefused("config/crafting.yaml", "{ level_min: 5, crude_percent: 10, fine_percent: 20 }", "{ level_min: 5, crude_percent: 90, fine_percent: 20 }",
            "CRF001", "drop_quality");

    [Fact]
    public void BoneleafAshbloomAndTimber_AreEachSomeRecipesInput()
    {
        var inputs = CraftingContent.Build(Game()).Recipes.Values.SelectMany(r => r.Inputs).Select(i => i.ItemId).ToHashSet();
        Assert.Contains("item.material.boneleaf", inputs);
        Assert.Contains("item.material.herb_ashbloom", inputs);
        Assert.Contains("item.material.timber", inputs);
    }

    [Fact]
    public void FamiliarityTakesFiveSteps()
    {
        var rules = ItemContent.BuildInventoryRules(Game(), 1_600);
        Assert.Equal(new[] { 3, 6, 10, 15, 25 }, rules.FamiliarityUsesPerStep);
    }
}
