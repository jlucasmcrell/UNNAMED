// UNNAMED Application - the presentation's copy of the player's motion, and how it is drawn ahead (D-11; CAMERA_PERSPECTIVE_AND_PRESENTATION.md §6)
// No Godot references - pure C#

using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application;

/// <summary>
/// What presentation remembers of the player's motion between ticks - the intent it last sent, the body as of the last tick and the step
/// that tick took - and the rule that draws the body a fraction of a tick ahead with the simulation's own movement function. A copy of
/// what it was told, never the truth: it writes nothing. Free of Godot, so it is tested (the Phase-1 technical audit, M-05 and L-14).
/// </summary>
public sealed class PlayerMotion
{
    private readonly GameSession _session;

    public PlayerMotion(GameSession session) => _session = session;

    /// <summary>The body as of the last simulated tick.</summary>
    public Body Body { get; private set; } = new(0, 0, 0, 0);

    /// <summary>The movement intent last sent: a wish is sent only when it differs.</summary>
    public MoveIntent Sent { get; private set; }

    /// <summary>How far the last tick moved the body: a dodge is drawn carrying on along it.</summary>
    public (long X, long Z) LastStep { get; private set; }

    /// <summary>A new game or a load: copy the body and the intent the world holds.</summary>
    public void Resync()
    {
        var player = _session.Simulation!.Player;
        Body = player.Body;
        Sent = player.Intent;
        LastStep = (0, 0);
    }

    public void OnBodyMoved(BodyMoved moved)
    {
        LastStep = (moved.To.XMm - moved.From.XMm, moved.To.ZMm - moved.From.ZMm);
        Body = moved.To;
    }

    /// <summary>
    /// A respawn set the body down at the Waystone, standing (M-05): the copy follows, so a key still held is sent again rather than
    /// taken for the intent already sent - and the jump to the Waystone is no step for a dodge to carry on along (L-14).
    /// </summary>
    public void OnRespawned(PlayerRespawned respawned)
    {
        Body = respawned.Body;
        LastStep = (0, 0);
        Sent = MoveIntent.Idle(respawned.Body.FacingMdeg);
    }

    /// <summary>A dodge was asked for: until its first step arrives, the step before it is no guide to where it goes (L-14).</summary>
    public void OnDodgeAsked() => LastStep = (0, 0);

    /// <summary>
    /// Whether a movement wish is worth a command: it differs from the intent last sent in direction or gait, or turns by more than half
    /// a degree - by any amount when <paramref name="precise"/>, while the body faces where the camera looks (a bow drawn, a guard, a
    /// working's tell), so a shot flies where the reticle points (the Phase-1 technical audit, L-13). When it is, it becomes the intent
    /// last sent.
    /// </summary>
    public bool Send(MoveIntent wish, bool precise = false)
    {
        int turn = ((wish.FacingMdeg - Sent.FacingMdeg) % 360_000 + 540_000) % 360_000 - 180_000;
        if (wish.DirXPermille == Sent.DirXPermille && wish.DirZPermille == Sent.DirZPermille && wish.Gait == Sent.Gait
            && Math.Abs(turn) <= (precise ? 0 : 500))
            return false;
        Sent = wish;
        return true;
    }

    /// <summary>
    /// Where to draw the body this frame: the last tick advanced by the frame's fraction of the next one, moving the way the simulation
    /// will (MovementSystem.Tick) - a dodge carries on as it went, a stagger or a dodge's recovery holds still, an attack, a working or a
    /// raised guard walks, and an empty stamina pool runs rather than sprints.
    /// </summary>
    public Body Predict(double alpha)
    {
        var setup = _session.Setup;
        var simulation = _session.Simulation!;
        var combat = simulation.Combat;
        var intent = Sent;
        switch (combat.Phase)
        {
            case CombatPhase.Dodge:
                return Body with { XMm = Body.XMm + (long)(LastStep.X * alpha), ZMm = Body.ZMm + (long)(LastStep.Z * alpha) };
            case CombatPhase.Staggered:
            case CombatPhase.Recovery when combat.AttackSource is null && combat.Casting is null:   // a dodge's: a working's recovery walks
                return Body;
            case CombatPhase.Windup or CombatPhase.Active or CombatPhase.Recovery:
                intent = intent with { Gait = Gait.Walk };
                break;
            default:
                if (combat.Blocking)
                    intent = intent with { Gait = Gait.Walk };
                else if (intent.Gait == Gait.Sprint && combat.Stamina == 0)
                    intent = intent with { Gait = Gait.Run };
                break;
        }
        // With the posture (the owner's M6 playtest): a jump's arc and a crouch's pace are drawn as the next tick will have them.
        return Kinematics.Step(Body, simulation.Posture, intent, setup.Movement, simulation.Space, simulation.DynamicBlockers,
            (int)Math.Round(alpha * setup.TickMilliseconds)).Body;
    }
}
