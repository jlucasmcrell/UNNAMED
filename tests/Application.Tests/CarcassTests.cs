// UNNAMED Application tests - the carcass a kill leaves (D18 SYSTEMS, needed-2, system 2: carcass and skinning)

using System;
using System.Collections.Immutable;
using System.Linq;
using UNNAMED.Domain.Combat;
using UNNAMED.World.Runtime;
using Xunit;

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
    public void AKilledWolf_LeavesACarcass_WhereItFell()
    {
        using var profile = new TempProfile();
        var arena = Arena.Open(Harness.Boot(profile), Open, 0, new[] { (Open.X, Open.Z + 2.0) });
        var killed = arena.Record<CreatureKilled>();
        var before = CarcassKeys(arena.Simulation);

        int ticks = arena.Fight(arena.Creature(), 400);

        var kill = Assert.Single(killed);
        Assert.Equal(Arena.Wolf, kill.DefId);
        Assert.InRange(ticks, 40, 200);

        var fresh = arena.Simulation.Nodes
            .Where(n => n.NodeDefId == "node.carcass.grey_wolf" && !before.Contains(n.Key))
            .ToList();
        var carcass = Assert.Single(fresh);
        var corpse = arena.Simulation.Creatures.Single(c => c.Key == kill.Creature);
        Assert.InRange(Math.Sqrt(Math.Pow(carcass.XMm - corpse.Body.XMm, 2) + Math.Pow(carcass.ZMm - corpse.Body.ZMm, 2)), 0, 500);
        Assert.True(carcass.Ready);
        Assert.Equal("item.material.meat_raw", carcass.ItemId);
        Assert.Equal("skill.skinning", arena.Simulation.Setup.Crafting.Nodes["node.carcass.grey_wolf"].SkillId);
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
