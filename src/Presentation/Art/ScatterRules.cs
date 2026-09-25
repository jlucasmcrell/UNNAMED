// UNNAMED Presentation - the ground scatter's rules (res://Art/scatter_rules.json): what grows or lies on each ground, and where it does not
// Godot presentation only, and free of Godot itself so the tests can read it: no gameplay state lives here (D-11)

using System.Text.Json;

namespace UNNAMED.Presentation.Art;

/// <summary>A worn path: a polyline of (x, z) points in metres.</summary>
public sealed record ScatterPath(string Note, IReadOnlyList<(float X, float Z)> Points);

/// <summary>A noise-driven patchiness: the density kept where the macro noise at this scale is above <see cref="From"/>, fully above <see cref="To"/>.</summary>
public sealed record ScatterPatch(float ScaleM, float From, float To);

/// <summary>
/// One kind of scatter (grass clumps, leaf litter, stones, or a prepared plant model named <c>model:&lt;semantic id&gt;</c>): its mesh, its densest and its density on each ground (by the ground's
/// world material ID), how it thins (patches, a bare yard round the buildings, under the trees, the path) and how it looks per ground.
/// </summary>
public sealed record ScatterKind(
    string Name, string Mesh, string? Material, float MaxPerM2, float FadeM, bool Shadows,
    IReadOnlyDictionary<string, float> PerM2, IReadOnlyDictionary<string, float> Lush, IReadOnlyDictionary<string, float> Tall,
    IReadOnlyList<ScatterPatch> Patches, IReadOnlyList<string> BareYardGrounds, float BareYardFromM, float BareYardToM,
    float UnderTreesKeep, float UnderTreesFromM, float UnderTreesToM, float NearTreesAdd, float NearTreesFromM, float NearTreesToM,
    float PathShoulders, float ClearMargin, bool OffPath,
    float? ChunkM = null, IReadOnlyList<float>? LodsM = null, float SizeMin = 1, float SizeMax = 1, float Stiffness = 1)
{
    /// <summary>The prepared model this kind scatters (<c>mesh: "model:&lt;id&gt;"</c>), or null for a procedural mesh.</summary>
    public string? ModelId => Mesh.StartsWith("model:", StringComparison.Ordinal) ? Mesh["model:".Length..] : null;
}

