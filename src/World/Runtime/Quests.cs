// UNNAMED World - quests at run time: instances, objective evaluation, rewards, and the quest debugger's trace (SYSTEMS.md S-29; D-07; M5)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
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

/// <summary>An objective's <c>choice</c> was made: its flag is set, and the journal keeps the line.</summary>
public sealed record ChoiceMade(string QuestId, string ObjectiveId, string FlagId, long Value, string Line, long Tick);

/// <summary>A competitive bounty was finished first by <see cref="ClaimantId"/> - the character's ID, or another actor's - and is closed until it is posted again.</summary>
public sealed record BountyClaimed(string QuestId, string ClaimantId, long Tick);

/// <summary>One reward of a completed quest, granted through its owner's command: what it was, and the definition and amount it names.</summary>
public sealed record RewardGranted(string QuestId, string Kind, string What, long Tick)
{
    public string? Ref { get; init; }

    public long Amount { get; init; }
}

// ── views ──────────────────────────────────────────────────────────────────────

/// <summary>A quest as the journal shows it: the objectives the character knows of, in authored order.</summary>
public sealed record QuestView(string Id, string Title, string Summary, QuestStatus Status, long StartedTick, long? EndedTick,
    ImmutableArray<ObjectiveView> Objectives)
{
    public QuestTrack Track { get; init; } = QuestTrack.Side;

    /// <summary>The main story's act; null off the main track.</summary>
    public int? Act { get; init; }

    /// <summary>Who finished this bounty first while the character held it, by name; null when nobody did.</summary>
    public string? LostTo { get; init; }
}

/// <summary>The journal: every quest the character took up (active first), and the Recent list, newest first.</summary>
public sealed record JournalView(ImmutableArray<QuestView> Quests, ImmutableArray<JournalEntry> Recent)
{
    /// <summary>The main story's quests, by act and then as they were taken up ("the story so far").</summary>
    public ImmutableArray<QuestView> Main => Quests.Where(q => q.Track == QuestTrack.Main).OrderBy(q => q.Act).ThenBy(q => q.StartedTick).ToImmutableArray();

    public ImmutableArray<QuestView> Side => Quests.Where(q => q.Track == QuestTrack.Side).ToImmutableArray();

    public ImmutableArray<QuestView> Bounties => Quests.Where(q => q.Track == QuestTrack.Bounty).ToImmutableArray();
}

public enum BountyStanding
{
    /// <summary>Posted, and nobody holds it.</summary>
    Open,

    /// <summary>The character holds it.</summary>
    Held,

    /// <summary>Done with for now - finished by the character or claimed by another, or failed - and not posted again yet.</summary>
    Claimed,
}

/// <summary>
/// One bounty on its giver's board: whether it is open, held by the character or claimed - by whom, and when it is posted again. A bounty
/// never posted to anyone yet is open.
/// </summary>
public sealed record BountyView(string QuestId, string Title, string? GiverId, bool Competitive, BountyStanding Standing)
{
    /// <summary>Who claimed it: the character's ID or another actor's; null unless <see cref="BountyStanding.Claimed"/>.</summary>
    public string? ClaimantId { get; init; }

    /// <summary>The claimant's name, as the board shows it ("Taken by ...").</summary>
    public string? ClaimantName { get; init; }

    /// <summary>When it is posted again; null when it is open or held.</summary>
    public long? PostedAgainTick { get; init; }
}

/// <summary>
/// The compass's mark for the tracked beat: the place, person or stone the active objective names, where it is now. The label is a person's
/// or a stone's name, and a place's ID (presentation names it from content).
/// </summary>
public sealed record QuestMarker(string QuestId, string ObjectiveId, TargetKind Kind, string Ref, string Label, long XMm, long ZMm);

/// <summary>One objective in the journal, with its count when it counts something.</summary>
public sealed record ObjectiveView(string Id, string Description, ObjectiveStatus Status, string? Progress);

// ── commands ───────────────────────────────────────────────────────────────────

/// <summary>A new character's story begins: every <c>on_new_game</c> quest not yet started starts now. Submitted once, by a new game.</summary>
public sealed record BeginStoryCommand(EntityId Actor) : GameCommand(Actor);

/// <summary>
/// Another actor - not the character, who claims a bounty by finishing it - finished a competitive bounty first. The actor is one the world
/// knows: a named NPC now, the region's adventurers once they arrive (WP13).
/// </summary>
public sealed record ClaimBountyCommand(EntityId Actor, string QuestId) : GameCommand(Actor);

// ── internal commands ──────────────────────────────────────────────────────────

