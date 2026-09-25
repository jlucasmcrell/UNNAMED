// UNNAMED Domain - quests as declarative objective graphs over world state (DATA_MODEL.md §4.11; SYSTEMS.md S-29; D-07; M5)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Crafting;

namespace UNNAMED.Domain.Quests;

/// <summary>
/// A quest (DATA_MODEL.md §4.11, Phase 1's subset): a graph of objectives, each a predicate over world state from a closed set of
/// types. The engine evaluates predicates and holds no per-quest logic (D-07). <see cref="Order"/> is the authored order, which
/// decides ties: objectives are evaluated in it.
/// </summary>
public sealed record QuestDefinition(
    string Id,
    string Title,
    string Summary,
    string? GiverId,
    string Entry,
    ImmutableArray<ObjectiveDefinition> Order,
    ImmutableArray<ObjectiveCondition> FailIf,
    ImmutableArray<QuestReward> Rewards)
{
    public ImmutableSortedDictionary<string, ObjectiveDefinition> Objectives { get; } =
        Order.ToImmutableSortedDictionary(o => o.Id, o => o, StringComparer.Ordinal);
}

/// <summary>
/// One objective. It becomes active when the quest starts (the entry) or when an objective naming it in <see cref="Next"/> is
/// satisfied - and then only once every objective in <see cref="AllOf"/> is satisfied (a join). A satisfied objective with nowhere
/// to go completes the quest. <see cref="FirstBranch"/> makes <see cref="Next"/> alternatives: the first of them satisfied is the
/// branch taken and closes the rest. A <see cref="TimeLimitTicks"/> objective not satisfied in time fails, and
/// <see cref="OnFail"/> names the objective that becomes active instead, or <see cref="QuestRules.FailQuest"/>.
/// </summary>
public sealed record ObjectiveDefinition(string Id, string Description, ObjectiveCondition Condition, ImmutableArray<string> Next)
{
    public ImmutableArray<string> AllOf { get; init; } = ImmutableArray<string>.Empty;

    public bool FirstBranch { get; init; }

    /// <summary>Kept out of the journal until it is satisfied (<c>visibility: hidden</c>).</summary>
    public bool Hidden { get; init; }

    public long? TimeLimitTicks { get; init; }

    public string? OnFail { get; init; }

    public bool Terminal => Next.IsEmpty;
}

// ── objective types: the closed set Phase 1 builds ─────────────────────────────

/// <summary>A closed-set predicate over world state (DATA_MODEL.md §4.11's table; the rest of the vocabulary arrives with its systems).</summary>
public abstract record ObjectiveCondition
{
    /// <summary>The type as content names it.</summary>
    public abstract string Type { get; }

    /// <summary>Counted from deeds done while the objective is active, rather than read from the world as it stands.</summary>
    public virtual bool CountsDeeds => false;
}

/// <summary><c>talk_to</c>: a line of the NPC's conversation has been heard - any of <see cref="Nodes"/>.</summary>
public sealed record TalkTo(string NpcId, string DialogueId, ImmutableArray<string> Nodes) : ObjectiveCondition
{
    public override string Type => "talk_to";
}

/// <summary><c>visit_location</c>: the place has been discovered - an earlier visit counts.</summary>
public sealed record VisitLocation(string LocationId) : ObjectiveCondition
{
    public override string Type => "visit_location";
}

/// <summary><c>explore_location</c>: the character stands within <see cref="WithinMm"/> of the place now.</summary>
public sealed record ExploreLocation(string LocationId, long WithinMm) : ObjectiveCondition
{
    public override string Type => "explore_location";
}

/// <summary>
/// <c>acquire_item</c>: the pack holds at least this many, at least this good - of the item, or of what it is made into
/// (<see cref="OrItems"/>): ore already smelted into a billet was still obtained.
/// </summary>
public sealed record AcquireItem(string ItemId, int Count, int QualityMin) : ObjectiveCondition
{
    public override string Type => "acquire_item";