/// <summary>
/// The rules the scatter view follows, as data so Phase B can extend them: the chunk size, the worn paths of each region, where nothing
/// grows (under water, on steep ground), and each kind. Read without trusting the file: a malformed kind is left out, with why.
/// </summary>
public sealed record ScatterRules(float ChunkM, float PathWidthM, float NeverBelowM, float NeverSlope,
    IReadOnlyDictionary<string, IReadOnlyList<ScatterPath>> Paths, IReadOnlyList<ScatterKind> Kinds, IReadOnlyList<string> Problems)
{
    public static ScatterRules None { get; } = new(16, 1.5f, 0.2f, 0.75f, new Dictionary<string, IReadOnlyList<ScatterPath>>(), Array.Empty<ScatterKind>(),
        Array.Empty<string>());

    public static ScatterRules Parse(string json)
    {
        var problems = new List<string>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            return None with { Problems = new[] { $"not JSON ({e.Message})" } };
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return None with { Problems = new[] { "the rules are not an object" } };
            var paths = new Dictionary<string, IReadOnlyList<ScatterPath>>(StringComparer.Ordinal);
            if (root.TryGetProperty("paths", out var regions) && regions.ValueKind == JsonValueKind.Object)
            {
                foreach (var region in regions.EnumerateObject())
                {
                    var list = new List<ScatterPath>();
                    if (region.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var path in region.Value.EnumerateArray())
                        {
                            if (Points(path) is { Count: >= 2 } points)
                                list.Add(new ScatterPath(Text(path, "note") ?? string.Empty, points));
                            else
                                problems.Add($"paths.{region.Name}: a path needs two or more [x, z] points");
                        }
                    }
                    paths[region.Name] = list;
                }
            }
            var kinds = new List<ScatterKind>();
            if (root.TryGetProperty("kinds", out var map) && map.ValueKind == JsonValueKind.Object)
            {
                foreach (var kind in map.EnumerateObject())
                {
                    if (Kind(kind.Name, kind.Value, out string? why) is { } read)
                        kinds.Add(read);
                    else
                        problems.Add($"kinds.{kind.Name}: {why}");
                }
            }
            var never = root.TryGetProperty("never", out var n) && n.ValueKind == JsonValueKind.Object ? n : default;
            var pathRule = root.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
            return new ScatterRules(Positive(root, "chunk_m", 16), Positive(pathRule, "width_m", 1.5f), Number(never, "below_height_m", 0.2f),
                Positive(never, "slope_over", 0.75f), paths, kinds, problems);
        }
    }

    private static ScatterKind? Kind(string name, JsonElement e, out string? why)
    {
        why = null;
        if (e.ValueKind != JsonValueKind.Object)
        {
            why = "not an object";
            return null;
        }
        if (Text(e, "mesh") is not { } mesh)
        {
            why = "no mesh";
            return null;
        }
        float max = Number(e, "max_per_m2", 0);
        if (!(max > 0))
        {
            why = "max_per_m2 is not a positive number";
            return null;
        }
        var perM2 = Map(e, "per_m2");
        if (perM2.Values.Any(v => v < 0 || v > max))
        {
            why = $"a per_m2 density is outside 0 to max_per_m2 ({max})";
            return null;
        }
        var patches = new List<ScatterPatch>();
        if (e.TryGetProperty("patches", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var patch in list.EnumerateArray())
                patches.Add(new ScatterPatch(Positive(patch, "scale_m", 10), Number(patch, "from", 0), Number(patch, "to", 1)));
        }
        var yard = e.TryGetProperty("bare_yard", out var y) && y.ValueKind == JsonValueKind.Object ? y : default;
        var under = e.TryGetProperty("under_trees", out var u) && u.ValueKind == JsonValueKind.Object ? u : default;
        var near = e.TryGetProperty("near_trees", out var t) && t.ValueKind == JsonValueKind.Object ? t : default;
        var grounds = yard.ValueKind == JsonValueKind.Object && yard.TryGetProperty("grounds", out var g) && g.ValueKind == JsonValueKind.Array
            ? g.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList()
            : new List<string>();
        return new ScatterKind(name, mesh, Text(e, "material"), max, Positive(e, "fade_m", 50), e.TryGetProperty("shadows", out var s) && s.ValueKind == JsonValueKind.True,
            perM2, Map(e, "lush"), Map(e, "tall"), patches, grounds, Number(yard, "from_m", 0), Number(yard, "to_m", 0),
            under.ValueKind == JsonValueKind.Object ? Number(under, "keep", 1) : 1, Number(under, "from_m", 0), Number(under, "to_m", 0),
            Number(near, "add", 0), Number(near, "from_m", 0), Number(near, "to_m", 0), Number(e, "path_shoulders", 0), Positive(e, "clear_margin", 1),
            !(e.TryGetProperty("off_path", out var off) && off.ValueKind == JsonValueKind.False),
            Number(e, "chunk_m", 0) is var c && c > 0 ? c : null, Numbers(e, "lods_m"),
            Numbers(e, "size") is { Count: 2 } size && size[0] > 0 && size[1] >= size[0] ? size[0] : 1,
            Numbers(e, "size") is { Count: 2 } size2 && size2[0] > 0 && size2[1] >= size2[0] ? size2[1] : 1, Positive(e, "stiffness", 1));
    }

    /// <summary>An array of numbers (the level distances, a size range), or null.</summary>
    private static List<float>? Numbers(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array && v.EnumerateArray().All(n => n.ValueKind == JsonValueKind.Number)
            ? v.EnumerateArray().Select(n => (float)n.GetDouble()).ToList() : null;

    private static List<(float, float)>? Points(JsonElement path)
    {
        if (path.ValueKind != JsonValueKind.Object || !path.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Array)
            return null;
        var read = new List<(float, float)>();
        foreach (var point in points.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 2 || point.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.Number))
                return null;
            read.Add(((float)point[0].GetDouble(), (float)point[1].GetDouble()));
        }
        return read;
    }

    private static Dictionary<string, float> Map(JsonElement e, string name) =>
        e.TryGetProperty(name, out var map) && map.ValueKind == JsonValueKind.Object
            ? map.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Number).ToDictionary(p => p.Name, p => (float)p.Value.GetDouble(), StringComparer.Ordinal)
            : new Dictionary<string, float>(StringComparer.Ordinal);

    private static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;

    private static float Number(JsonElement e, string name, float fallback) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && double.IsFinite(d)
            ? (float)d : fallback;

    private static float Positive(JsonElement e, string name, float fallback) => Number(e, name, fallback) is var n && n > 0 ? n : fallback;
}
