// UNNAMED World - the swing at a node at run time: the pick going in, and the strike that fills it (a timed player activity)
// No Godot references - pure C#

using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Progression;

namespace UNNAMED.World.Runtime;

// ── commands ────────────────────────────────────────────────────────────────

/// <summary>
/// Swing at a node within reach: the pick goes in for its ticks, and a node whose content asks for more than one swing keeps it going
/// until it gives. Refused where there is no node, out of reach, dead, at a node with no charges left, and while anything else is running.
/// </summary>
public sealed record SwingAtNodeCommand(EntityId Actor,string NodeKey) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

/// <summary>The pick is going in: the swing plays for its ticks, and the node is struck when they run out.</summary>
public sealed record SwingStarted(string NodeKey,string NodeDefId,long Tick);

/// <summary>
/// A swing landed and the node held: <see cref="StrikesLeft"/> more of it before it gives. Nothing has been taken - the yield is the last
/// swing's, and <see cref="NodeGathered"/> tells it.
/// </summary>
public sealed record NodeStruck(string NodeKey,string NodeDefId,int StrikesLeft,long Tick);

/// <summary>The swing that filled the node had the yield refused - a pack with no room in it - in the gather's own words.</summary>
public sealed record NodeRefused(string NodeKey,string Reason,long Tick);

// ── the system ──────────────────────────────────────────────────────────────

/// <summary>
/// Owns: <see cref="StateSlice.Activity"/> with fishing and lockpicking - one body is in one timed thing at a time, and a swing is the
/// third of them. One loop (SYSTEMS.md, gathering): E at a node starts the swing, which holds for <see cref="SwingTicks"/>; when they run
/// out the node is struck, and a node that takes more than one strike renews the swing, so the pick goes on until the node gives. The
/// strike that fills it hands the node to the gather, so the item, the practice and the toast land on that one tick. Moving off the node,
/// dying, entering combat, or a cancel ends it and grants nothing; the node's charges are untouched until the last swing lands.
/// </summary>
internal sealed class SwingingSystem
{
    /// <summary>How long one swing takes: 1.5 s, in ticks.</summary>
    public const int SwingTicks=30;

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;

    public SwingingSystem(SystemContext context,SliceOwner owner,EntityId player)
    {
        _context=context;
        _owner=owner;
        _player=player;
    }

    private RuntimeState State => _context.State;

    /// <summary>
    /// What the gather makes of a node key now - its definition, or the words it is refused in. The node's place, the arm's reach and its
    /// charges are the gather's own rules: a swing that guessed at them would refuse in words of its own.
    /// </summary>
    public Func<string,long,(NodeDefinition? Definition,string? Refusal)> Node { get; init; } = (key,_)=> (null,$"there is no node '{key}'");

    /// <summary>The yield a filled node gives: the gather itself, run on the strike that fills it, in the words it refuses in when it will not.</summary>
    public Func<string,long,string?> Gather { get; init; } = (_,_)=> null;

    public string? Handle(SwingAtNodeCommand command,long tick)
    {
        if(command.Actor!=_player) return $"unknown actor {command.Actor}";
        if(State.PlayerCombat.Defeated) return "dead";
        var (definition,refusal)=Node(command.NodeKey,tick);
        if(definition is null) return refusal!;
        if(State.Activity is not null || Busy(tick)) return "You are already doing something";
        State.SetActivity(_owner,new PlayerActivity(ActivityKind.Swing,command.NodeKey,tick,tick+SwingTicks,0) { Strike = 1 });
        _context.Events.Publish(new SwingStarted(command.NodeKey,definition.Id,tick));
        return null;
    }

    /// <summary>Cancel ends the swing where it stands, and nothing is granted: the same command that brings a line in.</summary>
    public string? Handle(CancelActivityCommand command)
    {
        if(command.Actor!=_player) return $"unknown actor {command.Actor}";
        if(State.Activity is { Kind: ActivityKind.Swing })
            State.SetActivity(_owner,null);
        return null;
    }

    public void Tick(long tick)
    {
        if(State.Activity is not { Kind: ActivityKind.Swing } activity) return;
        var (definition,_)=Node(activity.TargetKey,tick);
        // The breath, the fight and the arm's reach: a node that will not give any more - one walked away from - ends the swing with
        // nothing granted, as dying or a blow does.
        if(definition is null || State.PlayerCombat.Defeated || Busy(tick))
        {
            State.SetActivity(_owner,null);
            return;
        }
        if(tick<activity.UntilTick) return;
        Strike(activity,definition,tick);
    }

    /// <summary>
    /// A swing lands. Before the last, the node holds: the strike practises, because a swing at a seam teaches the hand even where nothing
    /// comes free of it, and the swing is renewed for the next one. The strike that fills the node is the gather's, which rolls the yield,
    /// records the node, practises it and tells it.
    /// </summary>
    private void Strike(PlayerActivity activity,NodeDefinition definition,long tick)
    {
        if(activity.Strike>=definition.Swings)
        {
            State.SetActivity(_owner,null);
            if(Gather(activity.TargetKey,tick) is { } refused)
                _context.Events.Publish(new NodeRefused(activity.TargetKey,refused,tick));
            return;
        }
        _context.Dispatch(new PracticeSkill(new SkillPractice(definition.SkillId,definition.Difficulty,PracticeOutcome.Success,tick)
        {
            NoveltyKey = definition.Id,
        }));
        _context.Events.Publish(new NodeStruck(activity.TargetKey,definition.Id,definition.Swings-activity.Strike,tick));
        State.SetActivity(_owner,activity with { StartedTick=tick,UntilTick=tick+SwingTicks,Strike=activity.Strike+1 });
    }

    /// <summary>Busy with a blow, a dodge, a stagger or a working: nothing time-shaped is started or kept through one.</summary>
    private bool Busy(long tick) =>
        State.PlayerCombat.Action.PhaseAt(tick,_context.Setup.Combat.Constants).Phase!=CombatPhase.Idle;
}
