// UNNAMED Domain - NPCs, relationships and dialogue as data (SYSTEMS.md S-24, S-26, S-28; DATA_MODEL.md §4.5, §4.12; M4)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Quests;

namespace UNNAMED.Domain.Social;

/// <summary>A named NPC as the simulation uses it (DATA_MODEL.md §4.5, Phase 1's subset): who they are, their role, what they offer.</summary>
public sealed record NpcDefinition(string Id, string Name, string Role, ImmutableArray<string> Services, string? MerchantId, string? DialogueId)
{
    /// <summary>Authored sex for named-body and groom selection; generated residents may omit it.</summary>
    public string? Sex { get; init; }

    /// <summary>Set for an NPC who can join the character (M6): how they fight.</summary>
    public CompanionProfile? Companion { get; init; }

    /// <summary>The faction the NPC belongs to (WP6), or none. Definition data: nothing can change it, so it is not saved.</summary>
    public string? FactionId { get; init; }

    /// <summary>The people the NPC belongs to (<c>people_ref</c>), or none recorded: heritage items pass only between one people (owner, 2026-10-24).</summary>
    public string? PeopleId { get; init; }

    /// <summary>Set for an NPC who can be hired (WP15a): the roles they take on and how they fight. Never on someone who can join as a companion.</summary>
    public HirelingProfile? Hireling { get; init; }

    /// <summary>What the NPC teaches, on what terms (WP9, C10): their <c>teaches</c> block, in content order. Empty for someone who teaches nothing.</summary>
    public ImmutableArray<TeachingOffer> Teaches { get; init; } = ImmutableArray<TeachingOffer>.Empty;

    /// <summary>A party member's values (WP15b): the deeds that move what they think of the character, and what they say of them.</summary>
    public ImmutableArray<ValueReaction> Values { get; init; } = ImmutableArray<ValueReaction>.Empty;

    /// <summary>The authored native creator look, constrained by its people when drawn.</summary>
    public CharacterAppearance? Appearance { get; init; }

    public bool Offers(string service) => Services.Contains(service);
}

/// <summary>The services Phase 1 builds (DATA_MODEL.md §4.5's list is longer; the rest arrive with their systems).</summary>
public static class NpcServices
{
    public const string Trade = "trade";

    /// <summary>A trader who mends gear for coin (WP3b): the repair service, out of the same purse as their trade.</summary>
    public const string Repair = "repair";

    public static readonly ImmutableArray<string> Built = ImmutableArray.Create(Trade, Repair);
}

/// <summary>
/// What one NPC thinks of the player, on named dimensions (SYSTEMS.md S-26): there is no single good-or-evil meter. Each value is
/// held in [-100, 100]; a value of 0 is not stored. <c>loyalty</c> (WP15b; canon EXT/COMPANIONS :38-52) is what keeps a party member
/// at the character's side; attraction and ideology come only with romance (DN10).
/// </summary>
public static class Relationships
{
    public const int Min = -100;
    public const int Max = 100;

    public static readonly ImmutableArray<string> Dimensions = ImmutableArray.Create("affection", "fear", "grudge", "loyalty", "respect", "trust");

    public static bool IsDimension(string dimension) => Dimensions.Contains(dimension);

    public static int Clamp(int value) => Math.Clamp(value, Min, Max);
}

// ── dialogue ─────────────────────────────────────────────────────────────────

/// <summary>A conversation graph (DATA_MODEL.md §4.12): the node it opens at and its nodes by ID.</summary>
public sealed record DialogueDefinition(string Id, ImmutableArray<string> Participants, string Root, ImmutableSortedDictionary<string, DialogueNode> Nodes)
{
    /// <summary>Lines said aloud, unprompted, to a character passing by: each one sayable while its conditions hold.</summary>
    public ImmutableArray<DialogueBark> Barks { get; init; } = ImmutableArray<DialogueBark>.Empty;

    /// <summary>
    /// Party banter (WP15b): lines a party member says on the road, unprompted, while their conditions hold - who else travels with them
    /// (<c>companion_present</c>), where they are (<c>near</c>), what the character did lately (<c>deed</c> with <c>within_s</c>). The
    /// party's voice says at most one line in a while (<c>config.party</c>'s <c>banter</c>).
    /// </summary>
    public ImmutableArray<DialogueBark> Banter { get; init; } = ImmutableArray<DialogueBark>.Empty;
}

