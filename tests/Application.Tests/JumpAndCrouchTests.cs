using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The owner's M6 playtest: a jump that carries over the hollow's small obstacles and nothing taller, tuned on the region's own geometry
/// (the fallen timber, the smithy fence, the cart, a lodge wall, a closed door, the fold), and a crouch that slows the body and takes it
/// under the Woundmoss beam - where it cannot stand or jump - and that a save keeps.
/// </summary>
public class JumpAndCrouchTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out;

    public JumpAndCrouchTests(Xunit.Abstractions.ITestOutputHelper output) => _out = output;

    private static readonly (string, double, double, string)[] Nobody = Array.Empty<(string, double, double, string)>();

    private static Arena Open(GameSession session, (double X, double Z) at, int facingDeg) =>
        Arena.OpenCreatures(session, session.Setup, at, facingDeg, Nobody);

    /// <summary>Run (or sprint) along +Z or -Z, jumping at one tick; where the body is after the arc has landed and the run gone on.</summary>
    private static double RunAndJump(GameSession session, (double X, double Z) from, int facingDeg, Gait gait, int takeOffTick, int ticks = 50)
    {
        var arena = Open(session, from, facingDeg);
        int dz = facingDeg == 0 ? 1000 : -1000;
        for (int tick = 0; tick < ticks; tick++)
        {
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, new MoveIntent(0, dz, gait, facingDeg * 1000)));
            if (tick == takeOffTick)
                arena.Simulation.Enqueue(new JumpCommand(arena.Player));
            arena.Tick();
        }
        return arena.Simulation.Player.Body.ZMm / 1000.0;
    }

    /// <summary>The take-off ticks, of a run towards a structure, that carry the body past its far face.</summary>
    private static List<int> Clearing(GameSession session, (double X, double Z) from, int facingDeg, Gait gait, double farFaceZ)
    {
        double radius = session.Setup.Movement.BodyRadiusMm / 1000.0;
        return Enumerable.Range(0, 40)
            .Where(t => facingDeg == 0 ? RunAndJump(session, from, facingDeg, gait, t) > farFaceZ + radius : RunAndJump(session, from, facingDeg, gait, t) < farFaceZ - radius)
            .ToList();
    }

    private static void Never(string what, List<int> clearing) =>
        Assert.True(clearing.Count == 0, $"{what} was cleared by take-offs at ticks {string.Join(", ", clearing)}");

    [Fact]
    public void FromARun_AJumpCarriesOverTheFallenTimber_WithATakeOffWindowOfTicks()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        // The timber lies 0.8 m high across x 163-171, z 147-147.8. Run north at it from 4 m short.
        var clearing = Clearing(session, (167, 143), 0, Gait.Run, 147.8);
        _out.WriteLine($"take-offs that clear the timber from a run: ticks {string.Join(", ", clearing)}");

        Assert.True(clearing.Count >= 2, $"only take-offs {string.Join(", ", clearing)} clear it");
        Assert.Equal(Enumerable.Range(clearing[0], clearing.Count), clearing);   // one window, not scattered luck
        // Without a jump the run stops at the timber.
        Assert.True(RunAndJump(session, (167, 143), 0, Gait.Run, takeOffTick: 999) < 147 - 0.34);
    }

    [Fact]
    public void AJump_NeverCarriesOverTheFenceTheCartAWallADoorOrTheFold()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        // The smithy fence: 1.2 m high, x 44-51.4, z 146-146.4 - approached from the north, at a run and at a sprint.
        Never("the fence at a run", Clearing(session, (47.5, 150), 180, Gait.Run, 146));
        Never("the fence at a sprint", Clearing(session, (47.5, 150), 180, Gait.Sprint, 146));
        // The merchant cart: 1.4 m, x 155.5-158.5, z 161.2-163.2 - at a sprint, from the south.
        Never("the cart", Clearing(session, (157, 157), 0, Gait.Sprint, 163.2));
        // The lodge's north wall: 3.2 m, z 131.6-132 - at a sprint, from the north.
        Never("the lodge wall", Clearing(session, (44, 137), 180, Gait.Sprint, 131.6));
        // The smithy's closed door: 2.4 m, on its west wall, x 53-53.4 - run at it along x.
        var door = Open(session, (49, 142), 90);
        for (int tick = 0; tick < 50; tick++)
        {
            door.Simulation.Enqueue(new MoveCommand(door.Player, new MoveIntent(1000, 0, Gait.Sprint, 90_000)));
            if (tick == 8)
                door.Simulation.Enqueue(new JumpCommand(door.Player));
            door.Tick();
        }
        Assert.True(door.Simulation.Player.Body.XMm < 53_000);
        // The fold round Tavar: 2.6 m, a circle of 3 m at (145, 42) - at a sprint, from the south.
        Never("the fold", Clearing(session, (145, 35), 0, Gait.Sprint, 45));
    }

    [Fact]
    public void Airborne_TheBodyCannotJumpAgainCrouchOrDodge_AndItLandsOnItsOwn()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Open(session, (70, 152), 0);

        Assert.Null(arena.Submit(new JumpCommand(arena.Player)));
        arena.Tick(2);
        Assert.True(arena.Simulation.Posture.Airborne);
        Assert.True(arena.Simulation.Player.Body.YMm > session.Setup.Layout.Space.Terrain.HeightAtMm(arena.Simulation.Player.Body.XMm, arena.Simulation.Player.Body.ZMm));
        Assert.Equal("already in the air", arena.Submit(new JumpCommand(arena.Player)));
        Assert.Equal("in the air", arena.Submit(new CrouchCommand(arena.Player, true)));
        Assert.Equal("cannot dodge in the air", arena.Submit(new DodgeCommand(arena.Player, 0, 1000)));

        arena.Tick(session.Setup.Movement.AirtimeMs / session.Setup.TickMilliseconds + 1);
        Assert.Equal(Posture.Grounded, arena.Simulation.Posture);
        var body = arena.Simulation.Player.Body;
        Assert.Equal(session.Setup.Layout.Space.Terrain.HeightAtMm(body.XMm, body.ZMm), body.YMm);
    }

    [Fact]
    public void Crouched_TheBodyGoesAtHalfPace_CannotDodge_AndSpendsNoStaminaAskingToSprint()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Open(session, (70, 152), 90);

        Assert.Null(arena.Submit(new CrouchCommand(arena.Player, true)));
        Assert.Equal(Stance.Crouched, arena.Simulation.Posture.Stance);
        long from = arena.Simulation.Player.Body.XMm;
        for (int tick = 0; tick < 20; tick++)
        {
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, new MoveIntent(1000, 0, Gait.Sprint, 90_000)));
            arena.Tick();
        }
        // A second at 50% of the base speed, whatever the gait asked: 1.6 m.
        Assert.InRange(arena.Simulation.Player.Body.XMm - from, 1_590, 1_610);
        Assert.Equal("cannot dodge while crouched", arena.Submit(new DodgeCommand(arena.Player, 0, 1000)));
        Assert.Null(arena.Simulation.Player.Progression.Pools.Stamina);   // still full: a crouch asking to sprint spends nothing
        Assert.Null(arena.Submit(new CrouchCommand(arena.Player, false)));
        Assert.Equal(Stance.Standing, arena.Simulation.Posture.Stance);
    }

    [Fact]
    public void CrouchedFootfalls_AreAWalks_SoASleeperFiveMetresOffSleepsOn_UntilTheCharacterRuns()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A wolf asleep 5 m north: a walk's footfalls carry 3 m, a run's 8 m (config.creature_behaviour).
        var arena = Arena.OpenCreatures(session, session.Setup, (70, 152), 90, new[] { (Arena.Wolf, 70.0, 157.0, "sleeper") });
        Assert.Null(arena.Submit(new CrouchCommand(arena.Player, true)));
        for (int tick = 0; tick < 60; tick++)
        {
            int dx = tick / 15 % 2 == 0 ? 1000 : -1000;
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, new MoveIntent(dx, 0, Gait.Run, dx > 0 ? 90_000 : 270_000)));
            arena.Tick();
        }
        Assert.True(arena.Creature().Asleep, "a crouched character woke the sleeper");
        Assert.Equal(0, arena.Creature().Awareness);

        Assert.Null(arena.Submit(new CrouchCommand(arena.Player, false)));
        for (int tick = 0; tick < 20; tick++)
        {
            int dx = tick / 10 % 2 == 0 ? 1000 : -1000;
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, new MoveIntent(dx, 0, Gait.Run, dx > 0 ? 90_000 : 270_000)));
            arena.Tick();
        }
        Assert.True(!arena.Creature().Asleep || arena.Creature().Awareness > 0, "a running character did not reach the sleeper's ears");
    }

    [Fact]
    public void TheWoundmossBeam_PassesACrouchedBody_ButNotAStandingOne_AndUnderItThereIsNoRoomToStandOrJump()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        // The beam: x 146-152, z 131.6-132.4, from 1.3 m to 1.7 m up. Standing, the run north stops at it.
        var standing = Open(session, (149, 129.5), 0);
        for (int tick = 0; tick < 40; tick++)
        {
            standing.Simulation.Enqueue(new MoveCommand(standing.Player, new MoveIntent(0, 1000, Gait.Run, 0)));
            standing.Tick();
        }
        Assert.True(standing.Simulation.Player.Body.ZMm < 131_600 - 340);

        // Crouched, it goes under - and halfway through, it cannot stand or jump.
        var crouched = Open(session, (149, 129.5), 0);
        Assert.Null(crouched.Submit(new CrouchCommand(crouched.Player, true)));
        while (crouched.Simulation.Player.Body.ZMm < 132_000)
        {
            crouched.Simulation.Enqueue(new MoveCommand(crouched.Player, new MoveIntent(0, 1000, Gait.Run, 0)));
            crouched.Tick();
        }
        crouched.Simulation.Enqueue(new MoveCommand(crouched.Player, MoveIntent.Idle(0)));
        crouched.Tick();
        Assert.Equal("no room to stand", crouched.Submit(new CrouchCommand(crouched.Player, false)));
        Assert.Equal("no room to stand", crouched.Submit(new JumpCommand(crouched.Player)));
        Assert.Equal(Stance.Crouched, crouched.Simulation.Posture.Stance);
        for (int tick = 0; tick < 30; tick++)
        {
            crouched.Simulation.Enqueue(new MoveCommand(crouched.Player, new MoveIntent(0, 1000, Gait.Run, 0)));
            crouched.Tick();
        }
        Assert.True(crouched.Simulation.Player.Body.ZMm > 132_400 + 350);
        Assert.Null(crouched.Submit(new CrouchCommand(crouched.Player, false)));
    }

    [Fact]
    public void ASaveMidJump_AndOneCrouchedUnderTheBeam_LoadAsTheyWere_AndGoOnTheSame()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var store = new SaveStore(profile.Root);

        foreach (bool underTheBeam in new[] { false, true })
        {
            var arena = Open(session, underTheBeam ? (149, 132.0) : (70, 152), 0);
            if (underTheBeam)
                Assert.Null(arena.Submit(new CrouchCommand(arena.Player, true)));
            else
                Assert.Null(arena.Submit(new JumpCommand(arena.Player)));
            arena.Tick(5);
            var before = arena.Simulation.Posture;

            store.Save(SaveSlots.Manual("posture"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
                arena.Simulation.WorldTick, 0));
            var loaded = Arena.Resume(arena.Simulation.Setup,
                store.Load(SaveSlots.Manual("posture"), new LoadContext(session.Generator, session.Content, new Registry())));

            Assert.Equal(before, loaded.Simulation.Posture);
            Assert.Equal(arena.Simulation.StateDigest(), loaded.Simulation.StateDigest());
            foreach (var world in new[] { arena, loaded })
            {
                world.Simulation.Enqueue(new MoveCommand(world.Player, new MoveIntent(0, 1000, Gait.Run, 0)));
                world.Tick(30);
            }
            Assert.Equal(arena.Simulation.StateDigest(), loaded.Simulation.StateDigest());
        }
    }
}
