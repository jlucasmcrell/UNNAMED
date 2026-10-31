using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Queue #5 over the game's own content, in a fresh session: reforging a piece up its tiers at its repair station, cutting a working into it at
/// Cairnwold's enchanting bench, the refusals, use drifting a working until it fades, and the work kept through a save and a load wherever the
/// piece lies.
/// </summary>
public class UpgradeEnchantingTests
{
    private const string Sword = "item.weapon.rusted_sword";
    private const string Vest = "item.armor.hide_vest";
    private const string Ingot = "item.material.iron_ingot";
    private const string Fang = "item.trinket.wolf_fang";
    private const string Sac = "item.material.cinder_sac";
    private const string Silk = "item.material.spider_silk";
    private const string Smithing = "skill.smithing";
    private const string Enchanting = "skill.enchanting";

    /// <summary>The region's own metres: the forge anvil at Ashen Hollow, Cairnwold's enchanting bench, and open ground away from both.</summary>
    private static readonly (double X, double Z) AtAnvil = (457.5, 741.3);
    private static readonly (double X, double Z) AtBench = (952.2, 681.0);
    private static readonly (double X, double Z) Away = (520.0, 660.0);
    private const string Chest = "container.waystation_chest";
    private static readonly (double X, double Z) AtChest = (438.0, 737.8);

    private static InventoryEntry Stack(string defId, int count = 1, ItemWear? wear = null, ItemWork? work = null) =>
        new(EntityId.NewId(EntityKind.Item), defId, count) { Wear = wear, Work = work };

