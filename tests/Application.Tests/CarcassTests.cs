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

    [Fact]
    public void EveryFaunaSpecies_LeavesTheBodyItsKindNames()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The fauna this branch carries: each kind names a carcass, and each carcass is a two-charge node that trains skill.skinning -
        // the hunting-to-skinning wire, asserted for the animals a hunter actually meets on the frontier.
        foreach (var (kind, carcass) in new[]
                 {
                     ("creature.beast.doe", "node.carcass.deer"), ("creature.beast.buck", "node.carcass.deer"),
                     ("creature.beast.elk", "node.carcass.elk"), ("creature.beast.moose", "node.carcass.moose"),
                     ("creature.beast.rabbit", "node.carcass.rabbit"), ("creature.beast.red_fox", "node.carcass.red_fox"),
                     ("creature.beast.duck", "node.carcass.duck"), ("creature.beast.crow", "node.carcass.crow"),
                     ("creature.fish.trout", "node.carcass.trout"),
                 })
        {
            Assert.Equal(carcass, session.Setup.Combat.Creatures[kind].CarcassRef);
            Assert.Equal("skill.skinning", session.Setup.Crafting.Nodes[carcass].SkillId);
            Assert.Equal(2, session.Setup.Crafting.Nodes[carcass].Charges);
        }
    }
}
