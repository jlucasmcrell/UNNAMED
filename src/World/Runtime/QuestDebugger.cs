// UNNAMED World - the quest debugger: "what is this quest waiting on right now?" (D-07; ROADMAP.md M5; SYSTEMS.md S-29)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>
/// The quest debugger's answer (ROADMAP.md M5): for a quest, the one-line <see cref="Answer"/> to "what is this quest waiting on
/// right now?"; for every objective it waits on, the current value of every predicate term and what in this world would satisfy
/// it; the <see cref="Problems"/> that mean it cannot complete as things stand; and the trace of its last evaluations.
/// </summary>
public sealed record QuestDiagnosis(string QuestId, string Title, string Status, string Answer, ImmutableArray<ObjectiveDiagnosis> Waiting,
    ImmutableArray<string> Problems, ImmutableArray<TraceEntry> Trace)
{
    /// <summary>The whole diagnosis as text, with the last <paramref name="traceEntries"/> trace entries: the debugger's transcript.</summary>
    public string ToText(int traceEntries = 6)
    {
        var text = new StringBuilder();
        text.Append(Title).Append(" (").Append(QuestId).Append(") - ").AppendLine(Status);
        text.Append("What is this quest waiting on right now? ").AppendLine(Answer);
        foreach (var objective in Waiting)
        {
            text.Append("  ").Append(objective.Id).Append(" [").Append(objective.Type).Append("] ").Append(objective.Description)
                .Append(" - active since tick ").Append(objective.ActiveSinceTick);
            if (objective.TimeLeft is { } left)
                text.Append(", ").Append(left);
            text.AppendLine();
            foreach (var term in objective.Terms)
                text.Append("    ").Append(term.Holds ? "[x] " : "[ ] ").Append(term.Label).Append(" = ").Append(term.Value)
                    .Append(" (wanted ").Append(term.Wanted).AppendLine(")");
            foreach (string way in objective.SatisfiedBy)
                text.Append("    - ").AppendLine(way);
        }
        foreach (string problem in Problems)
            text.Append("PROBLEM: ").AppendLine(problem);
        if (!Trace.IsEmpty && traceEntries > 0)
        {
            text.AppendLine("Trace (latest last):");
            foreach (var entry in Trace.Skip(Math.Max(0, Trace.Length - traceEntries)))
            {
                text.Append("  tick ").Append(entry.FromTick);
                if (entry.ToTick != entry.FromTick)
                    text.Append('-').Append(entry.ToTick).Append(" (").Append(entry.Evaluations).Append(" evaluations)");
                text.AppendLine();
                foreach (string line in entry.Lines)
                    text.Append("    ").AppendLine(line);
            }
        }
        // The same text on every platform: a transcript is compared and shown, and a label reads \r as a line of its own.
        return text.ToString().Replace("\r\n", "\n");
    }
}

/// <summary>One objective a quest waits on: its terms now, how long it has, and every way this world offers to satisfy it.</summary>
public sealed record ObjectiveDiagnosis(string Id, string Description, string Type, long ActiveSinceTick, string? TimeLeft,
    ImmutableArray<Term> Terms, ImmutableArray<string> SatisfiedBy);

/// <summary>Reads the quest system, the content and the world to explain a quest. It changes nothing.</summary>
internal sealed class QuestDebugger
{
    private readonly SystemContext _context;
    private readonly QuestSystem _quests;
    private readonly DialogueSystem _dialogue;
    private readonly Func<ImmutableArray<NodeView>> _nodes;
    private readonly Func<string, WaresView?> _wares;
    private readonly Func<ImmutableArray<ContainerView>> _containers;
    private readonly Func<ImmutableArray<CreatureView>> _creatures;

    public QuestDebugger(SystemContext context, QuestSystem quests, DialogueSystem dialogue, Func<ImmutableArray<NodeView>> nodes,
        Func<string, WaresView?> wares, Func<ImmutableArray<ContainerView>> containers, Func<ImmutableArray<CreatureView>> creatures)
    {
        _context = context;
        _quests = quests;
        _dialogue = dialogue;
        _nodes = nodes;
        _wares = wares;
        _containers = containers;
        _creatures = creatures;
    }

