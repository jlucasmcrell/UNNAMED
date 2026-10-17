// UNNAMED Content - NPCs and their conversations from content (DATA_MODEL.md §4.5, §4.12; SYSTEMS.md S-24, S-28; M4)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Factions;
using UNNAMED.Domain.Quests;
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
        { "visited", "world_state", "has_item", "relationship", "skill", "level", "quest_state", "companion_present", "deed", "standing", "act_done", "near" };

    private static readonly string[] Consequences =
        { "transfer_item", "teach", "set_world_flag", "record_relationship_event", "open_service", "start_quest", "recruit_companion", "order_companion",
          "report_act", "hire_hireling", "dismiss_companion" };

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
        // The party (WP15a): whoever can join or be hired travels under its rules - and a hireling fights, so companions' too.
        if (npcs is not null && npcs.Values.Any(n => n.Hireling is not null) && !loader.Definitions.ContainsKey("config.companion"))
            errors.Add(new ValidationError { SeverityLevel = ValidationError.Severity.Error, Code = "SOC001", Message = "config.companion: an NPC can be hired, so config.companion is required" });
        if (npcs is not null && (npcs.Values.Any(n => n.Companion is not null || n.Hireling is not null) || loader.Definitions.ContainsKey("config.party")))
            Try(() => BuildPartyRules(loader) ?? throw new FormatException("an NPC can join or be hired, so config.party is required"), "config.party", errors);
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
    /// The party's rules (<c>config.party</c>; WP15a, DN9, DN11): <c>max_members</c> beside the character, <c>grace_days</c> a hireling
    /// waits for an unpaid wage, <c>grave_wound_days_min</c>/<c>_max</c> a fallen companion recovers at home, <c>contract_days_min</c>/<c>_max</c>,
    /// and under <c>roles</c> each role's <c>wage_per_day</c>, <c>stance</c>, and a porter's <c>carry_bonus_kg</c>, a gatherer's
    /// <c>yields_per_day</c>, a crafter's <c>crafts_per_day</c>. Null when the pack has none.
    /// </summary>
    public static PartyRules? BuildPartyRules(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey("config.party"))
            return null;
        var map = Config(loader, "config.party");
        var roles = Map(map, "roles").ToImmutableSortedDictionary(
            kv => PartyKeys.ParseRole(kv.Key as string ?? "") ?? throw new FormatException($"config.party: '{kv.Key}' is not a role (guard, porter, gatherer, crafter)"),
            kv =>
            {
                var role = kv.Value as Dictionary<object, object> ?? throw new FormatException($"config.party: role {kv.Key} must be a map");
                string stance = role.GetValueOrDefault("stance") as string ?? "aggressive";
                return new RoleTerms(LongOr(role, "wage_per_day", -1), (long)Math.Round((role.ContainsKey("carry_bonus_kg") ? Number(role, "carry_bonus_kg") : 0) * 1000),
                    (int)LongOr(role, "yields_per_day", 0), (int)LongOr(role, "crafts_per_day", 0),
                    PartyKeys.ParseStance(stance) ?? throw new FormatException($"config.party: role {kv.Key}'s stance is aggressive, defensive or passive, not '{stance}'"));
            });
        int tickMs = WorldContent.TickMilliseconds(loader);
        var rules = new PartyRules(Int(map, "max_members"), Int(map, "grace_days"), Int(map, "grave_wound_days_min"), Int(map, "grave_wound_days_max"),
            Int(map, "contract_days_min"), Int(map, "contract_days_max"), roles)
        {
            Approval = map.ContainsKey("approval") ? Approval(Map(map, "approval")) : ApprovalRules.None,
            Banter = map.ContainsKey("banter") && Map(map, "banter") is var banter
                ? new BanterRules(ToTicks(Number(banter, "every_s"), tickMs), (int)LongOr(banter, "chance_percent", 100),
                    banter.ContainsKey("quiet_s") ? ToTicks(Number(banter, "quiet_s"), tickMs) : 0)
                : null,
        };
        return rules.Problem() is { } problem ? throw new FormatException($"config.party: {problem}") : rules;
    }

    /// <summary>
    /// <c>config.party</c>'s <c>approval</c> (WP15b): <c>loyalty_per_day</c> a companion or ally gains each midnight on their feet in the
    /// party, <c>loyalty_wage_paid</c> and <c>loyalty_wage_unpaid</c> a hireling gains or loses with each wage, and under <c>hesitate</c>,
    /// <c>refuse</c> and <c>leave</c> the bounds - <c>{ dimension, below | above }</c> - any one of which, past, is enough.
    /// </summary>
    private static ApprovalRules Approval(Dictionary<object, object> map)
    {
        ImmutableArray<DimensionBound> Bounds(string key) => Rows(map, key, $"config.party approval {key}").Select(row =>
        {
            string at = $"config.party approval {key}";
            bool below = row.ContainsKey("below"), above = row.ContainsKey("above");
            if (below == above)
                throw new FormatException($"{at}: a bound names exactly one of below and above");
            return new DimensionBound(Dimension(Text(row, "dimension"), at), below ? (int)LongOr(row, "below", 0) : null, above ? (int)LongOr(row, "above", 0) : null);
        }).ToImmutableArray();
        return new ApprovalRules((int)LongOr(map, "loyalty_per_day", 0), (int)LongOr(map, "loyalty_wage_paid", 0), (int)LongOr(map, "loyalty_wage_unpaid", 0),
            Bounds("hesitate"), Bounds("refuse"), Bounds("leave"));
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
            if (services.Contains(NpcServices.Repair) && !services.Contains(NpcServices.Trade))
                throw new FormatException($"{id}: a repairer is a trader too: the fee goes into their purse (WP3b)");
            if (map.GetValueOrDefault("unique") as string != "true")
                throw new FormatException($"{id}: Phase 1 builds named NPCs only (unique: true)");
            foreach (string field in new[] { "schedule_ref", "anchors", "combat_profile", "relationships_init", "name_pool" })
            {
                if (map.ContainsKey(field))
                    throw new FormatException($"{id}: {field} is not built in Phase 1");
            }
            // A named companion is gravely wounded where a hireling dies (DN9): one NPC is never both.
            if (map.ContainsKey("companion") && map.ContainsKey("hireling"))
                throw new FormatException($"{id}: someone who joins as a companion is never also hired (a companion is wounded where a hireling dies)");
            return new NpcDefinition(id, Text(map, "name"), role, services, merchant, map.GetValueOrDefault("dialogue_ref") as string)
            {
                Companion = map.ContainsKey("companion") ? Profile(id, Map(map, "companion"), loader) : null,
                FactionId = map.GetValueOrDefault("faction_ref") is string faction ? Defined(loader, faction, "faction", id) : null,
                Hireling = map.ContainsKey("hireling") ? HireProfile(id, Map(map, "hireling"), loader) : null,
                Teaches = TeachingContent.Offers(id, map, loader),
                Values = Rows(map, "values", id).Select((row, i) => Value($"{id} values[{i}]", row, loader)).ToImmutableArray(),
            };
        }).ToImmutableSortedDictionary(n => n.Id, n => n, StringComparer.Ordinal);

    /// <summary>
    /// One of a party member's <c>values</c> (WP15b): the <c>deed</c> (killed, crafted, harvested, delivered) on a subject named as a deed
    /// condition names it (<c>creature_ref</c>, <c>item_ref</c>, <c>resource_ref</c>) or by a content <c>tag</c> its definitions carry, or
    /// - a delivery to a named <c>npc_ref</c> - anything; the <c>changes</c> it makes, dimension by dimension (1 to 100 either way); and the
    /// <c>remark</c> said when it happens. A tag that names no subject is refused: the value could never be touched.
    /// </summary>
    private static ValueReaction Value(string at, Dictionary<object, object> map, ContentLoader loader)
    {
        string key = Text(map, "deed");
        var kind = DeedKeys.Parse(key) ?? throw new FormatException($"{at}: a deed is {string.Join(", ", DeedKeys.Tallied.Select(DeedKeys.Key))}, not '{key}'");
        var (field, subjectKind) = kind switch
        {
            DeedKind.Killed => ("creature_ref", "creature"),
            DeedKind.Harvested => ("resource_ref", "resource"),
            _ => ("item_ref", "item"),
        };
        string? npc = map.GetValueOrDefault("npc_ref") is string named ? Defined(loader, named, "npc", at) : null;
        if (npc is not null && kind != DeedKind.Delivered)
            throw new FormatException($"{at}: only a delivered deed names npc_ref");
        var ofKind = loader.Definitions.Values.Where(d => d.Kind == subjectKind || d.Kind.StartsWith(subjectKind + ".", StringComparison.Ordinal));
        ImmutableSortedSet<string> subjects;
        if (map.GetValueOrDefault(field) is string subject && !map.ContainsKey("tag"))
            subjects = ImmutableSortedSet.Create(StringComparer.Ordinal, Defined(loader, subject, subjectKind, at));
        else if (map.GetValueOrDefault("tag") is string tag && !map.ContainsKey(field))
            subjects = ofKind.Where(d => d.Tags?.Contains(tag) == true).Select(d => d.Id).ToImmutableSortedSet(StringComparer.Ordinal) is { IsEmpty: false } tagged
                ? tagged
                : throw new FormatException($"{at}: no {subjectKind} carries the tag '{tag}'");
        else if (!map.ContainsKey(field) && !map.ContainsKey("tag") && npc is not null)
            subjects = ofKind.Select(d => d.Id).ToImmutableSortedSet(StringComparer.Ordinal);
        else
            throw new FormatException($"{at}: a value names its subject by {field} or by tag (one of them), or is a delivery to an npc_ref");
        var changes = Map(map, "changes").ToImmutableSortedDictionary(kv => Dimension(kv.Key as string ?? "", at),
            kv => LongOr(Map(map, "changes"), kv.Key as string ?? "", 0) is var delta && delta != 0 && Math.Abs(delta) <= Relationships.Max
                ? (int)delta
                : throw new FormatException($"{at}: a value moves a dimension by 1 to 100 either way"), StringComparer.Ordinal);
        if (changes.IsEmpty)
            throw new FormatException($"{at}: a value changes at least one dimension");
        return new ValueReaction(kind, subjects, npc, changes, map.GetValueOrDefault("remark") as string);
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

    /// <summary>The body a hired resident works and fights with (WP12b): a companion's profile, read where config names it.</summary>
    internal static CompanionProfile HirelingBody(string id, Dictionary<object, object> map, ContentLoader loader) => Profile(id, map, loader);

    /// <summary>
    /// <c>hireling</c> (WP15a): what an NPC who can be hired takes on - <c>roles</c> (guard, porter, gatherer, crafter) - and fights with,
    /// as a companion's profile does (<c>health</c>, <c>weapon_item_ref</c>, <c>armor</c>).
    /// </summary>
    private static HirelingProfile HireProfile(string id, Dictionary<object, object> map, ContentLoader loader)
    {
        var roles = List(map, "roles").Select(r => PartyKeys.ParseRole(r as string ?? "")
            ?? throw new FormatException($"{id} hireling: '{r}' is not a role (guard, porter, gatherer, crafter)")).Distinct().ToImmutableArray();
        return roles.IsEmpty
            ? throw new FormatException($"{id} hireling: names at least one role")
            : new HirelingProfile(roles, Profile($"{id} hireling", map, loader));
    }

    /// <summary>
    /// Conversations (DATA_MODEL.md §4.12): <c>participants</c>, a <c>root_node</c>, and <c>nodes</c> by ID, each with its
    /// <c>text</c> (inline until localization), <c>variants</c> (E-6: <c>[{text, conditions}]</c>, other wordings said while their
    /// conditions hold), <c>choices</c>, <c>next</c>, <c>once</c> and <c>next_if_exhausted</c>. A node ID is lower-case letters, digits and
    /// underscores: line IDs (E-10) are derived from it.
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
            var dialogue = new DialogueDefinition(id, participants, Text(map, "root_node"), nodes)
            {
                Barks = Rows(map, "barks", id).Select((row, i) => new DialogueBark(Text(row, "text"),
                    Rows(row, "conditions", $"{id} bark {i}").Select(c => Condition($"{id} bark {i}", c, loader)).ToImmutableArray())).ToImmutableArray(),
                Banter = Rows(map, "banter", id).Select((row, i) => new DialogueBark(Text(row, "text"),
                    Rows(row, "conditions", $"{id} banter {i + 1}").Select(c => Condition($"{id} banter {i + 1}", c, loader)).ToImmutableArray())).ToImmutableArray(),
            };
            CheckGraph(dialogue);
            return dialogue;
        }).ToImmutableSortedDictionary(d => d.Id, d => d, StringComparer.Ordinal);

    private static DialogueNode Node(string id, string nodeId, Dictionary<object, object> map, ContentLoader loader)
    {
        string where = $"{id} node {nodeId}";
        if (nodeId.Length == 0 || !nodeId.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
            throw new FormatException($"{where}: a node ID is lower-case letters, digits and underscores (its line ID is derived from it)");
        var choices = Rows(map, "choices", where).Select(row => Choice(where, row, loader)).ToImmutableArray();
        if (choices.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != choices.Length)
            throw new FormatException($"{where}: two replies share an id");
        string? next = map.GetValueOrDefault("next") as string;
        if (next is not null && !choices.IsEmpty)
            throw new FormatException($"{where}: a node with replies goes on through them, not through next");
        var variants = Rows(map, "variants", where).Select((row, i) => new DialogueVariant(Text(row, "text"),
            Rows(row, "conditions", $"{where} variant {i + 1}").Select(c => Condition($"{where} variant {i + 1}", c, loader)).ToImmutableArray())).ToImmutableArray();
        // A variant with nothing to ask would always be said, and the node's own text never.
        if (variants.FirstOrDefault(v => v.Conditions.IsEmpty) is { } always)
            throw new FormatException($"{where}: variant {variants.IndexOf(always) + 1} has no conditions, so the line's own text is never said");
        return new DialogueNode(nodeId, Text(map, "text"), choices, next, map.GetValueOrDefault("once") as string == "true",
            map.GetValueOrDefault("next_if_exhausted") as string) { Variants = variants };
    }

    private static DialogueChoice Choice(string where, Dictionary<object, object> map, ContentLoader loader)
    {
        string at = $"{where} reply {Text(map, "id")}";
        var consequences = Rows(map, "consequences", at).Select(c => Consequence(at, c, loader)).ToImmutableArray();
        // An item changing hands, a hire and a lesson are the consequences that can be refused, so a reply carries at most one and it goes first.
        if (consequences.OfType<TransferItemConsequence>().Count() > 1)
            throw new FormatException($"{at}: a reply moves at most one item (transfer_item)");
        if (consequences.Count(c => c is TransferItemConsequence or HireHirelingConsequence or TeachConsequence) > 1)
            throw new FormatException($"{at}: a reply carries at most one consequence that can be refused (transfer_item, hire_hireling, teach)");
        return new DialogueChoice(Text(map, "id"), Text(map, "text"),
            Rows(map, "conditions", at).Select(c => Condition(at, c, loader)).ToImmutableArray(), consequences,
            map.GetValueOrDefault("next") as string);
    }

    private static DialogueCondition Condition(string at, Dictionary<object, object> map, ContentLoader loader)
    {
        string kind = Text(map, "kind");
        bool negated = map.GetValueOrDefault("not") as string == "true";
        if (negated && kind is not ("visited" or "has_item" or "quest_state" or "companion_present" or "deed"))
            throw new FormatException($"{at}: only visited, has_item, quest_state, companion_present and deed conditions take not");
        return kind switch
        {
            "visited" => new VisitedCondition(Text(map, "node"), negated)
            {
                DialogueId = map.GetValueOrDefault("dialogue_ref") is string other ? Defined(loader, other, "dialogue", at) : null,
            },
            "world_state" => WorldState(at, map, loader),
            "has_item" => new HasItemCondition(Defined(loader, Text(map, "item_ref"), "item", at), Positive(map, "count", 1),
                (int)LongOr(map, "quality_min", -1), negated),
            "relationship" => new RelationshipCondition(Defined(loader, Text(map, "npc_ref"), "npc", at), Dimension(Text(map, "dimension"), at),
                (int)LongOr(map, "min", Relationships.Min), (int)LongOr(map, "max", Relationships.Max)),
            "skill" => new SkillCondition(Defined(loader, Text(map, "skill_ref"), "skill", at), Positive(map, "min", 1)),
            "level" => new LevelCondition(Positive(map, "min", 1)),
            "quest_state" => QuestState(at, map, loader, negated),
            "companion_present" => new CompanionPresentCondition(Defined(loader, Text(map, "npc_ref"), "npc", at),
                map.GetValueOrDefault("order") is string order ? Order(order, at) : null, negated),
            "deed" => Deed(at, map, loader, negated),
            "standing" => Standing(at, map, loader),
            "act_done" => ActDone(at, map, loader),
            "near" => new NearCondition(Defined(loader, Text(map, "location_ref"), "location", at),
                map.ContainsKey("radius_m") ? Mm(map, "radius_m") is var radius && radius > 0 ? radius : throw new FormatException($"{at}: radius_m is positive") : null),
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
            "teach" => Teach(at, map),
            "give_recipe" => throw new FormatException($"{at}: give_recipe is replaced by teach, naming entries of the speaker's teaches block (WP9)"),
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
            "recruit_companion" => new RecruitCompanionConsequence
            {
                As = map.GetValueOrDefault("as") as string switch
                {
                    null or "companion" => PartyMembership.Companion,
                    "temporary_ally" => PartyMembership.TemporaryAlly,
                    var other => throw new FormatException($"{at}: recruit_companion joins as companion or temporary_ally, not '{other}' (hirelings are hired)"),
                },
            },
            "hire_hireling" => map.GetValueOrDefault("risk_stated") as string == "true"
                ? new HireHirelingConsequence(PartyKeys.ParseRole(Text(map, "role")) ?? throw new FormatException($"{at}: '{Text(map, "role")}' is not a role"),
                    Positive(map, "days", 1))
                : throw new FormatException($"{at}: hire_hireling needs risk_stated: true - the reply says the hireling can die (DN9)"),
            "dismiss_companion" => new DismissCompanionConsequence(),
            "order_companion" => new OrderCompanionConsequence(Order(Text(map, "order"), at)),
            "report_act" => ReportAct(at, map, loader),
            "add_reputation" => throw new FormatException($"{at}: add_reputation is refused: standing moves only when a member is told of an act (WP6)"),
            _ => throw new FormatException($"{at}: consequence '{command}' is not built in Phase 1 ({string.Join(", ", Consequences)})"),
        };
    }

    /// <summary>
    /// <c>teach</c> (WP9): <c>refs</c>, the entries of the speaker's <c>teaches</c> block this line teaches - all or nothing, on the block's
    /// terms. The cross-check holds them to a participant's entries given <c>by_dialogue</c>.
    /// </summary>
    private static TeachConsequence Teach(string at, Dictionary<object, object> map)
    {
        var refs = List(map, "refs").Select(r => r as string ?? throw new FormatException($"{at}: teach's refs are definition IDs")).ToImmutableArray();
        if (refs.IsEmpty || refs.Distinct(StringComparer.Ordinal).Count() != refs.Length)
            throw new FormatException($"{at}: teach names at least one entry in refs, each once");
        return new TeachConsequence(refs);
    }

    /// <summary><c>world_state</c>: a flag's value from <c>min</c> (1) to <c>max</c>, in the speaker's cell or a named place's (L-17).</summary>
    internal static WorldStateCondition WorldState(string at, Dictionary<object, object> map, ContentLoader loader) =>
        new(Defined(loader, Text(map, "flag_ref"), "world_flag", at), LongOr(map, "min", 1), LongOr(map, "max", long.MaxValue))
        {
            LocationId = map.GetValueOrDefault("location_ref") is string place ? Defined(loader, place, "location", at) : null,
        };

    /// <summary>
    /// <c>standing</c> (WP6): the character's tier with <c>faction_ref</c> from <c>min_tier</c> (default anathema) to <c>max_tier</c>
    /// (default exalted), as levels. FAC001 holds it to the speaker's own faction.
    /// </summary>
    private static StandingCondition Standing(string at, Dictionary<object, object> map, ContentLoader loader)
    {
        string faction = Defined(loader, Text(map, "faction_ref"), "faction", at);
        var (min, max) = FactionContent.TierRange(map, at);
        return new StandingCondition(faction, min, max);
    }

    /// <summary><c>act_done</c> (WP6): the act log holds this act. FAC001 allows it only beside a report of the same act.</summary>
    private static ActDoneCondition ActDone(string at, Dictionary<object, object> map, ContentLoader loader)
    {
        var (kind, subject) = FactionContent.ActAndSubject(loader, map, at);
        return new ActDoneCondition(kind, subject);
    }

    /// <summary><c>report_act</c> (WP6): the character tells the speaker of this act. FAC001 allows it only to a faction that reacts.</summary>
    private static ReportActConsequence ReportAct(string at, Dictionary<object, object> map, ContentLoader loader)
    {
        var (kind, subject) = FactionContent.ActAndSubject(loader, map, at);
        return new ReportActConsequence(kind, subject);
    }

    private static CompanionOrder Order(string order, string at) =>
        CompanionKeys.ParseOrder(order) ?? throw new FormatException($"{at}: a companion's order is follow or wait, not '{order}'");

    /// <summary><c>quest_state</c> (M5): a quest's state, or with <c>objective</c> one of its objectives'; the quest lint checks the objective exists.</summary>
    internal static QuestStateCondition QuestState(string at, Dictionary<object, object> map, ContentLoader loader, bool negated)
    {
        string quest = Defined(loader, Text(map, "quest_ref"), "quest", at);
        string? objective = map.GetValueOrDefault("objective") as string;
        string state = Text(map, "is");
        var states = objective is null ? QuestKeys.QuestStates : QuestKeys.ObjectiveStates;
        if (!states.Contains(state))
            throw new FormatException($"{at}: a {(objective is null ? "quest" : "objective")} is {string.Join(", ", states)}, not '{state}'");
        return new QuestStateCondition(quest, objective, state, negated);
    }

    /// <summary>
    /// <c>deed</c> (E-7): <c>deed</c> is <c>killed</c> (a <c>creature_ref</c>), <c>crafted</c> (an <c>item_ref</c>), <c>harvested</c> (a
    /// <c>resource_ref</c>) or <c>delivered</c> (an <c>item_ref</c>, optionally to <c>npc_ref</c>); <c>count</c> is how many times at least (1).
    /// </summary>
    internal static DeedCondition Deed(string at, Dictionary<object, object> map, ContentLoader loader, bool negated)
    {
        string key = Text(map, "deed");
        var kind = DeedKeys.Parse(key)
                   ?? throw new FormatException($"{at}: a deed is {string.Join(", ", DeedKeys.Tallied.Select(DeedKeys.Key))}, not '{key}'");
        var (field, subjectKind) = kind switch
        {
            DeedKind.Killed => ("creature_ref", "creature"),
            DeedKind.Harvested => ("resource_ref", "resource"),
            _ => ("item_ref", "item"),
        };
        string? npc = map.GetValueOrDefault("npc_ref") is string named ? Defined(loader, named, "npc", at) : null;
        if (npc is not null && kind != DeedKind.Delivered)
            throw new FormatException($"{at}: only a delivered deed names npc_ref");
        long? within = map.ContainsKey("within_s")
            ? ToTicks(Number(map, "within_s"), WorldContent.TickMilliseconds(loader)) is var ticks && ticks > 0 ? ticks : throw new FormatException($"{at}: within_s is positive")
            : null;
        return new DeedCondition(kind, Defined(loader, Text(map, field), subjectKind, at), Positive(map, "count", 1), negated) { NpcId = npc, WithinTicks = within };
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
            foreach (var visited in node.Variants.SelectMany(v => v.Conditions).OfType<VisitedCondition>().Where(v => v.DialogueId is null || v.DialogueId == id))
                Exists(visited.NodeId, $"node {node.Id}'s variant's visited condition");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var at = node; at.Once && at.NextIfExhausted is { } next; at = dialogue.Nodes[next])
            {
                if (!seen.Add(at.Id))
                    throw new FormatException($"{id}: spent lines loop through node {at.Id}");
            }
        }
        foreach (var visited in dialogue.Barks.Concat(dialogue.Banter).SelectMany(b => b.Conditions).OfType<VisitedCondition>().Where(v => v.DialogueId is null || v.DialogueId == id))
            Exists(visited.NodeId, "a bark's visited condition");
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
        // A lesson given by conversation (WP9) is given by a line of the teacher's own: otherwise nothing would ever teach it.
        foreach (var npc in npcs.Values)
        {
            var taught = npc.DialogueId is { } own && dialogues.TryGetValue(own, out var spoken)
                ? spoken.Nodes.Values.SelectMany(n => n.Choices).SelectMany(c => c.Consequences).OfType<TeachConsequence>().SelectMany(t => t.DefinitionIds).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            if (npc.Teaches.FirstOrDefault(o => o.ByDialogue && !taught.Contains(o.DefinitionId)) is { } untaught)
                throw new FormatException($"{npc.Id}: teaches {untaught.DefinitionId} by_dialogue, but no line of {npc.DialogueId ?? "their conversation"} teaches it");
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
            // Only someone who can be hired, in that role, is hired by their own conversation (WP15a).
            foreach (var hire in dialogue.Nodes.Values.SelectMany(n => n.Choices).SelectMany(c => c.Consequences).OfType<HireHirelingConsequence>())
            {
                if (!dialogue.Participants.Any(p => npcs.TryGetValue(p, out var npc) && npc.Hireling is { } h && h.Roles.Contains(hire.Role)))
                    throw new FormatException($"{dialogue.Id}: hires a {PartyKeys.Key(hire.Role)}, but none of its participants hires on as one");
            }
            // A line teaches only what its speaker gives by conversation (WP9): an entry of their block marked by_dialogue. The lesson is
            // asked of the NPC the conversation was opened with, so every NPC who opens it must give it.
            foreach (string taught in dialogue.Nodes.Values.SelectMany(n => n.Choices).SelectMany(c => c.Consequences).OfType<TeachConsequence>()
                         .SelectMany(t => t.DefinitionIds))
            {
                if (npcs.Values.Where(n => n.DialogueId == dialogue.Id).FirstOrDefault(n => !n.Teaches.Any(o => o.DefinitionId == taught && o.ByDialogue)) is { } speaker)
                    throw new FormatException($"{dialogue.Id}: teaches {taught}, which {speaker.Id}, who speaks it, does not give by_dialogue");
            }
            // Banter is said on the road by a party member (WP15b): only someone who can join or be hired has any.
            if (!dialogue.Banter.IsEmpty && !dialogue.Participants.Any(p => npcs.TryGetValue(p, out var npc) && (npc.Companion is not null || npc.Hireling is not null)))
                throw new FormatException($"{dialogue.Id}: has banter, but none of its participants can join or be hired");
            bool dismissal = dialogue.Nodes.Values.SelectMany(n => n.Choices).SelectMany(c => c.Consequences).Any(c => c is DismissCompanionConsequence);
            if (dismissal && !dialogue.Participants.Any(p => npcs.TryGetValue(p, out var npc) && (npc.Companion is not null || npc.Hireling is not null)))
                throw new FormatException($"{dialogue.Id}: dismisses a party member, but none of its participants can join or be hired");
            // A visited condition about another conversation names a line that conversation has.
            foreach (var visited in dialogue.Nodes.Values.SelectMany(n => n.Choices).SelectMany(c => c.Conditions)
                         .Concat(dialogue.Nodes.Values.SelectMany(n => n.Variants).SelectMany(v => v.Conditions))
                         .Concat(dialogue.Barks.Concat(dialogue.Banter).SelectMany(b => b.Conditions)).OfType<VisitedCondition>()
                         .Where(v => v.DialogueId is { } other && other != dialogue.Id))
            {
                if (!dialogues.TryGetValue(visited.DialogueId!, out var other) || !other.Nodes.ContainsKey(visited.NodeId))
                    throw new FormatException($"{dialogue.Id}: a visited condition names node '{visited.NodeId}' of {visited.DialogueId}, which it does not have");
            }
        }
        // Values are a party member's (WP15b): they react to what the character does with them along.
        if (npcs.Values.FirstOrDefault(n => !n.Values.IsEmpty && n.Companion is null && n.Hireling is null) is { } valued)
            throw new FormatException($"{valued.Id}: only someone who can join or be hired has values (they react to what the character does in the party)");
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
