// UNNAMED Application - the authoritative state as a field-by-field dump, and the comparison of two (PROTOTYPE.md §9 item 2; M6)
// No Godot references - pure C#

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application;

/// <summary>
/// Everything a save must bring back, as JSON with every field named (content bible §33: "field-by-field compare authoritative
/// state"): the world tick, the whole player record - body, pools, progression, pack, equipment, discoveries, effects, relationships,
/// conversations, quests, companions - and the whole world delta: cells, instances, containers and creatures. Nothing is left out by
/// hand; the records are written as they are. Then the same world as the running game holds it (<c>live</c>): after a load, what
/// the game rebuilt from those records. <see cref="Compare"/> walks two dumps leaf by leaf.
/// </summary>
public static partial class StateDump
{
    /// <summary>
    /// The dump. With <paramref name="replayable"/>, two runs of the same script compare equal when they played the same game: instance
    /// IDs are fresh on every new game (D-04: never derived from a seed), so lists are put in the order of what they hold rather than of
    /// their IDs, each ID is written as the order it first appears in, and the fields derived from IDs are left out.
    /// </summary>
    public static string Render(Simulation simulation, bool replayable = false)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        var root = new JsonObject
        {
            ["world_tick"] = simulation.WorldTick,
            ["player"] = JsonSerializer.SerializeToNode(simulation.CaptureRecord(), options),
            ["world"] = JsonSerializer.SerializeToNode(simulation.World.TakeSnapshot(), options),
            ["live"] = Live(simulation, options),
        };
        if (replayable)
        {
            if (root["player"] is JsonObject player)
            {
                player.Remove("AppearanceSeed");   // derived from the character's fresh ID
                player.Remove("Digest");
            }
            Order(root);
            Name(root, new Dictionary<string, string>(StringComparer.Ordinal));
        }
        return root.ToJsonString(options).Replace("\r\n", "\n");
    }

    /// <summary>
    /// The world read from the running systems rather than from the records a save is made of (the Phase-1 technical audit, T-01): every
    /// creature, NPC and companion as the game placed them, every container and item on the ground, node, switch, door, barrier and
    /// quest, and the character's combat pools and effects. What a save does not keep by design is left out, and named in
    /// <c>left_out</c>: a blow, dodge or working in progress, what it is aimed at and the guard (a companion's fight among them), and an
    /// open conversation with the NPC turned towards it.
    /// </summary>
    private static JsonObject Live(Simulation simulation, JsonSerializerOptions options)
    {
        var leftOut = new JsonArray();
        JsonNode View<T>(string name, IEnumerable<T> records, params string[] transient)
        {
            var list = new JsonArray(records.Select(r => (JsonNode?)Without(JsonSerializer.SerializeToNode(r, options)!.AsObject(), transient)).ToArray());
            foreach (string path in transient)
                leftOut.Add($"{name}[].{path}");
            return list;
        }

        var combat = Without(JsonSerializer.SerializeToNode(simulation.Combat, options)!.AsObject(), "Phase", "PhaseTicksLeft", "AttackSource",
            "Blocking", "Casting");
        foreach (string path in new[] { "Phase", "PhaseTicksLeft", "AttackSource", "Blocking", "Casting" })
            leftOut.Add($"combat.{path}");
        return new JsonObject
        {
            ["creatures"] = View("creatures", simulation.Creatures.OrderBy(c => c.Key, StringComparer.Ordinal), "Phase", "PhaseTicksLeft"),
            ["npcs"] = View("npcs", simulation.Npcs.OrderBy(n => n.Id, StringComparer.Ordinal), "Talking", "Body.FacingMdeg"),
            ["companions"] = View("companions", simulation.Companions.OrderBy(c => c.NpcId, StringComparer.Ordinal), "Doing"),
            ["containers"] = View("containers", simulation.Containers.OrderBy(c => c.Site.Key, StringComparer.Ordinal)),
            ["world_items"] = View("world_items", simulation.WorldItems.OrderBy(i => i.Id.ToString(), StringComparer.Ordinal)),
            ["nodes"] = View("nodes", simulation.Nodes.OrderBy(n => n.Key, StringComparer.Ordinal)),
            ["switches"] = View("switches", simulation.Switches),
            ["doors"] = View("doors", simulation.Doors),
            ["barriers"] = View("barriers", simulation.Barriers),
            ["quests"] = View("quests", simulation.Quests.OrderBy(q => q.Id, StringComparer.Ordinal)),
            // M7's paths, empty in play until something is built, an errand run or an act known. Pieces are their view (E5); until their
            // views land (work assignments E8-E9) the others read the world's records and the faction slice.
            ["pieces"] = View("pieces", simulation.Pieces),
            ["work_assignments"] = View("work_assignments", simulation.World.TakeSnapshot().NpcErrands),
            ["factions"] = View("factions", simulation.CaptureRecord().Factions.Standing),
            ["combat"] = combat,
            ["left_out"] = leftOut,
        };
    }

    /// <summary>A record without the named fields; a dotted name reaches into a field's own record.</summary>
    private static JsonObject Without(JsonObject record, params string[] paths)
    {
        foreach (string path in paths)
        {
            string[] steps = path.Split('.');
            var holder = record;
            foreach (string step in steps[..^1])
                holder = holder[step] as JsonObject ?? throw new InvalidOperationException($"{path}: {step} is not a record");
            if (!holder.Remove(steps[^1]))
                throw new InvalidOperationException($"{path}: there is no such field to leave out");
        }
        return record;
    }

    /// <summary>Every leaf that differs between two dumps, as "path: expected ..., actual ..."; <paramref name="leaves"/> counts what was compared.</summary>
    public static IReadOnlyList<string> Compare(string expected, string actual, out int leaves)
    {
        var differences = new List<string>();
        int count = 0;
        Walk("$", JsonNode.Parse(expected), JsonNode.Parse(actual));
        leaves = count;
        return differences;

        void Walk(string path, JsonNode? a, JsonNode? b)
        {
            switch (a, b)
            {
                case (JsonObject x, JsonObject y):
                    foreach (string key in x.Select(p => p.Key).Union(y.Select(p => p.Key)).OrderBy(k => k, StringComparer.Ordinal))
                    {
                        if (!x.ContainsKey(key) || !y.ContainsKey(key))
                        {
                            count++;
                            differences.Add($"{path}.{key}: expected {(x.ContainsKey(key) ? x[key]?.ToJsonString() ?? "null" : "(absent)")}, " +
                                            $"actual {(y.ContainsKey(key) ? y[key]?.ToJsonString() ?? "null" : "(absent)")}");
                            continue;
                        }
                        Walk($"{path}.{key}", x[key], y[key]);
                    }
                    break;
                case (JsonArray x, JsonArray y):
                    if (x.Count != y.Count)
                    {
                        count++;
                        differences.Add($"{path}: expected {x.Count} entries, actual {y.Count}");
                    }
                    for (int i = 0; i < Math.Min(x.Count, y.Count); i++)
                        Walk($"{path}[{i}]", x[i], y[i]);
                    break;
                default:
                    count++;
                    string left = a?.ToJsonString() ?? "null", right = b?.ToJsonString() ?? "null";
                    if (left != right)
                        differences.Add($"{path}: expected {left}, actual {right}");
                    break;
            }
        }
    }

    /// <summary>An instance ID as written: a three-letter kind, an underscore, a 26-character ULID (DATA_MODEL.md §2).</summary>
    [GeneratedRegex("^[a-z]{3}_[0-9A-Z]{26}$")]
    private static partial Regex InstanceId();

    /// <summary>A placed chest's container key (M7): <c>container.pce_</c> and its piece's ULID in lower case.</summary>
    [GeneratedRegex("^container\\.pce_([0-9a-z]{26})$")]
    private static partial Regex PieceChestKey();

    /// <summary>Every list of records in the order of what the records hold, their IDs masked.</summary>
    private static void Order(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (_, child) in o.ToList())
                    Order(child);
                break;
            case JsonArray a:
                foreach (var child in a)
                    Order(child);
                if (a.Count > 1 && a.All(e => e is JsonObject))
                {
                    var sorted = a.Select(e => (Key: Masked(e!), Node: e!)).OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Node).ToList();
                    a.Clear();
                    foreach (var element in sorted)
                    {
                        element.Parent?.AsArray().Remove(element);
                        a.Add(element);
                    }
                }
                break;
        }
    }

    private static string Masked(JsonNode node) => MaskIds(node.DeepClone()).ToJsonString();

    private static JsonNode MaskIds(JsonNode node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, child) in o.ToList())
                {
                    if (child is JsonValue v && v.TryGetValue(out string? text) && InstanceId().IsMatch(text))
                        o[key] = "#";
                    else if (child is JsonValue c && c.TryGetValue(out string? chest) && PieceChestKey().IsMatch(chest))
                        o[key] = "container.#";
                    else if (child is not null)
                        MaskIds(child);
                }
                break;
            case JsonArray a:
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i] is JsonValue v && v.TryGetValue(out string? text) && InstanceId().IsMatch(text))
                        a[i] = "#";
                    else if (a[i] is JsonValue c && c.TryGetValue(out string? chest) && PieceChestKey().IsMatch(chest))
                        a[i] = "container.#";
                    else if (a[i] is { } child)
                        MaskIds(child);
                }
                break;
        }
        return node;
    }

    /// <summary>Each ID as its kind and the order it first appears in: <c>itm#3</c>.</summary>
    private static void Name(JsonNode? node, Dictionary<string, string> names)
    {
        string Named(string id)
        {
            if (!names.TryGetValue(id, out string? name))
                names[id] = name = $"{id[..3]}#{names.Keys.Count(k => k.StartsWith(id[..4], StringComparison.Ordinal)) + 1}";
            return name;
        }

        switch (node)
        {
            case JsonObject o:
                foreach (var (key, child) in o.ToList())
                {
                    string name = InstanceId().IsMatch(key) ? Named(key) : key;
                    if (child is JsonValue v && v.TryGetValue(out string? text) && InstanceId().IsMatch(text))
                    {
                        if (name != key)
                            o.Remove(key);
                        o[name] = Named(text);
                        continue;
                    }
                    // A placed chest's key names the chest by its piece, so key and piece share one name (container.pce#3).
                    if (child is JsonValue c && c.TryGetValue(out string? chest) && PieceChestKey().Match(chest) is { Success: true } m)
                    {
                        o[name] = "container." + Named("pce_" + m.Groups[1].Value.ToUpperInvariant());
                        continue;
                    }
                    Name(child, names);
                    if (name != key)
                    {
                        o.Remove(key);
                        o[name] = child;
                    }
                }
                break;
            case JsonArray a:
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i] is JsonValue v && v.TryGetValue(out string? text) && InstanceId().IsMatch(text))
                        a[i] = Named(text);
                    else if (a[i] is JsonValue c && c.TryGetValue(out string? chest) && PieceChestKey().Match(chest) is { Success: true } m)
                        a[i] = "container." + Named("pce_" + m.Groups[1].Value.ToUpperInvariant());
                    else
                        Name(a[i], names);
                }
                break;
        }
    }
}
