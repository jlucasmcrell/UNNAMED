using UNNAMED.Domain.Items;

namespace UNNAMED.Content.Tests;

/// <summary>Spec 47 from content: what the infusions, runes, ring and armour say, and the ITM001 lint keeping the owner's rulings.</summary>
public class EnhancementContentTests
{
    private static ContentLoader Load(string root)
    {
        var loader = new ContentLoader();
        loader.LoadAll(root);
        return loader;
    }

    private static ContentLoader Game() => Load(Path.Combine(RepoPaths.Root(), "content"));

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

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-1b-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static void AssertRefused(string relativePath, string find, string replace, string code, string mentions)
    {
        using var content = new EditedContent(relativePath, find, replace);
        var loader = Load(content.Root);
        Assert.Contains(loader.Errors, e => e.Code == code && e.Message.Contains(mentions, StringComparison.Ordinal));
    }

    [Fact]
    public void TheGamesInfusionsRunesAndGear_SayWhatTheSpecSays()
    {
        var loader = Game();
        Assert.Empty(loader.Errors);
        var catalog = ItemContent.BuildCatalog(loader);
        var cinder = catalog.Get("item.material.cinder_sac").Enhancement!;
        Assert.Equal((EnhancementKind.Infusion, EnhancementTarget.Weapon, "effect.scorched", EnhancementVisual.Item, "ember"),
            (cinder.Kind, cinder.Target, cinder.OnHitEffect, cinder.Visual, cinder.VisualHook));
        Assert.Equal(("effect.bleeding", EnhancementVisual.None), (catalog.Get("item.trinket.wolf_fang").Enhancement!.OnHitEffect, catalog.Get("item.trinket.wolf_fang").Enhancement!.Visual));
        Assert.All(new[] { "keen", "warding", "steadfast" }, r => Assert.Equal(EnhancementVisual.None, catalog.Get($"item.rune.{r}").Enhancement!.Visual));
        Assert.Equal(EnhancementKind.Enchantment, catalog.Get("item.rune.ember_touched").Enhancement!.Kind);
        Assert.Equal((EquipSlot.Ring, "effect.gear_quartz"), (catalog.Get("item.trinket.quartz_ring").Slot!.Value, catalog.Get("item.trinket.quartz_ring").EquipEffect));
        var jack = catalog.Get("item.armor.lamework_jack");
        Assert.Equal((0.06, 0.25), (jack.MovementPenalty, jack.Noise));
        var rules = ItemContent.BuildInventoryRules(loader, 0).Enhancement;
        Assert.Equal(("effect.gear_unstable", "enchanting_table", "skill.enchanting"), (rules.InstabilityEffect, rules.EnchantingStation, rules.EnchantingSkill));
    }

    [Fact]
    public void APlainStatBumpThatShowsOnTheItem_IsRefused() =>
        AssertRefused("items/rune/keen.yaml", "essence: item.essence.resonant", "essence: item.essence.resonant, visual: item, visual_hook: glow", "ITM001",
            "a plain stat bump shows nothing on the item");

    [Fact]
    public void AWornEffectThatIsACoating_IsRefused() =>
        AssertRefused("items/rune/keen.yaml", "worn_effect: effect.gear_keen", "worn_effect: effect.coating_venom", "ITM001", "a worn effect is a modifier");

    [Fact]
    public void AnEssenceThatIsNoItem_IsRefused() =>
        AssertRefused("items/rune/keen.yaml", "essence: item.essence.resonant", "essence: item.essence.nothing", "ITM001", "is not an item");

    [Fact]
    public void AnArmourOnHit_IsRefused() =>
        AssertRefused("items/rune/warding.yaml", "worn_effect: effect.gear_warding", "on_hit: { effect: effect.bleeding, percent: 10 }", "ITM001",
            "only a weapon's enhancement");

    [Fact]
    public void AnEquipEffectOnSomethingNotWorn_IsRefused() =>
        AssertRefused("items/material/timber.yaml", "category: material", "category: material\nequip_effect: effect.gear_keen", "ITM001",
            "belong to something equipped");
}
