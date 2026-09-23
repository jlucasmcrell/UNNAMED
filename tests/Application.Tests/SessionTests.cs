using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>The session over the game's own content: boot, the fixed-step loop, and authoritative movement.</summary>
public class SessionTests
{
    [Fact]
    public void Boot_BuildsTheHollow_FromContent()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var layout = session.Setup.Layout;

        Assert.Equal(new[] { "r_0_0:c_00_00", "r_0_0:c_00_01", "r_0_0:c_01_00", "r_0_0:c_01_01" }, layout.CellKeys);
        Assert.Equal(new[] { "door.forge_shed", "door.longhouse" }, layout.Doors.Select(d => d.Key).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(new[] { "location.den_mouth", "location.herb_patch", "location.iron_shelf", "location.outpost" },
            layout.Locations.Select(l => l.Id));
        Assert.Equal((0L, 0L, 200_000L, 200_000L), (layout.Space.MinXMm, layout.Space.MinZMm, layout.Space.MaxXMm, layout.Space.MaxZMm));
        Assert.Equal(50, session.Setup.TickMilliseconds);
        Assert.Equal(new Body(55_000, 1_800, 60_000, 180_000), layout.Spawn);   // on the outpost terrace, facing into the outpost
    }

    [Fact]
    public void ANewGame_StartsAtTheSpawn_AtTickZero()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);

        Assert.Equal(0, simulation.WorldTick);
        Assert.Equal(session.Setup.Layout.Spawn, simulation.Player.Body);
        Assert.Equal(1, simulation.Player.Progression.Level);
        Assert.Equal(42UL, simulation.World.WorldSeed);
    }

    [Fact]
    public void AMoveCommand_MovesTheBody_AtTheConfiguredSpeed_OnTheNextTick()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        var start = simulation.Player.Body;

        session.Submit(new MoveCommand(simulation.PlayerId, new MoveIntent(0, 1000, Gait.Run, 0)));
        Assert.Equal(start, simulation.Player.Body);   // queued, not applied: nothing changes outside the tick

        Harness.Ticks(session, 1);
        // 3.2 m/s for one 50 ms tick is 160 mm; facing comes from the intent.
        Assert.Equal(new Body(start.XMm, start.YMm, start.ZMm + 160, 0), simulation.Player.Body);
    }

    [Theory]
    [InlineData(Gait.Walk, 80)]
    [InlineData(Gait.Run, 160)]
    [InlineData(Gait.Sprint, 256)]
    public void Gaits_ScaleTheBaseSpeed(Gait gait, long millimetresPerTick)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        long z = simulation.Player.Body.ZMm;

        session.Submit(new MoveCommand(simulation.PlayerId, new MoveIntent(0, 1000, gait, 0)));
        Harness.Ticks(session, 1);

        Assert.Equal(z + millimetresPerTick, simulation.Player.Body.ZMm);
    }

    [Fact]
    public void TheFrameLoop_RunsWholeTicks_AndReportsHowFarIntoTheNextOneItIs()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);

        Assert.Equal(0, session.Frame(0.02).TicksRun);
        Assert.Equal(0, session.Frame(0.02).TicksRun);
        var third = session.Frame(0.02);
        Assert.Equal(1, third.TicksRun);
        Assert.Equal(0.2, third.Alpha, precision: 6);
        Assert.Equal(1, simulation.WorldTick);
    }

    [Fact]
    public void ALongStall_IsClamped_NotReplayed()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);

        var result = session.Frame(10.0);

        Assert.Equal(5, result.TicksRun);   // GameSession.MaxFrameSeconds (0.25 s) of 50 ms ticks
        Assert.Equal(5, simulation.WorldTick);
    }

    [Fact]
    public void Movement_StopsAtTheEdgeOfTheHollow()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        // Out of the gate and east of the palisade, then due south until the edge stops the body.
        Assert.True(Harness.WalkPath(session, (55, 80), (120, 80)));

        session.Submit(new MoveCommand(simulation.PlayerId, new MoveIntent(0, -1000, Gait.Sprint, 180_000)));
        Harness.Ticks(session, 600);

        Assert.Equal(session.Setup.Movement.BodyRadiusMm, simulation.Player.Body.ZMm);
    }

    [Fact]
    public void TheBodyFollowsTheTerrain()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);

        Assert.True(Harness.WalkPath(session, (55, 80), (70, 110), (75, 150)));

        var body = simulation.Player.Body;
        Assert.Equal(session.Setup.Layout.Space.Terrain.HeightAtMm(body.XMm, body.ZMm), body.YMm);
        Assert.True(body.YMm > 3_000, $"the rock shelf is high ground; the body is at {body.YMm} mm");
    }

    [Fact]
    public void ARejectedCommand_IsAnEvent_WithAReason_NeverAnException()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        var rejected = Harness.Record<CommandRejected>(session);

        session.Submit(new MoveCommand(simulation.PlayerId, new MoveIntent(5000, 0, Gait.Run, 0)));
        session.Submit(new MoveCommand(simulation.PlayerId, new MoveIntent(0, 0, Gait.Run, 360_000)));
        session.Submit(new MoveCommand(UNNAMED.Domain.EntityId.NewId(UNNAMED.Domain.EntityKind.Character), MoveIntent.Idle(0)));
        Harness.Ticks(session, 1);

        Assert.Equal(3, rejected.Count);
        Assert.Contains("exceeds full deflection", rejected[0].Reason);
        Assert.Contains("facing", rejected[1].Reason);
        Assert.Contains("unknown actor", rejected[2].Reason);
        Assert.All(simulation.CommandLog, entry => Assert.NotNull(entry.RejectedReason));
        Assert.Equal(session.Setup.Layout.Spawn, simulation.Player.Body);
    }

    [Fact]
    public void EveryStateSlice_HasExactlyOneOwningSystem()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);

        Assert.Equal(Enum.GetValues<StateSlice>().OrderBy(s => s), simulation.SliceOwners.Keys.OrderBy(s => s));
        Assert.Equal("MovementSystem", simulation.SliceOwners[StateSlice.PlayerBody]);
        Assert.Equal("WorldFlagSystem", simulation.SliceOwners[StateSlice.WorldFlags]);
    }

    [Fact]
    public void Commands_CarryNoCameraState_SoEveryPerspectiveSubmitsTheSameThing()
    {
        // CAMERA_PERSPECTIVE_AND_PRESENTATION.md §6: perspective changes presentation and input mapping only.
        var commandTypes = typeof(GameCommand).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(GameCommand))).ToList();
        Assert.NotEmpty(commandTypes);
        foreach (var type in commandTypes)
        {
            foreach (var property in type.GetProperties())
            {
                string name = property.Name.ToLowerInvariant();
                Assert.False(name.Contains("camera") || name.Contains("view") || name.Contains("perspective") || name.Contains("zoom"),
                    $"{type.Name}.{property.Name} carries camera state into a command");
            }
        }
    }
}
