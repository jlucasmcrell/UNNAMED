// UNNAMED World - quests at run time: instances, objective evaluation, rewards, and the quest debugger's trace (SYSTEMS.md S-29; D-07; M5)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>The quests content defines (M5).</summary>
public sealed record QuestSetup(ImmutableSortedDictionary<string, QuestDefinition> Quests)
{
    public static readonly QuestSetup Empty = new(ImmutableSortedDictionary.Create<string, QuestDefinition>(StringComparer.Ordinal));
}

// ── events ─────────────────────────────────────────────────────────────────────

public sealed record QuestStarted(string QuestId, string? GiverId, long Tick);

public sealed record ObjectiveActivated(string QuestId, string ObjectiveId, long Tick);

public sealed record ObjectiveSatisfied(string QuestId, string ObjectiveId, long Tick);

/// <summary>A timed objective ran out of time.</summary>
public sealed record ObjectiveFailed(string QuestId, string ObjectiveId, long Tick);

/// <summary>An objective that can no longer matter: a branch not taken, or what was still active when the quest ended.</summary>
public sealed record ObjectiveClosed(string QuestId, string ObjectiveId, long Tick);

public sealed record QuestBranchTaken(string QuestId, string BranchPoint, string Branch, long Tick);

public sealed record QuestCompleted(string QuestId, string ByObjective, long Tick);

public sealed record QuestFailed(string QuestId, string By, long Tick);

/// <summary>One reward of a completed quest, granted through its owner's command: what it was, and the definition and amount it names.</summary>
public sealed record RewardGranted(string QuestId, string Kind, string What, long Tick)
{
    public string? Ref { get; init; }

    public long Amount { get; init; }
}

// ── views ──────────────────────────────────────────────────────────────────────

/// <summary>A quest as the journal shows it: the objectives the character knows of, in authored order.</summary>
public sealed record QuestView(string Id, string Title, string Summary, QuestStatus Status, long StartedTick, long? EndedTick,
    ImmutableArray<ObjectiveView> Objectives);

/// <summary>One objective in the journal, with its count when it counts something.</summary>
public sealed record ObjectiveView(string Id, string Description, ObjectiveStatus Status, string? Progress);

// ── internal commands ──────────────────────────────────────────────────────────

/// <summary>To <see cref="QuestSystem"/>: start a quest (a conversation's <c>start_quest</c>). A quest already started is refused.</summary>
internal sealed record StartQuest(string QuestId, string? GiverId) : InternalCommand;

/// <summary>To <see cref="QuestSystem"/>: the character did something a counting objective may be waiting for.</summary>
internal sealed record RecordDeed(Deed Deed) : InternalCommand;

/// <summary>
/// Owns: <see cref="StateSlice.Quests"/>. Starts quests, counts deeds toward active objectives, evaluates every active quest once a
/// tick after everything else has moved (so a discovery, a craft or a line heard this tick counts this tick), and grants a
/// completed quest's rewards once, through their owners' commands. Holds no per-quest logic: the definitions are data and the
/// rules are <see cref="QuestRules"/> (D-07). Keeps, transiently, a trace of recent evaluations for the quest debugger.
/// </summary>
internal sealed class QuestSystem : IQuestFacts
{
    /// <summary>How many distinct evaluations the trace keeps per quest; a run of identical evaluations is one entry.</summary>
    public const int TraceLength = 64;

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly Dictionary<string, LinkedList<TraceEntry>> _traces = new(StringComparer.Ordinal);

    public QuestSystem(SystemContext context, SliceOwner owner)
    {
        _context = context;
        _owner = owner;
    }

    private RuntimeState State => _context.State;
    private QuestSetup Setup => _context.Setup.Quests;

    public string? Handle(StartQuest command, long tick)
    {
        if (!Setup.Quests.TryGetValue(command.QuestId, out var quest))
            return $"there is no quest {command.QuestId}";
        if (State.Quests.ContainsKey(quest.Id))
            return $"{quest.Title} is already {QuestKeys.Key(State.Quests[quest.Id].Status)}";
        State.SetQuest(_owner, QuestRules.Start(quest, tick));
        _context.Events.Publish(new QuestStarted(quest.Id, command.GiverId, tick));
        _context.Events.Publish(new ObjectiveActivated(quest.Id, quest.Entry, tick));
        Trace(quest.Id, tick, ImmutableArray.Create($"started{(command.GiverId is { } giver ? " by " + giver : "")}; {quest.Entry} active"), force: true);
        return null;
    }

    public string? Handle(RecordDeed command)
    {
        foreach (var state in State.Quests.Values.Where(q => q.Status == QuestStatus.Active).ToList())
        {
            if (!Setup.Quests.TryGetValue(state.QuestId, out var quest))
                continue;
            var recorded = QuestRules.Record(quest, state, command.Deed);
            if (recorded == state || recorded.Objectives.SequenceEqual(state.Objectives))
                continue;
            State.SetQuest(_owner, recorded);
            var deed = command.Deed;
            Trace(quest.Id, deed.Tick, ImmutableArray.Create(
                $"deed: {deed.Kind.ToString().ToLowerInvariant()} {deed.Count} {deed.Subject}{(deed.NpcId is { } npc ? " to " + npc : "")}"), force: true);
        }
        return null;
    }