    private RuntimeState State => _context.State;
    private SimulationSetup Setup => _context.Setup;
    private long Now => State.WorldTick;

    public QuestDiagnosis Diagnose(string questId)
    {
        if (!Setup.Quests.Quests.TryGetValue(questId, out var quest))
        {
            return new QuestDiagnosis(questId, questId, "unknown", $"Nothing: {questId} is not a quest this content defines.",
                ImmutableArray<ObjectiveDiagnosis>.Empty, ImmutableArray.Create($"{questId} is not in content"), _quests.TraceOf(questId));
        }
        var trace = _quests.TraceOf(questId);
        if (!State.Quests.TryGetValue(questId, out var state))
        {
            var starters = Starters(questId).ToImmutableArray();
            string how = starters.IsEmpty ? "nothing in content starts it" : string.Join("; ", starters);
            return new QuestDiagnosis(quest.Id, quest.Title, QuestKeys.NotStarted, $"Its start: {how}.",
                ImmutableArray<ObjectiveDiagnosis>.Empty, starters.IsEmpty ? ImmutableArray.Create("nothing starts this quest") : ImmutableArray<string>.Empty, trace);
        }

        var problems = ImmutableArray.CreateBuilder<string>();
        foreach (var unknown in state.Objectives.Where(o => !quest.Objectives.ContainsKey(o.Id)))
            problems.Add($"the saved objective '{unknown.Id}' is no longer part of the quest");

        if (state.Status != QuestStatus.Active)
        {
            string by = state.EndedBy ?? "?";
            string answer = state.Status == QuestStatus.Completed
                ? $"Nothing: completed at tick {state.EndedTick} by {by}."
                : $"Nothing: failed at tick {state.EndedTick} - {FailureReason(quest, state, by)}.";
            return new QuestDiagnosis(quest.Id, quest.Title, QuestKeys.Key(state.Status), answer, ImmutableArray<ObjectiveDiagnosis>.Empty,
                problems.ToImmutable(), trace);
        }

        var waiting = ImmutableArray.CreateBuilder<ObjectiveDiagnosis>();
        foreach (var definition in quest.Order)
        {
            if (state.Objective(definition.Id) is not { Status: ObjectiveStatus.Active } active)
                continue;
            var evaluation = QuestRules.Evaluate(definition.Condition, _quests, active.Progress, active.ActivatedTick, Now);
            var ways = Ways(definition.Condition, problems).ToImmutableArray();
            string? left = definition.TimeLimitTicks is { } limit
                ? $"{Seconds(Math.Max(0, limit - (Now - active.ActivatedTick)))} left before it fails to {definition.OnFail}"
                : null;
            waiting.Add(new ObjectiveDiagnosis(definition.Id, definition.Description, definition.Condition.Type, active.ActivatedTick, left,
                evaluation.Terms, ways));
        }

        // A join whose prerequisite was closed or failed can never become active.
        foreach (var join in quest.Order.Where(o => !o.AllOf.IsEmpty && state.Objective(o.Id) is null))
        {
            foreach (string prerequisite in join.AllOf)
            {
                if (state.Objective(prerequisite) is { Status: ObjectiveStatus.Closed or ObjectiveStatus.Failed } dead)
                    problems.Add($"{join.Id} can never become active: it waits on {prerequisite}, which was {QuestKeys.Key(dead.Status)} at tick {dead.EndedTick}");
            }
        }

        string reply;
        if (waiting.Count == 0)
        {
            problems.Add("no objective is active and the quest is not finished: it is stalled");
            reply = "Nothing can move it: no objective is active.";
        }
        else
        {
            reply = "Waiting on " + string.Join("; ", waiting.Select(w =>
                $"{w.Id} ({w.Description}): " + string.Join(", ", w.Terms.Where(t => !t.Holds).DefaultIfEmpty(w.Terms.FirstOrDefault())
                    .Where(t => t is not null).Select(t => $"{t!.Label} = {t.Value}, wanted {t.Wanted}")))) + ".";
        }
        return new QuestDiagnosis(quest.Id, quest.Title, QuestKeys.Key(state.Status), reply, waiting.ToImmutable(), problems.ToImmutable(), trace);
    }