/// <summary>A bark: a line with no replies, said in passing while its conditions hold (the same conditions a reply takes).</summary>
public sealed record DialogueBark(string Text, ImmutableArray<DialogueCondition> Conditions);

/// <summary>
/// One line of a conversation and the replies to it. A <see cref="Once"/> node is spent once visited: arriving at it again goes
/// to <see cref="NextIfExhausted"/>, or ends the conversation if it has none. <see cref="Next"/>, on a node without replies,
/// continues without a choice.
/// </summary>
public sealed record DialogueNode(string Id, string Text, ImmutableArray<DialogueChoice> Choices, string? Next, bool Once, string? NextIfExhausted)
{
    /// <summary>
    /// E-6: other wordings of the same line, each said instead of <see cref="Text"/> while its conditions hold; the first that holds is
    /// said. The node, its replies and where it goes are the same whichever wording is said.
    /// </summary>
    public ImmutableArray<DialogueVariant> Variants { get; init; } = ImmutableArray<DialogueVariant>.Empty;
}

/// <summary>A wording of a node's line (E-6), said while its conditions hold (the same conditions a reply takes).</summary>
public sealed record DialogueVariant(string Text, ImmutableArray<DialogueCondition> Conditions);

/// <summary>A line as it is said: its derived line ID (E-10) and its words.</summary>
public sealed record SpokenLine(string LineId, string Text);

/// <summary>
/// Stable line IDs (E-10), derived from where a line stands in content and never written into it: <c>&lt;dialogue&gt;/&lt;node&gt;</c> for a
/// node's line, <c>&lt;dialogue&gt;/&lt;node&gt;.&lt;n&gt;</c> for its n-th variant, and <c>bark.&lt;npc&gt;.&lt;n&gt;</c> for the n-th bark of an NPC's
/// conversation, counting from 1 and without the <c>npc.</c> prefix. They name voice-over files (<c>vo/&lt;line-id&gt;.ogg</c>).
/// </summary>
public static class LineIds
{
    public static string Node(string dialogueId, string nodeId, int variant = 0) =>
        variant == 0 ? $"{dialogueId}/{nodeId}" : $"{dialogueId}/{nodeId}.{variant}";

    public static string Bark(string npcId, int number) => $"bark.{Short(npcId)}.{number}";

    /// <summary>The n-th banter line of an NPC's conversation (WP15b), counting from 1: <c>banter.&lt;npc&gt;.&lt;n&gt;</c>.</summary>
    public static string Banter(string npcId, int number) => $"banter.{Short(npcId)}.{number}";

    /// <summary>The n-th value reaction of an NPC (WP15b), counting from 1: <c>reaction.&lt;npc&gt;.&lt;n&gt;</c>.</summary>
    public static string Reaction(string npcId, int number) => $"reaction.{Short(npcId)}.{number}";

    private static string Short(string npcId) => npcId.StartsWith("npc.", StringComparison.Ordinal) ? npcId["npc.".Length..] : npcId;
}

/// <summary>A reply: offered while its conditions hold; choosing it runs its consequences, then goes to <see cref="Next"/> (none ends).</summary>
public sealed record DialogueChoice(string Id, string Text, ImmutableArray<DialogueCondition> Conditions,
    ImmutableArray<DialogueConsequence> Consequences, string? Next);

/// <summary>A closed-set condition over world state (DATA_MODEL.md §4.12; Phase 1 builds seven kinds, M5 and M6 two more, E-7 <c>deed</c>, WP6 <c>standing</c> and <c>act_done</c>, WP15b <c>near</c>).</summary>
public abstract record DialogueCondition;

/// <summary>
/// <c>visited</c>: whether a node of this conversation has been visited - or, <c>not: true</c>, has not. Naming
/// <see cref="DialogueId"/> (M6) asks about a line of another conversation: whether Tavar has been spoken to, asked of Sel.
/// </summary>
public sealed record VisitedCondition(string NodeId, bool Negated) : DialogueCondition
{
    public string? DialogueId { get; init; }
}

/// <summary>
/// <c>world_state</c>: a world flag's value in [min, max]. Dialogue reads and writes flags in the speaker's cell - or, reading, in the cell
/// of a named place (<c>location_ref</c>), so a speaker can know of something done elsewhere (the Phase-1 technical audit, L-17).
/// </summary>
public sealed record WorldStateCondition(string FlagId, long Min, long Max) : DialogueCondition
{
    public string? LocationId { get; init; }
}

