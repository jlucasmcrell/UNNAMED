using System.Collections.Immutable;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;

namespace UNNAMED.Domain.Tests.Items;

/// <summary>
/// Spec 47's rules as pure functions: slots by quality and material, the setting chance and its cracks, reforging, stability, what salvage
/// keeps, the owner's visual ruling (2026-10-21), and what worn gear adds - equip effects, procs, mobility and noise (ITM-19, 20, 64..69).
/// </summary>
public class EnhancementRuleTests
{
    private static readonly EnhancementRules Rules = EnhancementRules.Default with { InstabilityEffect = "effect.unstable" };

    private static ItemDefinition Piece(string id = "item.armor.vest", int? sockets = null, string? heritage = null) =>
        new(id, "armor", 1, 1000, 10, "common", false, false, EquipSlot.Chest, Requirements.None, null, 4)
        {
            EnhancementSlots = sockets,
            Heritage = heritage,
        };

    private static EnhancementSpec Spec(int tier = 1, int stability = 1, string? worn = "effect.warding", string? onHit = null, int percent = 0,
        EnhancementVisual visual = EnhancementVisual.None, string? hook = null, EnhancementTarget target = EnhancementTarget.Armour) =>
        new(EnhancementKind.Infusion, target, tier, 0, stability, worn, onHit, percent, visual) { VisualHook = hook, Essence = "item.essence.resonant" };

    private static ItemDefinition Material(string id, EnhancementSpec spec) =>
        new(id, "material", 5, 50, 5, "common", false, false, null, Requirements.None, null, 0) { Enhancement = spec };

    [Fact]
    public void Slots_RunOneToThree_ByQualityAndBase()
    {
        Assert.Equal(new[] { 1, 1, 2, 3 }, new[] { Quality.Crude, Quality.Standard, Quality.Fine, Quality.Masterwork }
            .Select(q => EnhancementRulesEngine.Slots(Piece(), q, Rules)));
        Assert.Equal(new[] { 1, 2, 3, 3 }, new[] { Quality.Crude, Quality.Standard, Quality.Fine, Quality.Masterwork }
            .Select(q => EnhancementRulesEngine.Slots(Piece(sockets: 2), q, Rules)));
        Assert.Equal(0, EnhancementRulesEngine.Slots(Piece(heritage: "people.veth"), Quality.Fine, Rules));   // heritage is never worked
        Assert.Equal(0, EnhancementRulesEngine.Slots(Material("item.material.x", Spec()), Quality.Standard, Rules));
    }

    [Fact]
    public void TheSettingChance_RisesWithSkillAndEnchantability_AndFallsWithTier_InsideItsBounds()
    {
        Assert.Equal(40 + 30 - 10, EnhancementRulesEngine.SetPercent(10, Spec(tier: 1), Rules));
        Assert.Equal(40 + 30 - 30, EnhancementRulesEngine.SetPercent(10, Spec(tier: 3), Rules));
        Assert.Equal(Rules.MaxPercent, EnhancementRulesEngine.SetPercent(60, Spec(), Rules));
        Assert.Equal(Rules.MinPercent, EnhancementRulesEngine.SetPercent(0, Spec(tier: 5) with { Enchantability = -50 }, Rules));
        Assert.Equal(SettingOutcome.Set, EnhancementRulesEngine.Setting(10, Spec(), Rules, 0.59, 0.0));
        Assert.Equal(SettingOutcome.Cracked, EnhancementRulesEngine.Setting(10, Spec(), Rules, 0.61, 0.34));
        Assert.Equal(SettingOutcome.Failed, EnhancementRulesEngine.Setting(10, Spec(), Rules, 0.61, 0.35));
    }

    [Fact]
    public void ASetting_FillsCracksOrFails_AndAReforgeConsumesTheOld()
    {
        var set = EnhancementRulesEngine.Apply(null, "item.a", null, SettingOutcome.Set);
        Assert.Equal(new[] { "item.a" }, set.Filled);
        Assert.Equal((0, 1), (set.Cracked, set.Attempts));
        var cracked = EnhancementRulesEngine.Apply(set, "item.b", null, SettingOutcome.Cracked);
        Assert.Equal(new[] { "item.a" }, cracked.Filled);
        Assert.Equal((1, 2), (cracked.Cracked, cracked.Attempts));
        Assert.Equal(1, EnhancementRulesEngine.FreeSlots(Piece(sockets: 3), Quality.Standard, cracked, Rules));
        var reforged = EnhancementRulesEngine.Apply(cracked, "item.c", 0, SettingOutcome.Set);
        Assert.Equal(new[] { "item.c" }, reforged.Filled);
        var lost = EnhancementRulesEngine.Apply(cracked, "item.c", 0, SettingOutcome.Failed);
        Assert.Empty(lost.Filled);   // the old one is consumed whatever the outcome
        Assert.True(lost.IsValid && reforged.IsValid);
        Assert.False(new ItemEnhancements(ImmutableArray.Create("item.a"), 0, 0).IsValid);   // a filled slot was set by an attempt
    }