    private string FailureReason(QuestDefinition quest, QuestState state, string by)
    {
        if (by.StartsWith("fail_if[", StringComparison.Ordinal) && int.TryParse(by[8..^1], out int i) && i < quest.FailIf.Length)
        {
            var terms = QuestRules.Evaluate(quest.FailIf[i], _quests, 0, state.StartedTick, Now).Terms;
            return $"{by} ({quest.FailIf[i].Type}) held: now " + string.Join(", ", terms.Select(t => $"{t.Label} = {t.Value}, fails at {t.Wanted}"));
        }
        if (quest.Objectives.TryGetValue(by, out var objective) && objective.TimeLimitTicks is { } limit)
            return $"{by} ({objective.Description}) was not done within its time limit of {Seconds(limit)}";
        return $"ended by {by}";
    }

    /// <summary>Every way this world offers, now, to make the condition hold; anything that makes it impossible goes to problems.</summary>
    private IEnumerable<string> Ways(ObjectiveCondition condition, ImmutableArray<string>.Builder problems)
    {
        switch (condition)
        {
            case TalkTo t:
                foreach (string way in TalkWays(t))
                    yield return way;
                break;
            case VisitLocation v:
                yield return $"walk within {Metres(Place(v.LocationId)?.DiscoveryRadiusMm ?? 0)} of {v.LocationId} {At(v.LocationId)}; now {Metres(_quests.DistanceMm(v.LocationId))} away";
                break;
            case ExploreLocation e:
                yield return $"stand within {Metres(e.WithinMm)} of {e.LocationId} {At(e.LocationId)}; now {Metres(_quests.DistanceMm(e.LocationId))} away";
                break;
            case AcquireItem a:
            {
                var sources = Sources(a.ItemId, out bool anyNow).ToList();
                foreach (string source in sources)
                    yield return source;
                if (!anyNow && _quests.Carried(a.ItemId, a.QualityMin) < a.Count)
                    problems.Add($"nothing in this world can supply {a.ItemId} now" + (sources.Count == 0 ? " (content has no source for it)" : ""));
                break;
            }
            case CraftItem c:
            {
                var recipes = Setup.Crafting.Recipes.Values.Where(r => r.OutputItemId == c.ItemId).ToList();
                foreach (var recipe in recipes)
                    yield return Recipe(recipe);
                yield return "an ItemCrafted of it" + (c.QualityMin > Quality.Crude ? $" at quality >= {c.QualityMin}" : "") + " while the objective is active";
                if (recipes.All(r => !Domain.Progression.ProgressionEngine.Knows(State.Progression, r.Id)))
                    problems.Add($"no recipe the character knows makes {c.ItemId}");
                break;
            }
            case HarvestResource h:
            {
                var nodes = _nodes().Where(n => Setup.Crafting.Nodes.TryGetValue(n.NodeDefId, out var d) && d.ResourceId == h.ResourceId).ToList();
                foreach (var node in nodes)
                    yield return $"gather at {Node(node)}";
                if (!nodes.Any(n => n.Ready || Setup.Crafting.Nodes[n.NodeDefId].Respawn != Respawn.None))
                    problems.Add($"every node of {h.ResourceId} is worked out for good");
                break;
            }
            case KillCreature k:
            {
                int alive = _creatures().Count(c => c.DefId == k.CreatureId && c.Alive);
                foreach (var spawn in Setup.Combat.Spawns.Where(s => s.Members.Any(m => m.CreatureId == k.CreatureId)))
                    yield return $"{k.CreatureId} spawns at {spawn.Key} ({Point(spawn.XMm, spawn.ZMm)})";
                yield return $"{alive} of them alive now; a CreatureKilled by the character while the objective is active counts";
                break;
            }
            case DeliverItem d:
                foreach (var (dialogue, node, choice) in Replies().Where(r => r.Dialogue.Participants.Contains(d.NpcId)
                             && r.Choice.Consequences.OfType<TransferItemConsequence>().Any(x => !x.ToPlayer && x.ItemId == d.ItemId)))
                    yield return $"hand it over: {Reply(d.NpcId, dialogue, node, choice)}";
                yield return $"carried now: {_quests.Carried(d.ItemId, Quality.Crude)}";
                break;
            case WorldStateObjective w:
                foreach (string way in FlagWays(w))
                    yield return way;
                break;
            case RelationshipValueObjective r:
                foreach (var (dialogue, node, choice) in Replies().Where(x => x.Choice.Consequences.OfType<RelationshipEventConsequence>()
                             .Any(e => e.NpcId == r.NpcId && e.Dimension == r.Dimension)))
                {
                    var e = choice.Consequences.OfType<RelationshipEventConsequence>().First(e => e.NpcId == r.NpcId && e.Dimension == r.Dimension);
                    yield return $"{e.Delta:+#;-#} {r.Dimension} ({e.EventKey}): {Reply(r.NpcId, dialogue, node, choice)}";
                }
                break;
            case WaitUntil w:
                yield return "time passing: nothing else";
                break;
        }
    }

