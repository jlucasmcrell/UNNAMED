// UNNAMED Content - quests from content (DATA_MODEL.md §4.11; SYSTEMS.md S-29; D-07; M5)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Crafting;
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
/// condition names a quest and objective that exist.
/// </summary>
public static class QuestContent
{
    private static readonly string[] Envelope = { "id", "kind", "schema", "display_key", "tags", "notes" };

    private static readonly string[] Fields = { "title", "summary", "giver_ref", "entry_objective", "objectives", "fail_if", "rewards" };

    /// <summary>DATA_MODEL.md §4.11's quest fields that Phase 1 does not build.</summary>
    private static readonly string[] FieldsNotBuilt =
        { "title_key", "summary_key", "journal_entries", "level_band", "faction_ref", "parent_quest_ref", "chain_index", "abandon_policy", "repeat_policy" };

    private static readonly string[] ObjectiveFields = { "id", "type", "description", "params", "next", "all_of", "branch", "visibility", "time_limit_min", "on_fail" };

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
            Rows(map, "rewards", id).Select((row, i) => Reward($"{id} rewards[{i}]", row, loader)).ToImmutableArray());
        CheckGraph(quest);
        return quest;
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
            "acquire_item" => new AcquireItem(Defined(loader, Text(p, "item_ref"), "item", where), Positive(p, "count", where, 1), QualityMin(p, where)),
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
            _ => throw new FormatException($"{at}: '{type}' is not built"),
        };
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
        "acquire_item" or "craft_item" => new[] { "item_ref", "count", "quality_min" },
        "harvest_resource" => new[] { "resource_ref", "count" },
        "kill_creature" => new[] { "creature_ref", "count" },
        "deliver_item" => new[] { "npc_ref", "item_ref", "count" },
        "world_state" => new[] { "flag_ref", "location_ref", "value", "min", "max" },
        "relationship_value" => new[] { "npc_ref", "dimension", "min", "max" },
        "wait_until" => new[] { "after_min" },
        _ => Array.Empty<string>(),
    };

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

        foreach (var quest in quests.Values)
        {
            if (!replies.Any(r => r.Choice.Consequences.OfType<StartQuestConsequence>().Any(s => s.QuestId == quest.Id)))
                throw new FormatException($"{quest.Id}: nothing starts it (no conversation's start_quest names it)");
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
                    case CraftItem c when !recipes.Values.Any(r => r.OutputItemId == c.ItemId):
                        throw new FormatException($"{at}: no recipe makes {c.ItemId}");
                    case HarvestResource h when !nodes.Values.Any(n => n.ResourceId == h.ResourceId):
                        throw new FormatException($"{at}: no node yields {h.ResourceId}");
                    case DeliverItem d when !replies.Any(r => r.Dialogue.Participants.Contains(d.NpcId)
                            && r.Choice.Consequences.OfType<TransferItemConsequence>().Any(x => !x.ToPlayer && x.ItemId == d.ItemId)):
                        throw new FormatException($"{at}: no reply of {d.NpcId}'s takes {d.ItemId}, so it can never be delivered");
                }
            }
        }

        foreach (var (dialogue, node, choice) in replies)
        {
            foreach (var condition in choice.Conditions.OfType<QuestStateCondition>())
            {
                string at = $"{dialogue.Id} node {node.Id} reply {choice.Id}";
                if (!quests.TryGetValue(condition.QuestId, out var quest))
                    throw new FormatException($"{at}: quest_state names {condition.QuestId}, which is not a quest");
                if (condition.ObjectiveId is { } objective && !quest.Objectives.ContainsKey(objective))
                    throw new FormatException($"{at}: {condition.QuestId} has no objective '{objective}'");
            }
        }
        return quests;
    }

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
