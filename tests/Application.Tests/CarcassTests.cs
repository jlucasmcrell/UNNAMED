// UNNAMED Application tests - the carcass a kill leaves (D18 SYSTEMS, needed-2, system 2: carcass and skinning)

using System;
using System.Collections.Immutable;
using System.Linq;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// HOOK.NODE.CARCASS is "a body worth skinning after the kill". The kill-to-carcass wire is what makes that a body and
/// not an authored prop: a creature whose kind names a <c>carcass_ref</c> leaves a harvestable node where it fell, the
/// node trains <c>skill.skinning</c>, and a kind that names none leaves nothing. The world already carries authored
/// carcasses, so each test compares the nodes before and after the fight rather than the whole list.
/// </summary>
public sealed class CarcassTests
{
    private static readonly (double X, double Z) Open = (120, 60);

    private static ImmutableHashSet<string> CarcassKeys(Simulation simulation) =>
        simulation.Nodes.Where(n => n.NodeDefId.StartsWith("node.carcass.", StringComparison.Ordinal))
            .Select(n => n.Key).ToImmutableHashSet();

    [Fact]
    public void TheWolfKind_NamesACarcass_AndTheNodeKindTrainsSkinning()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        Assert.Equal("node.carcass.grey_wolf", session.Setup.Combat.Creatures[Arena.Wolf].CarcassRef);
        Assert.Equal("skill.skinning", session.Setup.Crafting.Nodes["node.carcass.grey_wolf"].SkillId);
        Assert.Equal(2, session.Setup.Crafting.Nodes["node.carcass.grey_wolf"].Charges);
    }

    [Fact]
    public void AKilledWolf_LeavesACarcass_WhereItFell()
    {
        using var profile = new TempProfile();
        var arena = Arena.Open(Harness.Boot(profile), Open, 0, new[] { (Open.X, Open.Z + 2.0) });
        var killed = arena.Record<CreatureKilled>();
        var fell = arena.Creature().Body;
        var before = CarcassKeys(arena.Simulation);

        int ticks = arena.Fight(arena.Creature(), 400);

        var kill = Assert.Single(killed);
        Assert.Equal(Arena.Wolf, kill.DefId);
        Assert.InRange(ticks, 40, 200);

        var fresh = arena.Simulation.Nodes
            .Where(n => n.NodeDefId == "node.carcass.grey_wolf" && !before.Contains(n.Key))
            .ToList();
        Assert.True(fresh.Count == 1,
            $"new carcasses: {fresh.Count}; all carcass keys: {string.Join(",", CarcassKeys(arena.Simulation))}; " +
            $"kind in table: {arena.Simulation.Setup.Crafting.Nodes.ContainsKey("node.carcass.grey_wolf")}; " +
            $"def ref: {arena.Simulation.Setup.Combat.Creatures[Arena.Wolf].CarcassRef}");
        var carcass = fresh[0];
        // A dead creature leaves the world list, so the place it fell is taken before the fight.
        Assert.InRange(Math.Sqrt(Math.Pow(carcass.XMm - fell.XMm, 2) + Math.Pow(carcass.ZMm - fell.ZMm, 2)), 0, 3000);
        Assert.True(carcass.Ready);
        Assert.Equal("item.material.meat_raw", carcass.ItemId);
    }

    private static (Arena Arena, string Carcass) Killed(GameSession session, SimulationSetup setup, long respawnTicks = 0)
    {
        var arena = Arena.OpenCreatures(session, setup, Open, 0, new[] { (Arena.Wolf, Open.X, Open.Z + 2.0, "pack_hunter") }, respawnTicks: respawnTicks);
        var before = CarcassKeys(arena.Simulation);
        arena.Fight(arena.Creature(), 400);
        return (arena, Assert.Single(CarcassKeys(arena.Simulation).Except(before)));
    }

    /// <summary>The carcass lies as long as the corpse does: when the body rots away it goes too, in the running game and after a load.</summary>
    [Fact]
    public void ACarcass_RotsAwayWithItsCorpse_LiveAndAfterALoad()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var setup = session.Setup with { Combat = session.Setup.Combat with { CorpseDecayTicks = 200 } };
        var (arena, carcass) = Killed(session, setup);

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("kill"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        var loaded = Arena.Resume(arena.Simulation.Setup, store.Load(SaveSlots.Manual("kill"), new LoadContext(session.Generator, session.Content, new Registry())));
        Assert.Contains(carcass, CarcassKeys(loaded.Simulation));

        arena.Tick(220);
        loaded.Tick(220);
        Assert.DoesNotContain(carcass, CarcassKeys(arena.Simulation));
        Assert.DoesNotContain(carcass, CarcassKeys(loaded.Simulation));
    }

    /// <summary>A creature that comes back takes its old body with it, and the carcass with the body.</summary>
    [Fact]
    public void ACarcass_GoesWhenItsCreatureReturns()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, carcass) = Killed(session, session.Setup, respawnTicks: 100);
        // Out past the leash, so the spawner is free to bring the wolf back.
        long x = (long)((Open.X + 60 + arena.Origin.X) * 1000), z = (long)((Open.Z + arena.Origin.Z) * 1000);
        Assert.Null(arena.Simulation.Submit(new Relocate(new Body(x, arena.Simulation.Setup.Layout.Space.HeightAtMm(x, z), z, 0))));

        arena.Tick(120);
        Assert.Equal(CreatureCondition.Alive, arena.Creature().Condition);
        Assert.DoesNotContain(carcass, CarcassKeys(arena.Simulation));
    }

    /// <summary>
    /// The whole hide chain in a fresh session: the kill leaves a carcass, its first harvest is the meat and its second the pelt, and the
    /// pelt, carried to the Foldscar's tanning rack, teaches the tanning by experiment and is worked into leather there.
    /// </summary>
    [Fact]
    public void AWolfKill_GivesItsMeat_ThenItsPelt_AndThePeltTansIntoLeather()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, key) = Killed(session, session.Setup);
        arena.Tick(40);   // the last blow's recovery done, so the hands are free for the rack
        void StandAt(long x, long z) =>
            Assert.Null(arena.Simulation.Submit(new Relocate(new Body(x, arena.Simulation.Setup.Layout.Space.HeightAtMm(x, z), z, 0))));
        int Carried(string item) => arena.Simulation.Player.Inventory.Where(e => e.DefId == item).Sum(e => e.Count);

        var carcass = arena.Simulation.Nodes.Single(n => n.Key == key);
        StandAt(carcass.XMm, carcass.ZMm - 500);
        Assert.Null(arena.Submit(new GatherCommand(arena.Player, key)));
        Assert.True(Carried("item.material.meat_raw") >= 1);
        Assert.Equal(0, Carried("item.material.pelt_grey_wolf"));
        Assert.Equal("item.material.pelt_grey_wolf", arena.Simulation.Nodes.Single(n => n.Key == key).ItemId);
        Assert.Null(arena.Submit(new GatherCommand(arena.Player, key)));
        Assert.True(Carried("item.material.pelt_grey_wolf") >= 1);
        Assert.False(arena.Simulation.Nodes.Single(n => n.Key == key).Ready);

        var rack = arena.Simulation.Setup.Layout.Stations.Single(s => s.Key == "station.foldscar_tanning");
        StandAt(rack.XMm, rack.ZMm - 1000);
        Assert.Null(arena.Submit(new ExperimentCommand(arena.Player, rack.Key)));
        Assert.Null(arena.Submit(new CraftCommand(arena.Player, "recipe.tanning.wolf_hide")));
        Assert.Equal(1, Carried("item.material.leather"));
    }

    [Fact]
    public void ACreatureWhoseKindNamesNoCarcass_LeavesNothing()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var wolf = session.Setup.Combat.Creatures[Arena.Wolf] with { CarcassRef = null };
        var setup = session.Setup with
        {
            Combat = session.Setup.Combat with
            {
                Creatures = session.Setup.Combat.Creatures.SetItem(Arena.Wolf, wolf),
            },
        };
        var arena = Arena.OpenCreatures(session, setup, Open, 0,
            new[] { (Arena.Wolf, Open.X, Open.Z + 2.0, "pack_hunter") });
        var before = CarcassKeys(arena.Simulation);

        arena.Fight(arena.Creature(), 400);

        Assert.Equal(before, CarcassKeys(arena.Simulation));
    }
}
