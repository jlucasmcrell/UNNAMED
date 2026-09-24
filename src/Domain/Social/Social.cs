// UNNAMED Domain - NPCs, relationships and dialogue as data (SYSTEMS.md S-24, S-26, S-28; DATA_MODEL.md §4.5, §4.12; M4)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Social;

/// <summary>A named NPC as the simulation uses it (DATA_MODEL.md §4.5, Phase 1's subset): who they are, their role, what they offer.</summary>
public sealed record NpcDefinition(string Id, string Name, string Role, ImmutableArray<string> Services, string? MerchantId, string? DialogueId)
{
    public bool Offers(string service) => Services.Contains(service);
}

/// <summary>The services Phase 1 builds (DATA_MODEL.md §4.5's list is longer; the rest arrive with their systems).</summary>
public static class NpcServices
{
    public const string Trade = "trade";

    public static readonly ImmutableArray<string> Built = ImmutableArray.Create(Trade);
}

/// <summary>
/// What one NPC thinks of the player, on named dimensions (SYSTEMS.md S-26): there is no single good-or-evil meter. Each value is
/// held in [-100, 100]; a value of 0 is not stored.
/// </summary>
public static class Relationships
{
    public const int Min = -100;
    public const int Max = 100;

    public static readonly ImmutableArray<string> Dimensions = ImmutableArray.Create("affection", "fear", "grudge", "respect", "trust");

    public static bool IsDimension(string dimension) => Dimensions.Contains(dimension);

    public static int Clamp(int value) => Math.Clamp(value, Min, Max);
}

// ── dialogue ─────────────────────────────────────────────────────────────────

/// <summary>A conversation graph (DATA_MODEL.md §4.12): the node it opens at and its nodes by ID.</summary>
public sealed record DialogueDefinition(string Id, ImmutableArray<string> Participants, string Root, ImmutableSortedDictionary<string, DialogueNode> Nodes);

/// <summary>
/// One line of a conversation and the replies to it. A <see cref="Once"/> node is spent once visited: arriving at it again goes
/// to <see cref="NextIfExhausted"/>, or ends the conversation if it has none. <see cref="Next"/>, on a node without replies,
/// continues without a choice.
/// </summary>
public sealed record DialogueNode(string Id, string Text, ImmutableArray<DialogueChoice> Choices, string? Next, bool Once, string? NextIfExhausted);

/// <summary>A reply: offered while its conditions hold; choosing it runs its consequences, then goes to <see cref="Next"/> (none ends).</summary>
public sealed record DialogueChoice(string Id, string Text, ImmutableArray<DialogueCondition> Conditions,
    ImmutableArray<DialogueConsequence> Consequences, string? Next);

/// <summary>A closed-set condition over world state (DATA_MODEL.md §4.12; Phase 1 builds six kinds).</summary>
public abstract record DialogueCondition;

/// <summary><c>visited</c>: whether a node of this conversation has been visited - or, <c>not: true</c>, has not.</summary>
public sealed record VisitedCondition(string NodeId, bool Negated) : DialogueCondition;

/// <summary><c>world_state</c>: a world flag's value in [min, max]. Dialogue reads and writes flags in the speaker's cell.</summary>
public sealed record WorldStateCondition(string FlagId, long Min, long Max) : DialogueCondition;

/// <summary><c>has_item</c>: at least <c>count</c> carried, at least <c>quality_min</c> good - or, <c>not: true</c>, fewer.</summary>
public sealed record HasItemCondition(string ItemId, int Count, int QualityMin, bool Negated) : DialogueCondition;

/// <summary><c>relationship</c>: what the NPC thinks of the player on one dimension, in [min, max].</summary>
public sealed record RelationshipCondition(string NpcId, string Dimension, int Min, int Max) : DialogueCondition;

/// <summary><c>skill</c>: a skill at least this level.</summary>
public sealed record SkillCondition(string SkillId, int Min) : DialogueCondition;

/// <summary><c>level</c>: the character at least this level.</summary>
public sealed record LevelCondition(int Min) : DialogueCondition;

/// <summary>A closed-set consequence (DATA_MODEL.md §4.12): dialogue emits these as commands and never writes state itself.</summary>
public abstract record DialogueConsequence;

/// <summary><c>transfer_item</c>: the speaker gives the player an item, or takes one (<c>to: npc</c>).</summary>
public sealed record TransferItemConsequence(string ItemId, int Count, bool ToPlayer) : DialogueConsequence;

/// <summary><c>give_recipe</c>: the speaker teaches a recipe - a learning event from a teacher (PROGRESSION.md §4.4).</summary>
public sealed record GiveRecipeConsequence(string RecipeId) : DialogueConsequence;

/// <summary><c>set_world_flag</c>: a flag in the speaker's cell takes a value.</summary>
public sealed record SetWorldFlagConsequence(string FlagId, long Value) : DialogueConsequence;

/// <summary><c>record_relationship_event</c>: what the speaker thinks of the player moves on one dimension, for a named reason.</summary>
public sealed record RelationshipEventConsequence(string NpcId, string Dimension, int Delta, string EventKey) : DialogueConsequence;

/// <summary><c>open_service</c>: the speaker opens a service to the player (Phase 1: trade).</summary>
public sealed record OpenServiceConsequence(string Service) : DialogueConsequence;

/// <summary>What a condition may ask of the world, answered by the simulation (read only).</summary>
public interface IDialogueFacts
{
    bool Visited(string dialogueId, string nodeId);

    long WorldFlag(string flagId);

    int Carried(string itemId, int qualityMin);

    int Relationship(string npcId, string dimension);

    int SkillLevel(string skillId);

    int Level { get; }
}

public static class DialogueRules
{
    public static bool Holds(DialogueCondition condition, string dialogueId, IDialogueFacts facts) => condition switch
    {
        VisitedCondition v => facts.Visited(dialogueId, v.NodeId) != v.Negated,
        WorldStateCondition w => facts.WorldFlag(w.FlagId) is var value && value >= w.Min && value <= w.Max,
        HasItemCondition h => facts.Carried(h.ItemId, h.QualityMin) >= h.Count != h.Negated,
        RelationshipCondition r => facts.Relationship(r.NpcId, r.Dimension) is var value && value >= r.Min && value <= r.Max,
        SkillCondition s => facts.SkillLevel(s.SkillId) >= s.Min,
        LevelCondition l => facts.Level >= l.Min,
        _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown dialogue condition"),
    };

    public static bool Offered(DialogueChoice choice, string dialogueId, IDialogueFacts facts) =>
        choice.Conditions.All(c => Holds(c, dialogueId, facts));

    /// <summary>
    /// The node a conversation arrives at: a spent once-node passes on to its <c>next_if_exhausted</c>, and so on; null when
    /// the way ends at a spent node with nowhere to pass on to. The content lint refuses a loop of once-nodes.
    /// </summary>
    public static DialogueNode? Arrive(DialogueDefinition dialogue, string nodeId, Func<string, bool> visited)
    {
        var node = dialogue.Nodes[nodeId];
        for (int hops = 0; node.Once && visited(node.Id); hops++)
        {
            if (node.NextIfExhausted is not { } next || hops > dialogue.Nodes.Count)
                return null;
            node = dialogue.Nodes[next];
        }
        return node;
    }
}