    public void Tick(long tick)
    {
        foreach (var state in State.Quests.Values.Where(q => q.Status == QuestStatus.Active).ToList())
        {
            if (!Setup.Quests.TryGetValue(state.QuestId, out var quest))
                continue;
            var step = QuestRules.Advance(quest, state, this, tick);
            var lines = step.Evaluations.SelectMany(e => e.Evaluation.Terms.Select(t =>
                $"{e.ObjectiveId}: {t.Label} = {t.Value} (wanted {t.Wanted}) {(t.Holds ? "holds" : "waiting")}")).ToList();
            lines.AddRange(step.Transitions.Select(Describe));
            Trace(quest.Id, tick, lines.ToImmutableArray(), force: !step.Transitions.IsEmpty);
            if (step.Transitions.IsEmpty)
                continue;
            State.SetQuest(_owner, step.State);
            foreach (var t in step.Transitions)
                Publish(quest.Id, t, tick);
            if (step.State.Status == QuestStatus.Completed)
                Grant(quest, tick);
        }
    }

    /// <summary>Every reward of a completed quest, once: completion happens once, because a completed quest is never evaluated again.</summary>
    private void Grant(QuestDefinition quest, long tick)
    {
        foreach (var reward in quest.Rewards)
        {
            string what;
            string? named;
            long amount;
            switch (reward)
            {
                case XpReward xp:
                    _context.Dispatch(new AwardExperience(new XpAward(XpSource.QuestObjective, xp.Amount, tick)));
                    (what, named, amount) = ($"{xp.Amount} XP", null, xp.Amount);
                    break;
                case CurrencyReward coin:
                    _context.Dispatch(new AddCurrency(coin.Amount));
                    (what, named, amount) = ($"{coin.Amount} coin", null, coin.Amount);
                    break;
                case ItemReward item:
                    _context.Dispatch(new GrantItem(item.ItemId, item.Count, Quality.Standard));
                    (what, named, amount) = ($"{item.Count} {item.ItemId}", item.ItemId, item.Count);
                    break;
                case TechniqueReward technique:
                    _context.Dispatch(new LearnTechnique(new TechniqueLearning(technique.DefinitionId, LearningSource.Quest, tick) { SourceRef = quest.Id }));
                    (what, named, amount) = (technique.DefinitionId, technique.DefinitionId, 1);
                    break;
                case RelationshipReward relationship:
                    _context.Dispatch(new ChangeRelationship(relationship.NpcId, relationship.Dimension, relationship.Delta, quest.Id));
                    (what, named, amount) = ($"{relationship.NpcId} {relationship.Dimension} {relationship.Delta:+#;-#}", relationship.NpcId, relationship.Delta);
                    break;
                case WorldFlagReward flag:
                    _context.Dispatch(new SetWorldFlag(CellOfLocation(flag.LocationId), flag.FlagId, flag.Value));
                    (what, named, amount) = ($"{flag.FlagId} = {flag.Value} at {flag.LocationId}", flag.FlagId, flag.Value);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown reward {reward.GetType().Name}");
            }
            _context.Events.Publish(new RewardGranted(quest.Id, reward.Kind, what, tick) { Ref = named, Amount = amount });
        }
    }

    public ImmutableArray<QuestView> Views() =>
        State.Quests.Values
            .Where(s => Setup.Quests.ContainsKey(s.QuestId))
            .OrderBy(s => s.Status == QuestStatus.Active ? 0 : 1).ThenBy(s => s.StartedTick).ThenBy(s => s.QuestId, StringComparer.Ordinal)
            .Select(s =>
            {
                var quest = Setup.Quests[s.QuestId];
                // The journal shows what the character knows of: objectives reached and not closed (never a branch not taken),
                // a hidden one only once satisfied.
                var objectives = quest.Order.Select(o => (Definition: o, State: s.Objective(o.Id)))
                    .Where(x => x.State is { } st && st.Status != ObjectiveStatus.Closed && (!x.Definition.Hidden || st.Status == ObjectiveStatus.Satisfied))
                    .Select(x => new ObjectiveView(x.Definition.Id, x.Definition.Description, x.State!.Status, Progress(x.Definition, x.State)))
                    .ToImmutableArray();
                return new QuestView(quest.Id, quest.Title, quest.Summary, s.Status, s.StartedTick, s.EndedTick, objectives);
            }).ToImmutableArray();

    private static string? Progress(ObjectiveDefinition definition, ObjectiveState state) => definition.Condition switch
    {
        CraftItem c when c.Count > 1 => $"{Math.Min(state.Progress, c.Count)}/{c.Count}",
        HarvestResource h when h.Count > 1 => $"{Math.Min(state.Progress, h.Count)}/{h.Count}",
        KillCreature k when k.Count > 1 => $"{Math.Min(state.Progress, k.Count)}/{k.Count}",
        DeliverItem d when d.Count > 1 => $"{Math.Min(state.Progress, d.Count)}/{d.Count}",
        _ => null,
    };