/// <summary><c>has_item</c>: at least <c>count</c> carried, at least <c>quality_min</c> good - or, <c>not: true</c>, fewer.</summary>
public sealed record HasItemCondition(string ItemId, int Count, int QualityMin, bool Negated) : DialogueCondition;

/// <summary><c>relationship</c>: what the NPC thinks of the player on one dimension, in [min, max].</summary>
public sealed record RelationshipCondition(string NpcId, string Dimension, int Min, int Max) : DialogueCondition;

/// <summary><c>skill</c>: a skill at least this level.</summary>
public sealed record SkillCondition(string SkillId, int Min) : DialogueCondition;

/// <summary><c>level</c>: the character at least this level.</summary>
public sealed record LevelCondition(int Min) : DialogueCondition;

/// <summary>
/// <c>quest_state</c> (M5): a quest is <c>not_started</c>, <c>active</c>, <c>completed</c> or <c>failed</c> - or, naming an objective,
/// the objective is <c>not_reached</c>, <c>active</c>, <c>satisfied</c>, <c>failed</c> or <c>closed</c>; <c>not: true</c> inverts it.
/// </summary>
public sealed record QuestStateCondition(string QuestId, string? ObjectiveId, string State, bool Negated) : DialogueCondition;

/// <summary>
/// <c>companion_present</c> (M6): the NPC is the character's companion - and, naming an order, under that order; <c>not: true</c>
/// inverts it.
/// </summary>
public sealed record CompanionPresentCondition(string NpcId, CompanionOrder? Order, bool Negated) : DialogueCondition;

/// <summary>
/// <c>deed</c> (E-7): the character has done something at least <see cref="Count"/> times - killed a creature, crafted or harvested
/// something, or delivered an item (to <see cref="NpcId"/>, when named); <c>not: true</c> inverts it.
/// </summary>
public sealed record DeedCondition(DeedKind Kind, string SubjectId, int Count, bool Negated) : DialogueCondition
{
    public string? NpcId { get; init; }

    /// <summary>
    /// <c>within_s</c> (WP15b, banter's "recent deed"): the deed was last done no longer ago than this many ticks. Null: whenever. The
    /// count is the tally's as ever; recency asks only when it was last added to.
    /// </summary>
    public long? WithinTicks { get; init; }
}

/// <summary><c>near</c> (WP15b): the character is within <see cref="RadiusMm"/> of a place's anchor - the place's discovery radius unless named.</summary>
public sealed record NearCondition(string LocationId, long? RadiusMm) : DialogueCondition;

/// <summary><c>people</c> (owner, 2026-10-24): the character is of this people - a heritage keeper's lines are for their own.</summary>
public sealed record PeopleCondition(string PeopleId) : DialogueCondition;

/// <summary>
/// <c>standing</c> (WP6): the character's standing with a faction is at a tier from <c>min_tier</c> to <c>max_tier</c>, compared by level.
/// FAC001 holds it to the speaker's own faction.
/// </summary>
public sealed record StandingCondition(string FactionId, int MinLevel, int MaxLevel) : DialogueCondition;

/// <summary><c>act_done</c> (WP6): the character's act log holds at least one act of this kind and subject.</summary>
public sealed record ActDoneCondition(string Kind, string Subject) : DialogueCondition;

/// <summary>
/// How many times the character has done one deed (E-7) - to one NPC, for a delivery - and when they last did it. What a <c>deed</c>
/// condition reads; quests count their own.
/// </summary>
public sealed record DeedTally(DeedKind Kind, string SubjectId, string? NpcId, int Count, long LastTick)
{
    /// <summary>The tally's key: one tally per deed, subject and NPC.</summary>
    public string Key => DeedKeys.TallyKey(Kind, SubjectId, NpcId);
}

/// <summary>
/// The deeds' stable keys in content and saves. Only the deeds E-7 tallies have one: a reply chosen or a stone travelled to (WP4) is counted
/// by the quests waiting for it, never tallied.
/// </summary>
public static class DeedKeys
{
    public static readonly ImmutableArray<DeedKind> Tallied = ImmutableArray.Create(DeedKind.Crafted, DeedKind.Harvested, DeedKind.Killed, DeedKind.Delivered);

    public static string Key(DeedKind kind) => kind switch
    {
        DeedKind.Crafted => "crafted",
        DeedKind.Harvested => "harvested",
        DeedKind.Killed => "killed",
        DeedKind.Delivered => "delivered",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown deed"),
    };

