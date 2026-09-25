// UNNAMED Presentation - the asset pipeline's per-asset records (a world material's, an animation clip's), read and checked field by field
// Godot presentation only, and free of Godot itself so the tests can read it: no gameplay state lives here (D-11)

using System.Text.Json;

namespace UNNAMED.Presentation.Art;

/// <summary>A world material's record (<c>materials/&lt;id&gt;/&lt;id&gt;_material.json</c>): its maps' file names, its tile size and normal strength.</summary>
public sealed record MaterialRecord(string Basecolor, string? Normal, string? Orm, float TileSizeM, float NormalStrength);

/// <summary>An animation clip's record (<c>animation/clips/anim.&lt;id&gt;.json</c>): whether it loops, how long it runs, and its timed events.</summary>
public sealed record ClipRecord(bool Loop, double Seconds, IReadOnlyList<(string Id, double Time)> Events);

/// <summary>
/// The records the asset pipeline writes beside its files, read without trusting them (the Phase-1 technical audit, H-02): every field is
/// checked for its kind before it is used, and a record with a field of the wrong kind - a JSON <c>null</c> where a file name belongs, a
/// tile size of 0 - is refused whole, with why, never thrown. Its caller draws the greybox for that one record.
/// </summary>
public static class ArtRecords
{
    public const float DefaultTileSizeM = 4f;

    /// <summary>A world material's record, or null and why it cannot be used.</summary>
    public static MaterialRecord? Material(string json, out string? problem)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Refuse($"the record is {Kind(root)}, not an object", out problem);
            if (!root.TryGetProperty("maps", out var maps) || maps.ValueKind != JsonValueKind.Object)
                return Refuse(root.TryGetProperty("maps", out var m) ? $"maps is {Kind(m)}, not an object" : "it names no maps", out problem);
            if (FileName(maps, "basecolor", required: true, out string? basecolor) is { } bad)
                return Refuse(bad, out problem);
            if (FileName(maps, "normal", required: false, out string? normal) is { } badNormal)
                return Refuse(badNormal, out problem);
            if (FileName(maps, "orm", required: false, out string? orm) is { } badOrm)
                return Refuse(badOrm, out problem);
            float tile = DefaultTileSizeM;
            if (root.TryGetProperty("tile_size_m", out var t))
            {
                if (t.ValueKind != JsonValueKind.Number || !t.TryGetDouble(out double metres) || !double.IsFinite(metres) || metres <= 0)
                    return Refuse($"tile_size_m is {Describe(t)}, not a positive number of metres", out problem);
                tile = (float)metres;
            }
            float strength = 1f;
            if (root.TryGetProperty("pbr", out var pbr))
            {
                if (pbr.ValueKind != JsonValueKind.Object)
                    return Refuse($"pbr is {Kind(pbr)}, not an object", out problem);
                if (pbr.TryGetProperty("normal_strength", out var s))
                {
                    if (s.ValueKind != JsonValueKind.Number || !s.TryGetDouble(out double value) || !double.IsFinite(value) || value < 0)
                        return Refuse($"pbr.normal_strength is {Describe(s)}, not a number of 0 or more", out problem);
                    strength = (float)value;
                }
            }
            problem = null;
            return new MaterialRecord(basecolor!, normal, orm, tile, strength);
        }
        catch (JsonException e)
        {
            return Refuse($"it is not JSON ({e.Message})", out problem);
        }
    }

    /// <summary>A clip's record, or null and why it cannot be used.</summary>
    public static ClipRecord? Clip(string json, out string? problem)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Refuse<ClipRecord>($"the record is {Kind(root)}, not an object", out problem);
            bool loop = false;
            if (root.TryGetProperty("loop", out var l))
            {
                if (l.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return Refuse<ClipRecord>($"loop is {Describe(l)}, not true or false", out problem);
                loop = l.GetBoolean();
            }
            double seconds = 0;
            if (root.TryGetProperty("duration_s", out var d))
            {
                if (d.ValueKind != JsonValueKind.Number || !d.TryGetDouble(out seconds) || !double.IsFinite(seconds) || seconds < 0)
                    return Refuse<ClipRecord>($"duration_s is {Describe(d)}, not a length in seconds", out problem);
            }
            var events = new List<(string, double)>();
            if (root.TryGetProperty("events", out var list))
            {
                if (list.ValueKind != JsonValueKind.Array)
                    return Refuse<ClipRecord>($"events is {Kind(list)}, not a list", out problem);
                int n = 0;
                foreach (var e in list.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object)
                        return Refuse<ClipRecord>($"event {n} is {Kind(e)}, not an object", out problem);
                    if (!e.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))
                        return Refuse<ClipRecord>($"event {n} has no id (it is {(e.TryGetProperty("id", out var i) ? Describe(i) : "missing")})", out problem);
                    if (!e.TryGetProperty("time", out var time) || time.ValueKind != JsonValueKind.Number || !time.TryGetDouble(out double at) || !double.IsFinite(at))
                        return Refuse<ClipRecord>($"event {n} ({id.GetString()}) has no time in seconds", out problem);
                    events.Add((id.GetString()!, at));
                    n++;
                }
            }
            problem = null;
            return new ClipRecord(loop, seconds, events);
        }
        catch (JsonException e)
        {
            return Refuse<ClipRecord>($"it is not JSON ({e.Message})", out problem);
        }
    }

    /// <summary>A map's file name in its material's folder: a non-empty name, never a path out of the folder. Null when fine, else why.</summary>
    private static string? FileName(JsonElement maps, string key, bool required, out string? name)
    {
        name = null;
        if (!maps.TryGetProperty(key, out var value))
            return required ? $"maps.{key} is missing" : null;
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            return $"maps.{key} is {Describe(value)}, not a file name";
        string text = value.GetString()!;
        if (Path.IsPathRooted(text) || text.Contains(':') || text.StartsWith('/') || text.StartsWith('\\') || text.Split('/', '\\').Any(part => part == ".."))
            return $"maps.{key} ({text}) is not a file in the material's folder";
        name = text;
        return null;
    }

    private static MaterialRecord? Refuse(string why, out string? problem) => Refuse<MaterialRecord>(why, out problem);

    private static T? Refuse<T>(string why, out string? problem) where T : class
    {
        problem = why;
        return null;
    }

    private static string Kind(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null => "null",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Array => "a list",
        JsonValueKind.Object => "an object",
        _ => "undefined",
    };

    private static string Describe(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number or JsonValueKind.String => $"{e.GetRawText()}",
        _ => Kind(e),
    };
}
