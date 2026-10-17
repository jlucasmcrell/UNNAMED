// UNNAMED Domain - quests as declarative objective graphs over world state (DATA_MODEL.md §4.11; SYSTEMS.md S-29; D-07; M5)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Companions;
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

    /// <summary>A repeatable quest (<c>repeat_policy</c>) may be taken again this long after it last ended; null when it is taken once.</summary>
    public long? RepeatAfterTicks { get; init; }

    /// <summary>Which part of the journal it belongs to (<c>track</c>): the main story, a side quest, or standing bounty work.</summary>
    public QuestTrack Track { get; init; } = QuestTrack.Side;

    /// <summary>The act of the main story it belongs to (<c>act</c>); null off the main track.</summary>
    public int? Act { get; init; }

    /// <summary>Started for every new character, before anything is said to them (<c>on_new_game</c>).</summary>
    public bool OnNewGame { get; init; }

    /// <summary>
    /// A bounty anyone may take (<c>competitive</c>): the first actor to finish it - the character or another - claims it, and it is
    /// closed to everyone else until it is posted again.
    /// </summary>
    public bool Competitive { get; init; }
}

/// <summary>The journal's sections (<c>track</c>).</summary>
public enum QuestTrack { Main, Side, Bounty }

public static class QuestTracks
{
    public static string Key(QuestTrack track) => track switch
    {
        QuestTrack.Main => "main",
        QuestTrack.Side => "side",
        QuestTrack.Bounty => "bounty",
        _ => throw new ArgumentOutOfRangeException(nameof(track)),
    };

    public static QuestTrack? Parse(string key) => key switch
    {
        "main" => QuestTrack.Main,
        "side" => QuestTrack.Side,
        "bounty" => QuestTrack.Bounty,
        _ => null,
    };
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

    /// <summary>What happens in the world when the objective is satisfied (<c>effects</c>), in authored order.</summary>
    public ImmutableArray<QuestEffect> Effects { get; init; } = ImmutableArray<QuestEffect>.Empty;

    public bool Terminal => Next.IsEmpty;
}

/// <summary>A closed-set effect of an objective being satisfied, applied through its owner's command once.</summary>
public abstract record QuestEffect
{
    public abstract string Kind { get; }
}

/// <summary>
/// <c>choice</c>: a choice the story remembers - a namespaced world flag (<c>world.main.carrier</c>) set in the cell that holds the place,
/// and the line the journal's Recent list keeps for it.
/// </summary>
public sealed record ChoiceEffect(string FlagId, string LocationId, long Value, string Line) : QuestEffect
{
    public override string Kind => "choice";
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

/// <summary>
/// <c>defeat_creature</c> / <c>defeat_boss</c>: the creature's defeat is on record - a world flag written from its death, by whoever's
/// hand, in the cell that holds <see cref="LocationId"/>. A death before the quest counts. A boss is a creature that does not return.
/// </summary>
public sealed record DefeatCreature(string CreatureId, string FlagId, string LocationId, bool Boss) : ObjectiveCondition
{
    public override string Type => Boss ? "defeat_boss" : "defeat_creature";
}

/// <summary><c>companion_present</c>: the NPC is the character's companion and on their feet - and, naming an order, under it.</summary>
public sealed record CompanionPresent(string NpcId, CompanionOrder? Order) : ObjectiveCondition
{
    public override string Type => "companion_present";
}

/// <summary><c>dialogue_choice</c>: this reply, at this line of the NPC's conversation, was given while the objective was active.</summary>
public sealed record ChoiceGiven(string NpcId, string DialogueId, string NodeId, string ChoiceId) : ObjectiveCondition
{
    public override string Type => "dialogue_choice";
    public override bool CountsDeeds => true;

    /// <summary>The deed's subject: the reply, by its conversation and line.</summary>
    public string Subject => ChoiceSubject(DialogueId, NodeId, ChoiceId);

    public static string ChoiceSubject(string dialogueId, string nodeId, string choiceId) => $"{dialogueId}/{nodeId}/{choiceId}";
}

/// <summary><c>travel_to</c>: the character arrived at the Waystone by the stones while the objective was active.</summary>
public sealed record TravelTo(string StoneKey) : ObjectiveCondition
{
    public override string Type => "travel_to";
    public override bool CountsDeeds => true;
}

/// <summary><c>waystone_known</c>: the character knows the Waystone - touched or woken; an earlier touch counts.</summary>
public sealed record WaystoneKnown(string StoneKey) : ObjectiveCondition
{
    public override string Type => "waystone_known";
}

/// <summary>The vocabulary: what Phase 1 builds, and the rest of DATA_MODEL.md §4.11's closed set, which arrives with its systems.</summary>
public static class ObjectiveTypes
{
    public static readonly ImmutableArray<string> Built = ImmutableArray.Create(
        "talk_to", "visit_location", "explore_location", "acquire_item", "craft_item", "harvest_resource", "kill_creature",
        "deliver_item", "world_state", "relationship_value", "wait_until",
        "defeat_creature", "defeat_boss", "companion_present", "dialogue_choice", "travel_to", "waystone_known");

