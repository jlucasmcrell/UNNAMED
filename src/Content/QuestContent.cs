// UNNAMED Content - quests from content (DATA_MODEL.md §4.11; SYSTEMS.md S-29; D-07; M5)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// Builds the quests and lints what the reference pass cannot see (QST001): every objective's <c>type</c> is one of the closed set
/// and one Phase 1 builds, with exactly the parameters it takes; every objective is reachable from the entry and none is an orphan;
/// the graph has no cycle, a join waits only on objectives that lead to it and never on two alternatives of one branch, a timed
/// objective says where failure goes; rewards are closed-set kinds; and across content, something starts every quest, every line
/// a <c>talk_to</c> waits for exists, every crafted or delivered item can be crafted or delivered, and every <c>quest_state</c>
/// condition names a quest and objective that exist. WP4 adds the story fields (<c>track</c>, <c>act</c>, <c>on_new_game</c>,
/// <c>competitive</c>), objective <c>effects</c> (<c>choice</c>), and the objectives <c>defeat_creature</c>/<c>defeat_boss</c> (a boss never
/// returns; one defeat flag records one creature in one place), <c>companion_present</c> (someone who can join), <c>dialogue_choice</c> (a
/// reply the line offers), <c>travel_to</c> and <c>waystone_known</c> (a stone some region places).
/// </summary>
public static class QuestContent
{
    private static readonly string[] Envelope = { "id", "kind", "schema", "display_key", "tags", "notes" };

    private static readonly string[] Fields =
        { "title", "summary", "giver_ref", "entry_objective", "objectives", "fail_if", "rewards", "repeat_policy", "track", "act", "on_new_game", "competitive", "companion_ref", "confides_at" };

    /// <summary>DATA_MODEL.md §4.11's quest fields that Phase 1 does not build.</summary>
    private static readonly string[] FieldsNotBuilt =
        { "title_key", "summary_key", "journal_entries", "level_band", "faction_ref", "parent_quest_ref", "chain_index", "abandon_policy" };

    private static readonly string[] ObjectiveFields =
        { "id", "type", "description", "params", "next", "all_of", "branch", "visibility", "time_limit_min", "on_fail", "effects" };

