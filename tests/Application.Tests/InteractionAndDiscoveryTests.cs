using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>Doors (interaction against the body, world flags in the cell delta) and discovery credit.</summary>
public class InteractionAndDiscoveryTests
{
    private static readonly CellKey Outpost = CellKey.Parse("r_0_0:c_00_01");
    private const string LonghouseFlag = "world.hollow.longhouse_door_open";

    /// <summary>Stand just outside the longhouse door, on its east side.</summary>
    private static Simulation AtTheLonghouseDoor(GameSession session)
    {
        var simulation = session.NewGame("Wanderer", seed: 42);
        Assert.True(Harness.WalkPath(session, (44, 138), (54.5, 134), (53, 128)));
        return simulation;
    }

    [Fact]
    public void TheLonghouseDoor_OpensFromWithinReach_AndItsStateIsAWorldFlagInItsCell()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = AtTheLonghouseDoor(session);
        var toggled = Harness.Record<DoorToggled>(session);
        var flags = Harness.Record<WorldFlagChanged>(session);

        session.Submit(new InteractCommand(simulation.PlayerId, "door.longhouse"));
        Harness.Ticks(session, 1);

        Assert.True(Assert.Single(toggled).Open);
        Assert.Equal(("r_0_0:c_00_01", LonghouseFlag, 0L, 1L), (flags[0].CellKey, flags[0].FlagId, flags[0].From, flags[0].To));
        Assert.Equal(1, simulation.World.GetFlag(Outpost, LonghouseFlag));
        Assert.True(simulation.Doors.Single(d => d.Site.Key == "door.longhouse").Open);
    }

    [Fact]
    public void ADoorOutOfReach_IsRejected_MeasuredFromTheBody()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        var rejected = Harness.Record<CommandRejected>(session);

        session.Submit(new InteractCommand(simulation.PlayerId, "door.longhouse"));
        Harness.Ticks(session, 1);

        Assert.Contains("reach is 1.60 m", Assert.Single(rejected).Reason);
        Assert.Equal(0, simulation.World.GetFlag(Outpost, LonghouseFlag));
    }

    [Fact]
    public void AClosedDoorBlocks_AndAnOpenDoorLetsTheBodyIn()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = AtTheLonghouseDoor(session);

        Assert.False(Harness.WalkTo(session, 44_000, 128_000, maxTicks: 200));
        Assert.True(simulation.Player.Body.XMm >= 52_000 + session.Setup.Movement.BodyRadiusMm, "the closed door let the body through");

        session.Submit(new InteractCommand(simulation.PlayerId, "door.longhouse"));
        Assert.True(Harness.WalkTo(session, 44_000, 128_000));
        Assert.True(simulation.Player.Body.XMm < 51_600, "the body is inside the longhouse");
    }

    [Fact]
    public void ADoorWillNotCloseOnTheBody()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = AtTheLonghouseDoor(session);
        session.Submit(new InteractCommand(simulation.PlayerId, "door.longhouse"));
        Assert.True(Harness.WalkTo(session, 51_800, 128_000, toleranceMm: 50));
        var rejected = Harness.Record<CommandRejected>(session);

        session.Submit(new InteractCommand(simulation.PlayerId, "door.longhouse"));
        Harness.Ticks(session, 1);

        Assert.Contains("doorway", Assert.Single(rejected).Reason);
        Assert.Equal(1, simulation.World.GetFlag(Outpost, LonghouseFlag));
    }

    [Fact]
    public void AnUnknownTarget_IsRejected()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        var rejected = Harness.Record<CommandRejected>(session);

        session.Submit(new InteractCommand(simulation.PlayerId, "door.nowhere"));
        Harness.Ticks(session, 1);

        Assert.Contains("nothing to interact with", Assert.Single(rejected).Reason);
    }

    [Fact]
    public void TheOutpost_IsDiscoveredOnTheFirstTick_ForNoXp()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        var discovered = Harness.Record<LocationDiscovered>(session);
        var xp = Harness.Record<ExperienceGained>(session);

        Harness.Ticks(session, 1);

        Assert.Equal("location.outpost", Assert.Single(discovered).LocationId);
        Assert.Empty(xp);
        Assert.Equal(new DiscoveryRecord("location.outpost", DiscoveryMethod.Visited, 1), Assert.Single(simulation.Player.Discoveries));
    }

    [Fact]
    public void WalkingIntoTheDenMouth_DiscoversItOnce_AndEarnsDiscoveryXp()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        var discovered = Harness.Record<LocationDiscovered>(session);
        var xp = Harness.Record<ExperienceGained>(session);

        Assert.True(Harness.WalkPath(session, (60, 156), (90, 165), (100, 168), (110, 168), (133, 168)));
        Assert.True(Harness.WalkPath(session, (120, 160), (133, 168)));   // out of the radius and back in

        var den = Assert.Single(discovered, d => d.LocationId == "location.den_mouth");
        Assert.Equal(DiscoveryMethod.Visited, den.Method);
        var award = Assert.Single(xp, e => e.Tick == den.Tick);
        Assert.Equal((XpSource.Discovery, 25L), (award.Source, award.Awarded));
        Assert.Contains(simulation.Player.Discoveries, d => d.LocationId == "location.den_mouth" && d.Tick == den.Tick);
        Assert.Equal(simulation.Player.Discoveries.Length, discovered.Count);
    }

    [Fact]
    public void Tiers_AreStepwise_AndDiscoveryWaitsForTierA()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var bus = new EventBus();
        var tierChanges = new List<CellTierChanged>();
        var discoveries = new List<LocationDiscovered>();
        bus.Subscribe<CellTierChanged>(tierChanges.Add);
        bus.Subscribe<LocationDiscovered>(discoveries.Add);
        // A 20 m full-simulation radius, so the den's cell starts far outside tier A.
        var setup = session.Setup with { Tiers = new TierRules(20_000, 60_000, 150_000, 2_000) };
        var player = Simulation.NewCharacter(setup, UNNAMED.Domain.EntityId.NewId(UNNAMED.Domain.EntityKind.Character), "Wanderer", 7);
        var simulation = Simulation.Start(setup, player, new WorldDelta(session.Generator, 42, new UNNAMED.EntityRegistry.EntityRegistry()), 0, bus);
        const string denCell = "r_0_0:c_01_01";
        Assert.NotEqual(SimulationTier.A, simulation.CellTiers[denCell]);

        foreach (var (x, z) in new[] { (60.0, 156.0), (90.0, 165.0), (100.0, 168.0), (110.0, 168.0), (133.0, 168.0) })
        {
            for (int i = 0; i < 2000; i++)
            {
                var body = simulation.Player.Body;
                double dx = x * 1000 - body.XMm, dz = z * 1000 - body.ZMm;
                if (Math.Sqrt(dx * dx + dz * dz) < 300)
                    break;
                simulation.Enqueue(new MoveCommand(simulation.PlayerId, Harness.Toward(dx, dz)));
                simulation.DrainCommands();
                simulation.Step();
            }
        }

        var den = tierChanges.Where(c => c.CellKey == denCell).ToList();
        Assert.All(den, c => Assert.Equal(1, Math.Abs((int)c.To - (int)c.From)));   // one step at a time, never A <-> D
        Assert.Equal(SimulationTier.A, den[^1].To);
        long denReachedA = den.Last(c => c.To == SimulationTier.A).Tick;
        Assert.True(discoveries.Single(d => d.LocationId == "location.den_mouth").Tick >= denReachedA);
    }
}
