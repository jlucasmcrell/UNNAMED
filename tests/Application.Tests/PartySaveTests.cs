using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP15a's party through a save (schema 21): a hireling's contract and work, every member's stance and standing rules, a companion
/// recovering at home and a hireling who died - saved, loaded, and going on the same in both worlds.
/// </summary>
public class PartySaveTests
{
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Sel = "npc.ashen_hollow.sel_arien";
    private const string Tavar = "npc.ashen_hollow.tavar_orr";

    /// <summary>The setup with these townsfolk hireable in every role, each with a spear.</summary>
    private static SimulationSetup Hireable(SimulationSetup setup, params string[] ids) => Harness.HireableTownsfolk(setup, ids);

    private static object Of(CompanionRecord c) =>
        (c.NpcId, c.Order, c.Condition, c.XMm, c.ZMm, c.Health, c.Membership, c.Stance, c.Tactics, c.Contract, string.Join(";", c.Trail));

    [Fact]
    public void TheParty_RoundTripsThroughASave_AndGoesOnTheSame()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var rules = Hireable(session.Setup, Kera, Renn, Sel);
        var stand = rules.Layout.Nodes.First(n => n.Name == "ash_stand");
        var cell = CellKey.OfWorld(stand.XMm / 1000.0, stand.ZMm / 1000.0);
        var gatherer = new CompanionRecord(Kera, CompanionOrder.Wait, CompanionCondition.Up, Harness.InRegion(179, 137).XMm, Harness.InRegion(179, 137).ZMm, 0, 50)
        {
            Membership = PartyMembership.Hireling,
            Stance = CompanionStance.Defensive,
            Tactics = new PartyTactics(false, true, true),
            Contract = new HirelingContract(HirelingRole.Gatherer, 9, 5, PartyRules.WageOnly) { DaysServed = 2, Purse = 15, WorkSite = Keys.NodeKey(cell, stand.Name, 0) },
        };
        var guard = new CompanionRecord(Renn, CompanionOrder.Follow, CompanionCondition.Downed, Harness.InRegion(61, 141).XMm, Harness.InRegion(61, 141).ZMm, 0, 0)
        {
            DownedTick = 0,
            Membership = PartyMembership.Hireling,
            Contract = new HirelingContract(HirelingRole.Guard, 4, 8, PartyRules.WageOnly) { Purse = 8 },
        };
        var tavar = new CompanionRecord(Tavar, CompanionOrder.Follow, CompanionCondition.Downed, Harness.InRegion(62, 142).XMm, Harness.InRegion(62, 142).ZMm, 0, 0)
        {
            DownedTick = 0,
        };
        var arena = Arena.OpenCreatures(session, rules, (60.3, 140.3), 0, Array.Empty<(string, double, double, string)>(),
            r => new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory, r.Progression, r.FacingMdeg, r.Discoveries, r.Equipment, 40,
                r.Effects, companions: new[] { gatherer, guard, tavar }));
        arena.Tick(session.Setup.Social.Companions!.ReviveWindowTicks + 1);   // the guard dies; Tavar goes home wounded
        var before = arena.Simulation.CaptureRecord();
        Assert.Equal(new[] { Renn }, before.Party.Fallen);
        Assert.Equal(Tavar, Assert.Single(before.Party.Recovering).NpcId);

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("party"), SaveDocuments.Capture(arena.Simulation.World, before, session.Content, arena.Simulation.WorldTick, 0));
        var loaded = Arena.Resume(arena.Simulation.Setup, store.Load(SaveSlots.Manual("party"), new LoadContext(session.Generator, session.Content, new Registry())));

        var after = loaded.Simulation.CaptureRecord();
        Assert.Equal(before.Digest, after.Digest);
        Assert.Equal(Of(Assert.Single(before.Companions)), Of(Assert.Single(after.Companions)));
        Assert.DoesNotContain(loaded.Simulation.Npcs, n => n.Id == Renn);
        Assert.Equal(arena.Simulation.StateDigest(), loaded.Simulation.StateDigest());

        // Both go on the same: two days away, the gatherer working and paid, Tavar well again.
        long day = session.Setup.Clock.TicksPerDay;
        arena.Simulation.Advance(2 * day);
        loaded.Simulation.Advance(2 * day);
        Assert.Equal(Of(arena.Simulation.CaptureRecord().Companions.Single()), Of(loaded.Simulation.CaptureRecord().Companions.Single()));
        Assert.Equal(arena.Simulation.CaptureRecord().Currency, loaded.Simulation.CaptureRecord().Currency);
        Assert.Equal(arena.Simulation.CaptureRecord().Party, loaded.Simulation.CaptureRecord().Party);
    }

    /// <summary>
    /// WP15b's sheets through a save (schema 25): a member's level and skills, a loan and a gift worn, a thing of their own - saved, loaded,
    /// still each item's own identity, and growing the same in both worlds.
    /// </summary>
    [Fact]
    public void ThePartysSheets_RoundTripThroughASave_AndGrowTheSame()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var rules = Hireable(session.Setup, Kera);
        var stand = rules.Layout.Nodes.First(n => n.Name == "ash_stand");
        var cell = CellKey.OfWorld(stand.XMm / 1000.0, stand.ZMm / 1000.0);
        var gatherer = new CompanionRecord(Kera, CompanionOrder.Wait, CompanionCondition.Up, Harness.InRegion(179, 137).XMm, Harness.InRegion(179, 137).ZMm, 0, 60)
        {
            Membership = PartyMembership.Hireling,
            Contract = new HirelingContract(HirelingRole.Gatherer, 9, 5, PartyRules.WageOnly) { WorkSite = Keys.NodeKey(cell, stand.Name, 0) },
        };
        var cap = new InventoryEntry(EntityId.NewId(EntityKind.Item), "item.armor.hide_cap", 1);
        var vest = new InventoryEntry(EntityId.NewId(EntityKind.Item), "item.armor.hide_vest", 1) { Quality = 1 };
        var meat = new InventoryEntry(EntityId.NewId(EntityKind.Item), "item.consumable.roast_meat", 2);
        var sheet = new PartySheet(Kera, Domain.Progression.ProgressionEngine.Create(rules.Progression) with { Level = 2, UnspentAttributePoints = 1 })
        {
            Gear = ImmutableSortedDictionary.Create<Domain.Items.EquipSlot, PartyGear>()
                .Add(Domain.Items.EquipSlot.Head, new PartyGear(cap, Loaned: false)).Add(Domain.Items.EquipSlot.Chest, new PartyGear(vest, Loaned: true)),
            Belongings = ImmutableArray.Create(meat),
        };
        var arena = Arena.OpenCreatures(session, rules, (180, 137), 0, Array.Empty<(string, double, double, string)>(),
            r => new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory, r.Progression, r.FacingMdeg, r.Discoveries, r.Equipment, 40,
                r.Effects, companions: new[] { gatherer }) { PartySheets = ImmutableArray.Create(sheet) });
        var before = arena.Simulation.CaptureRecord();

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("sheets"), SaveDocuments.Capture(arena.Simulation.World, before, session.Content, arena.Simulation.WorldTick, 0));
        var loaded = Arena.Resume(arena.Simulation.Setup, store.Load(SaveSlots.Manual("sheets"), new LoadContext(session.Generator, session.Content, new Registry())));

        var after = loaded.Simulation.CaptureRecord();
        Assert.Equal(before.Digest, after.Digest);
        Assert.Equal(sheet, Assert.Single(after.PartySheets));
        Assert.Equal(arena.Simulation.StateDigest(), loaded.Simulation.StateDigest());
        Assert.Equal(Member(arena.Simulation).Armor, Member(loaded.Simulation).Armor);

        // Both go on the same: three days of work at the stand, her gathering growing.
        long day = session.Setup.Clock.TicksPerDay;
        arena.Simulation.Advance(3 * day);
        loaded.Simulation.Advance(3 * day);
        Assert.Equal(Assert.Single(arena.Simulation.CaptureRecord().PartySheets), Assert.Single(loaded.Simulation.CaptureRecord().PartySheets));
        Assert.NotEmpty(loaded.Simulation.PartySheet(Kera)!.Skills);
    }

    private static CompanionView Member(Simulation simulation) => simulation.Companions.Single(c => c.NpcId == Kera);
}