    private static Arena At(GameSession session, (double X, double Z) place, IEnumerable<InventoryEntry> extra, params (string Skill, int Level)[] skills) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, Array.Empty<(string, double, double, string)>(), r =>
        {
            var inventory = r.Inventory.Where(e => e.DefId != Sword).Concat(extra).ToList();
            var equipment = r.Equipment.Where(kv => kv.Key != EquipSlot.MainHand && inventory.Any(e => e.ItemId == kv.Value))
                .Concat(inventory.Where(e => e.DefId == Sword).Take(1).Select(e => KeyValuePair.Create(EquipSlot.MainHand, e.ItemId)));
            var progression = skills.Aggregate(r.Progression, (p, s) => p with { Skills = p.Skills.SetItem(s.Skill, new SkillState(s.Level, 0)) });
            return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, progression, r.FacingMdeg, r.Discoveries,
                equipment, 0, r.Effects, r.Relationships, r.Conversations);
        }, origin: (0, 0));

    private static InventoryEntry Carried(Arena arena, string defId) => arena.Simulation.Player.Inventory.First(e => e.DefId == defId);

    private static int Count(Arena arena, string defId) => arena.Simulation.Player.Inventory.Where(e => e.DefId == defId).Sum(e => e.Count);

    [Fact]
    public void TheContent_HasTheBench_TheSkill_AndWorkingsForWeaponsAndArmour()
    {
        using var profile = new TempProfile();
        var setup = Harness.Boot(profile).Setup;
        Assert.Contains(setup.Layout.Stations, s => s.Kind == setup.Crafting.Upgrades.EnchantingStation);
        Assert.Contains(setup.Crafting.Workings.Values, w => w.Target == WorkingTarget.Weapon && w.SkillMin == 0);
        Assert.Contains(setup.Crafting.Workings.Values, w => w.Target == WorkingTarget.Armor && w.SkillMin == 0);
        Assert.All(setup.Crafting.Workings.Values, w => Assert.NotNull(setup.Items.Catalog.Find(w.MaterialId)));
    }

    [Fact]
    public void AtTheAnvil_AReforgeSpendsIngots_RaisesTheTier_AndTheBlowsHitHarder()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtAnvil, new[] { Stack(Sword), Stack(Ingot, 6) }, (Smithing, 25));
        var upgraded = arena.Record<ItemUpgraded>();
        var sword = Carried(arena, Sword).ItemId;
        int before = arena.Simulation.Combat.Weapon.DamageMin;
        int smithing = ProgressionEngine.SkillLevel(arena.Simulation.Player.Progression, Smithing);

        Assert.Null(arena.Submit(new UpgradeCommand(arena.Player, sword)));
        Assert.Equal(new ItemWork(1, null), Carried(arena, Sword).Work);
        Assert.Equal(4, Count(arena, Ingot));
        Assert.Equal(before + 1, arena.Simulation.Combat.Weapon.DamageMin);

        Assert.Null(arena.Submit(new UpgradeCommand(arena.Player, sword)));
        Assert.Equal(new ItemWork(2, null), Carried(arena, Sword).Work);
        Assert.Equal(0, Count(arena, Ingot));
        Assert.Equal(before + 2, arena.Simulation.Combat.Weapon.DamageMin);
        Assert.Equal(new[] { 1, 2 }, upgraded.Select(u => u.Tier));
        Assert.All(upgraded, u => Assert.Equal((sword, "station.forge_anvil"), (u.Item, u.Station)));
        Assert.True(arena.Simulation.Player.Progression.Skills[Smithing].ProgressXp > 0
            || ProgressionEngine.SkillLevel(arena.Simulation.Player.Progression, Smithing) > smithing);   // the work taught the hand

        // The third tier asks smithing 30 and six ingots.
        Assert.Equal($"reforging {Sword} to tier 3 needs {Smithing} 30; you have {ProgressionEngine.SkillLevel(arena.Simulation.Player.Progression, Smithing)}",
            arena.Submit(new UpgradeCommand(arena.Player, sword)));
    }

    [Fact]
    public void AReforge_IsRefused_AwayFromTheStation_WornOut_PastTheTopTier_OrWithoutTheMaterial()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var away = At(session, Away, new[] { Stack(Sword), Stack(Ingot, 6) }, (Smithing, 50));
        Assert.Equal("no anvil in reach", away.Submit(new UpgradeCommand(away.Player, Carried(away, Sword).ItemId)));

        var worn = At(session, AtAnvil, new[] { Stack(Sword, wear: new ItemWear(30, Quality.Standard)), Stack(Ingot, 6) }, (Smithing, 50));
        Assert.Equal($"{Sword} must be repaired before it is reforged", worn.Submit(new UpgradeCommand(worn.Player, Carried(worn, Sword).ItemId)));

        var top = At(session, AtAnvil, new[] { Stack(Sword, work: new ItemWork(3, null)), Stack(Ingot, 10) }, (Smithing, 50));
        Assert.Equal($"{Sword} is already reforged as far as it goes (tier 3)", top.Submit(new UpgradeCommand(top.Player, Carried(top, Sword).ItemId)));

        var poor = At(session, AtAnvil, new[] { Stack(Sword), Stack(Ingot, 1) }, (Smithing, 50));
        Assert.Equal($"needs 2 {Ingot}", poor.Submit(new UpgradeCommand(poor.Player, Carried(poor, Sword).ItemId)));
        Assert.Equal(1, Count(poor, Ingot));   // nothing spent on a refusal
        Assert.Null(Carried(poor, Sword).Work);

        Assert.Equal($"{Ingot} is not a weapon or armour a smith can reforge", poor.Submit(new UpgradeCommand(poor.Player, Carried(poor, Ingot).ItemId)));
    }

    [Fact]
    public void AtTheEnchantingBench_AWorkingIsCut_SpendsItsAnchor_AndTheHandGatesTheStrongerOne()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtBench, new[] { Stack(Sword), Stack(Fang), Stack(Sac, 2), Stack(Vest), Stack(Silk, 2) });
        var inscribed = arena.Record<ItemInscribed>();
        var sword = Carried(arena, Sword).ItemId;
        int before = arena.Simulation.Combat.Weapon.DamageMin;
        string type = arena.Simulation.Combat.Weapon.DamageType;

        Assert.Null(arena.Submit(new InscribeCommand(arena.Player, sword, "working.keen_edge")));
        Assert.Equal(new ItemWork(0, new Inscription("working.keen_edge", 3)), Carried(arena, Sword).Work);
        Assert.Equal(0, Count(arena, Fang));
        Assert.Equal(before + 1, arena.Simulation.Combat.Weapon.DamageMin);

        Assert.Equal($"Ember Anchor needs {Enchanting} 15; you have {ProgressionEngine.SkillLevel(arena.Simulation.Player.Progression, Enchanting)}",
            arena.Submit(new InscribeCommand(arena.Player, sword, "working.ember_anchor")));
        Assert.Equal("Keen Edge is cut into a weapon, not " + Vest, arena.Submit(new InscribeCommand(arena.Player, Carried(arena, Vest).ItemId, "working.keen_edge")));

        Assert.Null(arena.Submit(new InscribeCommand(arena.Player, Carried(arena, Vest).ItemId, "working.warding_weave")));
        Assert.Equal(new ItemWork(0, new Inscription("working.warding_weave", 3)), Carried(arena, Vest).Work);
        Assert.Equal(0, Count(arena, Silk));
        Assert.Equal(new[] { "working.keen_edge", "working.warding_weave" }, inscribed.Select(i => i.WorkingId));
        Assert.All(inscribed, i => Assert.Equal("station.cairnwold.enchanting_bench", i.Station));
        Assert.True(arena.Simulation.Player.Progression.Skills.TryGetValue(Enchanting, out var learned) && (learned.Level > 0 || learned.ProgressXp > 0));

        // A practised enchanter cuts the anchored working over the keen edge: the blow burns, and the old working is cut away.
        var practised = At(session, AtBench, new[] { Stack(Sword, work: new ItemWork(1, new Inscription("working.keen_edge", 2))), Stack(Sac, 2) }, (Enchanting, 30));
        var replaced = practised.Record<ItemInscribed>();
        Assert.Null(practised.Submit(new InscribeCommand(practised.Player, Carried(practised, Sword).ItemId, "working.ember_anchor")));
        Assert.Equal(new ItemWork(1, new Inscription("working.ember_anchor", 5)), Carried(practised, Sword).Work);
        Assert.Equal("working.keen_edge", Assert.Single(replaced).Replaced);
        Assert.Equal("fire", practised.Simulation.Combat.Weapon.DamageType);
        Assert.NotEqual("fire", type);
        Assert.Equal(before + 2, practised.Simulation.Combat.Weapon.DamageMin);   // a tier and the anchor's ember

        var away = At(session, Away, new[] { Stack(Sword), Stack(Fang) });
        Assert.Equal("no enchanting_bench in reach", away.Submit(new InscribeCommand(away.Player, Carried(away, Sword).ItemId, "working.keen_edge")));
        Assert.Equal("working.nothing is not a working", away.Submit(new InscribeCommand(away.Player, Carried(away, Sword).ItemId, "working.nothing")));
    }

    [Fact]
    public void Reforging_TestsTheWorking_AndUseDriftsIt_UntilItFades_LeavingThePieceWhole()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtAnvil, new[] { Stack(Sword, work: new ItemWork(0, new Inscription("working.keen_edge", 1))), Stack(Ingot, 2) }, (Smithing, 20));
        var faded = arena.Record<InscriptionFaded>();
        var sword = Carried(arena, Sword).ItemId;

        Assert.Null(arena.Submit(new UpgradeCommand(arena.Player, sword)));
        Assert.Equal(new ItemWork(1, null), Carried(arena, Sword).Work);
        Assert.Equal(("working.keen_edge", sword), (Assert.Single(faded).WorkingId, faded[0].Item));

        // Nineteen points of wear, a working with one point left: the next blow landed drifts it out.
        int stretch = session.Setup.Crafting.Upgrades.WearPerStability;
        var fight = Arena.OpenCreatures(session, session.Setup, (120, 60), 0, new[] { (Arena.Wolf, 120.0, 62.0, "pack_hunter") }, r =>
        {
            var piece = Stack(Sword, wear: new ItemWear(stretch - 1, Quality.Standard), work: new ItemWork(2, new Inscription("working.keen_edge", 1)));
            var inventory = r.Inventory.Where(e => e.DefId != Sword).Append(piece).ToList();
            return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg, r.Discoveries,
                r.Equipment.Where(kv => kv.Key != EquipSlot.MainHand).Append(KeyValuePair.Create(EquipSlot.MainHand, piece.ItemId)), 0, r.Effects,
                r.Relationships, r.Conversations);
        });
        var drifted = fight.Record<InscriptionFaded>();
        var hits = fight.Record<HitResolved>();
        fight.Fight(fight.Creature(), 600);

        Assert.True(hits.Any(h => h.Attacker == fight.Player && !h.Dodged), "no blow landed");
        var piece = Carried(fight, Sword);
        Assert.Equal(new ItemWork(2, null), piece.Work);   // the tiers are the core's and stay
        Assert.Equal("working.keen_edge", Assert.Single(drifted).WorkingId);
        Assert.Equal(piece.ItemId, fight.Simulation.Player.Equipment[EquipSlot.MainHand]);
    }

    [Fact]
    public void ThePiecesWork_IsKeptThroughASaveAndALoad_CarriedInAChestAndOnTheGround()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var carried = Stack(Sword, work: new ItemWork(2, new Inscription("working.ember_anchor", 4)));
        var stored = Stack(Vest, work: new ItemWork(1, new Inscription("working.warding_weave", 3)));
        var dropped = Stack("item.weapon.march_spear", work: new ItemWork(3, null));
        var arena = At(session, AtChest, new[] { carried, stored, dropped });
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, stored.ItemId.Value, ItemPlace.Carried, ItemPlace.In(Chest), 1)));
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, dropped.ItemId.Value, ItemPlace.Carried, ItemPlace.Ground, 1)));

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Quick, SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content, arena.Simulation.WorldTick, 0));
        var loaded = store.Load(SaveSlots.Quick, new LoadContext(session.Generator, session.Content, new Registry()));

        Assert.True(loaded.IsComplete);
        Assert.Equal(carried.Work, loaded.Player.Inventory.Single(e => e.ItemId == carried.ItemId).Work);
        var snapshot = loaded.World.TakeSnapshot();
        Assert.Equal(stored.Work, snapshot.Containers.Single(c => c.Key == Chest).Items.Single(i => i.ItemId == stored.ItemId).Work);
        Assert.Equal(dropped.Work, snapshot.Created.Single(c => c.InstanceId == dropped.ItemId).Work);

        // Taken back up after the load, the pieces still carry it.
        var resumed = Arena.Resume(arena.Simulation.Setup, loaded);
        Assert.Null(resumed.Submit(new MoveItemCommand(resumed.Player, dropped.ItemId.Value, ItemPlace.Ground, ItemPlace.Carried, 1)));
        Assert.Equal(dropped.Work, resumed.Simulation.Player.Inventory.Single(e => e.ItemId == dropped.ItemId).Work);
    }
}
