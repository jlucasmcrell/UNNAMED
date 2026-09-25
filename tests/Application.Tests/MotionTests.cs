using UNNAMED.Domain.Combat;
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

    private static PlayerRecord WithABow(PlayerRecord r)
    {
        var bow = Arena.Stack("item.weapon.hunting_bow", 1);
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Append(bow).Append(Arena.Stack("item.ammo.arrow_rough", 20)),
            r.Progression, r.FacingMdeg, r.Discoveries, new[] { KeyValuePair.Create(Domain.Items.EquipSlot.MainHand, bow.ItemId) }, r.Currency, r.Effects);
    }

    /// <summary>
    /// The Phase-1 technical audit, L-13: a facing was sent only once it had turned more than half a degree, and the reticle traced the
    /// camera's facing, so a bow drawn while the aim crept along loosed down an older line than the reticle showed - some 35 cm off at
    /// 40 m, a wolf's width. While the body faces where the camera looks every turn is sent, so the body keeps up with the aim tick by
    /// tick; and a drawn bow's reticle traces the body's facing, which the release holds, so the shot stops where the reticle stood.
    /// </summary>
    [Fact]
    public void ABowDrawnWhileTheAimCreeps_KeepsUpWithTheAim_AndLoosesWhereTheReticleStood()
    {
        using var profile = new TempProfile();
        var session = Harness.Playing(profile, (70, 152), 90, WithABow);
        var motion = Watching(session);
        var loosed = Harness.Record<ShotLoosed>(session);
        var simulation = session.Simulation!;
        long range = simulation.Combat.Weapon.ReachMm;
        int facing = 90_000;
        session.Submit(new AttackCommand(simulation.PlayerId));
        (long X, long Z) reticle = default;
        for (int frames = 0; frames < 200 && loosed.Count == 0; frames++)
        {
            facing += 250;   // a quarter of a degree a tick
            if (motion.Send(MoveIntent.Idle(facing), precise: true))
                session.Submit(new MoveCommand(simulation.PlayerId, MoveIntent.Idle(facing)));
            var (x, z, _) = simulation.Aim(simulation.Player.Body.FacingMdeg, range);   // the drawn bow's reticle, as the frame draws it
            reticle = (x, z);
            bool drawing = simulation.Combat.Phase == CombatPhase.Windup;
            session.Frame(session.TickSeconds);
            if (drawing && simulation.Combat.Phase == CombatPhase.Windup)
                Assert.Equal(facing, simulation.Player.Body.FacingMdeg);   // the body turned all the way the aim did
        }

        var shot = Assert.Single(loosed);
        Assert.Equal(reticle, (shot.ToXMm, shot.ToZMm));
    }
}