    private static readonly string[] ObjectiveFieldsNotBuilt =
        { "description_key", "hidden", "optional", "any_of", "not", "timer_anchor", "world_time_required", "requirements" };

    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        var quests = new List<QuestDefinition>();
        foreach (var definition in loader.GetByKind("quest").Values.OrderBy(d => d.Id, StringComparer.Ordinal))
            Try(() => { quests.Add(Parse(definition.Id, definition.YamlSource, loader)); return definition; }, definition.Id, errors);
        if (errors.Count == 0)
            Try(() => CrossCheck(quests.ToImmutableSortedDictionary(q => q.Id, q => q, StringComparer.Ordinal), loader), "quests", errors);
        return errors;
    }

    public static ImmutableSortedDictionary<string, QuestDefinition> BuildQuests(ContentLoader loader) =>
        loader.GetByKind("quest").Values.Select(d => Parse(d.Id, d.YamlSource, loader))
            .ToImmutableSortedDictionary(q => q.Id, q => q, StringComparer.Ordinal);

    /// <summary>
    /// One quest from its YAML: <c>title</c> and <c>summary</c> (inline until localization), <c>giver_ref</c>, <c>entry_objective</c>,
    /// <c>objectives</c> in authored order, <c>fail_if</c> and <c>rewards</c>. Content references resolve against the loaded content.
    /// </summary>
    public static QuestDefinition Parse(string id, string? yaml, ContentLoader loader)
    {
        var map = Read(yaml);
        foreach (string key in map.Keys.Select(k => k as string ?? ""))
        {
            if (FieldsNotBuilt.Contains(key))
                throw new FormatException($"{id}: {key} is not built in Phase 1");
            if (!Envelope.Contains(key) && !Fields.Contains(key))
                throw new FormatException($"{id}: '{key}' is not a quest field");
        }
        // Game minutes become ticks through config.time, read only when a quest counts time.
        var ticksPerMinute = new Lazy<double>(() => TicksPerGameMinute(loader));
        var objectives = Rows(map, "objectives", id).Select(row => Objective(id, row, loader, ticksPerMinute)).ToImmutableArray();
        if (objectives.IsEmpty)
            throw new FormatException($"{id}: a quest has objectives");
        var failIf = Rows(map, "fail_if", id).Select((row, i) => row.Keys.All(k => k as string is "type" or "params")
            ? Condition($"{id} fail_if[{i}]", row, loader, ticksPerMinute)
            : throw new FormatException($"{id} fail_if[{i}]: a fail_if entry is a type and its params")).ToImmutableArray();
        if (failIf.FirstOrDefault(c => c.CountsDeeds) is { } counting)
            throw new FormatException($"{id}: fail_if reads the world as it stands; {counting.Type} counts deeds");
        string? giver = map.GetValueOrDefault("giver_ref") as string;
        if (giver is not null)
            Defined(loader, giver, "npc", id);
        var quest = new QuestDefinition(id, Text(map, "title"), Text(map, "summary"), giver, Text(map, "entry_objective"), objectives, failIf,
            Rows(map, "rewards", id).Select((row, i) => Reward($"{id} rewards[{i}]", row, loader)).ToImmutableArray())
        {
            RepeatAfterTicks = map.ContainsKey("repeat_policy") ? Repeat(id, Map(map, "repeat_policy"), ticksPerMinute) : null,
            Track = map.GetValueOrDefault("track") is { } track
                ? QuestTracks.Parse(track as string ?? "") ?? throw new FormatException($"{id}: track is main, side or bounty, not '{track}'")
                : QuestTrack.Side,
            Act = map.ContainsKey("act") ? Positive(map, "act", id) : null,
            OnNewGame = Flag(map, "on_new_game", id),
            Competitive = Flag(map, "competitive", id),
            CompanionId = map.GetValueOrDefault("companion_ref") is string companion ? Companion(loader, companion, id) : null,
            ConfidesAt = map.ContainsKey("confides_at") ? (int)LongOf(map, "confides_at", id) : null,
        };
        // A personal quest (WP15b): a companion's, gated on their trust; and only a personal quest changes its companion's approval.
        if (quest.ConfidesAt is { } gate && (quest.CompanionId is null || gate is < Relationships.Min or > Relationships.Max))
            throw new FormatException($"{id}: confides_at gates a personal quest (companion_ref) on trust, from {Relationships.Min} to {Relationships.Max}");
        if (quest.CompanionId is null && quest.Rewards.OfType<ApprovalChangeReward>().Any())
            throw new FormatException($"{id}: an approval_change reward moves the personal quest's companion's regard, so the quest names companion_ref");
        if (quest.CompanionId is not null && (quest.Track == QuestTrack.Bounty || quest.Competitive))
            throw new FormatException($"{id}: a companion's personal quest is not bounty work");
        if (quest.Track == QuestTrack.Main != quest.Act.HasValue)
            throw new FormatException($"{id}: a main-track quest names its act, and only a main-track quest has one");
        if (quest.Track == QuestTrack.Bounty && quest.RepeatAfterTicks is null)
            throw new FormatException($"{id}: a bounty is standing work, posted again after it is done (repeat_policy)");
        if (quest.Competitive && quest.Track != QuestTrack.Bounty)
            throw new FormatException($"{id}: only a bounty (track: bounty) is competitive");
        if (quest.OnNewGame && quest.RepeatAfterTicks is not null)
            throw new FormatException($"{id}: a quest every new character starts is taken once (no repeat_policy)");
        CheckGraph(quest);
        return quest;
    }

    /// <summary><c>companion_ref</c> (WP15b): an NPC who can join the character as a companion - a personal quest is a companion's.</summary>
    private static string Companion(ContentLoader loader, string npcId, string at)
    {
        Defined(loader, npcId, "npc", at);
        return Read(loader.Definitions[npcId].YamlSource).ContainsKey("companion")
            ? npcId
            : throw new FormatException($"{at}: companion_ref {npcId} cannot join the character as a companion");
    }

    private static bool Flag(Dictionary<object, object> map, string key, string at) =>
        map.GetValueOrDefault(key) switch
        {
            null => false,
            "true" => true,
            "false" => false,
            var other => throw new FormatException($"{at}: {key} is true or false, not '{other}'"),
        };

    /// <summary>
    /// <c>repeat_policy: { repeatable, cooldown_min }</c>: a repeatable quest is offered again <c>cooldown_min</c> game minutes after it
    /// ends. Its counted objectives count afresh each time, so each is a hard count (PROGRESSION.md AG-5).
    /// </summary>
    private static long? Repeat(string id, Dictionary<object, object> policy, Lazy<double> ticksPerMinute)
    {
        if (policy.Keys.Select(k => k as string ?? "").FirstOrDefault(k => k is not ("repeatable" or "cooldown_min")) is { } stray)
            throw new FormatException($"{id}: '{stray}' is not part of repeat_policy (repeatable, cooldown_min)");
        if (policy.GetValueOrDefault("repeatable") as string != "true")
            return policy.ContainsKey("cooldown_min") ? throw new FormatException($"{id}: only a repeatable quest has a cooldown_min") : null;
        return Ticks(Positive(policy, "cooldown_min", $"{id} repeat_policy"), ticksPerMinute);
    }

    private static ObjectiveDefinition Objective(string questId, Dictionary<object, object> map, ContentLoader loader, Lazy<double> ticksPerMinute)
    {
        string id = Text(map, "id");
        string at = $"{questId} objective {id}";
        foreach (string key in map.Keys.Select(k => k as string ?? ""))
        {
            if (ObjectiveFieldsNotBuilt.Contains(key))
                throw new FormatException($"{at}: {key} is not built in Phase 1" + (key == "hidden" ? " (visibility: hidden)" : ""));
            if (!ObjectiveFields.Contains(key))
                throw new FormatException($"{at}: '{key}' is not an objective field");
        }
        var condition = Condition(at, map, loader, ticksPerMinute);
        string branch = map.GetValueOrDefault("branch") as string ?? "all";
        if (branch is not ("all" or "first"))
            throw new FormatException($"{at}: branch is all or first, not '{branch}'");
        string visibility = map.GetValueOrDefault("visibility") as string ?? "when_active";
        if (visibility is not ("when_active" or "hidden"))
            throw new FormatException($"{at}: visibility is when_active or hidden in Phase 1, not '{visibility}'");
        long? limit = map.ContainsKey("time_limit_min") ? Ticks(Positive(map, "time_limit_min", at), ticksPerMinute) : null;
        string? onFail = map.GetValueOrDefault("on_fail") as string;
        if (limit is null != onFail is null)
            throw new FormatException($"{at}: a time_limit_min says where failure goes (on_fail), and only a timed objective fails");
        return new ObjectiveDefinition(id, Text(map, "description"), condition, Ids(map, "next", at))
        {
            Effects = Rows(map, "effects", at).Select((row, i) => Effect($"{at} effects[{i}]", row, loader)).ToImmutableArray(),
            AllOf = Ids(map, "all_of", at),
            FirstBranch = branch == "first",
            Hidden = visibility == "hidden",
            TimeLimitTicks = limit,
            OnFail = onFail,
        };
    }

    /// <summary>An objective's predicate - or a <c>fail_if</c> entry, which is the same shape: <c>type</c> and its <c>params</c>.</summary>
    private static ObjectiveCondition Condition(string at, Dictionary<object, object> map, ContentLoader loader, Lazy<double> ticksPerMinute)
    {
        string type = Text(map, "type");
        if (ObjectiveTypes.NotBuilt.TryGetValue(type, out var waitsFor))
            throw new FormatException($"{at}: objective type '{type}' is not built in Phase 1 (it waits for {waitsFor})");
        if (!ObjectiveTypes.Built.Contains(type))
            throw new FormatException($"{at}: '{type}' is not an objective type (DATA_MODEL.md §4.11's closed set: {string.Join(", ", ObjectiveTypes.Built)})");
        var p = map.TryGetValue("params", out var value) ? value as Dictionary<object, object> ?? throw new FormatException($"{at}: params is a map")
            : new Dictionary<object, object>();
        string where = $"{at} ({type})";
        ObjectiveCondition condition = type switch
        {
            "talk_to" => TalkTo(where, p, loader),
            "visit_location" => new VisitLocation(Defined(loader, Text(p, "location_ref"), "location", where)),
            "explore_location" => new ExploreLocation(Defined(loader, Text(p, "location_ref"), "location", where),
                Mm(p, "within_m") is var within && within > 0 ? within : throw new FormatException($"{where}: within_m is more than 0")),
            "acquire_item" => Acquire(where, p, loader),
            "craft_item" => new CraftItem(Defined(loader, Text(p, "item_ref"), "item", where), Positive(p, "count", where, 1), QualityMin(p, where)),
            "harvest_resource" => new HarvestResource(Defined(loader, Text(p, "resource_ref"), "resource", where), Positive(p, "count", where, 1)),
            "kill_creature" => new KillCreature(Defined(loader, Text(p, "creature_ref"), "creature", where), Positive(p, "count", where, 1)),
            "deliver_item" => new DeliverItem(Defined(loader, Text(p, "npc_ref"), "npc", where), Defined(loader, Text(p, "item_ref"), "item", where),
                Positive(p, "count", where, 1)),
            "world_state" => new WorldStateObjective(Defined(loader, Text(p, "flag_ref"), "world_flag", where),
                Defined(loader, Text(p, "location_ref"), "location", where),
                p.ContainsKey("value") ? LongOf(p, "value", where) : LongOr(p, "min", long.MinValue, where),
                p.ContainsKey("value") ? LongOf(p, "value", where) : LongOr(p, "max", long.MaxValue, where)),
            "relationship_value" => new RelationshipValueObjective(Defined(loader, Text(p, "npc_ref"), "npc", where), Dimension(Text(p, "dimension"), where),
                (int)Math.Max(LongOr(p, "min", int.MinValue, where), int.MinValue), (int)Math.Min(LongOr(p, "max", int.MaxValue, where), int.MaxValue)),
            "wait_until" => new WaitUntil(Ticks(Positive(p, "after_min", where), ticksPerMinute)),
            "defeat_creature" or "defeat_boss" => new DefeatCreature(Defined(loader, Text(p, "creature_ref"), "creature", where),
                Defined(loader, Text(p, "flag_ref"), "world_flag", where), Defined(loader, Text(p, "location_ref"), "location", where), type == "defeat_boss"),
            "companion_present" => new CompanionPresent(Defined(loader, Text(p, "npc_ref"), "npc", where),
                p.GetValueOrDefault("order") is { } order
                    ? CompanionKeys.ParseOrder(order as string ?? "") ?? throw new FormatException($"{where}: order is follow or wait, not '{order}'")
                    : null),
            "dialogue_choice" => Choice(where, p, loader),
            "travel_to" => new TravelTo(StoneKey(Text(p, "waystone_ref"), where)),
            "waystone_known" => new WaystoneKnown(StoneKey(Text(p, "waystone_ref"), where)),
            "faction_reputation" => FactionReputationOf(where, p, loader),
            _ => throw new FormatException($"{at}: '{type}' is not built"),
        };
        if (p.ContainsKey("ever"))
        {
            bool ever = p["ever"] as string switch { "true" => true, "false" => false, _ => throw new FormatException($"{where}: ever is true or false") };
            condition = condition switch
            {
                AcquireItem a when !ever || a.QualityMin == Quality.Crude => a with { Ever = ever },
                CraftItem c when !ever || c.QualityMin == Quality.Crude => c with { Ever = ever },
                _ => throw new FormatException($"{where}: ever applies only to acquire_item/craft_item without a quality minimum; the historical ledger has no quality bands"),
            };
        }
        var allowed = Params(type);
        if (p.Keys.Select(k => k as string ?? "").FirstOrDefault(k => !allowed.Contains(k)) is { } stray)
            throw new FormatException($"{where}: '{stray}' is not a parameter of {type} ({string.Join(", ", allowed)})");
        if (condition is WorldStateObjective { Min: var min, Max: var max } && (min == long.MinValue && max == long.MaxValue || min > max))
            throw new FormatException($"{where}: world_state names a value, or a min and a max in order");
        return condition;
    }

    private static string[] Params(string type) => type switch
    {
        "talk_to" => new[] { "npc_ref", "dialogue_ref", "nodes" },
        "visit_location" => new[] { "location_ref" },
        "explore_location" => new[] { "location_ref", "within_m" },
        "acquire_item" => new[] { "item_ref", "or_item_refs", "count", "quality_min", "ever" },
        "craft_item" => new[] { "item_ref", "count", "quality_min", "ever" },
        "harvest_resource" => new[] { "resource_ref", "count" },
        "kill_creature" => new[] { "creature_ref", "count" },
        "deliver_item" => new[] { "npc_ref", "item_ref", "count" },
        "world_state" => new[] { "flag_ref", "location_ref", "value", "min", "max" },
        "relationship_value" => new[] { "npc_ref", "dimension", "min", "max" },
        "wait_until" => new[] { "after_min" },
        "defeat_creature" or "defeat_boss" => new[] { "creature_ref", "flag_ref", "location_ref" },
        "companion_present" => new[] { "npc_ref", "order" },
        "dialogue_choice" => new[] { "npc_ref", "dialogue_ref", "node", "choice" },
        "travel_to" or "waystone_known" => new[] { "waystone_ref" },
        "faction_reputation" => new[] { "faction_ref", "min_tier", "max_tier" },
        _ => Array.Empty<string>(),
    };

    /// <summary><c>faction_reputation</c> (WP6): the character's standing with <c>faction_ref</c> in a tier range.</summary>
    private static FactionReputation FactionReputationOf(string where, Dictionary<object, object> p, ContentLoader loader)
    {
        var (min, max) = FactionContent.TierRange(p, where);
        return new FactionReputation(Defined(loader, Text(p, "faction_ref"), "faction", where), min, max);
    }

    /// <summary>A Waystone's key, as a region names it (<c>waystone.&lt;name&gt;</c>); that some region has it is checked across content.</summary>
    private static string StoneKey(string key, string at) =>
        key.StartsWith("waystone.", StringComparison.Ordinal) && key.Length > "waystone.".Length
            ? key
            : throw new FormatException($"{at}: '{key}' is not a Waystone key (waystone.<name>)");

    /// <summary><c>dialogue_choice</c>: the NPC's own conversation unless one is named, the line, and the reply given there.</summary>
    private static ChoiceGiven Choice(string where, Dictionary<object, object> p, ContentLoader loader)
    {
        string npc = Defined(loader, Text(p, "npc_ref"), "npc", where);
        string dialogue = p.GetValueOrDefault("dialogue_ref") as string
            ?? Read(loader.Definitions[npc].YamlSource).GetValueOrDefault("dialogue_ref") as string
            ?? throw new FormatException($"{where}: {npc} has no conversation");
        Defined(loader, dialogue, "dialogue", where);
        return new ChoiceGiven(npc, dialogue, Text(p, "node"), Text(p, "choice"));
    }

    /// <summary>
    /// An objective's effect - the closed set is <c>choice</c>: a namespaced world flag (<c>flag_ref</c>, read and set in the cell of
    /// <c>location_ref</c>) takes <c>value</c> (1 unless named), and the journal keeps <c>line</c>.
    /// </summary>
    private static QuestEffect Effect(string at, Dictionary<object, object> map, ContentLoader loader)
    {
        string kind = Text(map, "kind");
        if (kind != "choice")
            throw new FormatException($"{at}: '{kind}' is not an objective effect (choice)");
        if (map.Keys.Select(k => k as string ?? "").FirstOrDefault(k => k is not ("kind" or "flag_ref" or "location_ref" or "value" or "line")) is { } stray)
            throw new FormatException($"{at}: '{stray}' is not part of a choice (flag_ref, location_ref, value, line)");
        string flag = Defined(loader, Text(map, "flag_ref"), "world_flag", at);
        if (flag.Split('.').Length < 3)
            throw new FormatException($"{at}: a choice's flag is namespaced (world.<story>.<name>), not '{flag}'");
        string line = Text(map, "line");
        if (string.IsNullOrWhiteSpace(line))
            throw new FormatException($"{at}: a choice has a journal line");
        return new ChoiceEffect(flag, Defined(loader, Text(map, "location_ref"), "location", at), LongOr(map, "value", 1, at), line);
    }

    /// <summary><c>acquire_item</c>: the item, and <c>or_item_refs</c> - other items that count too, such as what it is made into.</summary>
    private static AcquireItem Acquire(string where, Dictionary<object, object> p, ContentLoader loader)
    {
        string item = Defined(loader, Text(p, "item_ref"), "item", where);
        var alternatives = p.ContainsKey("or_item_refs")
            ? List(p, "or_item_refs").Select(v => Defined(loader, v as string ?? throw new FormatException($"{where}: or_item_refs lists item IDs"), "item", where))
                .ToImmutableArray()
            : ImmutableArray<string>.Empty;
        if (alternatives.Contains(item) || alternatives.Distinct(StringComparer.Ordinal).Count() != alternatives.Length)
            throw new FormatException($"{where}: or_item_refs names each item once, and not the item_ref itself");
        return new AcquireItem(item, Positive(p, "count", where, 1), QualityMin(p, where)) { OrItems = alternatives };
    }

    /// <summary><c>talk_to</c>: the NPC's own conversation unless one is named, and its opening line unless lines are named.</summary>
    private static TalkTo TalkTo(string where, Dictionary<object, object> p, ContentLoader loader)
    {
        string npc = Defined(loader, Text(p, "npc_ref"), "npc", where);
        string dialogue = p.GetValueOrDefault("dialogue_ref") as string
            ?? Read(loader.Definitions[npc].YamlSource).GetValueOrDefault("dialogue_ref") as string
            ?? throw new FormatException($"{where}: {npc} has no conversation");
        Defined(loader, dialogue, "dialogue", where);
        var nodes = p.ContainsKey("nodes")
            ? List(p, "nodes").Select(n => n as string ?? throw new FormatException($"{where}: nodes are node IDs")).ToImmutableArray()
            : ImmutableArray.Create(Text(Read(loader.Definitions[dialogue].YamlSource), "root_node"));
        if (nodes.IsEmpty)
            throw new FormatException($"{where}: nodes names at least one line");
        return new TalkTo(npc, dialogue, nodes);
    }

    private static QuestReward Reward(string at, Dictionary<object, object> map, ContentLoader loader)
    {
        string kind = Text(map, "kind");
        if (RewardKinds.NotBuilt.Contains(kind))
            throw new FormatException($"{at}: reward kind '{kind}' is not built in Phase 1");
        return kind switch
        {
            "xp" => new XpReward(Positive(map, "amount", at)),
            "currency" => new CurrencyReward(Positive(map, "amount", at)),
            "item" => new ItemReward(Defined(loader, Text(map, "item_ref"), "item", at), Positive(map, "count", at, 1)),
            "recipe" => new TechniqueReward(Defined(loader, Text(map, "recipe_ref"), "recipe", at), kind),
            "spell" => new TechniqueReward(Defined(loader, Text(map, "spell_ref"), "spell", at), kind),
            "relationship" => new RelationshipReward(Defined(loader, Text(map, "npc_ref"), "npc", at), Dimension(Text(map, "dimension"), at),
                LongOf(map, "amount", at) is var delta && delta != 0 && Math.Abs(delta) <= Relationships.Max
                    ? (int)delta
                    : throw new FormatException($"{at}: a relationship reward moves a dimension by 1 to 100 either way")),
            "world_flag" => new WorldFlagReward(Defined(loader, Text(map, "flag_ref"), "world_flag", at),
                Defined(loader, Text(map, "location_ref"), "location", at), LongOr(map, "value", 1, at)),
            "approval_change" => Map(map, "changes").ToImmutableSortedDictionary(kv => Dimension(kv.Key as string ?? "", at),
                kv => LongOf(Map(map, "changes"), kv.Key as string ?? "", at) is var d && d != 0 && Math.Abs(d) <= Relationships.Max
                    ? (int)d
                    : throw new FormatException($"{at}: an approval change moves a dimension by 1 to 100 either way"), StringComparer.Ordinal) is { IsEmpty: false } changes
                ? new ApprovalChangeReward(changes)
                : throw new FormatException($"{at}: an approval change moves at least one dimension"),
            "reputation" => new ReputationReward(Defined(loader, Text(map, "faction_ref"), "faction", at),
                LongOf(map, "amount", at) is var points && points != 0 && Math.Abs(points) <= FactionContent.MaxDelta
                    ? (int)points
                    : throw new FormatException($"{at}: a reputation reward moves standing by 1 to {FactionContent.MaxDelta} points either way")),
            _ => throw new FormatException($"{at}: '{kind}' is not a reward kind ({string.Join(", ", RewardKinds.Built)})"),
        };
    }

    /// <summary>
    /// The graph: the entry and every name it uses exist; everything is reachable from the entry (no orphans); nothing loops; a
    /// branch point has alternatives; a join waits only on objectives that lead to it, and never on two alternatives of one branch.
    /// </summary>
    private static void CheckGraph(QuestDefinition quest)
    {
        string id = quest.Id;
        if (quest.Objectives.Count != quest.Order.Length)
            throw new FormatException($"{id}: two objectives share an id");
        void Exists(string name, string what)
        {
            if (!quest.Objectives.ContainsKey(name))
                throw new FormatException($"{id}: {what} names objective '{name}', which it does not have");
        }
        Exists(quest.Entry, "entry_objective");
        if (!quest.Objectives[quest.Entry].AllOf.IsEmpty)
            throw new FormatException($"{id}: the entry objective waits on nothing (all_of)");
        foreach (var o in quest.Order)
        {
            foreach (string n in o.Next)
                Exists(n, $"{o.Id}'s next");
            foreach (string a in o.AllOf)
            {
                Exists(a, $"{o.Id}'s all_of");
                if (!quest.Objectives[a].Next.Contains(o.Id))
                    throw new FormatException($"{id}: {o.Id} waits on {a}, which does not lead to it (next)");
            }
            if (o.OnFail is { } onFail && onFail != QuestRules.FailQuest)
                Exists(onFail, $"{o.Id}'s on_fail");
            if (o.FirstBranch && o.Next.Length < 2)
                throw new FormatException($"{id}: {o.Id} branches (branch: first) between at least two objectives");
        }

        // Alternatives of one branch can never both be satisfied, so no join may wait on two of them.
        foreach (var point in quest.Order.Where(o => o.FirstBranch))
        {
            foreach (var join in quest.Order.Where(o => o.AllOf.Count(point.Next.Contains) > 1))
                throw new FormatException($"{id}: {join.Id} waits on two alternatives of {point.Id}'s branch, and only one is ever taken");
        }

        var edges = quest.Order.ToDictionary(o => o.Id, o => o.Next.Concat(o.OnFail is { } f && f != QuestRules.FailQuest ? new[] { f } : Array.Empty<string>()).ToList(),
            StringComparer.Ordinal);
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>(new[] { quest.Entry });
        while (stack.Count > 0)
        {
            string at = stack.Pop();
            if (reached.Add(at))
                foreach (string n in edges[at])
                    stack.Push(n);
        }
        if (quest.Order.FirstOrDefault(o => !reached.Contains(o.Id)) is { } orphan)
            throw new FormatException($"{id}: nothing leads to objective {orphan.Id} (an orphan)");

        var state = new Dictionary<string, int>(StringComparer.Ordinal);   // 1 visiting, 2 done
        void Visit(string at)
        {
            state[at] = 1;
            foreach (string n in edges[at])
            {
                if (state.GetValueOrDefault(n) == 1)
                    throw new FormatException($"{id}: objectives loop through {n}");
                if (state.GetValueOrDefault(n) == 0)
                    Visit(n);
            }
            state[at] = 2;
        }
        Visit(quest.Entry);
    }

    /// <summary>What a quest needs from the rest of content, and what conversations say about quests.</summary>
    private static object CrossCheck(ImmutableSortedDictionary<string, QuestDefinition> quests, ContentLoader loader)
    {
        var dialogues = SocialContent.BuildDialogues(loader);
        var npcs = SocialContent.BuildNpcs(loader);
        var recipes = CraftingContent.BuildRecipes(loader);
        var nodes = CraftingContent.BuildNodes(loader);
        var replies = dialogues.Values.SelectMany(d => d.Nodes.Values.SelectMany(n => n.Choices.Select(c => (Dialogue: d, Node: n, Choice: c)))).ToList();
        var renewable = new Lazy<HashSet<string>>(() => Renewable(loader, recipes, nodes));
        var usedUp = new Lazy<HashSet<string>>(() => CombatContent.BuildUseEffects(loader).Keys.Concat(MagicContent.Build(loader).Teaches.Keys)
            .ToHashSet(StringComparer.Ordinal));

        // Every Waystone key the regions place, read only when an objective names a stone.
        var stones = new Lazy<HashSet<string>>(() => loader.GetByKind("region").Keys
            .SelectMany(region => WorldContent.BuildLayout(loader, region).Waystones.Select(w => w.Key)).ToHashSet(StringComparer.Ordinal));
        var creatureSpawns = new Lazy<List<UNNAMED.World.Runtime.SpawnSite>>(() => loader.GetByKind("region").Keys.SelectMany(region => CombatContent.BuildSpawns(loader, region)).ToList());
        var defeats = new Dictionary<string, (string Creature, string Location, string At)>(StringComparer.Ordinal);

        foreach (var quest in quests.Values)
        {
            if (!quest.OnNewGame && !replies.Any(r => r.Choice.Consequences.OfType<StartQuestConsequence>().Any(s => s.QuestId == quest.Id)))
                throw new FormatException($"{quest.Id}: nothing starts it (no conversation's start_quest names it, and it is not on_new_game)");
            foreach (var objective in quest.Order.Select(o => (o.Id, o.Condition)).Concat(quest.FailIf.Select((c, i) => ($"fail_if[{i}]", c))))
            {
                string at = $"{quest.Id} {objective.Item1}";
                switch (objective.Item2)
                {
                    case TalkTo t:
                        var dialogue = dialogues[t.DialogueId];
                        if (!dialogue.Participants.Contains(t.NpcId))
                            throw new FormatException($"{at}: {t.NpcId} does not speak {t.DialogueId}");
                        if (t.Nodes.FirstOrDefault(n => !dialogue.Nodes.ContainsKey(n)) is { } missing)
                            throw new FormatException($"{at}: {t.DialogueId} has no line '{missing}'");
                        break;
                    case AcquireItem a:
                        FiniteSupply(at, a, recipes, renewable.Value, usedUp.Value);
                        break;
                    case CraftItem c when !recipes.Values.Any(r => r.OutputItemId == c.ItemId):
                        throw new FormatException($"{at}: no recipe makes {c.ItemId}");
                    case HarvestResource h when !nodes.Values.Any(n => n.ResourceId == h.ResourceId):
                        throw new FormatException($"{at}: no node yields {h.ResourceId}");
                    case DeliverItem d when !replies.Any(r => r.Dialogue.Participants.Contains(d.NpcId)
                            && r.Choice.Consequences.OfType<TransferItemConsequence>().Any(x => !x.ToPlayer && x.ItemId == d.ItemId)):
                        throw new FormatException($"{at}: no reply of {d.NpcId}'s takes {d.ItemId}, so it can never be delivered");
                    case DefeatCreature d:
                        // One flag records one creature's defeat, in one place: every objective naming it reads what the death wrote.
                        if (defeats.TryGetValue(d.FlagId, out var first) && (first.Creature != d.CreatureId || first.Location != d.LocationId))
                            throw new FormatException($"{at}: {d.FlagId} already records {first.Creature} at {first.Location} ({first.At})");
                        defeats[d.FlagId] = (d.CreatureId, d.LocationId, at);
                        if (!creatureSpawns.Value.Any(s => s.Members.Any(m => m.CreatureId == d.CreatureId)))
                            throw new FormatException($"{at}: no spawn places {d.CreatureId}, so it can never be defeated");
                        if (d.Boss && creatureSpawns.Value.FirstOrDefault(s => s.RespawnTicks > 0 && s.Members.Any(m => m.CreatureId == d.CreatureId)) is { } returns)
                            throw new FormatException($"{at}: a boss does not return, and {d.CreatureId} returns at {returns.Key}");
                        break;
                    case CompanionPresent c when npcs.TryGetValue(c.NpcId, out var npc) && npc.Companion is null:
                        throw new FormatException($"{at}: {c.NpcId} cannot join the character, so is never a companion");
                    case ChoiceGiven c:
                    {
                        var conversation = dialogues[c.DialogueId];
                        if (!conversation.Participants.Contains(c.NpcId))
                            throw new FormatException($"{at}: {c.NpcId} does not speak {c.DialogueId}");
                        if (!conversation.Nodes.TryGetValue(c.NodeId, out var line))
                            throw new FormatException($"{at}: {c.DialogueId} has no line '{c.NodeId}'");
                        if (line.Choices.All(x => x.Id != c.ChoiceId))
                            throw new FormatException($"{at}: the line '{c.NodeId}' of {c.DialogueId} offers no reply '{c.ChoiceId}'");
                        break;
                    }
                    case TravelTo t when !stones.Value.Contains(t.StoneKey):
                        throw new FormatException($"{at}: no region places the Waystone {t.StoneKey}");
                    case WaystoneKnown w when !stones.Value.Contains(w.StoneKey):
                        throw new FormatException($"{at}: no region places the Waystone {w.StoneKey}");
                }
            }
        }

        CheckQuestOffers(quests, dialogues);
        var asking = replies.Select(r => (At: $"{r.Dialogue.Id} node {r.Node.Id} reply {r.Choice.Id}", r.Choice.Conditions))
            .Concat(dialogues.Values.SelectMany(d => d.Barks.Select((b, i) => (At: $"{d.Id} bark {i}", b.Conditions))))
            .Concat(dialogues.Values.SelectMany(d => d.Nodes.Values.SelectMany(n => n.Variants.Select((v, i) => (At: $"{d.Id} node {n.Id} variant {i + 1}", v.Conditions)))));
        // A teacher's quest requirement (WP9) asks as a quest_state condition does.
        var lessons = npcs.Values.SelectMany(n => n.Teaches.Select((t, i) => (At: $"{n.Id} teaches[{i}]",
            Conditions: t.Requirements.OfType<QuestRequirement>().Select(q => q.Condition))));
        foreach (var (at, conditions) in asking.Select(a => (a.At, a.Conditions.OfType<QuestStateCondition>())).Concat(lessons))
        {
            foreach (var condition in conditions)
            {
                if (!quests.TryGetValue(condition.QuestId, out var quest))
                    throw new FormatException($"{at}: quest_state names {condition.QuestId}, which is not a quest");
                if (condition.ObjectiveId is { } objective && !quest.Objectives.ContainsKey(objective))
                    throw new FormatException($"{at}: {condition.QuestId} has no objective '{objective}'");
            }
        }
        return quests;
    }

    /// <summary>
    /// A reply that starts a quest must not outlive it (owner finding 53: finished quests were still offered). It is hidden once the
    /// quest is completed and, unless hearing its line is one of that quest's talks, while the quest is active too - by a
    /// <c>quest_state</c> condition, or by being said once (a <c>once</c> line, or <c>visited</c> on its own line, <c>not: true</c>).
    /// A reply starting several quests needs the completion guard on one of them; each it does not advance still needs the offer guard.
    /// </summary>
    public static void CheckQuestOffers(IReadOnlyDictionary<string, QuestDefinition> quests, IReadOnlyDictionary<string, DialogueDefinition> dialogues)
    {
        foreach (var dialogue in dialogues.Values)
        foreach (var node in dialogue.Nodes.Values)
        foreach (var choice in node.Choices)
        {
            var started = choice.Consequences.OfType<StartQuestConsequence>().Select(s => s.QuestId).ToList();
            if (started.Count == 0 || node.Once
                || choice.Conditions.Any(c => c is VisitedCondition { Negated: true, DialogueId: null } v && v.NodeId == choice.Next))
                continue;
            var guards = choice.Conditions.OfType<QuestStateCondition>().Where(c => c.ObjectiveId is null).ToList();
            bool Offer(QuestStateCondition c) => c.State is QuestKeys.Available or QuestKeys.NotStarted ? !c.Negated : c.State == "active" && c.Negated;
            bool Ends(QuestStateCondition c) => c.State is QuestKeys.Available or QuestKeys.NotStarted or "active" ? !c.Negated : c.State == "completed" && c.Negated;
            string at = $"{dialogue.Id} node {node.Id} reply {choice.Id}";
            if (!started.Any(q => guards.Any(g => g.QuestId == q && Ends(g))))
                throw new FormatException($"{at}: starts {string.Join(", ", started)} but is still offered once it is completed");
            foreach (string questId in started)
            {
                bool advances = quests[questId].Order.Any(o => o.Condition switch
                {
                    TalkTo t => t.DialogueId == dialogue.Id && t.Nodes.Contains(choice.Next ?? ""),
                    ChoiceGiven c => c.DialogueId == dialogue.Id && c.NodeId == choice.Next,
                    _ => false,
                });
                if (!advances && !guards.Any(g => g.QuestId == questId && Offer(g)))
                    throw new FormatException($"{at}: offers {questId} but is still offered while it is active or done");
            }
        }
    }

    /// <summary>
    /// An <c>acquire_item</c> waits for what is carried when it is reached. If nothing renews an item it counts and the player can use
    /// that item up first - a recipe makes it into something else, or using it spends it - the objective can become impossible for
    /// good (ore smelted before the quest asked for it). So every recipe that consumes a finite counted item must make something the
    /// objective also counts, and a finite counted item must not be one that using spends.
    /// </summary>
    private static void FiniteSupply(string at, AcquireItem objective, ImmutableSortedDictionary<string, RecipeDefinition> recipes,
        HashSet<string> renewable, HashSet<string> usedUp)
    {
        var counted = objective.OrItems.Prepend(objective.ItemId).ToHashSet(StringComparer.Ordinal);
        foreach (string item in counted.Where(i => !renewable.Contains(i)).OrderBy(i => i, StringComparer.Ordinal))
        {
            if (recipes.Values.Where(r => r.Inputs.Any(input => input.ItemId == item)).FirstOrDefault(r => !counted.Contains(r.OutputItemId)) is { } recipe)
                throw new FormatException($"{at}: nothing renews {item}, and {recipe.Id} makes it into {recipe.OutputItemId}, which the objective does not " +
                                          $"count - crafting ahead would make it impossible (count {recipe.OutputItemId} in or_item_refs)");
            if (usedUp.Contains(item))
                throw new FormatException($"{at}: nothing renews {item}, and using it spends it - using it first would make the objective impossible");
        }
    }

    /// <summary>What the world makes again: a node that grows back, the loot of creatures that return, and what is made only of those.</summary>
    private static HashSet<string> Renewable(ContentLoader loader, ImmutableSortedDictionary<string, RecipeDefinition> recipes,
        ImmutableSortedDictionary<string, NodeDefinition> nodes)
    {
        var renewable = nodes.Values.Where(n => n.Respawn != Respawn.None).Select(n => n.ItemId).ToHashSet(StringComparer.Ordinal);
        var tables = ItemContent.BuildLootTables(loader);
        var creatures = CombatContent.BuildCreatures(loader, WorldContent.TickMilliseconds(loader));
        var returning = loader.GetByKind("region").Keys.SelectMany(region => CombatContent.BuildSpawns(loader, region))
            .Where(s => s.RespawnTicks > 0).SelectMany(s => s.Members).Select(m => m.CreatureId).Distinct(StringComparer.Ordinal);
        foreach (string table in returning.Select(c => creatures.GetValueOrDefault(c)?.LootTableId).OfType<string>())
            renewable.UnionWith(Drops(table, tables, 0));
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var recipe in recipes.Values.Where(r => !renewable.Contains(r.OutputItemId) && r.Inputs.All(i => renewable.Contains(i.ItemId))))
                grew |= renewable.Add(recipe.OutputItemId);
        }
        return renewable;
    }

    private static IEnumerable<string> Drops(string tableId, ImmutableSortedDictionary<string, LootTable> tables, int depth) =>
        depth < 8 && tables.TryGetValue(tableId, out var table)
            ? table.Weighted.Concat(table.Independent).Concat(table.Guaranteed)
                .SelectMany(e => e.ItemId is { } item ? new[] { item } : e.TableId is { } nested ? Drops(nested, tables, depth + 1) : Array.Empty<string>())
            : Array.Empty<string>();

    private static double TicksPerGameMinute(ContentLoader loader)
    {
        var time = Config(loader, "config.time");
        return Number(time, "ticks_per_second") * Number(time, "seconds_per_game_minute");
    }

    private static long Ticks(long minutes, Lazy<double> ticksPerMinute) => (long)Math.Round(minutes * ticksPerMinute.Value, MidpointRounding.AwayFromZero);

    private static ImmutableArray<string> Ids(Dictionary<object, object> map, string key, string at) =>
        map.ContainsKey(key)
            ? List(map, key).Select(v => v as string ?? throw new FormatException($"{at}: {key} lists objective IDs")).ToImmutableArray()
            : ImmutableArray<string>.Empty;

    private static int QualityMin(Dictionary<object, object> p, string at) =>
        (int)LongOr(p, "quality_min", Quality.Crude, at) is var q && Quality.IsValid(q)
            ? q
            : throw new FormatException($"{at}: quality_min is {Quality.Crude} (crude), {Quality.Standard} (standard) or {Quality.Fine} (fine)");

    private static string Defined(ContentLoader loader, string id, string kind, string at) =>
        loader.Definitions.TryGetValue(id, out var definition) && (definition.Kind == kind || definition.Kind.StartsWith(kind + ".", StringComparison.Ordinal))
            ? id
            : throw new FormatException($"{at}: {id} is not a defined {kind}");

    private static string Dimension(string dimension, string at) =>
        Relationships.IsDimension(dimension)
            ? dimension
            : throw new FormatException($"{at}: '{dimension}' is not a relationship dimension ({string.Join(", ", Relationships.Dimensions)})");

    private static long LongOf(Dictionary<object, object> map, string key, string at) =>
        long.TryParse(map.GetValueOrDefault(key) as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : throw new FormatException($"{at}: '{key}' must be a whole number");

    private static long LongOr(Dictionary<object, object> map, string key, long fallback, string at) =>
        map.ContainsKey(key) ? LongOf(map, key, at) : fallback;

    private static int Positive(Dictionary<object, object> map, string key, string at, int? fallback = null)
    {
        if (!map.ContainsKey(key) && fallback is { } value)
            return value;
        return LongOf(map, key, at) is var n && n is >= 1 and <= int.MaxValue ? (int)n : throw new FormatException($"{at}: '{key}' must be at least 1");
    }

    private static void Try(Func<object> build, string what, List<ValidationError> errors)
    {
        try
        {
            build();
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(new ValidationError { SeverityLevel = ValidationError.Severity.Error, Code = "QST001", Message = $"{what}: {e.Message}" });
        }
    }
}
