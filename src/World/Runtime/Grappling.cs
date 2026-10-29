// UNNAMED World - the grapple at run time: a line thrown to an anchor, and the pull up it (a timed player activity)
// No Godot references - pure C#

using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

// ── the system ──────────────────────────────────────────────────────────────

/// <summary>
/// Owns: <see cref="StateSlice.Activity"/> with fishing, the pick, the swing, the wash and the read - one body is in one timed thing at a time,
/// and a throw is the sixth of them.
///
/// One loop (SYSTEMS.md, traversal; D21, <c>HOOK.TRAV.GRAPPLE</c>): F throws the line at the anchor the grapple prefixes put above the body
/// (<see cref="GrappleCommand"/>), which plays for <see cref="ThrowTicks"/>; when they run out the pull is made - the body is put on the surface the
/// anchor stands on, on its feet, by <see cref="Relocate"/>, because the body is the movement system's. A straight pull, not a swing: the body
/// arrives at the anchor's base rather than on an arc through the air, and the clip that plays it is the throw's own where there is one.
///
/// The line is not an item: no rope or cord is a thing this build's content has, so the ability rests on the anchor alone for now
/// (<c>ROADMAP\SYSTEMS_NEEDED.md</c> §2, and the item gap on the D19 list).
/// </summary>
internal sealed class GrappleSystem
{
    /// <summary>How long a line takes to go out and the pull to be made: 2 s, in ticks.</summary>
    public const int ThrowTicks = 40;

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;

    public GrappleSystem(SystemContext context, SliceOwner owner, EntityId player)
    {
        _context = context;
        _owner = owner;
        _player = player;
    }

    private RuntimeState State => _context.State;

    private WalkSpace Space => _context.Setup.SpaceFor(State.Space);

    /// <summary>
    /// Throw the line. What the body is doing decides the refusal, in the order the climb refuses it: a dead body throws nothing, the water is no
    /// place to plant a line, a body in the air or in a fight has no hands free, and a body already in a timed thing is already busy. With those
    /// clear, there has to be an anchor in reach - and an anchor whose far side has somewhere to stand, or the pull would be onto nothing.
    /// </summary>
    public string? Handle(GrappleCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.PlayerCombat.Defeated)
            return "dead";
        if (State.Swimming)
            return "the water is no place to throw a line";
        if (State.Posture.Airborne)
            return "in the air";
        var combat = State.PlayerCombat;
        var (phase, _) = combat.Action.PhaseAt(tick, _context.Setup.Combat.Constants);
        if (phase != CombatPhase.Idle || combat.Blocking || _context.ControlOn(_player) != ControlKind.None)
            return "busy";
        if (State.Activity is not null)
            return "You are already doing something";
        if (Kinematics.GrappleAt(State.Body, _context.PlayerMovement, Space) is not { } anchor)
            return "no anchor in reach";
        State.SetActivity(_owner, new PlayerActivity(ActivityKind.GrappleThrow, anchor.Id, tick, tick + ThrowTicks, 0));
        _context.Events.Publish(new GrappleThrown(_player, anchor.Id, tick));
        return null;
    }

    /// <summary>A cancel ends a throw cleanly, like a line coming in: nothing is pulled, and nothing is granted.</summary>
    public string? Handle(CancelActivityCommand command)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.Activity is { Kind: ActivityKind.GrappleThrow })
            State.SetActivity(_owner, null);
        return null;
    }

    public void Tick(long tick)
    {
        if (State.Activity is not { Kind: ActivityKind.GrappleThrow } activity)
            return;
        // The breath, the fight and the place end the throw where they end a line: the water it was thrown from is no place to be pulled from.
        if (State.PlayerCombat.Defeated || Busy(tick) || State.Swimming)
        {
            State.SetActivity(_owner, null);
            return;
        }
        if (tick < activity.UntilTick)
            return;
        State.SetActivity(_owner, null);
        Pull(tick);
    }

    /// <summary>
    /// The throw has run out and the body is hauled in: the anchor is read again where the body now stands - a body that walked out of reach takes
    /// nothing with it, and no pull is made where there is nothing to be pulled to - and the landing it gives is where the body is put. The pull
    /// settles the body on its feet on the surface the anchor stands on, which is the movement system's own act, so it is asked for.
    /// </summary>
    private void Pull(long tick)
    {
        if (Kinematics.GrappleAt(State.Body, _context.PlayerMovement, Space) is not { } anchor)
            return;
        _context.Dispatch(new Relocate(anchor.Landing));
        _context.Events.Publish(new Grappled(_player, anchor.Id, tick));
    }

    /// <summary>Busy with a blow, a dodge, a stagger or a working: nothing time-shaped is started or kept through one.</summary>
    private bool Busy(long tick) =>
        State.PlayerCombat.Action.PhaseAt(tick, _context.Setup.Combat.Constants).Phase != CombatPhase.Idle;
}