    /// <summary>Named by the closed set and not built yet, each with what it waits for.</summary>
    public static readonly ImmutableSortedDictionary<string, string> NotBuilt = new Dictionary<string, string>
    {
        ["discover_secret"] = "secrets (S-30)",
        ["use_recipe"] = "recipe-use records; craft_item instead",
        ["construct_building"] = "building (M7)",
        ["upgrade_settlement"] = "settlement simulation (M10)",
        ["kill_named"] = "named spawns (M8)",
        ["survive_encounter"] = "encounters (M8)",
        ["escort_npc"] = "NPCs that travel",
        ["defend_location"] = "waves (M8)",
        ["solve_puzzle"] = "puzzles (M8)",
        ["faction_reputation"] = "factions (M7)",
        ["faction_state"] = "factions (M7)",
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

    /// <summary>A condition's word for a quest that would start now: never started, or a repeatable one ended long enough ago.</summary>
    public const string Available = "available";

    public static readonly ImmutableArray<string> QuestStates = ImmutableArray.Create(NotStarted, "active", "completed", "failed", Available);

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

    /// <summary>The character knows the Waystone.</summary>
    bool KnowsStone(string stoneKey) => false;

    /// <summary>The NPC's order when they are the character's companion and on their feet; null otherwise.</summary>
    CompanionOrder? CompanionPresent(string npcId) => null;
}

public enum DeedKind { Crafted, Harvested, Killed, Delivered, Chose, Travelled }

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

    /// <summary>Whether the quest would start now: never started, or repeatable and ended at least its repeat time ago.</summary>
    public static bool CanStart(QuestDefinition quest, QuestState? state, long tick) =>
        state is null || state.Status != QuestStatus.Active && quest.RepeatAfterTicks is { } after && state.EndedTick is { } ended && tick >= ended + after;

    /// <summary><see cref="QuestState.EndedBy"/> of a competitive bounty another actor finished while the character held it.</summary>
    public const string LostTo = "lost_to:";

    /// <summary><see cref="QuestState.EndedBy"/> of a competitive bounty another actor finished while the character did not hold it.</summary>
    public const string ClaimedBy = "claimed_by:";

    /// <summary>Who else finished a competitive bounty first, or null when nobody did.</summary>
    public static string? Claimant(QuestState state) =>
        state.EndedBy is { } by && (by.StartsWith(LostTo, StringComparison.Ordinal) || by.StartsWith(ClaimedBy, StringComparison.Ordinal))
            ? by[(by.IndexOf(':') + 1)..]
            : null;

    /// <summary>Whether the record is of the character's own doing: false for a bounty someone else claimed while they never held it.</summary>
    public static bool Held(QuestState state) => state.EndedBy?.StartsWith(ClaimedBy, StringComparison.Ordinal) != true;

    /// <summary>Whether another actor may claim the bounty now: it is competitive, and held by the character or posted and open.</summary>
    public static bool CanClaim(QuestDefinition quest, QuestState? state, long tick) =>
        quest.Competitive && (state is { Status: QuestStatus.Active } || CanStart(quest, state, tick));

    /// <summary>
    /// Another actor finished a competitive bounty first: the character's hold on it, if they had one, fails with everything still active
    /// closed; otherwise the posting is recorded as taken. Either way it is open again only once its repeat time has passed.
    /// </summary>
    public static QuestState Claim(QuestDefinition quest, QuestState? state, string claimantId, long tick)
    {
        if (!CanClaim(quest, state, tick))
            throw new InvalidOperationException($"{quest.Id} cannot be claimed now");
        if (state is { Status: QuestStatus.Active })
            return state with
            {
                Status = QuestStatus.Failed,
                EndedTick = tick,
                EndedBy = LostTo + claimantId,
                Objectives = state.Objectives.Select(o => o.Status == ObjectiveStatus.Active ? o with { Status = ObjectiveStatus.Closed, EndedTick = tick } : o)
                    .ToImmutableArray(),
            };
        return new QuestState(quest.Id, QuestStatus.Failed, tick, tick, ClaimedBy + claimantId,
            ImmutableArray.Create(new ObjectiveState(quest.Entry, ObjectiveStatus.Closed, tick, tick, 0)));
    }