    [Fact]
    public void StrongEnhancements_PastAPiecesStability_MakeItUnstable()
    {
        var catalog = new ItemCatalog(new[] { Material("item.big", Spec(stability: 3)), Material("item.small", Spec(stability: 1)) });
        var two = new ItemEnhancements(ImmutableArray.Create("item.big", "item.small"), 0, 2);
        Assert.Equal((4, 4), (EnhancementRulesEngine.StabilityUsed(two, catalog), EnhancementRulesEngine.StabilityCapacity(Quality.Standard, Rules)));
        Assert.False(EnhancementRulesEngine.Unstable(Quality.Standard, two, catalog, Rules));
        Assert.True(EnhancementRulesEngine.Unstable(Quality.Crude, two, catalog, Rules));
        var worn = GearRules.WornEffects(new[] { (EquipSlot.Chest, Piece(), Quality.Crude, (ItemEnhancements?)two) }, catalog, Rules);
        Assert.Equal(new[] { "effect.unstable", "effect.warding" }, worn);   // the warding once, though two pieces of it are set
    }

    [Fact]
    public void Salvage_KeepsAnEnhancementWhole_OnlyPastItsTiersSkill_ElseLeavesItsEssence()
    {
        var tier2 = Spec(tier: 2);
        Assert.Equal(0, EnhancementRulesEngine.RecoverPercent(9, tier2, Rules));
        Assert.Equal(50, EnhancementRulesEngine.RecoverPercent(10, tier2, Rules));
        Assert.Equal(100, EnhancementRulesEngine.RecoverPercent(40, tier2, Rules));
        Assert.Equal("item.essence.resonant", EnhancementRulesEngine.Salvaged("item.rune", 9, tier2, Rules, 0.0));
        Assert.Equal("item.rune", EnhancementRulesEngine.Salvaged("item.rune", 10, tier2, Rules, 0.49));
        Assert.Equal("item.essence.resonant", EnhancementRulesEngine.Salvaged("item.rune", 10, tier2, Rules, 0.5));
        Assert.Null(EnhancementRulesEngine.Salvaged("item.rune", 0, tier2 with { Essence = null }, Rules, 0.0));
    }

    [Fact]
    public void TheOwnersVisualRuling_IsKept()
    {
        Assert.Null(Spec().VisualRefusal());   // a plain stat bump, no visual
        Assert.NotNull(Spec(visual: EnhancementVisual.Item, hook: "glow").VisualRefusal());   // a plain stat bump never shows on the item
        Assert.Null(Spec(worn: null, onHit: "effect.scorched", percent: 20, visual: EnhancementVisual.Item, hook: "ember", target: EnhancementTarget.Weapon).VisualRefusal());
        Assert.Null(Spec(visual: EnhancementVisual.Body, hook: "aura").VisualRefusal());   // a full-body buff may show on the body
        Assert.NotNull(Spec(worn: null, onHit: "effect.x", percent: 5, visual: EnhancementVisual.Body, hook: "aura").VisualRefusal());
        Assert.NotNull(Spec(hook: "ember").VisualRefusal());
        var catalog = new ItemCatalog(new[] { Material("item.ember", Spec(worn: null, onHit: "effect.scorched", percent: 20, visual: EnhancementVisual.Item,
            hook: "ember", target: EnhancementTarget.Weapon)), Material("item.keen", Spec(target: EnhancementTarget.Weapon)) });
        var sword = new ItemEnhancements(ImmutableArray.Create("item.keen", "item.ember"), 0, 2);
        var worn = GearRules.Enhancements(new[] { (EquipSlot.MainHand, Piece(), (ItemEnhancements?)sword) }, catalog);
        Assert.Equal(new[] { new EnhancementVisualHook(EquipSlot.MainHand, EnhancementVisual.Item, "ember") }, GearRules.Visuals(worn));
        Assert.Equal(new[] { ("effect.scorched", 20) }, GearRules.Procs(sword, catalog));
    }

    [Fact]
    public void WornGear_AddsItsEquipEffect_ItsMobilityCost_AndItsNoise()
    {
        var ring = new ItemDefinition("item.trinket.ring", "misc", 1, 20, 30, "common", false, false, EquipSlot.Ring, Requirements.None, null, 0)
        {
            EquipEffect = "effect.quartz",
        };
        var jack = Piece("item.armor.jack") with { MovementPenalty = 0.06, Noise = 0.25 };
        var boots = Piece("item.armor.boots") with { MovementPenalty = 0.02, Noise = 0.2 };
        Assert.Equal(new[] { "effect.quartz" },
            GearRules.WornEffects(new[] { (EquipSlot.Ring, ring, Quality.Standard, (ItemEnhancements?)null) }, ItemCatalog.Empty, Rules));
        Assert.Equal(0.08, GearRules.MovementPenalty(new[] { jack, boots, ring }, Rules), 6);
        Assert.Equal(1.45, GearRules.NoiseFactor(new[] { jack, boots, ring }), 6);
        Assert.Equal(Rules.MaxMovementPenalty, GearRules.MovementPenalty(Enumerable.Repeat(jack with { MovementPenalty = 0.4 }, 3), Rules));
    }
}
