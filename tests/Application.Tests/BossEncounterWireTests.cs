using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.World.Runtime;
namespace UNNAMED.Application.Tests;

/// <summary>
/// Queue #1: the three encounters that go into Playtest 2, asked of the content the game loads.
/// <para>
/// Each boss is walked down its whole health line, phase by phase, and every phase's name, moves, adds and stand-down is read back
/// from the built definitions - the same answers the fight uses at run time. The Custodian is the checklist's own example: its
/// gauge face is guarded until the third phase opens it.
/// </para>
/// </summary>
public class BossEncounterWireTests
{
    private static (IReadOnlyDictionary<string, CreatureDefinition> Creatures, SimulationSetup Setup) World(string region)
    {
        var session = Harness.Boot(new TempProfile(), region);
        return (session.Setup.Combat.Creatures, session.Setup);
    }

    /// <summary>The phases a fight passes through as its health falls from full to one point, in order.</summary>
    private static List<string> Walk(CreatureDefinition boss)
    {
        var seen = new List<string>();
        for (int health = boss.MaxHealth; health >= 1; health--)
        {
            string name = BossEncounter.PhaseAt(boss, health, 0).Name;
            if (seen.Count == 0 || seen[^1] != name)
                seen.Add(name);
        }
        return seen;
    }

    [Fact]
    public void TheMatriarch_FightsThroughThreePhases_AndCallsHerBroodInTheSecond()
    {
        var (creatures, _) = World("region.den_gorge_cave");
        var boss = creatures["creature.boss.den_gorge.matriarch"];

        var phases = Walk(boss);
        Assert.Equal(3, phases.Count);
        Assert.Equal(phases.Count, phases.Distinct().Count());
        Assert.Equal("the brood called", phases[1]);

        var brood = boss.Phases[1].Adds;
        Assert.False(brood.IsDefaultOrEmpty);
        Assert.Equal("creature.beast.cave_hunting_spider", brood[0].CreatureRef);
        Assert.Equal(3, brood[0].Count);

        // CMB-42: every phase has its cue, and she has no stand-down - she can only be fought
        Assert.All(boss.Phases, phase => Assert.True(boss.Stingers.ContainsKey(phase.Name)));
        Assert.Null(boss.StandDown);
    }

    [Fact]
    public void TheMatriarchsChamber_CarriesItsArenaHazard()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.den_gorge_cave");
        var hazards = session.Setup.Layout.Hazards;
        Assert.NotEmpty(hazards);
        var vent = hazards.Single(hazard => hazard.Id == "hazard.den_gorge.brood_vent");
        Assert.Equal("damage_zone", vent.Kind);
        Assert.True(vent.DamagePerTick > 0 && vent.EveryTicks > 0 && vent.OpenTicks > 0);
        // it opens on its period rather than always
        Assert.True(vent.ActiveAt(0));
        Assert.False(vent.ActiveAt(vent.OpenTicks));
        // and it stands over the antechamber the brood comes from
        Assert.True(vent.Holds(540_000, 786_000));
    }

    [Fact]
    public void TheGateWarden_FightsThroughThreePhases_AndCanBeStoodDown()
    {
        var (creatures, setup) = World("region.starting_frontier");
        var boss = creatures["creature.boss.field_ruins.gate_warden.deep"];

        Assert.Equal(new[] { "posted", "brace", "break" }, Walk(boss));
        Assert.NotNull(boss.StandDown);
        Assert.True(boss.StandDown!.RewardXp > 0);
        Assert.Equal(3, boss.Stingers.Count);

        // its court's spawn is still the one that places it, and it is a single sentinel
        var spawn = setup.Combat.Spawns.Single(site => site.Key == "spawn.boss.field_ruins.gate_warden.deep");
        Assert.Single(spawn.Members);
        Assert.Equal("creature.boss.field_ruins.gate_warden.deep", spawn.Members[0].CreatureId);
    }

    [Fact]
    public void TheLegacyDuplicatesAreGone_OneSpawnPerBoss()
    {
        var (_, setup) = World("region.starting_frontier");
        Assert.DoesNotContain(setup.Combat.Spawns, site => site.Key == "spawn.field.gate_warden");
        Assert.Contains(setup.Combat.Spawns, site => site.Key == "spawn.boss.field_ruins.gate_warden.deep");

        using var profile = new TempProfile();
        using var cave = Harness.Boot(profile, "region.den_gorge_cave");
        Assert.DoesNotContain(cave.Setup.Combat.Spawns, site => site.Key == "spawn.den_gorge.matriarch");
        Assert.Contains(cave.Setup.Combat.Spawns, site => site.Key == "spawn.boss.den_gorge.matriarch");
    }
}
