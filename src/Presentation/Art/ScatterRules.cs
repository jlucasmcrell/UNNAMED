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
    float? ChunkM = null, IReadOnlyList<float>? LodsM = null, float SizeMin = 1, float SizeMax = 1, float Stiffness = 1,
    IReadOnlyDictionary<string, string>? OnlyWhen = null)
{
    /// <summary>Density added near the drainage (<c>near_water</c>: add, from_m, to_m) - reeds along the Charwood stream (Phase B demo).</summary>
    public float NearWaterAdd { get; init; }
    public float NearWaterFromM { get; init; }
    public float NearWaterToM { get; init; }

    /// <summary>Whether this kind bends in the wind. Off for stones and anything else that is not a plant: every
    /// model kind goes through the foliage shader, which displaces vertices by wind, and a swaying boulder is what
    /// that costs when nothing says otherwise. Default on, so existing kinds are unchanged.</summary>
    public bool Wind { get; init; } = true;

    /// <summary>Whether a casting kind keeps its shadow on every level (<c>far_shadows</c>), not only the two nearest. For big still
    /// casters - a rock, a sapling stand - whose shadow would otherwise switch off where the third level begins, a pop the owner saw
    /// on a house wall at 20 m. Wind-driven foliage keeps the near-only default, so its far cards never cast slabs.</summary>
    public bool FarShadows { get; init; }

    /// <summary>Several prepared forms for one kind, one drawn per instance. One clump stamped a quarter of a
    /// million times is a plantation however good the clump is; a pool is how a field gets variety without a new
    /// kind per form. Empty means the kind draws its single <see cref="Mesh"/>.</summary>
    public IReadOnlyList<string> Pool { get; init; } = Array.Empty<string>();

    /// <summary>
    /// How far a prepared plant leans from its ground's colour toward dry straw, as a multiplier on a uniform
    /// 0..1 draw (<c>tint_lean</c> in the rules). The lean is a warm/cool axis, not a brightness factor, so a
    /// stand of plants is not one hue at many brightnesses.
    ///
    /// <b>0 means "not configured", and reproduces the historical formula exactly</b>
    /// (<c>own*own*0.45</c>, mean lean <c>0.45/3 = 0.15</c>, skewed hard toward zero so most instances took
    /// almost no lean). That default is deliberate: every kind that does not name <c>tint_lean</c> - which is
    /// every kind in the shared depot's rules, and so every kind of every other region - renders byte-for-byte
    /// as it did before this field existed. Only a kind that opts in changes, which is what makes the change
    /// auditable one kind at a time.
    ///
    /// A value above 0 switches to a LINEAR lean, which is the shape the card-clump branch below records as
    /// the fix for exactly this defect - "own*own was tried first and was too timid ... Linear own against a
    /// browner target gives a mean lean near 0.38 and a visible mix, which is what the references show" - a
    /// fix that was applied only to the crossed-card path, which is now dead. Cairnwold sets 0.55 (mean 0.275).
    /// </summary>
    public float TintLean { get; init; }

    /// <summary>The prepared model this kind scatters (<c>mesh: "model:&lt;id&gt;"</c>), or null for a procedural mesh.</summary>
    public string? ModelId => Mesh.StartsWith("model:", StringComparison.Ordinal) ? Mesh["model:".Length..] : null;

    /// <summary>How this kind is wrong in this world (<c>otherflora</c>), at full strangeness; null keeps it as scanned.</summary>
    public Otherflora? Otherflora { get; init; }

    /// <summary>Where the place is strange enough for this kind (<c>where_strange</c>: [from, to] of the rules' otherworld field): none below
    /// from, all of its density above to. Null grows it anywhere. The signature plants of the thin places.</summary>
    public (float From, float To)? WhereStrange { get; init; }
}

/// <summary>
/// One kind's "plausible but wrong" (art direction D.1/D.2), each lever at full strangeness - the shader scales it by where the plant
/// stands (Greybox/Otherworld.cs). Colours are linear RGB. Hue: degrees the albedo turns about the grey axis. Backlight: the colour
/// light comes through the leaf. Pale: toward bone white. Sheen: a cool, glossy, reflecting leaf. Late: how far the sway lags (0-1 of
/// a breath). Glow: a night emission on the bright (or, with <c>glow_green</c>, the green: moss) parts. Vein, iridescence, lichen: for
/// stones - a tinted seam, a thin-film shimmer along it, a coloured crust on the upward faces.
/// </summary>
public sealed record Otherflora(float Hue, float[]? Backlight, float Pale, float Sheen, float Late, float[]? Glow, bool GlowGreen,
    float[]? Vein, float Iridescence, float[]? Lichen);

/// <summary>A point on the ground (metres).</summary>
public readonly record struct GroundPoint(float X, float Z);

