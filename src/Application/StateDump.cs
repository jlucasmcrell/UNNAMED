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
/// hand; the records are written as they are. <see cref="Compare"/> walks two dumps leaf by leaf.
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
                            differences.Add($"{path}.{key}: expected {(x.ContainsKey(key) ? x[key]?.ToJsonString() : "(absent)")}, actual {(y.ContainsKey(key) ? y[key]?.ToJsonString() : "(absent)")}");
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
                    else if (child is not null)
                        MaskIds(child);
                }
                break;
            case JsonArray a:
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i] is JsonValue v && v.TryGetValue(out string? text) && InstanceId().IsMatch(text))
                        a[i] = "#";
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
                    else
                        Name(a[i], names);
                }
                break;
        }
    }
}