    /// <summary>Where an objective sends the character, when it names a place, a person or a stone; null when it names none.</summary>
    public static QuestTarget? Target(ObjectiveCondition condition) => condition switch
    {
        VisitLocation v => new QuestTarget(TargetKind.Location, v.LocationId),
        ExploreLocation e => new QuestTarget(TargetKind.Location, e.LocationId),
        WorldStateObjective w => new QuestTarget(TargetKind.Location, w.LocationId),
        DefeatCreature d => new QuestTarget(TargetKind.Location, d.LocationId),
        TalkTo t => new QuestTarget(TargetKind.Npc, t.NpcId),
        DeliverItem d => new QuestTarget(TargetKind.Npc, d.NpcId),
        ChoiceGiven d => new QuestTarget(TargetKind.Npc, d.NpcId),
        TravelTo t => new QuestTarget(TargetKind.Stone, t.StoneKey),
        WaystoneKnown w => new QuestTarget(TargetKind.Stone, w.StoneKey),
        _ => null,
    };

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
            case DefeatCreature d:
            {
                long value = facts.WorldFlag(d.FlagId, d.LocationId);
                return One(new Term($"{d.CreatureId} defeated ({d.FlagId} at {d.LocationId})", value.ToString(), ">= 1", value >= 1));
            }
            case CompanionPresent c:
            {
                var order = facts.CompanionPresent(c.NpcId);
                string value = order is { } o ? CompanionKeys.Key(o) : "absent";
                return One(new Term($"{c.NpcId} with the character", value, c.Order is { } wanted ? CompanionKeys.Key(wanted) : "present",
                    order is not null && (c.Order is null || c.Order == order)));
            }
            case ChoiceGiven d:
                return One(new Term($"'{d.ChoiceId}' given at '{d.NodeId}' ({d.DialogueId}) while active", progress.ToString(), ">= 1", progress >= 1));
            case TravelTo t:
                return One(new Term($"arrived at {t.StoneKey} by the stones while active", progress.ToString(), ">= 1", progress >= 1));
            case WaystoneKnown w:
                return One(Is($"{w.StoneKey} known", facts.KnowsStone(w.StoneKey)));
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
        ChoiceGiven c => deed.Kind == DeedKind.Chose && deed.Subject == c.Subject && deed.NpcId == c.NpcId,
        TravelTo t => deed.Kind == DeedKind.Travelled && deed.Subject == t.StoneKey,
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

// ── targets and the journal ────────────────────────────────────────────────────

public enum TargetKind { Location, Npc, Stone }

/// <summary>What an objective points at: a place, a person, or a Waystone, by its ID or key.</summary>
public sealed record QuestTarget(TargetKind Kind, string Ref);

public enum JournalEntryKind { Started, Progress, Choice, Completed, Failed, Claimed, Away }

/// <summary>One line of the journal's Recent list, at the tick it happened.</summary>
public sealed record JournalEntry(long Tick, JournalEntryKind Kind, string Text)
{
    /// <summary>The quest the line is about; null for a line from elsewhere (the away log).</summary>
    public string? QuestId { get; init; }
}

/// <summary>
/// The journal's Recent list, derived from what the quest records already hold - when each was taken up, each objective's end, the choices
/// made on the way, how it ended - so it needs no record of its own.
/// </summary>
public static class QuestJournal
{
    /// <summary>How many lines the Recent list shows.</summary>
    public const int RecentLength = 20;

    /// <summary>Every line one quest's record gives, oldest first; <paramref name="name"/> names a claimant.</summary>
    public static IEnumerable<JournalEntry> Entries(QuestDefinition quest, QuestState state, Func<string, string> name)
    {
        if (!QuestRules.Held(state))
            yield break;
        yield return new JournalEntry(state.StartedTick, JournalEntryKind.Started, $"Took up {quest.Title}.") { QuestId = quest.Id };
        foreach (var objective in quest.Order)
        {
            if (state.Objective(objective.Id) is not { EndedTick: { } at } s)
                continue;
            if (s.Status == ObjectiveStatus.Satisfied)
            {
                yield return new JournalEntry(at, JournalEntryKind.Progress, objective.Description) { QuestId = quest.Id };
                foreach (var choice in objective.Effects.OfType<ChoiceEffect>())
                    yield return new JournalEntry(at, JournalEntryKind.Choice, choice.Line) { QuestId = quest.Id };
            }
            else if (s.Status == ObjectiveStatus.Failed)
            {
                yield return new JournalEntry(at, JournalEntryKind.Failed, $"Too late: {objective.Description}") { QuestId = quest.Id };
            }
        }
        if (state.EndedTick is not { } ended)
            yield break;
        if (QuestRules.Claimant(state) is { } claimant)
            yield return new JournalEntry(ended, JournalEntryKind.Claimed, $"{name(claimant)} finished {quest.Title} first.") { QuestId = quest.Id };
        else if (state.Status == QuestStatus.Completed)
            yield return new JournalEntry(ended, JournalEntryKind.Completed, $"Finished {quest.Title}.") { QuestId = quest.Id };
        else
            yield return new JournalEntry(ended, JournalEntryKind.Failed, $"Failed {quest.Title}.") { QuestId = quest.Id };
    }

    /// <summary>The newest lines first, at most <paramref name="length"/>: within a tick, what came later first.</summary>
    public static ImmutableArray<JournalEntry> Recent(IEnumerable<JournalEntry> entries, int length = RecentLength) =>
        entries.Select((e, i) => (Entry: e, Index: i)).OrderByDescending(x => x.Entry.Tick).ThenByDescending(x => x.Index)
            .Take(length).Select(x => x.Entry).ToImmutableArray();
}