/// <summary>Where the world is strange (<c>otherworld</c>): the villages it is calm round, the Foldscar it is strangest at, and the strangeness
/// at a village centre and in the open.</summary>
public sealed record OtherworldField(IReadOnlyList<GroundPoint> Calm, GroundPoint Fold, float FoldFullM, float FoldNoneM, float Village, float Wild)
{
    /// <summary>The strangeness at a point, 0-1, before the run's strength: Greybox/Otherworld.cs's otherworld_at, in C#.</summary>
    public float At(float x, float z)
    {
        static float Smooth(float a, float b, float v) => Math.Clamp((v - a) / (b - a), 0, 1) is var t ? t * t * (3 - 2 * t) : 0;
        float calm = Calm.Min(c => MathF.Sqrt((x - c.X) * (x - c.X) + (z - c.Z) * (z - c.Z)));
        float open = Village + (Wild - Village) * Smooth(45, 170, calm);
        float fold = 1 - Smooth(FoldFullM, FoldNoneM, MathF.Sqrt((x - Fold.X) * (x - Fold.X) + (z - Fold.Z) * (z - Fold.Z)));
        return Math.Max(open, fold);
    }
}

/// <summary>
/// The rules the scatter view follows, as data so Phase B can extend them: the chunk size, the worn paths of each region, where nothing
/// grows (under water, on steep ground), and each kind. Read without trusting the file: a malformed kind is left out, with why.
/// </summary>
public sealed record ScatterRules(float ChunkM, float PathWidthM, float NeverBelowM, float NeverSlope,
    IReadOnlyDictionary<string, IReadOnlyList<ScatterPath>> Paths, IReadOnlyList<ScatterKind> Kinds, IReadOnlyList<string> Problems)
{
    /// <summary>The strangeness field, or null when the rules name none.</summary>
    public OtherworldField? Otherworld { get; init; }

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
            OtherworldField? field = null;
            if (root.TryGetProperty("otherworld", out var o) && o.ValueKind == JsonValueKind.Object)
            {
                var calm = o.TryGetProperty("calm", out var c) && c.ValueKind == JsonValueKind.Array
                    ? c.EnumerateArray().Select(Point).OfType<GroundPoint>().ToList() : new List<GroundPoint>();
                if (calm.Count is < 1 or > 2 || (o.TryGetProperty("fold", out var f) ? Point(f) : null) is not { } fold
                    || Numbers(o, "fold_m") is not { Count: 2 } reach || !(reach[1] > reach[0]))
                    problems.Add("otherworld: needs calm (one or two [x, z]), fold [x, z] and fold_m [full, none]");
                else
                    field = new OtherworldField(calm, fold, reach[0], reach[1], Math.Clamp(Number(o, "village", 0.25f), 0, 1), Math.Clamp(Number(o, "wild", 0.6f), 0, 1));
            }
            return new ScatterRules(Positive(root, "chunk_m", 16), Positive(pathRule, "width_m", 1.5f), Number(never, "below_height_m", 0.2f),
                Positive(never, "slope_over", 0.75f), paths, kinds, problems) { Otherworld = field };
        }
    }

    private static GroundPoint? Point(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 2 && e.EnumerateArray().All(v => v.ValueKind == JsonValueKind.Number)
            ? new GroundPoint((float)e[0].GetDouble(), (float)e[1].GetDouble()) : null;

    private static Otherflora? Flora(JsonElement e)
    {
        if (!e.TryGetProperty("otherflora", out var o) || o.ValueKind != JsonValueKind.Object)
            return null;
        float[]? Rgb(string name) => Numbers(o, name) is { Count: 3 } v ? v.ToArray() : null;
        return new Otherflora(Number(o, "hue", 0), Rgb("backlight"), Math.Clamp(Number(o, "pale", 0), 0, 1), Math.Clamp(Number(o, "sheen", 0), 0, 1),
            Math.Clamp(Number(o, "late", 0), 0, 1), Rgb("glow"), o.TryGetProperty("glow_green", out var g) && g.ValueKind == JsonValueKind.True,
            Rgb("vein"), Math.Clamp(Number(o, "iridescence", 0), 0, 1), Rgb("lichen"));
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
        var water = e.TryGetProperty("near_water", out var nw) && nw.ValueKind == JsonValueKind.Object ? nw : default;
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
            Numbers(e, "size") is { Count: 2 } size2 && size2[0] > 0 && size2[1] >= size2[0] ? size2[1] : 1, Positive(e, "stiffness", 1),
            e.TryGetProperty("only_when", out var when) && when.ValueKind == JsonValueKind.Object
                ? when.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal)
                : null)
        {
            NearWaterAdd = Number(water, "add", 0), NearWaterFromM = Number(water, "from_m", 0), NearWaterToM = Number(water, "to_m", 0),
            Wind = !(e.TryGetProperty("wind", out var wind) && wind.ValueKind == JsonValueKind.False),
            FarShadows = e.TryGetProperty("far_shadows", out var far) && far.ValueKind == JsonValueKind.True,
            TintLean = Math.Clamp(Number(e, "tint_lean", 0f), 0f, 1f),
            Pool = e.TryGetProperty("pool", out var poolList) && poolList.ValueKind == JsonValueKind.Array
                ? poolList.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String)
                    .Select(v => v.GetString()!.StartsWith("model:", StringComparison.Ordinal) ? v.GetString()!["model:".Length..] : v.GetString()!)
                    .ToList()
                : Array.Empty<string>(),
            Otherflora = Flora(e),
            WhereStrange = Numbers(e, "where_strange") is { Count: 2 } strange && strange[1] > strange[0] ? (strange[0], strange[1]) : null,
        };
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
