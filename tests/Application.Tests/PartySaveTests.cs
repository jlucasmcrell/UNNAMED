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

    private static SimulationSetup Hireable(SimulationSetup setup, params string[] ids) => setup with
    {
        Social = setup.Social with
        {
            Npcs = ids.Aggregate(setup.Social.Npcs, (npcs, id) => npcs.SetItem(id, npcs[id] with
            {
                Hireling = new HirelingProfile(Enum.GetValues<HirelingRole>().ToImmutableArray(),
                    new CompanionProfile(60, "item.weapon.march_spear", ImmutableSortedDictionary<BodyRegion, int>.Empty)),
            })),
        },
    };

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
        var loaded = Arena.Resume(rules, store.Load(SaveSlots.Manual("party"), new LoadContext(session.Generator, session.Content, new Registry())));

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
}
