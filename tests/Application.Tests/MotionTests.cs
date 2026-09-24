using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The Phase-1 technical audit, M-05 and L-14: the presentation's copy of the player's motion across a respawn, and the body drawn ahead
/// of the tick - through <see cref="PlayerMotion"/>, the code the controller runs, fed by the session's own events.
/// </summary>
public class MotionTests
{
    private const string Ward = "spell.warding.brace_ward";

    private static PlayerRecord Health(PlayerRecord r, int health) =>
        r.WithProgression(r.Progression with { Pools = r.Progression.Pools with { Health = health } });

    private static PlayerMotion Watching(GameSession session)
    {
        var motion = new PlayerMotion(session);
        motion.Resync();
        session.Subscribe<BodyMoved>(motion.OnBodyMoved);
        session.Subscribe<PlayerRespawned>(motion.OnRespawned);
        return motion;
    }

    /// <summary>What the controller does with a wish each frame: send it when it differs from what was sent.</summary>
    private static void Steer(GameSession session, PlayerMotion motion, MoveIntent wish)
    {
        if (motion.Send(wish))
            session.Submit(new MoveCommand(session.Simulation!.PlayerId, wish));
    }

    /// <summary>Walk into the wolves' den with three health, holding the key, until the pack kills the character and they respawn.</summary>
    private static (GameSession Session, PlayerMotion Motion, MoveIntent Held) DieHoldingAKey(TempProfile profile)
    {
        var session = Harness.Playing(profile, (112, 176), 0, r => Health(r, 3));
        var motion = Watching(session);
        var respawned = Harness.Record<PlayerRespawned>(session);
        var held = Harness.Toward(0, 1, Gait.Walk);
        for (int frames = 0; frames < 2_000 && respawned.Count == 0; frames++)
        {
            Steer(session, motion, held);
            session.Frame(session.TickSeconds);
        }
        Assert.Single(respawned);
        return (session, motion, held);
    }

    [Fact]
    public void AKeyHeldThroughARespawn_IsSentAgain_AndTheBodyMovesOnTheNextTick()
    {
        using var profile = new TempProfile();
        var (session, motion, held) = DieHoldingAKey(profile);
        var spawn = session.Simulation!.Player.Body;
        Assert.False(session.Simulation.Player.Intent.IsMoving);   // the respawn stood the body still

        Steer(session, motion, held);   // the same key, still down
        session.Frame(session.TickSeconds);

        var now = session.Simulation.Player.Body;
        Assert.True(session.Simulation.Player.Intent.IsMoving);
        Assert.NotEqual((spawn.XMm, spawn.ZMm), (now.XMm, now.ZMm));
    }

    [Fact]
    public void ADodgeAskedRightAfterARespawn_IsNotDrawnAlongTheJumpToTheWaystone()
    {
        using var profile = new TempProfile();
        var (session, motion, _) = DieHoldingAKey(profile);
        Assert.Equal((0L, 0L), motion.LastStep);   // the jump to the Waystone is not a step

        motion.OnDodgeAsked();
        session.Submit(new DodgeCommand(session.Simulation!.PlayerId, 1000, 0));
        session.Frame(0);   // a frame that runs no tick: the dodge has begun, and no step of it has arrived
        Assert.Equal(CombatPhase.Dodge, session.Simulation.Combat.Phase);
        var drawn = motion.Predict(0.5);
        Assert.Equal((motion.Body.XMm, motion.Body.ZMm), (drawn.XMm, drawn.ZMm));

        // Once its first step arrives, the dodge is drawn carrying on along it, as the simulation moves it.
        session.Frame(session.TickSeconds);
        var ahead = motion.Predict(1.0);
        session.Frame(session.TickSeconds);
        if (session.Simulation.Combat.Phase == CombatPhase.Dodge)
            Assert.Equal((session.Simulation.Player.Body.XMm, session.Simulation.Player.Body.ZMm), (ahead.XMm, ahead.ZMm));
    }

    [Fact]
    public void AWorkingsRecovery_IsDrawnWalking_AsTheSimulationMovesTheBody()
    {
        using var profile = new TempProfile();
        var session = Harness.Playing(profile, (120, 60), 0, r => r.WithProgression(r.Progression with
        {
            Known = r.Progression.Known.SetItem(Ward, new KnownTechnique(LearningSource.Book, "item.tome.resonance_primer", 0)),
            Skills = r.Progression.Skills.SetItem("skill.warding", new SkillState(5, 0)),
        }));
        var motion = Watching(session);
        var walking = Harness.Toward(1, 0, Gait.Walk);
        Steer(session, motion, walking);
        session.Frame(session.TickSeconds);
        session.Submit(new CastCommand(session.Simulation!.PlayerId, Ward));

        int checkedTicks = 0;
        for (int frames = 0; frames < 200 && (checkedTicks == 0 || session.Simulation.Combat.Phase != CombatPhase.Idle); frames++)
        {
            var combat = session.Simulation.Combat;
            bool recovering = combat.Phase == CombatPhase.Recovery && combat.Casting is not null;
            var ahead = motion.Predict(1.0);
            Steer(session, motion, walking);
            session.Frame(session.TickSeconds);
            if (!recovering)
                continue;
            var now = session.Simulation.Player.Body;
            Assert.Equal((now.XMm, now.ZMm), (ahead.XMm, ahead.ZMm));
            checkedTicks++;
        }
        Assert.True(checkedTicks > 0, "the working never recovered");
    }
}