    public static DeedKind? Parse(string key) => Tallied.Cast<DeedKind?>().FirstOrDefault(k => Key(k!.Value) == key);

    public static string TallyKey(DeedKind kind, string subjectId, string? npcId) => $"{Key(kind)}|{subjectId}|{npcId}";
}

/// <summary>A closed-set consequence (DATA_MODEL.md §4.12): dialogue emits these as commands and never writes state itself.</summary>
public abstract record DialogueConsequence;

/// <summary><c>transfer_item</c>: the speaker gives the player an item, or takes one (<c>to: npc</c>).</summary>
public sealed record TransferItemConsequence(string ItemId, int Count, bool ToPlayer) : DialogueConsequence;

/// <summary><c>set_world_flag</c>: a flag in the speaker's cell takes a value.</summary>
public sealed record SetWorldFlagConsequence(string FlagId, long Value) : DialogueConsequence;

/// <summary><c>record_relationship_event</c>: what the speaker thinks of the player moves on one dimension, for a named reason.</summary>
public sealed record RelationshipEventConsequence(string NpcId, string Dimension, int Delta, string EventKey) : DialogueConsequence;

/// <summary><c>open_service</c>: the speaker opens a service to the player (Phase 1: trade).</summary>
public sealed record OpenServiceConsequence(string Service) : DialogueConsequence;

/// <summary><c>start_quest</c> (M5): the speaker gives the player a quest. A quest already started is not started again.</summary>
public sealed record StartQuestConsequence(string QuestId) : DialogueConsequence;

/// <summary><c>recruit_companion</c> (M6): the speaker joins the character, following - as a companion, or with <c>as: temporary_ally</c> (WP15a).</summary>
public sealed record RecruitCompanionConsequence : DialogueConsequence
{
    public PartyMembership As { get; init; } = PartyMembership.Companion;
}

/// <summary>
/// <c>hire_hireling</c> (WP15a): the speaker hires on in a role for so many days at the role's wage, the first day paid now. The reply
/// that carries it states the risk (DN9: a hireling can die), which the content attests with <c>risk_stated: true</c>.
/// </summary>
public sealed record HireHirelingConsequence(HirelingRole Role, int Days) : DialogueConsequence;

/// <summary><c>dismiss_companion</c> (WP15a): the speaker leaves the party and goes home; a hireling's wage already paid is not refunded.</summary>
public sealed record DismissCompanionConsequence : DialogueConsequence;

/// <summary><c>order_companion</c> (M6): the speaker, already a companion, takes an order - follow or wait.</summary>
public sealed record OrderCompanionConsequence(CompanionOrder Order) : DialogueConsequence;

/// <summary>
/// <c>rent_room</c>: paid restorative rest at the speaker's inn, with no world-time skip (DN8).
/// </summary>
public sealed record RentRoomConsequence : DialogueConsequence;

/// <summary>
/// <c>report_act</c> (WP6): the character tells the speaker of every logged act of this kind and subject; the speaker's faction learns
/// them. The only way an act reaches a faction (report-only standing).
/// </summary>
public sealed record ReportActConsequence(string Kind, string Subject) : DialogueConsequence;

/// <summary>
/// <c>tell_lore</c> (spec 41): the speaker tells the character about something they know of, and the page or pages that answer to it are
/// filed in the journal. The words are the lore content's, as the speaker varies them: the reply is what the speaker says, and the page is
/// what the character learns. An entry already found opens nothing.
/// </summary>
public sealed record TellLoreConsequence(string EntryId) : DialogueConsequence;

/// <summary>What a condition may ask of the world, answered by the simulation (read only).</summary>
public interface IDialogueFacts
{
    bool Visited(string dialogueId, string nodeId);

    long WorldFlag(string flagId, string? locationId);

    int Carried(string itemId, int qualityMin);

    int Relationship(string npcId, string dimension);

    int SkillLevel(string skillId);

    int Level { get; }

    /// <summary>A quest's state as a key (<c>not_started</c> before it starts), or, naming an objective, the objective's (<c>not_reached</c>).</summary>
    string QuestState(string questId, string? objectiveId);

    /// <summary>Whether a <c>start_quest</c> would start the quest now (<c>available</c>): never started, or repeatable and due again.</summary>
    bool QuestAvailable(string questId) => QuestState(questId, null) == QuestKeys.NotStarted;