    /// <summary>The last evaluations of a quest, oldest first (transient: a load starts an empty trace).</summary>
    public ImmutableArray<TraceEntry> TraceOf(string questId) =>
        _traces.TryGetValue(questId, out var trace) ? trace.ToImmutableArray() : ImmutableArray<TraceEntry>.Empty;

    /// <summary>
    /// A tick-by-tick trace, run-length collapsed: an evaluation identical to the one before it widens that entry's tick range
    /// instead of adding one, so the trace keeps the last <see cref="TraceLength"/> distinct evaluations however long nothing changed.
    /// </summary>
    private void Trace(string questId, long tick, ImmutableArray<string> lines, bool force)
    {
        if (!_traces.TryGetValue(questId, out var trace))
            _traces[questId] = trace = new LinkedList<TraceEntry>();
        if (!force && trace.Last?.Value is { } last && last.Lines.SequenceEqual(lines))
        {
            trace.Last.Value = last with { ToTick = tick, Evaluations = last.Evaluations + 1 };
            return;
        }
        trace.AddLast(new TraceEntry(tick, tick, 1, lines));
        while (trace.Count > TraceLength)
            trace.RemoveFirst();
    }

    private static string Describe(Transition t) => t.Kind switch
    {
        TransitionKind.Activated => $"-> {t.ObjectiveId} active",
        TransitionKind.Satisfied => $"-> {t.ObjectiveId} satisfied",
        TransitionKind.Failed => $"-> {t.ObjectiveId} failed (time limit)",
        TransitionKind.Closed => $"-> {t.ObjectiveId} closed",
        TransitionKind.BranchTaken => $"-> {t.ObjectiveId} branched to {t.Detail}",
        TransitionKind.Completed => $"-> quest completed by {t.ObjectiveId}",
        TransitionKind.QuestFailed => $"-> quest failed by {t.ObjectiveId}",
        _ => t.ToString(),
    };

    /// <summary>Each change as its own event type: subscribers subscribe by type.</summary>
    private void Publish(string questId, Transition t, long tick)
    {
        var events = _context.Events;
        switch (t.Kind)
        {
            case TransitionKind.Activated: events.Publish(new ObjectiveActivated(questId, t.ObjectiveId, tick)); break;
            case TransitionKind.Satisfied: events.Publish(new ObjectiveSatisfied(questId, t.ObjectiveId, tick)); break;
            case TransitionKind.Failed: events.Publish(new ObjectiveFailed(questId, t.ObjectiveId, tick)); break;
            case TransitionKind.Closed: events.Publish(new ObjectiveClosed(questId, t.ObjectiveId, tick)); break;
            case TransitionKind.BranchTaken: events.Publish(new QuestBranchTaken(questId, t.ObjectiveId, t.Detail!, tick)); break;
            case TransitionKind.Completed: events.Publish(new QuestCompleted(questId, t.ObjectiveId, tick)); break;
            case TransitionKind.QuestFailed: events.Publish(new QuestFailed(questId, t.ObjectiveId, tick)); break;
            default: throw new ArgumentOutOfRangeException(nameof(t));
        }
    }

    internal CellKey CellOfLocation(string locationId) =>
        _context.Setup.Layout.Locations.FirstOrDefault(l => l.Id == locationId) is { } place
            ? CellKey.OfWorld(place.XMm / 1000.0, place.ZMm / 1000.0)
            : throw new InvalidOperationException($"{locationId} is not a place in this region");

    // ── what predicates may ask ─────────────────────────────────────────────

    public bool Heard(string dialogueId, string nodeId) =>
        State.Conversations.TryGetValue(dialogueId, out var heard) && heard.Contains(nodeId);

    public bool Discovered(string locationId) => State.Discoveries.ContainsKey(locationId);

    public long DistanceMm(string locationId)
    {
        if (_context.Setup.Layout.Locations.FirstOrDefault(l => l.Id == locationId) is not { } place)
            return long.MaxValue;
        var body = State.Body;
        return (long)Math.Round(Math.Sqrt(Math.Pow(place.XMm - body.XMm, 2) + Math.Pow(place.ZMm - body.ZMm, 2)));
    }

    public int Carried(string itemId, int qualityMin) =>
        State.Inventory.Where(e => e.DefId == itemId && e.Quality >= qualityMin).Sum(e => e.Count);

    public long WorldFlag(string flagId, string locationId) =>
        _context.Setup.Layout.Locations.Any(l => l.Id == locationId) ? State.World.GetFlag(CellOfLocation(locationId), flagId) : 0;

    public int Relationship(string npcId, string dimension) => State.RelationshipOf(npcId, dimension);
}

/// <summary>
/// One entry of a quest's trace: from <see cref="FromTick"/> to <see cref="ToTick"/>, <see cref="Evaluations"/> evaluations that
/// all said <see cref="Lines"/> - every term of every active objective with its value, and any change the evaluation made.
/// </summary>
public sealed record TraceEntry(long FromTick, long ToTick, int Evaluations, ImmutableArray<string> Lines);