    /// <summary>Items that count as well (<c>or_item_refs</c>): what the item is made into, so work done ahead of the objective still counts.</summary>
    public ImmutableArray<string> OrItems { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary><c>craft_item</c>: this many made, at least this good, while the objective is active.</summary>
public sealed record CraftItem(string ItemId, int Count, int QualityMin) : ObjectiveCondition
{
    public override string Type => "craft_item";
    public override bool CountsDeeds => true;
}

/// <summary><c>harvest_resource</c>: this much of the resource gathered while the objective is active.</summary>
public sealed record HarvestResource(string ResourceId, int Count) : ObjectiveCondition
{
    public override string Type => "harvest_resource";
    public override bool CountsDeeds => true;
}

/// <summary><c>kill_creature</c>: this many of the creature killed by the character while the objective is active.</summary>
public sealed record KillCreature(string CreatureId, int Count) : ObjectiveCondition
{
    public override string Type => "kill_creature";
    public override bool CountsDeeds => true;
}

/// <summary><c>deliver_item</c>: this many handed to the NPC in conversation while the objective is active.</summary>
public sealed record DeliverItem(string NpcId, string ItemId, int Count) : ObjectiveCondition
{
    public override string Type => "deliver_item";
    public override bool CountsDeeds => true;
}

/// <summary><c>world_state</c>: a world flag's value in [min, max], in the cell that holds <see cref="LocationId"/>.</summary>
public sealed record WorldStateObjective(string FlagId, string LocationId, long Min, long Max) : ObjectiveCondition
{
    public override string Type => "world_state";
}

/// <summary><c>relationship_value</c>: what the NPC thinks of the player on one dimension, in [min, max].</summary>
public sealed record RelationshipValueObjective(string NpcId, string Dimension, int Min, int Max) : ObjectiveCondition
{
    public override string Type => "relationship_value";
}

/// <summary><c>wait_until</c>: this long has passed since the objective became active.</summary>
public sealed record WaitUntil(long AfterTicks) : ObjectiveCondition
{
    public override string Type => "wait_until";
}

/// <summary>The vocabulary: what Phase 1 builds, and the rest of DATA_MODEL.md §4.11's closed set, which arrives with its systems.</summary>
public static class ObjectiveTypes
{
    public static readonly ImmutableArray<string> Built = ImmutableArray.Create(
        "talk_to", "visit_location", "explore_location", "acquire_item", "craft_item", "harvest_resource", "kill_creature",
        "deliver_item", "world_state", "relationship_value", "wait_until");

    /// <summary>Named by the closed set and not built yet, each with what it waits for.</summary>
    public static readonly ImmutableSortedDictionary<string, string> NotBuilt = new Dictionary<string, string>
    {
        ["discover_secret"] = "secrets (S-30)",
        ["dialogue_choice"] = "choice records; talk_to a node the reply leads to instead",
        ["use_recipe"] = "recipe-use records; craft_item instead",
        ["construct_building"] = "building (M7)",
        ["upgrade_settlement"] = "settlement simulation (M10)",
        ["kill_named"] = "named spawns (M8)",
        ["defeat_boss"] = "bosses (M8)",
        ["survive_encounter"] = "encounters (M8)",
        ["escort_npc"] = "NPCs that travel",
        ["defend_location"] = "waves (M8)",
        ["solve_puzzle"] = "puzzles (M8)",
        ["faction_reputation"] = "factions (M7)",
        ["faction_state"] = "factions (M7)",
        ["companion_present"] = "a quest that asks after a companion (companions arrived in M6; no quest needs one yet)",
        ["know_fact"] = "knowledge facts",
        ["time_window"] = "the time of day",
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);
}

// ── rewards ────────────────────────────────────────────────────────────────────

/// <summary>A closed-set reward (DATA_MODEL.md §4.11), granted through its owner's command once, when the quest completes.</summary>
public abstract record QuestReward
{
    public abstract string Kind { get; }
}

public sealed record XpReward(long Amount) : QuestReward
{
    public override string Kind => "xp";
}

public sealed record CurrencyReward(long Amount) : QuestReward
{
    public override string Kind => "currency";
}

public sealed record ItemReward(string ItemId, int Count) : QuestReward
{
    public override string Kind => "item";
}

/// <summary>A recipe or a formula, learned from the quest (PROGRESSION.md §4.4).</summary>
public sealed record TechniqueReward(string DefinitionId, string RewardKind) : QuestReward
{
    public override string Kind => RewardKind;
}

public sealed record RelationshipReward(string NpcId, string Dimension, int Delta) : QuestReward
{
    public override string Kind => "relationship";
}

public sealed record WorldFlagReward(string FlagId, string LocationId, long Value) : QuestReward
{
    public override string Kind => "world_flag";
}

public static class RewardKinds
{
    public static readonly ImmutableArray<string> Built = ImmutableArray.Create("xp", "currency", "item", "recipe", "spell", "relationship", "world_flag");

    public static readonly ImmutableArray<string> NotBuilt = ImmutableArray.Create(
        "ability", "reputation", "title", "access", "companion", "property", "permanent_ability", "transformation");
}

// ── state ──────────────────────────────────────────────────────────────────────

public enum QuestStatus { Active, Completed, Failed }

public enum ObjectiveStatus { Active, Satisfied, Failed, Closed }

/// <summary>One objective of a started quest. <see cref="Progress"/> counts deeds done while it was active.</summary>
public sealed record ObjectiveState(string Id, ObjectiveStatus Status, long ActivatedTick, long? EndedTick, int Progress);

/// <summary>
/// A started quest, as the player's record keeps it (SYSTEMS.md S-29: instance state, per-objective progress, branch selections as
/// closed objectives, failure records). <see cref="EndedBy"/> names what ended it: the objective that completed or failed it, or
/// <c>fail_if[n]</c>.
/// </summary>
public sealed record QuestState(string QuestId, QuestStatus Status, long StartedTick, long? EndedTick, string? EndedBy,
    ImmutableArray<ObjectiveState> Objectives)
{
    public ObjectiveState? Objective(string id) => Objectives.FirstOrDefault(o => o.Id == id);
}

/// <summary>Saved and displayed names are stable snake_case keys, never enum ordinals.</summary>
public static class QuestKeys
{
    public static string Key(QuestStatus status) => status switch
    {
        QuestStatus.Active => "active",
        QuestStatus.Completed => "completed",
        QuestStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static string Key(ObjectiveStatus status) => status switch
    {
        ObjectiveStatus.Active => "active",
        ObjectiveStatus.Satisfied => "satisfied",
        ObjectiveStatus.Failed => "failed",
        ObjectiveStatus.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static QuestStatus? ParseQuest(string key) => key switch
    {
        "active" => QuestStatus.Active,
        "completed" => QuestStatus.Completed,
        "failed" => QuestStatus.Failed,
        _ => null,
    };

    public static ObjectiveStatus? ParseObjective(string key) => key switch
    {
        "active" => ObjectiveStatus.Active,
        "satisfied" => ObjectiveStatus.Satisfied,
        "failed" => ObjectiveStatus.Failed,
        "closed" => ObjectiveStatus.Closed,
        _ => null,
    };

    /// <summary>What a quest is to a condition that asks: its status, or <c>not_started</c>.</summary>
    public const string NotStarted = "not_started";

    /// <summary>What an objective is to a condition that asks before the quest reaches it.</summary>
    public const string NotReached = "not_reached";

    public static readonly ImmutableArray<string> QuestStates = ImmutableArray.Create(NotStarted, "active", "completed", "failed");

    public static readonly ImmutableArray<string> ObjectiveStates = ImmutableArray.Create(NotReached, "active", "satisfied", "failed", "closed");
}

// ── what predicates may ask, and deeds ─────────────────────────────────────────

/// <summary>What an objective's predicate may ask of the world, answered by the simulation (read only).</summary>
public interface IQuestFacts
{
    bool Heard(string dialogueId, string nodeId);

    bool Discovered(string locationId);

    /// <summary>From the character to the place's anchor, on the ground plane.</summary>
    long DistanceMm(string locationId);

    int Carried(string itemId, int qualityMin);

    /// <summary>A world flag in the cell that holds the place.</summary>
    long WorldFlag(string flagId, string locationId);

    int Relationship(string npcId, string dimension);
}

public enum DeedKind { Crafted, Harvested, Killed, Delivered }

/// <summary>Something the character did that a counting objective may be waiting for.</summary>
public sealed record Deed(DeedKind Kind, string Subject, int Count, int Quality, string? NpcId, long Tick);

// ── evaluation ─────────────────────────────────────────────────────────────────

/// <summary>One term of a predicate as it stands: what is asked, its value now, the value wanted, and whether it holds.</summary>
public sealed record Term(string Label, string Value, string Wanted, bool Holds);

/// <summary>A predicate's evaluation: whether it holds, and every term with its current value (the debugger's raw material).</summary>
public sealed record Evaluation(bool Holds, ImmutableArray<Term> Terms);

public enum TransitionKind { Activated, Satisfied, Failed, Closed, BranchTaken, Completed, QuestFailed }

/// <summary>One change a step made. For <see cref="TransitionKind.BranchTaken"/>, the objective is the branch point and the detail the branch.</summary>
public sealed record Transition(TransitionKind Kind, string ObjectiveId, string? Detail);

/// <summary>The outcome of one step: the new state, what changed in order, and what each active objective's predicate said.</summary>
public sealed record QuestStep(QuestState State, ImmutableArray<Transition> Transitions,
    ImmutableArray<(string ObjectiveId, Evaluation Evaluation)> Evaluations);

public static class QuestRules
{
    /// <summary><c>on_fail: fail_quest</c>.</summary>
    public const string FailQuest = "fail_quest";

    /// <summary>A quest starts with its entry objective active.</summary>
    public static QuestState Start(QuestDefinition quest, long tick) =>
        new(quest.Id, QuestStatus.Active, tick, null, null,
            ImmutableArray.Create(new ObjectiveState(quest.Entry, ObjectiveStatus.Active, tick, null, 0)));

    /// <summary>A predicate against the world as it stands, and the objective's own progress and age.</summary>
    public static Evaluation Evaluate(ObjectiveCondition condition, IQuestFacts facts, int progress, long activatedTick, long tick)
    {
        switch (condition)
        {
            case TalkTo t:
            {
                var terms = t.Nodes.Select(n => Is($"heard '{n}' ({t.DialogueId})", facts.Heard(t.DialogueId, n))).ToImmutableArray();
                return new Evaluation(terms.Any(x => x.Holds), terms);
            }
            case VisitLocation v:
                return One(Is($"{v.LocationId} discovered", facts.Discovered(v.LocationId)));
            case ExploreLocation e:
            {
                long distance = facts.DistanceMm(e.LocationId);
                return One(new Term($"distance to {e.LocationId}", Metres(distance), $"<= {Metres(e.WithinMm)}", distance <= e.WithinMm));
            }
            case AcquireItem a:
            {
                int carried = facts.Carried(a.ItemId, a.QualityMin) + a.OrItems.Sum(item => facts.Carried(item, a.QualityMin));
                string what = a.OrItems.IsEmpty ? a.ItemId : $"{a.ItemId} (or {string.Join(", ", a.OrItems)})";
                return One(new Term($"{what} carried{QualityNote(a.QualityMin)}", carried.ToString(), $">= {a.Count}", carried >= a.Count));
            }
            case CraftItem c:
                return One(new Term($"{c.ItemId} made while active{QualityNote(c.QualityMin)}", progress.ToString(), $">= {c.Count}", progress >= c.Count));
            case HarvestResource h:
                return One(new Term($"{h.ResourceId} gathered while active", progress.ToString(), $">= {h.Count}", progress >= h.Count));
            case KillCreature k:
                return One(new Term($"{k.CreatureId} killed while active", progress.ToString(), $">= {k.Count}", progress >= k.Count));
            case DeliverItem d:
                return One(new Term($"{d.ItemId} handed to {d.NpcId} while active", progress.ToString(), $">= {d.Count}", progress >= d.Count));
            case WorldStateObjective w:
            {
                long value = facts.WorldFlag(w.FlagId, w.LocationId);
                return One(new Term($"{w.FlagId} at {w.LocationId}", value.ToString(), Range(w.Min, w.Max), value >= w.Min && value <= w.Max));
            }
            case RelationshipValueObjective r:
            {
                int value = facts.Relationship(r.NpcId, r.Dimension);
                return One(new Term($"{r.NpcId} {r.Dimension}", value.ToString(), Range(r.Min, r.Max), value >= r.Min && value <= r.Max));
            }
            case WaitUntil w:
            {
                long waited = tick - activatedTick;
                return One(new Term("ticks since active", waited.ToString(), $">= {w.AfterTicks}", waited >= w.AfterTicks));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown objective type");
        }
    }

    /// <summary>A deed counts toward every active objective of the quest that is waiting for exactly that deed.</summary>
    public static QuestState Record(QuestDefinition quest, QuestState state, Deed deed)
    {
        if (state.Status != QuestStatus.Active)
            return state;
        var objectives = state.Objectives.Select(o =>
            o.Status == ObjectiveStatus.Active && quest.Objectives.TryGetValue(o.Id, out var definition) && Counts(definition.Condition, deed)
                ? o with { Progress = checked(o.Progress + deed.Count) }
                : o).ToImmutableArray();
        return state with { Objectives = objectives };
    }

    private static bool Counts(ObjectiveCondition condition, Deed deed) => condition switch
    {
        CraftItem c => deed.Kind == DeedKind.Crafted && deed.Subject == c.ItemId && deed.Quality >= c.QualityMin,
        HarvestResource h => deed.Kind == DeedKind.Harvested && deed.Subject == h.ResourceId,
        KillCreature k => deed.Kind == DeedKind.Killed && deed.Subject == k.CreatureId,
        DeliverItem d => deed.Kind == DeedKind.Delivered && deed.Subject == d.ItemId && deed.NpcId == d.NpcId,
        _ => false,
    };

    /// <summary>
    /// One evaluation of an active quest at <paramref name="tick"/>. Active objectives are evaluated in authored order; one that holds
    /// is satisfied, which activates what follows it, and the step repeats until nothing more changes - so a run of objectives the
    /// world already satisfies completes in one tick. A timed objective that did not hold in time then fails. A satisfied objective
    /// with nowhere to go completes the quest; last, a quest still active fails if any <c>fail_if</c> predicate holds. Satisfied,
    /// failed and closed objectives never change again: re-evaluating a satisfied objective changes nothing.
    /// </summary>
    public static QuestStep Advance(QuestDefinition quest, QuestState state, IQuestFacts facts, long tick)
    {
        var transitions = ImmutableArray.CreateBuilder<Transition>();
        var evaluations = new Dictionary<string, Evaluation>(StringComparer.Ordinal);
        if (state.Status != QuestStatus.Active)
            return new QuestStep(state, transitions.ToImmutable(), ImmutableArray<(string, Evaluation)>.Empty);

        var objectives = state.Objectives.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var status = QuestStatus.Active;
        string? endedBy = null;

        bool changed = true;
        for (int pass = 0; changed && status == QuestStatus.Active && pass <= quest.Order.Length; pass++)
        {
            changed = false;
            foreach (var definition in quest.Order)
            {
                if (status != QuestStatus.Active)
                    break;
                if (!objectives.TryGetValue(definition.Id, out var current) || current.Status != ObjectiveStatus.Active)
                    continue;
                var evaluation = Evaluate(definition.Condition, facts, current.Progress, current.ActivatedTick, tick);
                evaluations[definition.Id] = evaluation;
                if (!evaluation.Holds)
                    continue;
                changed = true;
                Satisfy(definition);
            }
        }

        // Timers, after every predicate had its chance this tick: holding at the deadline counts.
        foreach (var definition in quest.Order)
        {
            if (status != QuestStatus.Active)
                break;
            if (definition.TimeLimitTicks is not { } limit || !objectives.TryGetValue(definition.Id, out var current)
                || current.Status != ObjectiveStatus.Active || tick - current.ActivatedTick < limit)
                continue;
            objectives[definition.Id] = current with { Status = ObjectiveStatus.Failed, EndedTick = tick };
            transitions.Add(new Transition(TransitionKind.Failed, definition.Id, null));
            if (definition.OnFail is null || definition.OnFail == FailQuest)
                End(QuestStatus.Failed, definition.Id);
            else
                Activate(definition.OnFail);
        }

        if (status == QuestStatus.Active)
        {
            for (int i = 0; i < quest.FailIf.Length; i++)
            {
                if (!Evaluate(quest.FailIf[i], facts, 0, state.StartedTick, tick).Holds)
                    continue;
                End(QuestStatus.Failed, $"fail_if[{i}]");
                break;
            }
        }

        var next = state with
        {
            Status = status,
            EndedTick = status == QuestStatus.Active ? null : tick,
            EndedBy = endedBy,
            Objectives = quest.Order.Where(o => objectives.ContainsKey(o.Id)).Select(o => objectives[o.Id])
                .Concat(objectives.Values.Where(o => !quest.Objectives.ContainsKey(o.Id)).OrderBy(o => o.Id, StringComparer.Ordinal))
                .ToImmutableArray(),
        };
        // Every predicate evaluated this tick, its last evaluation, in authored order.
        var evaluated = quest.Order.Where(o => evaluations.ContainsKey(o.Id)).Select(o => (o.Id, evaluations[o.Id])).ToImmutableArray();
        return new QuestStep(next, transitions.ToImmutable(), evaluated);

        void Satisfy(ObjectiveDefinition definition)
        {
            objectives[definition.Id] = objectives[definition.Id] with { Status = ObjectiveStatus.Satisfied, EndedTick = tick };
            transitions.Add(new Transition(TransitionKind.Satisfied, definition.Id, null));

            // A branch point whose alternatives include this one: this is the branch taken, and the others close.
            foreach (var point in quest.Order.Where(p => p.FirstBranch && p.Next.Contains(definition.Id)
                         && objectives.TryGetValue(p.Id, out var s) && s.Status == ObjectiveStatus.Satisfied))
            {
                transitions.Add(new Transition(TransitionKind.BranchTaken, point.Id, definition.Id));
                foreach (string other in point.Next.Where(n => n != definition.Id))
                    Close(other);
            }

            if (definition.Terminal)
            {
                End(QuestStatus.Completed, definition.Id);
                return;
            }
            foreach (string follower in definition.Next)
                Activate(follower);
        }

        void Activate(string id)
        {
            if (objectives.ContainsKey(id) || !quest.Objectives.TryGetValue(id, out var definition))
                return;
            if (definition.AllOf.Any(a => !objectives.TryGetValue(a, out var s) || s.Status != ObjectiveStatus.Satisfied))
                return;
            objectives[id] = new ObjectiveState(id, ObjectiveStatus.Active, tick, null, 0);
            transitions.Add(new Transition(TransitionKind.Activated, id, null));
        }

        void Close(string id)
        {
            // An alternative not yet reached is closed too, so nothing can reach it later.
            if (objectives.TryGetValue(id, out var s))
            {
                if (s.Status != ObjectiveStatus.Active)
                    return;
                objectives[id] = s with { Status = ObjectiveStatus.Closed, EndedTick = tick };
            }
            else
            {
                objectives[id] = new ObjectiveState(id, ObjectiveStatus.Closed, tick, tick, 0);
            }
            transitions.Add(new Transition(TransitionKind.Closed, id, null));
        }

        void End(QuestStatus outcome, string by)
        {
            status = outcome;
            endedBy = by;
            foreach (var (id, s) in objectives.ToList())
            {
                if (s.Status != ObjectiveStatus.Active)
                    continue;
                objectives[id] = s with { Status = ObjectiveStatus.Closed, EndedTick = tick };
                transitions.Add(new Transition(TransitionKind.Closed, id, null));
            }
            transitions.Add(new Transition(outcome == QuestStatus.Completed ? TransitionKind.Completed : TransitionKind.QuestFailed, by, null));
        }
    }

    private static Evaluation One(Term term) => new(term.Holds, ImmutableArray.Create(term));

    private static Term Is(string label, bool holds) => new(label, holds ? "yes" : "no", "yes", holds);

    private static string QualityNote(int qualityMin) => qualityMin > Quality.Crude ? $" (quality >= {qualityMin})" : "";

    private static string Metres(long mm) => (mm / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m";

    private static string Range(long min, long max) =>
        (min, max) switch
        {
            (long.MinValue, _) or (int.MinValue, _) => $"<= {max}",
            (_, long.MaxValue) or (_, int.MaxValue) => $">= {min}",
            _ when min == max => $"= {min}",
            _ => $"in [{min}, {max}]",
        };
}
