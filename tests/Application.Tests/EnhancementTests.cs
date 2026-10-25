using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Spec 47 over the game's own content: infusions set at the forge and the bench, runes at the enchanting table, cracks and reforging, the
/// worn effects and procs combat reads, the owner's visual ruling, armour's mobility and noise, the equip effect of a ring, and salvage
/// getting an enhancement back out whole or as its essence (ITM-19, 20, 64..69).
/// </summary>
public class EnhancementTests
{
    private const string Sword = "item.weapon.rusted_sword";
    private const string Cinder = "item.material.cinder_sac";
    private const string Fang = "item.trinket.wolf_fang";
    private const string Keen = "item.rune.keen";
    private const string Warding = "item.rune.warding";
    private static readonly (double X, double Z) AtHearth = (59.5, 143);
    private static readonly (double X, double Z) AtBench = (37.5, 143.5);
    private static readonly (double X, double Z) Open = (120, 60);

    /// <summary>The same piece's identity in every arena, so the same state rolls the same: what a reload would see.</summary>
    private static readonly EntityId Blade = EntityId.Create(EntityKind.Item, 1_700_000_000_101, new byte[] { 7, 7, 7, 7, 7, 7, 7, 7, 7, 1 });

    private static InventoryEntry Stack(string defId, int count = 1, int quality = Quality.Standard) =>
        new(EntityId.NewId(EntityKind.Item), defId, count) { Quality = quality };

    private static PlayerRecord With(PlayerRecord r, IEnumerable<InventoryEntry> extra, params (string Skill, int Level)[] skills) =>
        new(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(extra),
            r.Progression with { Skills = skills.Aggregate(r.Progression.Skills, (s, k) => s.SetItem(k.Skill, new SkillState(k.Level, 0))) },
            r.FacingMdeg, r.Discoveries, r.Equipment, r.Currency, r.Effects);

    /// <summary>The game's rules, with every setting succeeding (100) or failing (0), and failures always cracking.</summary>
    private static SimulationSetup Certain(GameSession session, int percent, Func<SimulationSetup, SimulationSetup>? more = null)
    {
        var rules = session.Setup.Items.Inventory.Enhancement with { MinPercent = percent, MaxPercent = percent, CrackPercent = 100 };
        var setup = session.Setup with { Items = session.Setup.Items with { Inventory = session.Setup.Items.Inventory with { Enhancement = rules } } };
        return more?.Invoke(setup) ?? setup;
    }

    /// <summary>An enchanting table at the hollow's bench (the region places none yet: the coordinator's to place).</summary>
    private static SimulationSetup WithTable(SimulationSetup setup)
    {
        var bench = setup.Layout.Stations.First(s => s.Kind == "workbench");
        return setup with { Layout = setup.Layout with { Stations = setup.Layout.Stations.Add(new StationSite("station.test.enchanting_table", "enchanting_table", bench.XMm, bench.ZMm)) } };
    }

    private static Arena At(GameSession session, SimulationSetup setup, (double X, double Z) place, Func<PlayerRecord, PlayerRecord> change) =>
        Arena.OpenCreatures(session, setup, place, 0, Array.Empty<(string, double, double, string)>(), change);

    private static InventoryEntry Carried(Arena arena, EntityId id) => arena.Simulation.Player.Inventory.Single(e => e.ItemId == id);

    [Fact]
    public void AnInfusion_IsSetAtTheForge_SpendingItsMaterial_AndFillsASlot()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var blade = new InventoryEntry(Blade, Sword, 1);
        var arena = At(session, Certain(session, 100), AtHearth, r => With(r, new[] { blade, Stack(Cinder, 2) }));
        var events = arena.Record<ItemEnhanced>();
        Assert.Equal((1, 1), (arena.Simulation.Enhancement(Blade)!.Slots, arena.Simulation.Enhancement(Blade)!.Free));
        var preview = arena.Simulation.PreviewEnhance(new EnhanceCommand(arena.Player, Blade, Cinder));
        Assert.Equal((null, "forge", "skill.smithing"), (preview.Refusal, preview.Station, preview.Skill));

