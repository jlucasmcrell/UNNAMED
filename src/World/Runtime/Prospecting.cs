// UNNAMED World - reading the ground at run time: the look before the work, and what it says when it has had its ticks (a timed player activity)
// No Godot references - pure C#

using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Progression;

namespace UNNAMED.World.Runtime;

// ── commands ────────────────────────────────────────────────────────────────

/// <summary>
/// Read the ground a node stands on: what material it is, what grade, and how much of it is left. Refused where there is no node, out of
/// reach, dead, at a node already read, and while anything else is running - never for being short of the hand the node's grade wants,
/// because a hand that cannot work a seam is exactly the hand that should read it first.
/// </summary>
public sealed record ProspectCommand(EntityId Actor, string NodeKey) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

/// <summary>The look has begun: it holds for its ticks, and nothing is known until they run out.</summary>
public sealed record ProspectStarted(string NodeKey, string NodeDefId, long Tick);

/// <summary>
/// The ground has been read: the material, its grade and what the node still holds. It is known from here on for that node, in this save,
/// and a second look is refused with the facts already in hand.
/// </summary>
public sealed record NodeProspected(string NodeKey, string NodeDefId, string ResourceId, int Tier, int ChargesLeft, long Tick);

// ── the system ──────────────────────────────────────────────────────────────

/// <summary>
/// Owns: <see cref="StateSlice.Activity"/> with fishing, lockpicking, the swing and the wash - one body is in one timed thing at a time, and
/// the read is the fifth of them (SYSTEMS.md S-17; skill.prospecting). One loop: E on a node reads its ground, which holds for
/// <see cref="ProspectTicks"/>; when they run out the node's material, grade and remaining charges are recorded in the node's own cell and
/// published, and the read practises skill.prospecting whatever the node turned out to be. Moving off, dying, a blow or a working ends it
/// with nothing read and nothing practised.
/// </summary>
internal sealed class ProspectingSystem
{
    public const string Skill = "skill.prospecting";

    /// <summary>How long one read takes: 1 s, in ticks.</summary>
    public const int ProspectTicks = 20;

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;

    public ProspectingSystem(SystemContext context, SliceOwner owner, EntityId player)
    {
        _context = context;
        _owner = owner;
        _player = player;
    }

    private RuntimeState State => _context.State;

    /// <summary>
    /// What the gather makes of a node key now - its definition, what it holds and whether it has been read, or the words it is refused in.
    /// The node's place and the arm's reach are the gather's own rules, and its grade's demand is deliberately not asked: the read is for
    /// the hand that cannot work it yet.
    /// </summary>
    public Func<string, (NodeDefinition? Definition, int ChargesLeft, bool Read, string? Refusal)> Node { get; init; } =
        key => (null, 0, false, $"there is no node '{key}'");

    /// <summary>Record the read, once and for good, in the node's own cell (the gather's rule, so the flag is the same one it writes).</summary>
    public Action<string> Mark { get; init; } = _ => { };

    public string? Handle(ProspectCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.PlayerCombat.Defeated)
            return "dead";
        var (definition, _, read, refusal) = Node(command.NodeKey);
        if (definition is null)
            return refusal!;
        if (read)
            return "you have read this ground already";
        if (State.Activity is not null || Busy(tick))
            return "You are already doing something";
        State.SetActivity(_owner, new PlayerActivity(ActivityKind.Prospect, command.NodeKey, tick, tick + ProspectTicks, 0));
        _context.Events.Publish(new ProspectStarted(command.NodeKey, definition.Id, tick));
        return null;
    }

    /// <summary>Cancel ends a read cleanly, and nothing is known: the same command that brings a line in ends the look.</summary>
    public string? Handle(CancelActivityCommand command)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (State.Activity is { Kind: ActivityKind.Prospect })
            State.SetActivity(_owner, null);
        return null;
    }

    public void Tick(long tick)
    {
        if (State.Activity is not { Kind: ActivityKind.Prospect } activity)
            return;
        // The breath, the fight and the place: the same reasons a line comes in end the look with nothing read.
        var (definition, chargesLeft, _, _) = Node(activity.TargetKey);
        if (definition is null || State.PlayerCombat.Defeated || Busy(tick))
        {
            State.SetActivity(_owner, null);
            return;
        }
        if (tick < activity.UntilTick)
            return;
        State.SetActivity(_owner, null);
        Mark(activity.TargetKey);
        // The read teaches what it reads: a better grade is a harder read and a better lesson, and it is practised even where the node
        // reads as something the hand cannot work - that hand has still learned the ground.
        _context.Dispatch(new PracticeSkill(new SkillPractice(Skill, CraftingRules.MinSkill(definition.Tier), PracticeOutcome.Success, tick)
        {
            NoveltyKey = definition.Id,
        }));
        _context.Events.Publish(new NodeProspected(activity.TargetKey, definition.Id, definition.ResourceId ?? "", definition.Tier, chargesLeft, tick));
    }

    /// <summary>Busy with a blow, a dodge, a stagger or a working: nothing time-shaped is started or kept through one.</summary>
    private bool Busy(long tick) =>
        State.PlayerCombat.Action.PhaseAt(tick, _context.Setup.Combat.Constants).Phase != CombatPhase.Idle;
}