    private IEnumerable<string> TalkWays(TalkTo t)
    {
        if (!Setup.Social.Dialogues.TryGetValue(t.DialogueId, out var dialogue))
            yield break;
        string name = Setup.Social.Npcs.TryGetValue(t.NpcId, out var npc) ? npc.Name : t.NpcId;
        if (State.Npcs.TryGetValue(t.NpcId, out var body))
        {
            double distance = Math.Sqrt(Math.Pow(body.Body.XMm - State.Body.XMm, 2) + Math.Pow(body.Body.ZMm - State.Body.ZMm, 2));
            yield return $"talk to {name} ({Point(body.Body.XMm, body.Body.ZMm)}), within {Metres(_context.TalkReachMm)}; now {Metres((long)distance)} away";
        }
        foreach (string line in t.Nodes.Where(n => !_quests.Heard(t.DialogueId, n)))
        {
            if (line == dialogue.Root)
                yield return $"'{line}' is the conversation's opening line";
            foreach (var from in dialogue.Nodes.Values)
            {
                if (from.Next == line)
                    yield return $"'{line}' follows the line '{from.Id}'";
                if (from.NextIfExhausted == line)
                    yield return $"'{line}' follows the line '{from.Id}' once that is spent";
                foreach (var choice in from.Choices.Where(c => c.Next == line))
                    yield return $"'{line}' is reached by {Reply(t.NpcId, dialogue, from, choice)}";
            }
        }
    }

    private IEnumerable<string> FlagWays(WorldStateObjective w)
    {
        var cell = _quests.CellOfLocation(w.LocationId);
        yield return $"{w.FlagId} is read in cell {cell} (the cell of {w.LocationId})";
        foreach (var other in Setup.Layout.CellKeys.Select(CellKey.Parse).Where(c => c != cell))
        {
            if (State.World.GetFlag(other, w.FlagId) is var value && value != 0)
                yield return $"note: {w.FlagId} = {value} in cell {other}, which this objective does not read";
        }
        foreach (var door in Setup.Layout.Doors.Where(d => d.FlagId == w.FlagId))
            yield return $"the door {door.Key} sets it when opened or closed";
        foreach (var site in Setup.Layout.Switches.Where(s => s.FlagId == w.FlagId))
        {
            var (x, z) = Footprints.Center(site.Body);
            var missing = site.Requires.Where(f => State.World.GetFlag(Simulation.CellOf(site), f) == 0).ToList();
            yield return $"{site.Verb.ToLowerInvariant()} the {site.Name} at ({Point(x, z)}): {site.Key}, in cell {Simulation.CellOf(site)}" +
                (missing.Count == 0 ? "" : $"; it waits on {string.Join(", ", missing)}");
        }
        foreach (var (dialogue, node, choice) in Replies().Where(r => r.Choice.Consequences.OfType<SetWorldFlagConsequence>().Any(s => s.FlagId == w.FlagId)))
        {
            string speaker = dialogue.Participants.First();
            yield return $"set in {speaker}'s cell by {Reply(speaker, dialogue, node, choice)}";
        }
        foreach (var quest in Setup.Quests.Quests.Values.Where(q => q.Rewards.OfType<WorldFlagReward>().Any(r => r.FlagId == w.FlagId)))
            yield return $"set when {quest.Id} completes";
    }