        Assert.Null(arena.Submit(new EnhanceCommand(arena.Player, Blade, Cinder)));

        Assert.Equal(new[] { Cinder }, Carried(arena, Blade).Enhancements!.Filled);
        Assert.Equal(1, arena.Simulation.Player.Inventory.Where(e => e.DefId == Cinder).Sum(e => e.Count));
        Assert.Equal(SettingOutcome.Set, Assert.Single(events).Outcome);
        Assert.Equal($"{Sword} has no free slot", arena.Submit(new EnhanceCommand(arena.Player, Blade, Cinder)));
    }

    [Fact]
    public void ASetting_IsRefused_AtTheWrongBench_OnTheWrongPiece_OrWithoutItsMaterial()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var blade = new InventoryEntry(Blade, Sword, 1);
        var vest = Stack("item.armor.hide_vest");
        var bench = At(session, session.Setup, AtBench, r => With(r, new[] { blade, vest, Stack(Cinder), Stack(Warding) }));
        Assert.Equal("no forge in reach", bench.Submit(new EnhanceCommand(bench.Player, Blade, Cinder)));
        Assert.Equal($"{Cinder} goes into weapon, not armour", bench.Submit(new EnhanceCommand(bench.Player, vest.ItemId, Cinder)));
        Assert.Equal("no enchanting_table in reach", bench.Submit(new EnhanceCommand(bench.Player, vest.ItemId, Warding)));
        Assert.Equal($"needs 1 {Fang}", bench.Submit(new EnhanceCommand(bench.Player, Blade, Fang)));
        Assert.Equal("item.material.timber is not an infusion or a rune", bench.Submit(new EnhanceCommand(bench.Player, Blade, "item.material.timber")));
        var kept = bench.Simulation.Player.Inventory.FirstOrDefault(e => e.DefId == "item.heritage.kept_roll");
        Assert.Null(bench.Simulation.Enhancement(vest.ItemId)!.Filled.FirstOrDefault());
        Assert.True(kept is null || bench.Simulation.Enhancement(kept.ItemId) is null);
    }

    [Fact]
    public void AFailedSetting_SpendsItsMaterial_AndCanCrackTheSlot()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var blade = new InventoryEntry(Blade, Sword, 1) { Quality = Quality.Fine };   // two slots
        var arena = At(session, Certain(session, 0), AtHearth, r => With(r, new[] { blade, Stack(Cinder, 3) }));

        Assert.Null(arena.Submit(new EnhanceCommand(arena.Player, Blade, Cinder)));

        var slots = arena.Simulation.Enhancement(Blade)!;
        Assert.Equal((2, 1, 1), (slots.Slots, slots.Cracked, slots.Free));
        Assert.Empty(slots.Filled);
        Assert.Equal(2, arena.Simulation.Player.Inventory.Where(e => e.DefId == Cinder).Sum(e => e.Count));
    }

    [Fact]
    public void TheSamePiece_InTheSameState_RollsTheSame_SoAReloadCannotRollAgain()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        SettingOutcome Try()
        {
            var arena = At(session, session.Setup, AtHearth, r => With(r, new[] { new InventoryEntry(Blade, Sword, 1), Stack(Cinder) }));
            var events = arena.Record<ItemEnhanced>();
            Assert.Null(arena.Submit(new EnhanceCommand(arena.Player, Blade, Cinder)));
            return Assert.Single(events).Outcome;
        }
        Assert.Equal(Try(), Try());
    }

    [Fact]
    public void ARuneAtTheEnchantingTable_TrainsEnchanting_AndAReforgeReplacesIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var vest = Stack("item.armor.hide_vest");
        var arena = At(session, Certain(session, 100, WithTable), AtBench, r => With(r, new[] { vest, Stack(Warding), Stack("item.rune.steadfast") }));
        var practice = arena.Record<SkillPracticed>();

        Assert.Null(arena.Submit(new EnhanceCommand(arena.Player, vest.ItemId, Warding)));
        Assert.Contains(practice, p => p.SkillId == "skill.enchanting");
        Assert.Null(arena.Submit(new EnhanceCommand(arena.Player, vest.ItemId, "item.rune.steadfast", Replace: 0)));

        Assert.Equal(new[] { "item.rune.steadfast" }, Carried(arena, vest.ItemId).Enhancements!.Filled);
        Assert.DoesNotContain(arena.Simulation.Player.Inventory, e => e.DefId is Warding or "item.rune.steadfast");
    }

    [Fact]
    public void WornEnhancements_AndARingsEquipEffect_AreCarriedWhileEquipped_AndShowOnlyWhereTheRulingSays()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var ring = Stack("item.trinket.quartz_ring");
        var arena = At(session, Certain(session, 100, WithTable), AtBench, r =>
        {
            var sword = r.Equipment[EquipSlot.MainHand];
            return With(r.WithInventory(r.Inventory.Select(e => e.ItemId == sword
                ? e with { Enhancements = new ItemEnhancements(ImmutableArray.Create(Keen, "item.rune.ember_touched"), 0, 2) } : e)), new[] { ring });
        });
        Assert.Contains(arena.Simulation.Combat.Effects, e => e.EffectId == "effect.gear_keen" && e.ExpiresTick == long.MaxValue);
        Assert.Equal(new[] { new EnhancementVisualHook(EquipSlot.MainHand, EnhancementVisual.Item, "ember") }, arena.Simulation.EnhancementVisuals);
        Assert.DoesNotContain(arena.Simulation.Combat.Effects, e => e.EffectId == "effect.gear_quartz");

        Assert.Null(arena.Submit(new EquipCommand(arena.Player, ring.ItemId)));
        Assert.Contains(arena.Simulation.Combat.Effects, e => e.EffectId == "effect.gear_quartz");
        Assert.Null(arena.Submit(new UnequipCommand(arena.Player, EquipSlot.Ring)));
        Assert.DoesNotContain(arena.Simulation.Combat.Effects, e => e.EffectId == "effect.gear_quartz");
        arena.Tick(400);   // worn effects never run out, and never tick
        Assert.Contains(arena.Simulation.Combat.Effects, e => e.EffectId == "effect.gear_keen");
    }

    [Fact]
    public void AnInfusedWeapon_PutsItsEffectOnWhatItWounds()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The fang at every wound, so one fight shows it: content's own chance is 15%.
        var catalog = session.Setup.Items.Catalog;
        var fang = catalog.Get(Fang) with { Enhancement = catalog.Get(Fang).Enhancement! with { OnHitPercent = 100 } };
        var setup = session.Setup with { Items = session.Setup.Items with { Catalog = new ItemCatalog(catalog.Definitions.Values.Where(d => d.Id != Fang).Append(fang)) } };
        var arena = Arena.OpenCreatures(session, setup, Open, 0, new[] { (Arena.Wolf, Open.X, Open.Z + 2.0, "pack_hunter") }, r =>
        {
            var sword = r.Equipment[EquipSlot.MainHand];
            return r.WithInventory(r.Inventory.Select(e => e.ItemId == sword ? e with { Enhancements = new ItemEnhancements(ImmutableArray.Create(Fang), 0, 1) } : e));
        });
        var applied = arena.Record<EffectApplied>();

        arena.Fight(arena.Creature(), 400);

        Assert.Contains(applied, a => a.EffectId == "effect.bleeding" && a.Target != arena.Player);
    }

    [Fact]
    public void HeavyArmour_SlowsTheWearer_AndMakesEveryStepLouder()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A wolf ten metres off, facing away: a run carries 8 m bare, 11.6 m in the lamework jack and sabatons (ATD §1).
        bool Heard(bool armoured)
        {
            var jack = Stack("item.armor.lamework_jack");
            var sabatons = Stack("item.armor.lame_sabatons");
            var arena = Arena.OpenCreatures(session, session.Setup, Open, 180, new[] { (Arena.Wolf, Open.X, Open.Z + 10.0, "pack_hunter") }, r =>
            {
                var worn = With(r, new[] { jack, sabatons });
                return armoured ? new PlayerRecord(worn.Id, worn.Name, worn.XMm, worn.YMm, worn.ZMm, worn.AppearanceSeed, worn.Inventory, worn.Progression,
                    worn.FacingMdeg, worn.Discoveries, worn.Equipment.Add(EquipSlot.Chest, jack.ItemId).Add(EquipSlot.Feet, sabatons.ItemId), worn.Currency, worn.Effects) : worn;
            });
            if (armoured)
                Assert.Equal((long)Math.Round(session.Setup.Movement.BaseSpeedMmPerSecond * (1 - 0.08)), arena.Simulation.PlayerMovement.BaseSpeedMmPerSecond);
            else
                Assert.Equal(session.Setup.Movement.BaseSpeedMmPerSecond, arena.Simulation.PlayerMovement.BaseSpeedMmPerSecond);
            var noticed = arena.Record<CreatureNoticed>();
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, new MoveIntent(0, -1000, Gait.Run, 180_000)));
            arena.Tick(2);
            return noticed.Count > 0;
        }
        Assert.False(Heard(armoured: false));
        Assert.True(Heard(armoured: true));
    }

    [Fact]
    public void Salvage_RecoversAnEnhancementWhole_WithTheSkill_AndOnlyItsEssenceWithout()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        ImmutableArray<(string Enhancement, string? Returns)> Salvaged(int enchanting)
        {
            var gloves = Stack("item.armor.smith_gloves") with { Enhancements = new ItemEnhancements(ImmutableArray.Create(Warding), 0, 1) };
            var arena = At(session, session.Setup, (28, 44.1), r => With(r, new[] { gloves }, ("skill.enchanting", enchanting)));
            var preview = arena.Simulation.Salvage(gloves.ItemId);
            Assert.Null(preview.Refusal);
            Assert.Null(arena.Submit(new SalvageCommand(arena.Player, gloves.ItemId)));
            if (preview.Enhancements.Single().Returns is { } back)
                Assert.Contains(arena.Simulation.Player.Inventory, e => e.DefId == back);
            return preview.Enhancements;
        }
        Assert.Equal(new[] { (Warding, (string?)Warding) }, Salvaged(40));   // tier 2: whole from skill 10, certain by 20
        Assert.Equal(new[] { (Warding, (string?)"item.essence.resonant") }, Salvaged(0));
    }

    [Fact]
    public void Heritage_IsNeverSalvaged_NorEnhanced()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var roll = Stack("item.heritage.kept_roll");
        var arena = At(session, session.Setup, (28, 44.1), r => With(r, new[] { roll }));
        Assert.Equal("Heritage is never broken down.", arena.Simulation.Salvage(roll.ItemId).Refusal);
        Assert.NotNull(arena.Submit(new SalvageCommand(arena.Player, roll.ItemId)));
        Assert.Contains(arena.Simulation.Player.Inventory, e => e.ItemId == roll.ItemId);
        Assert.Null(SalvageRules.Plan(session.Setup.Items.Catalog.Get("item.heritage.kept_roll"), session.Setup.Crafting.Recipes.Values, session.Setup.Items.Catalog));
        Assert.Null(arena.Simulation.Enhancement(roll.ItemId));
        Assert.False(EnhancementRulesEngine.Enhanceable(session.Setup.Items.Catalog.Get("item.heritage.kept_roll")));
    }
}
