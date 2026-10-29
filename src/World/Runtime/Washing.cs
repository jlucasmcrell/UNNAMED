// UNNAMED World - washing at a bank at run time: a short rest at the water's edge (a timed player activity)
// No Godot references - pure C#

using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

// ── commands ────────────────────────────────────────────────────────────────

/// <summary>Wash at the bank the character stands at. Refused away from the water, while swimming, and while anything else is running.</summary>
public sealed record WashCommand(EntityId Actor) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

/// <summary>The wash is done: the water it was done at, and the tick it finished - the toast and any sound hang on this.</summary>
public sealed record Washed(string WaterKey,long Tick);

// ── the system ──────────────────────────────────────────────────────────────

/// <summary>
/// Owns: <see cref="StateSlice.Activity"/> with fishing, lockpicking and the swing at a node - one body is in one timed thing at a time,
/// and a wash is the fourth of them. The water is a place with a second use (D20): at a bank, a key press begins a rest of
/// <see cref="WashTicks"/>, and when they run out a little of the body's wind is back, in the pools' own command, and
/// <see cref="Washed"/> says so. It is a rest and not a buff, so it gives back a fifth of the base pool. Wading into the water, moving
/// off the bank, dying, a blow or a working, or a cancel ends it and gives nothing back.
/// </summary>
internal sealed class WashingSystem
{
    /// <summary>How long a wash takes: 1.5 s, in ticks.</summary>
    public const int WashTicks=30;

    /// <summary>What the rest gives back: a fifth of the base stamina pool (config.progression <c>stamina_max</c> base), so a rest, not a cure.</summary>
    public const int StaminaRestored=10;

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;

    public WashingSystem(SystemContext context,SliceOwner owner,EntityId player)
    {
        _context=context;
        _owner=owner;
        _player=player;
    }

    private RuntimeState State => _context.State;
    private WalkSpace Space => _context.Setup.Layout.Space;

    public string? Handle(WashCommand command,long tick)
    {
        if(command.Actor!=_player) return $"unknown actor {command.Actor}";
        if(State.PlayerCombat.Defeated) return "dead";
        if(State.Swimming) return "Not while you are in the water";
        if(State.Activity is not null || Busy(tick)) return "You are already doing something";
        if(!State.Space.IsExterior || Space.WaterAt(State.Body.XMm,State.Body.ZMm) is not { } water) return "Stand at the water's edge";
        State.SetActivity(_owner,new PlayerActivity(ActivityKind.Wash,water.Key,tick,tick+WashTicks,0));
        return null;
    }

    /// <summary>Cancel ends a wash where it stands, and nothing is given back: the same command that brings a line in.</summary>
    public string? Handle(CancelActivityCommand command)
    {
        if(command.Actor!=_player) return $"unknown actor {command.Actor}";
        if(State.Activity is { Kind: ActivityKind.Wash })
            State.SetActivity(_owner,null);
        return null;
    }

    public void Tick(long tick)
    {
        if(State.Activity is not { Kind: ActivityKind.Wash } activity) return;
        // The breath, the bank and the water: either is reason enough for the wash to stop where it is, with nothing given back.
        if(State.PlayerCombat.Defeated || State.Swimming || Busy(tick) || !State.Space.IsExterior
            || Space.WaterAt(State.Body.XMm,State.Body.ZMm) is null)
        {
            State.SetActivity(_owner,null);
            return;
        }
        if(tick<activity.UntilTick) return;
        State.SetActivity(_owner,null);
        _context.Dispatch(new ChangePools(0,StaminaRestored));
        _context.Events.Publish(new Washed(activity.TargetKey,tick));
    }

    /// <summary>Busy with a blow, a dodge, a stagger or a working: nothing time-shaped is started or kept through one.</summary>
    private bool Busy(long tick) =>
        State.PlayerCombat.Action.PhaseAt(tick,_context.Setup.Combat.Constants).Phase!=CombatPhase.Idle;
}