    /// <summary>Where an item can come from now: nodes, recipes, traders, containers, conversations.</summary>
    private IEnumerable<string> Sources(string itemId, out bool anyNow)
    {
        var sources = new List<string>();
        bool now = false;
        foreach (var node in _nodes().Where(n => n.ItemId == itemId))
        {
            now |= node.Ready || Setup.Crafting.Nodes[node.NodeDefId].Respawn != Respawn.None;
            sources.Add($"gather at {Node(node)}");
        }
        foreach (var recipe in Setup.Crafting.Recipes.Values.Where(r => r.OutputItemId == itemId))
        {
            now |= Domain.Progression.ProgressionEngine.Knows(State.Progression, recipe.Id);
            sources.Add("make it: " + Recipe(recipe));
        }
        foreach (var npc in Setup.Social.Npcs.Values.Where(n => n.MerchantId is not null))
        {
            if (_wares(npc.Id) is not { } wares)
                continue;
            var stock = wares.Wares.Where(w => w.ItemId == itemId).ToList();
            if (stock.Count == 0)
                continue;
            now = true;
            sources.Add($"buy it from {npc.Name}: {stock.Sum(s => s.Count)} in the wares at {stock.Min(s => s.Price)} each (coin {State.Currency})");
        }
        foreach (var container in _containers().Where(c => c.Items.Any(i => i.DefId == itemId)))
        {
            now = true;
            sources.Add($"{container.Items.Where(i => i.DefId == itemId).Sum(i => i.Count)} in {container.Site.Key} ({Point(container.Site.XMm, container.Site.ZMm)})");
        }
        foreach (var creature in Setup.Combat.Creatures.Values.Where(c => c.LootTableId is { } table && TableHolds(table, itemId, 0)))
        {
            int alive = _creatures().Count(c => c.DefId == creature.Id && c.Alive);
            bool respawns = Setup.Combat.Spawns.Any(s => s.RespawnTicks > 0 && s.Members.Any(m => m.CreatureId == creature.Id));
            now |= alive > 0 || respawns;
            sources.Add($"dropped by {creature.Id} ({creature.LootTableId}): {alive} alive now" + (respawns ? ", and they return" : ""));
        }
        foreach (var (dialogue, node, choice) in Replies().Where(r => r.Choice.Consequences.OfType<TransferItemConsequence>().Any(x => x.ToPlayer && x.ItemId == itemId)))
        {
            string speaker = dialogue.Participants.First();
            bool spent = node.Once && _quests.Heard(dialogue.Id, node.Id) && State.Conversation?.NodeId != node.Id;
            now |= !spent;
            sources.Add($"given by {Reply(speaker, dialogue, node, choice)}" + (spent ? " - but that line is spent" : ""));
        }
        anyNow = now;
        return sources;
    }

    /// <summary>Whether a loot table, or one it nests, can drop the item.</summary>
    private bool TableHolds(string tableId, string itemId, int depth) =>
        depth < 8 && Setup.Items.LootTables.TryGetValue(tableId, out var table)
        && table.Weighted.Concat(table.Independent).Concat(table.Guaranteed)
            .Any(e => e.ItemId == itemId || e.TableId is { } nested && TableHolds(nested, itemId, depth + 1));

    private IEnumerable<string> Starters(string questId) =>
        Replies().Where(r => r.Choice.Consequences.OfType<StartQuestConsequence>().Any(s => s.QuestId == questId))
            .Select(r => Reply(r.Dialogue.Participants.First(), r.Dialogue, r.Node, r.Choice));

    private IEnumerable<(DialogueDefinition Dialogue, DialogueNode Node, DialogueChoice Choice)> Replies() =>
        Setup.Social.Dialogues.Values.SelectMany(d => d.Nodes.Values.SelectMany(n => n.Choices.Select(c => (d, n, c))));