    /// <summary>The order an NPC who is the character's companion is under; null when they are not a companion (M6).</summary>
    CompanionOrder? CompanionOrderOf(string npcId);

    /// <summary>How many times the character has done this deed (E-7): of any NPC when <paramref name="npcId"/> is null.</summary>
    int Deeds(DeedKind kind, string subjectId, string? npcId);

    /// <summary>The level of the character's standing tier with a faction (WP6); 0, neutral, when nothing moved it.</summary>
    int StandingLevel(string factionId) => 0;

    /// <summary>Whether the character's act log holds an act of this kind and subject (WP6).</summary>
    bool ActDone(string kind, string subject) => false;

    /// <summary>The tick this deed was last done (WP15b), or null when never: of any NPC when <paramref name="npcId"/> is null.</summary>
    long? DeedLastTick(DeedKind kind, string subjectId, string? npcId) => null;

    /// <summary>The world tick the facts are read at (WP15b: a recent deed is recent against it).</summary>
    long Now => 0;

    /// <summary>Whether the character is within a place's radius (WP15b); null radius: the place's own discovery radius.</summary>
    bool Near(string locationId, long? radiusMm) => false;

    /// <summary>The character's people (the creator's choice); null when none is known.</summary>
    string? People => null;
}

public static class DialogueRules
{
    public static bool Holds(DialogueCondition condition, string dialogueId, IDialogueFacts facts) => condition switch
    {
        VisitedCondition v => facts.Visited(v.DialogueId ?? dialogueId, v.NodeId) != v.Negated,
        WorldStateCondition w => facts.WorldFlag(w.FlagId, w.LocationId) is var value && value >= w.Min && value <= w.Max,
        HasItemCondition h => facts.Carried(h.ItemId, h.QualityMin) >= h.Count != h.Negated,
        RelationshipCondition r => facts.Relationship(r.NpcId, r.Dimension) is var value && value >= r.Min && value <= r.Max,
        SkillCondition s => facts.SkillLevel(s.SkillId) >= s.Min,
        LevelCondition l => facts.Level >= l.Min,
        QuestStateCondition { ObjectiveId: null, State: QuestKeys.Available } q => facts.QuestAvailable(q.QuestId) != q.Negated,
        QuestStateCondition q => facts.QuestState(q.QuestId, q.ObjectiveId) == q.State != q.Negated,
        CompanionPresentCondition p => (facts.CompanionOrderOf(p.NpcId) is { } order && (p.Order is null || p.Order == order)) != p.Negated,
        DeedCondition { WithinTicks: { } within } d =>
            (facts.Deeds(d.Kind, d.SubjectId, d.NpcId) >= d.Count && facts.DeedLastTick(d.Kind, d.SubjectId, d.NpcId) is { } last && facts.Now - last <= within) != d.Negated,
        DeedCondition d => facts.Deeds(d.Kind, d.SubjectId, d.NpcId) >= d.Count != d.Negated,
        NearCondition n => facts.Near(n.LocationId, n.RadiusMm),
        PeopleCondition p => facts.People == p.PeopleId,
        StandingCondition s => facts.StandingLevel(s.FactionId) is var level && level >= s.MinLevel && level <= s.MaxLevel,
        ActDoneCondition a => facts.ActDone(a.Kind, a.Subject),
        _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown dialogue condition"),
    };

    public static bool Offered(DialogueChoice choice, string dialogueId, IDialogueFacts facts) =>
        choice.Conditions.All(c => Holds(c, dialogueId, facts));

    /// <summary>The line a node says now (E-6): its first variant whose conditions hold, or its own text; with the line's ID (E-10).</summary>
    public static SpokenLine Line(DialogueNode node, string dialogueId, IDialogueFacts facts)
    {
        for (int i = 0; i < node.Variants.Length; i++)
        {
            if (node.Variants[i].Conditions.All(c => Holds(c, dialogueId, facts)))
                return new SpokenLine(LineIds.Node(dialogueId, node.Id, i + 1), node.Variants[i].Text);
        }
        return new SpokenLine(LineIds.Node(dialogueId, node.Id), node.Text);
    }

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

/// <summary>A paid, authored local passage. Arrival belongs to the named location; no world clock is skipped.</summary>
public sealed record TakePassageConsequence(string LocationId, long XMm, long ZMm, long Coin, long? MinTideMm = null) : DialogueConsequence;