/// <summary>
/// To <see cref="QuestSystem"/>: start a quest (a conversation's <c>start_quest</c>). A quest already started is refused, unless it is
/// repeatable and due again.
/// </summary>
internal sealed record StartQuest(string QuestId, string? GiverId) : InternalCommand;

/// <summary>To <see cref="QuestSystem"/>: the character did something a counting objective may be waiting for.</summary>
internal sealed record RecordDeed(Deed Deed) : InternalCommand;

/// <summary>
/// Owns: <see cref="StateSlice.Quests"/>. Starts quests, counts deeds toward active objectives, evaluates every active quest once a
/// tick after everything else has moved (so a discovery, a craft or a line heard this tick counts this tick), and grants a
/// completed quest's rewards once, through their owners' commands. Holds no per-quest logic: the definitions are data and the
/// rules are <see cref="QuestRules"/> (D-07). Keeps, transiently, a trace of recent evaluations for the quest debugger.
/// </summary>
internal sealed class QuestSystem : IQuestFacts, IAbstractAdvance
{
    /// <summary>How many distinct evaluations the trace keeps per quest; a run of identical evaluations is one entry.</summary>
    public const int TraceLength = 64;

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly Dictionary<string, LinkedList<TraceEntry>> _traces = new(StringComparer.Ordinal);

    private readonly EntityId _player;

    public QuestSystem(SystemContext context, SliceOwner owner, EntityId player)
    {
        _context = context;
        _owner = owner;
        _player = player;
    }

    private RuntimeState State => _context.State;
    private QuestSetup Setup => _context.Setup.Quests;

    /// <summary>
    /// What the quests listen for, each answered with a command: a creature's death writes its defeat flag, by whoever's hand; a reply given
    /// and an arrival by the stones count for the objectives waiting for them.
    /// </summary>
    public void Listen(SimulationEvents events)
    {
        events.Listen<CreatureKilled>(e => Defeated(e.DefId));
        events.Listen<ReplyChosen>(e => _context.Dispatch(new RecordDeed(new Deed(DeedKind.Chose, ChoiceGiven.ChoiceSubject(e.DialogueId, e.NodeId, e.ReplyId), 1,
            Quality.Standard, e.NpcId, e.Tick))));
        events.Listen<PlayerTravelled>(e => _context.Dispatch(new RecordDeed(new Deed(DeedKind.Travelled, e.ToKey, 1, Quality.Standard, null, e.Tick))));
    }

    /// <summary>Every defeat flag content names, by creature: the flag and the place whose cell holds it.</summary>
    private ImmutableSortedDictionary<string, ImmutableArray<(string FlagId, string LocationId)>> DefeatFlags =>
        _defeatFlags ??= Setup.Quests.Values.SelectMany(q => q.Order.Select(o => o.Condition).Concat(q.FailIf)).OfType<DefeatCreature>()
            .GroupBy(d => d.CreatureId, StringComparer.Ordinal)
            .ToImmutableSortedDictionary(g => g.Key, g => g.Select(d => (d.FlagId, d.LocationId)).Distinct().ToImmutableArray(), StringComparer.Ordinal);

    private ImmutableSortedDictionary<string, ImmutableArray<(string FlagId, string LocationId)>>? _defeatFlags;

    private void Defeated(string creatureId)
    {
        if (!DefeatFlags.TryGetValue(creatureId, out var flags))
            return;
        foreach (var (flag, location) in flags)
        {
            // A place another region holds is read as unset here, and is not written here either.
            if (!_context.Setup.Layout.Locations.Any(l => l.Id == location) || WorldFlag(flag, location) != 0)
                continue;
            _context.Dispatch(new SetWorldFlag(CellOfLocation(location), flag, 1));
        }
    }

    public string? Handle(BeginStoryCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        foreach (var quest in Setup.Quests.Values.Where(q => q.OnNewGame && !State.Quests.ContainsKey(q.Id)))
            Handle(new StartQuest(quest.Id, null), tick);
        return null;
    }

    public string? Handle(ClaimBountyCommand command, long tick)
    {
        if (command.Actor == _player)
            return "the character claims a bounty by finishing it";
        if (Claimants().All(c => c.Id != command.Actor))
            return $"unknown actor {command.Actor}";
        if (!Setup.Quests.TryGetValue(command.QuestId, out var quest))
            return $"there is no quest {command.QuestId}";
        if (!quest.Competitive)
            return $"{quest.Title} is not open to anyone else";
        var held = State.Quests.GetValueOrDefault(quest.Id);
        if (!QuestRules.CanClaim(quest, held, tick))
            return $"{quest.Title} is already claimed";
        var claimed = QuestRules.Claim(quest, held, command.Actor.Value, tick);
        State.SetQuest(_owner, claimed);
        if (held is { Status: QuestStatus.Active })
        {
            foreach (var o in held.Objectives.Where(o => o.Status == ObjectiveStatus.Active))
                _context.Events.Publish(new ObjectiveClosed(quest.Id, o.Id, tick));
            _context.Events.Publish(new QuestFailed(quest.Id, claimed.EndedBy!, tick));
        }
        _context.Events.Publish(new BountyClaimed(quest.Id, command.Actor.Value, tick));
        Trace(quest.Id, tick, ImmutableArray.Create($"claimed by {command.Actor}"), force: true);
        return null;
    }