    /// <summary>A reply, and whether it would be offered now - with each condition that stops it.</summary>
    private string Reply(string npcId, DialogueDefinition dialogue, DialogueNode node, DialogueChoice choice)
    {
        string name = Setup.Social.Npcs.TryGetValue(npcId, out var npc) ? npc.Name : npcId;
        var checks = _dialogue.Check(npcId, dialogue.Id, choice);
        var failing = checks.Where(c => !c.Holds).Select(c => DescribeCondition(c.Condition, dialogue.Id)).ToList();
        return $"{name}'s reply '{choice.Id}' at the line '{node.Id}' ({dialogue.Id}): " +
            (failing.Count == 0 ? "offered now" : "not offered now - " + string.Join("; ", failing));
    }

    private string DescribeCondition(DialogueCondition condition, string dialogueId) => condition switch
    {
        VisitedCondition v => (v.Negated ? $"needs '{v.NodeId}' unheard" : $"needs '{v.NodeId}' heard") +
            (v.DialogueId is { } other ? $" in {other}" : "") + (v.Negated ? ", and it was heard" : " first"),
        WorldStateCondition w => $"needs {w.FlagId} in [{w.Min}, {w.Max}] at the speaker",
        HasItemCondition h => h.Negated
            ? $"needs fewer than {h.Count} {h.ItemId}{(h.QualityMin > Quality.Crude ? $" of quality >= {h.QualityMin}" : "")} carried"
            : $"needs {h.Count} {h.ItemId}{(h.QualityMin > Quality.Crude ? $" of quality >= {h.QualityMin}" : "")} carried; {_quests.Carried(h.ItemId, h.QualityMin)} are",
        RelationshipCondition r => $"needs {r.NpcId} {r.Dimension} in [{r.Min}, {r.Max}]; it is {State.RelationshipOf(r.NpcId, r.Dimension)}",
        SkillCondition s => $"needs {s.SkillId} {s.Min}",
        LevelCondition l => $"needs level {l.Min}; the character is {State.Progression.Level}",
        QuestStateCondition q => $"needs {q.QuestId}{(q.ObjectiveId is { } o ? " " + o : "")} {(q.Negated ? "not " : "")}{q.State}",
        _ => condition.ToString(),
    };

    private string Recipe(RecipeDefinition recipe)
    {
        bool known = Domain.Progression.ProgressionEngine.Knows(State.Progression, recipe.Id);
        var station = Setup.Layout.Stations.Where(s => s.Kind == recipe.StationKind)
            .OrderBy(s => Math.Pow(s.XMm - State.Body.XMm, 2) + Math.Pow(s.ZMm - State.Body.ZMm, 2)).FirstOrDefault();
        string inputs = string.Join(", ", recipe.Inputs.Select(i => $"{i.Count} {i.ItemId} (carried {_quests.Carried(i.ItemId, Quality.Crude)})"));
        return $"{recipe.Id} at a {recipe.StationKind}" + (station is null ? " (none in this region)" : $" ({Point(station.XMm, station.ZMm)})") +
            $", {(known ? "known" : "NOT known")}, from {inputs}";
    }

    private string Node(NodeView node)
    {
        var definition = Setup.Crafting.Nodes[node.NodeDefId];
        string state = node.Ready ? "ready" : definition.Respawn == Respawn.None ? "worked out for good" : "regrowing";
        return $"{node.Name} ({Point(node.XMm, node.ZMm)}) [{node.NodeDefId}]: {state}";
    }

    private LocationSiteRef? Place(string locationId) =>
        Setup.Layout.Locations.FirstOrDefault(l => l.Id == locationId) is { } site ? new LocationSiteRef(site.XMm, site.ZMm, site.DiscoveryRadiusMm) : null;

    private string At(string locationId) => Place(locationId) is { } p ? $"({Point(p.XMm, p.ZMm)})" : "(not in this region)";

    private sealed record LocationSiteRef(long XMm, long ZMm, long DiscoveryRadiusMm);

    private string Seconds(long ticks) =>
        (ticks * _context.Setup.TickMilliseconds / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " s";

    private static string Metres(long mm) =>
        mm == long.MaxValue ? "unknown" : (mm / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " m";

    private static string Point(long xMm, long zMm) =>
        string.Create(CultureInfo.InvariantCulture, $"{xMm / 1000.0:0.0}, {zMm / 1000.0:0.0}");
}
