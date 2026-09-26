// UNNAMED Content - NPCs and their conversations from content (DATA_MODEL.md §4.5, §4.12; SYSTEMS.md S-24, S-28; M4)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Factions;
using UNNAMED.Domain.Social;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// Builds the NPCs and their conversations, and lints what the reference and semantic passes cannot see (SOC001): an NPC's role
/// and services are ones Phase 1 builds and a trader names its stock; a conversation's every way leads to a node that exists, a
/// spent line never loops, and every condition and consequence is one of the closed set with what it needs. An NPC who can join
/// the character names how they fight, and <c>config.companion</c> how companions behave (M6).
/// </summary>
public static class SocialContent
{
    /// <summary>DATA_MODEL.md §4.5's roles.</summary>
    private static readonly string[] Roles =
        { "villager", "merchant", "guard", "craftsperson", "quest_giver", "trainer", "innkeeper", "noble", "bandit", "scholar", "steward" };

    private static readonly string[] Conditions =
        { "visited", "world_state", "has_item", "relationship", "skill", "level", "quest_state", "companion_present", "reputation", "act_done" };

    private static readonly string[] Consequences =
        { "transfer_item", "give_recipe", "set_world_flag", "record_relationship_event", "open_service", "start_quest", "recruit_companion", "order_companion",
          "report_act" };

    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        ImmutableSortedDictionary<string, NpcDefinition>? npcs = null;
        ImmutableSortedDictionary<string, DialogueDefinition>? dialogues = null;
        Try(() => npcs = BuildNpcs(loader), "npcs", errors);
        Try(() => dialogues = BuildDialogues(loader), "dialogues", errors);
        if (npcs is not null && dialogues is not null)
            Try(() => CrossCheck(npcs, dialogues), "npcs", errors);
        // Companions (M6): an NPC who can join needs the rules companions behave by.
        if (npcs is not null && (npcs.Values.Any(n => n.Companion is not null) || loader.Definitions.ContainsKey("config.companion")))
            Try(() => BuildCompanionTuning(loader) ?? throw new FormatException("an NPC can join the character, so config.companion is required"), "config.companion", errors);
        return errors;
    }

    /// <summary>
    /// How companions behave (<c>config.companion</c>, M6): distances in metres, times in seconds, in the content; millimetres and ticks
    /// here. Null when the pack has none (a pack in which nobody can join).
    /// </summary>
    public static CompanionTuning? BuildCompanionTuning(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey("config.companion"))
            return null;
        var map = Config(loader, "config.companion");
        int tickMs = WorldContent.TickMilliseconds(loader);
        var tuning = new CompanionTuning(Mm(map, "follow_near_m"), Mm(map, "run_beyond_m"), Mm(map, "sprint_beyond_m"), Mm(map, "catch_up_beyond_m"),
            ToTicks(Number(map, "snag_s"), tickMs), Mm(map, "trail_step_m"), Int(map, "trail_marks"), Mm(map, "fight_radius_m"), Mm(map, "leash_m"),
            Mm(map, "guard_radius_m"), ToTicks(Number(map, "attack_pause_s"), tickMs), ToTicks(Number(map, "revive_window_s"), tickMs),
            Int(map, "revive_percent"), ToTicks(Number(map, "regen_delay_s"), tickMs), Int(map, "regen_per_s"));
        return tuning.Problem() is { } problem ? throw new FormatException($"config.companion: {problem}") : tuning;
    }

    /// <summary>
    /// NPCs (DATA_MODEL.md §4.5, Phase 1's subset): a <c>name</c>, a <c>role</c>, the <c>services</c> they offer (trade, with its
    /// <c>merchant_ref</c>), the <c>dialogue_ref</c> they speak, and <c>unique: true</c> - Phase 1's NPCs are all named. Schedules,
    /// anchors, combat profiles and NPC-to-NPC relationships are not built.
    /// </summary>
    public static ImmutableSortedDictionary<string, NpcDefinition> BuildNpcs(ContentLoader loader) =>
        loader.GetByKind("npc").Values.Select(definition =>
        {
            var map = Read(definition.YamlSource);
            string id = definition.Id;
            string role = Text(map, "role");
            if (!Roles.Contains(role))
                throw new FormatException($"{id}: role '{role}' is not one of DATA_MODEL.md §4.5's ({string.Join(", ", Roles)})");
            var services = (map.GetValueOrDefault("services") as List<object> ?? new List<object>()).Select(s => s as string ?? "").ToImmutableArray();
            if (services.FirstOrDefault(s => !NpcServices.Built.Contains(s)) is { } unbuilt)
                throw new FormatException($"{id}: service '{unbuilt}' is not built in Phase 1 ({string.Join(", ", NpcServices.Built)})");
            string? merchant = map.GetValueOrDefault("merchant_ref") as string;
            if (services.Contains(NpcServices.Trade) != merchant is not null)
                throw new FormatException($"{id}: a trader names its merchant_ref, and only a trader does");
            if (map.GetValueOrDefault("unique") as string != "true")
                throw new FormatException($"{id}: Phase 1 builds named NPCs only (unique: true)");
            foreach (string field in new[] { "schedule_ref", "anchors", "combat_profile", "relationships_init", "name_pool" })
            {
                if (map.ContainsKey(field))
                    throw new FormatException($"{id}: {field} is not built in Phase 1");
            }
            return new NpcDefinition(id, Text(map, "name"), role, services, merchant, map.GetValueOrDefault("dialogue_ref") as string)
            {
                Companion = map.ContainsKey("companion") ? Profile(id, Map(map, "companion"), loader) : null,
                FactionId = map.GetValueOrDefault("faction_ref") is string faction ? Defined(loader, faction, "faction", id) : null,
                WorksAt = WorksAt(id, map),
            };
        }).ToImmutableSortedDictionary(n => n.Id, n => n, StringComparer.Ordinal);

    /// <summary><c>works_at</c> (M7): the station kinds an NPC works at when asked, each once, as text; none when absent. BLD005 checks the kinds.</summary>
    private static ImmutableArray<string> WorksAt(string id, Dictionary<object, object> map)
    {
        if (!map.ContainsKey("works_at"))
            return ImmutableArray<string>.Empty;
        var kinds = List(map, "works_at").Select(k => k as string ?? throw new FormatException($"{id}: works_at names station kinds as text")).ToImmutableArray();
        if (kinds.IsEmpty || kinds.Distinct(StringComparer.Ordinal).Count() != kinds.Length)
            throw new FormatException($"{id}: works_at names at least one station kind, each once");
        return kinds;
    }

    /// <summary>
    /// <c>companion</c> (M6): what an NPC who can join the character fights with - <c>health</c>, a <c>weapon_item_ref</c> (a melee weapon;
    /// its damage, reach and speed are the item's) and <c>armor</c> by body region.
    /// </summary>
    private static CompanionProfile Profile(string id, Dictionary<object, object> map, ContentLoader loader)
    {
        string weapon = Defined(loader, Text(map, "weapon_item_ref"), "item.weapon", $"{id} companion");
        if (Read(loader.Definitions[weapon].YamlSource).ContainsKey("ammo_item_ref"))
            throw new FormatException($"{id} companion: {weapon} is a ranged weapon; a Phase-1 companion fights hand to hand");
        var armor = map.ContainsKey("armor") ? Map(map, "armor") : new Dictionary<object, object>();
        var profile = new CompanionProfile(Int(map, "health"), weapon,
            armor.ToImmutableSortedDictionary(kv => BodyRegions.Parse(kv.Key as string ?? ""), kv => Int(armor, kv.Key as string ?? "")));
        return profile.MaxHealth > 0 && profile.Armor.Values.All(a => a >= 0)
            ? profile
            : throw new FormatException($"{id} companion: health is positive and armor is never negative");
    }

    /// <summary>
    /// Conversations (DATA_MODEL.md §4.12): <c>participants</c>, a <c>root_node</c>, and <c>nodes</c> by ID, each with its
    /// <c>text</c> (inline until localization), <c>choices</c>, <c>next</c>, <c>once</c> and <c>next_if_exhausted</c>.
    /// </summary>
    public static ImmutableSortedDictionary<string, DialogueDefinition> BuildDialogues(ContentLoader loader) =>
        loader.GetByKind("dialogue").Values.Select(definition =>
        {
            var map = Read(definition.YamlSource);
            string id = definition.Id;
            var participants = List(map, "participants").Select(p => p as string ?? "").ToImmutableArray();
            if (participants.IsEmpty || participants.Any(p => !loader.GetByKind("npc").ContainsKey(p)))
                throw new FormatException($"{id}: participants must name its NPCs");
            var nodes = Map(map, "nodes").ToImmutableSortedDictionary(
                kv => kv.Key as string ?? "",
                kv => Node(id, kv.Key as string ?? "", kv.Value as Dictionary<object, object> ?? throw new FormatException($"{id}: node {kv.Key} must be a map"), loader),
                StringComparer.Ordinal);
            var dialogue = new DialogueDefinition(id, participants, Text(map, "root_node"), nodes);
            CheckGraph(dialogue);
            return dialogue;
        }).ToImmutableSortedDictionary(d => d.Id, d => d, StringComparer.Ordinal);

    private static DialogueNode Node(string id, string nodeId, Dictionary<object, object> map, ContentLoader loader)
    {
        string where = $"{id} node {nodeId}";
        var choices = Rows(map, "choices", where).Select(row => Choice(where, row, loader)).ToImmutableArray();
        if (choices.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != choices.Length)
            throw new FormatException($"{where}: two replies share an id");
        string? next = map.GetValueOrDefault("next") as string;
        if (next is not null && !choices.IsEmpty)
            throw new FormatException($"{where}: a node with replies goes on through them, not through next");
        return new DialogueNode(nodeId, Text(map, "text"), choices, next, map.GetValueOrDefault("once") as string == "true",
            map.GetValueOrDefault("next_if_exhausted") as string);
    }

    private static DialogueChoice Choice(string where, Dictionary<object, object> map, ContentLoader loader)
    {
        string at = $"{where} reply {Text(map, "id")}";
        var consequences = Rows(map, "consequences", at).Select(c => Consequence(at, c, loader)).ToImmutableArray();
        // An item changing hands is the one consequence that can be refused, so a reply carries at most one and it goes first.
        if (consequences.OfType<TransferItemConsequence>().Count() > 1)
            throw new FormatException($"{at}: a reply moves at most one item (transfer_item)");
        return new DialogueChoice(Text(map, "id"), Text(map, "text"),
            Rows(map, "conditions", at).Select(c => Condition(at, c, loader)).ToImmutableArray(), consequences,
            map.GetValueOrDefault("next") as string);
    }

    private static DialogueCondition Condition(string at, Dictionary<object, object> map, ContentLoader loader)
    {
        string kind = Text(map, "kind");
        bool negated = map.GetValueOrDefault("not") as string == "true";
        if (negated && kind is not ("visited" or "has_item" or "quest_state" or "companion_present"))
            throw new FormatException($"{at}: only visited, has_item, quest_state and companion_present conditions take not");
        return kind switch
        {
            "visited" => new VisitedCondition(Text(map, "node"), negated)
            {
                DialogueId = map.GetValueOrDefault("dialogue_ref") is string other ? Defined(loader, other, "dialogue", at) : null,
            },
            "world_state" => new WorldStateCondition(Defined(loader, Text(map, "flag_ref"), "world_flag", at),
                LongOr(map, "min", 1), LongOr(map, "max", long.MaxValue))
            {
                LocationId = map.GetValueOrDefault("location_ref") is string place ? Defined(loader, place, "location", at) : null,
            },
            "has_item" => new HasItemCondition(Defined(loader, Text(map, "item_ref"), "item", at), Positive(map, "count", 1),
                (int)LongOr(map, "quality_min", -1), negated),
            "relationship" => new RelationshipCondition(Defined(loader, Text(map, "npc_ref"), "npc", at), Dimension(Text(map, "dimension"), at),
                (int)LongOr(map, "min", Relationships.Min), (int)LongOr(map, "max", Relationships.Max)),
            "skill" => new SkillCondition(Defined(loader, Text(map, "skill_ref"), "skill", at), Positive(map, "min", 1)),
            "level" => new LevelCondition(Positive(map, "min", 1)),
            "quest_state" => QuestState(at, map, loader, negated),
            "companion_present" => new CompanionPresentCondition(Defined(loader, Text(map, "npc_ref"), "npc", at),
                map.GetValueOrDefault("order") is string order ? Order(order, at) : null, negated),
            "reputation" => Reputation(at, map, loader),
            "act_done" => ActDone(at, map, loader),
            _ => throw new FormatException($"{at}: condition '{kind}' is not built in Phase 1 ({string.Join(", ", Conditions)})"),
        };
    }

    private static DialogueConsequence Consequence(string at, Dictionary<object, object> map, ContentLoader loader)
    {
        string command = Text(map, "command");
        return command switch
        {
            "transfer_item" => new TransferItemConsequence(Defined(loader, Text(map, "item_ref"), "item", at), Positive(map, "count", 1),
                Text(map, "to") switch
                {
                    "player" => true,
                    "npc" => false,
                    var to => throw new FormatException($"{at}: transfer_item goes to player or npc, not '{to}'"),
                }),
            "give_recipe" => new GiveRecipeConsequence(Defined(loader, Text(map, "recipe_ref"), "recipe", at)),
            "set_world_flag" => new SetWorldFlagConsequence(Defined(loader, Text(map, "flag_ref"), "world_flag", at), LongOr(map, "value", 1)),
            "record_relationship_event" => new RelationshipEventConsequence(Defined(loader, Text(map, "npc_ref"), "npc", at),
                Dimension(Text(map, "dimension"), at),
                LongOr(map, "delta", 0) is var delta && delta != 0 && Math.Abs(delta) <= Relationships.Max
                    ? (int)delta
                    : throw new FormatException($"{at}: a relationship event moves a dimension by a delta of 1 to 100 either way"),
                Text(map, "event")),
            "open_service" => new OpenServiceConsequence(NpcServices.Built.Contains(Text(map, "service"))
                ? Text(map, "service")
                : throw new FormatException($"{at}: service '{Text(map, "service")}' is not built in Phase 1")),
            "start_quest" => new StartQuestConsequence(Defined(loader, Text(map, "quest_ref"), "quest", at)),
            "recruit_companion" => new RecruitCompanionConsequence(),
            "order_companion" => new OrderCompanionConsequence(Order(Text(map, "order"), at)),
            "report_act" => ReportAct(at, map, loader),
            "add_reputation" => throw new FormatException($"{at}: add_reputation is refused: reputation moves only through acts a faction learns of (M7)"),
            _ => throw new FormatException($"{at}: consequence '{command}' is not built in Phase 1 ({string.Join(", ", Consequences)})"),
        };
    }

    /// <summary>
    /// <c>reputation</c> (M7): the character's tier with <c>faction_ref</c> from <c>min_tier</c> (default anathema) to <c>max_tier</c>
    /// (default exalted), as levels. FAC001 holds it to the speaker's own faction (R1).
    /// </summary>
    private static ReputationCondition Reputation(string at, Dictionary<object, object> map, ContentLoader loader)
    {
        string faction = Defined(loader, Text(map, "faction_ref"), "faction", at);
        int min = StandingLadder.LevelOf(map.GetValueOrDefault("min_tier") as string ?? "anathema");
        int max = StandingLadder.LevelOf(map.GetValueOrDefault("max_tier") as string ?? "exalted");
        return min <= max ? new ReputationCondition(faction, min, max) : throw new FormatException($"{at}: min_tier is above max_tier");
    }

    /// <summary><c>act_done</c> (M7): the act log holds this act. FAC001 allows it only beside a report of the same act (R3).</summary>
    private static ActDoneCondition ActDone(string at, Dictionary<object, object> map, ContentLoader loader)
    {
        var (kind, subject) = FactionContent.ActAndSubject(loader, map, at);
        return new ActDoneCondition(kind, subject);
    }

    /// <summary><c>report_act</c> (M7): the character tells the speaker of this act. FAC001 allows it only to a faction that reacts (R4).</summary>
    private static ReportActConsequence ReportAct(string at, Dictionary<object, object> map, ContentLoader loader)
    {
        var (kind, subject) = FactionContent.ActAndSubject(loader, map, at);
        return new ReportActConsequence(kind, subject);
    }

    private static CompanionOrder Order(string order, string at) =>
        CompanionKeys.ParseOrder(order) ?? throw new FormatException($"{at}: a companion's order is follow or wait, not '{order}'");

    /// <summary><c>quest_state</c> (M5): a quest's state, or with <c>objective</c> one of its objectives'; the quest lint checks the objective exists.</summary>
    private static QuestStateCondition QuestState(string at, Dictionary<object, object> map, ContentLoader loader, bool negated)
    {
        string quest = Defined(loader, Text(map, "quest_ref"), "quest", at);
        string? objective = map.GetValueOrDefault("objective") as string;
        string state = Text(map, "is");
        var states = objective is null ? QuestKeys.QuestStates : QuestKeys.ObjectiveStates;
        if (!states.Contains(state))
            throw new FormatException($"{at}: a {(objective is null ? "quest" : "objective")} is {string.Join(", ", states)}, not '{state}'");
        return new QuestStateCondition(quest, objective, state, negated);
    }

    /// <summary>Every way through a conversation leads to a node it has, and no chain of spent lines loops.</summary>
    private static void CheckGraph(DialogueDefinition dialogue)
    {
        string id = dialogue.Id;
        void Exists(string? node, string what)
        {
            if (node is not null && !dialogue.Nodes.ContainsKey(node))
                throw new FormatException($"{id}: {what} names node '{node}', which it does not have");
        }
        Exists(dialogue.Root, "root_node");
        foreach (var node in dialogue.Nodes.Values)
        {
            Exists(node.Next, $"node {node.Id}'s next");
            Exists(node.NextIfExhausted, $"node {node.Id}'s next_if_exhausted");
            if (node.NextIfExhausted is not null && !node.Once)
                throw new FormatException($"{id}: node {node.Id} has next_if_exhausted but is not once");
            foreach (var choice in node.Choices)
            {
                Exists(choice.Next, $"node {node.Id} reply {choice.Id}");
                foreach (var visited in choice.Conditions.OfType<VisitedCondition>().Where(v => v.DialogueId is null || v.DialogueId == id))
                    Exists(visited.NodeId, $"node {node.Id} reply {choice.Id}'s visited condition");
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var at = node; at.Once && at.NextIfExhausted is { } next; at = dialogue.Nodes[next])
            {
                if (!seen.Add(at.Id))
                    throw new FormatException($"{id}: spent lines loop through node {at.Id}");
            }
        }
        foreach (var node in dialogue.Nodes.Values.Where(n => n.Once))
            OnceLineKeepsItsConsequences(dialogue, node);
    }

    /// <summary>
    /// A once line is spent when it is heard, not when it is answered: a character who walks away, is refused (a full pack) or loads a save
    /// before answering never hears it again. So a reply of a once line that does something - gives, teaches, starts, recruits, moves
    /// what an NPC thinks - must be offered again, the same reply with the same consequences, by a line its <c>next_if_exhausted</c>
    /// falls back to, gated there on whatever marks it received. Otherwise the line must not be once.
    /// </summary>
    private static void OnceLineKeepsItsConsequences(DialogueDefinition dialogue, DialogueNode node)
    {
        foreach (var choice in node.Choices.Where(c => !c.Consequences.IsEmpty))
        {
            bool offeredAgain = false;
            var seen = new HashSet<string>(StringComparer.Ordinal) { node.Id };
            for (string? next = node.NextIfExhausted; next is not null && seen.Add(next) && !offeredAgain; )
            {
                var fallback = dialogue.Nodes[next];
                offeredAgain = fallback.Choices.Any(c => c.Id == choice.Id && c.Consequences.SequenceEqual(choice.Consequences));
                next = fallback.Once ? fallback.NextIfExhausted : null;
            }
            if (!offeredAgain)
                throw new FormatException($"{dialogue.Id}: node {node.Id} is spent once heard, and its reply {choice.Id} carries consequences - " +
                                          "a character who leaves before answering would lose them for good; offer the same reply on the line it falls " +
                                          "back to (next_if_exhausted), gated on what marks it received, or do not make the line once");
        }
    }

    /// <summary>An NPC's conversation names it; a conversation that opens a service is spoken by someone who offers it.</summary>
    private static object CrossCheck(ImmutableSortedDictionary<string, NpcDefinition> npcs, ImmutableSortedDictionary<string, DialogueDefinition> dialogues)
    {
        foreach (var npc in npcs.Values.Where(n => n.DialogueId is not null))
        {
            if (dialogues.TryGetValue(npc.DialogueId!, out var dialogue) && !dialogue.Participants.Contains(npc.Id))
                throw new FormatException($"{npc.Id}: {npc.DialogueId} does not name it among its participants");
        }
        foreach (var dialogue in dialogues.Values)
        {
            foreach (var open in dialogue.Nodes.Values.SelectMany(n => n.Choices).SelectMany(c => c.Consequences).OfType<OpenServiceConsequence>())
            {
                if (!dialogue.Participants.Any(p => npcs.TryGetValue(p, out var npc) && npc.Offers(open.Service)))
                    throw new FormatException($"{dialogue.Id}: opens {open.Service}, which none of its participants offers");
            }
            // Only someone who can join is recruited or ordered by their own conversation (M6).
            bool companionship = dialogue.Nodes.Values.SelectMany(n => n.Choices).SelectMany(c => c.Consequences)
                .Any(c => c is RecruitCompanionConsequence or OrderCompanionConsequence);
            if (companionship && !dialogue.Participants.Any(p => npcs.TryGetValue(p, out var npc) && npc.Companion is not null))
                throw new FormatException($"{dialogue.Id}: recruits or orders a companion, but none of its participants can join the character");
            // A visited condition about another conversation names a line that conversation has.
            foreach (var visited in dialogue.Nodes.Values.SelectMany(n => n.Choices).SelectMany(c => c.Conditions).OfType<VisitedCondition>()
                         .Where(v => v.DialogueId is { } other && other != dialogue.Id))
            {
                if (!dialogues.TryGetValue(visited.DialogueId!, out var other) || !other.Nodes.ContainsKey(visited.NodeId))
                    throw new FormatException($"{dialogue.Id}: a visited condition names node '{visited.NodeId}' of {visited.DialogueId}, which it does not have");
            }
        }
        return npcs;
    }

    private static string Defined(ContentLoader loader, string id, string kind, string at) =>
        loader.Definitions.TryGetValue(id, out var definition) && (definition.Kind == kind || definition.Kind.StartsWith(kind + ".", StringComparison.Ordinal))
            ? id
            : throw new FormatException($"{at}: {id} is not a defined {kind}");

    private static string Dimension(string dimension, string at) =>
        Relationships.IsDimension(dimension)
            ? dimension
            : throw new FormatException($"{at}: '{dimension}' is not a relationship dimension ({string.Join(", ", Relationships.Dimensions)})");

    private static long LongOr(Dictionary<object, object> map, string key, long fallback) =>
        !map.TryGetValue(key, out var value) ? fallback
        : long.TryParse(value as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed
        : throw new FormatException($"'{key}' must be a whole number");

    private static int Positive(Dictionary<object, object> map, string key, int fallback) =>
        LongOr(map, key, fallback) is var value && value is >= 1 and <= int.MaxValue ? (int)value : throw new FormatException($"'{key}' must be at least 1");

    private static void Try(Func<object> build, string what, List<ValidationError> errors)
    {
        try
        {
            build();
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(new ValidationError { SeverityLevel = ValidationError.Severity.Error, Code = "SOC001", Message = $"{what}: {e.Message}" });
        }
    }
}