    /// <summary>The actors besides the character who may claim a bounty, with their names: the named NPCs (WP13 adds the adventurers).</summary>
    private IEnumerable<(EntityId Id, string Name)> Claimants() => State.Npcs.Values.Select(n => (n.InstanceId, n.Definition.Name));

    /// <summary>A claimant by name: the character, a known actor, or the ID itself.</summary>
    private string ClaimantName(string id) =>
        id == _player.Value ? "You" : Claimants().FirstOrDefault(c => c.Id.Value == id) is { Name: { } name } ? name : id;

    /// The absence needs nothing (WP11): a repeatable quest's cooldown is a world-tick deadline (<see cref="QuestRules.CanStart"/>), so one
    /// that ran out while the character was away can be taken again at once. Objectives are not worked off-screen.
    /// </summary>
    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
    }

    public string? Handle(StartQuest command, long tick)
    {
        if (!Setup.Quests.TryGetValue(command.QuestId, out var quest))
            return $"there is no quest {command.QuestId}";
        // A repeatable quest, once its repeat time has passed since it ended, starts afresh over its last record.
        if (State.Quests.TryGetValue(quest.Id, out var last) && !QuestRules.CanStart(quest, last, tick))
            return $"{quest.Title} is already {QuestKeys.Key(last.Status)}";
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
            {
                Publish(quest.Id, t, tick);
                if (t.Kind == TransitionKind.Satisfied)
                    Apply(quest, quest.Objectives[t.ObjectiveId], tick);
            }
            if (step.State.Status == QuestStatus.Completed)
            {
                Grant(quest, tick);
                if (quest.Competitive)
                    _context.Events.Publish(new BountyClaimed(quest.Id, _player.Value, tick));
            }
        }
    }

    /// <summary>An objective's effects, once: a satisfied objective is never satisfied again.</summary>
    private void Apply(QuestDefinition quest, ObjectiveDefinition objective, long tick)
    {
        foreach (var effect in objective.Effects)
        {
            switch (effect)
            {
                case ChoiceEffect choice:
                    _context.Dispatch(new SetWorldFlag(CellOfLocation(choice.LocationId), choice.FlagId, choice.Value));
                    _context.Events.Publish(new ChoiceMade(quest.Id, objective.Id, choice.FlagId, choice.Value, choice.Line, tick));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown effect {effect.GetType().Name}");
            }
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
                    _context.Dispatch(new GrantItem(item.ItemId, item.Count, Quality.Standard) { SourceRef = quest.Id });
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
                case ReputationReward reputation:
                    _context.Dispatch(new GrantStanding(reputation.FactionId, reputation.Delta, quest.Id));
                    (what, named, amount) = ($"{reputation.FactionId} {reputation.Delta:+#;-#}", reputation.FactionId, reputation.Delta);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown reward {reward.GetType().Name}");
            }
            _context.Events.Publish(new RewardGranted(quest.Id, reward.Kind, what, tick) { Ref = named, Amount = amount });
        }
    }

    public ImmutableArray<QuestView> Views() =>
        State.Quests.Values
            .Where(s => Setup.Quests.ContainsKey(s.QuestId) && QuestRules.Held(s))
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
                return new QuestView(quest.Id, quest.Title, quest.Summary, s.Status, s.StartedTick, s.EndedTick, objectives)
                {
                    Track = quest.Track,
                    Act = quest.Act,
                    LostTo = QuestRules.Claimant(s) is { } claimant ? ClaimantName(claimant) : null,
                };
            }).ToImmutableArray();

    /// <summary>The journal: the quests, and the Recent list their records give.</summary>
    public JournalView Journal() =>
        new(Views(), QuestJournal.Recent(State.Quests.Values.Where(s => Setup.Quests.ContainsKey(s.QuestId))
            .SelectMany(s => QuestJournal.Entries(Setup.Quests[s.QuestId], s, ClaimantName))));

    /// <summary>Every bounty and where it stands on its giver's board, by giver and then title.</summary>
    public ImmutableArray<BountyView> Bounties(long tick) =>
        Setup.Quests.Values.Where(q => q.Track == QuestTrack.Bounty)
            .OrderBy(q => q.GiverId ?? "", StringComparer.Ordinal).ThenBy(q => q.Title, StringComparer.Ordinal)
            .Select(q =>
            {
                var s = State.Quests.GetValueOrDefault(q.Id);
                var view = new BountyView(q.Id, q.Title, q.GiverId, q.Competitive, s is { Status: QuestStatus.Active } ? BountyStanding.Held : BountyStanding.Open);
                if (s is null || s.Status == QuestStatus.Active || QuestRules.CanStart(q, s, tick))
                    return view;
                // Ended and not posted again yet: claimed - by another, or by the character, who finished it.
                string? claimant = QuestRules.Claimant(s) ?? (s.Status == QuestStatus.Completed ? _player.Value : null);
                return view with
                {
                    Standing = BountyStanding.Claimed,
                    ClaimantId = claimant,
                    ClaimantName = claimant is null ? null : ClaimantName(claimant),
                    PostedAgainTick = q.RepeatAfterTicks is { } after && s.EndedTick is { } ended ? ended + after : null,
                };
            }).ToImmutableArray();

    /// <summary>
    /// The quest the tracker follows: the one asked for while it is active; else the main story's, earliest act first; else the first
    /// active quest. Null when nothing is under way.
    /// </summary>
    public string? Tracked(string? preferred)
    {
        var active = State.Quests.Values.Where(s => s.Status == QuestStatus.Active && Setup.Quests.ContainsKey(s.QuestId)).ToList();
        if (preferred is not null && active.Any(s => s.QuestId == preferred))
            return preferred;
        return active.OrderBy(s => Setup.Quests[s.QuestId].Track == QuestTrack.Main ? 0 : 1).ThenBy(s => Setup.Quests[s.QuestId].Act ?? 0)
            .ThenBy(s => s.StartedTick).ThenBy(s => s.QuestId, StringComparer.Ordinal).FirstOrDefault()?.QuestId;
    }

    /// <summary>
    /// The compass's mark for a quest: the first active objective, in authored order, whose place, person or stone the character knows -
    /// a place discovered, someone they have spoken with, a stone they know or whose place they found. Null when there is none.
    /// </summary>
    public QuestMarker? Marker(string questId)
    {
        if (!Setup.Quests.TryGetValue(questId, out var quest) || State.Quests.GetValueOrDefault(questId) is not { Status: QuestStatus.Active } state)
            return null;
        var layout = _context.Setup.Layout;
        foreach (var objective in quest.Order.Where(o => !o.Hidden && state.Objective(o.Id) is { Status: ObjectiveStatus.Active }))
        {
            if (QuestRules.Target(objective.Condition) is not { } target)
                continue;
            switch (target.Kind)
            {
                case TargetKind.Location when layout.Locations.FirstOrDefault(l => l.Id == target.Ref) is { } place && Discovered(place.Id):
                    return new QuestMarker(quest.Id, objective.Id, target.Kind, target.Ref, place.Id, place.XMm, place.ZMm);
                case TargetKind.Npc when State.Npcs.TryGetValue(target.Ref, out var npc) && npc.Definition.DialogueId is { } dialogue
                                         && State.Conversations.ContainsKey(dialogue):
                    return new QuestMarker(quest.Id, objective.Id, target.Kind, target.Ref, npc.Definition.Name, npc.Body.XMm, npc.Body.ZMm);
                case TargetKind.Stone when layout.FindWaystone(target.Ref) is { } stone && (KnowsStone(stone.Key) || Discovered(stone.LocationId)):
                    return new QuestMarker(quest.Id, objective.Id, target.Kind, target.Ref, stone.Name, stone.ArrivalXMm, stone.ArrivalZMm);
            }
        }
        return null;
    }

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

    public int StandingLevel(string factionId) => _context.Setup.Factions.Ladder.StandingTierOf(State.StandingOf(factionId)).Level;

    public bool KnowsStone(string stoneKey) => State.KnownStones.ContainsKey(stoneKey);

    public CompanionOrder? CompanionPresent(string npcId) =>
        State.Companions.TryGetValue(npcId, out var companion) && companion.Condition == CompanionCondition.Up ? companion.Order : null;
}

/// <summary>
/// One entry of a quest's trace: from <see cref="FromTick"/> to <see cref="ToTick"/>, <see cref="Evaluations"/> evaluations that
/// all said <see cref="Lines"/> - every term of every active objective with its value, and any change the evaluation made.
/// </summary>
public sealed record TraceEntry(long FromTick, long ToTick, int Evaluations, ImmutableArray<string> Lines);
