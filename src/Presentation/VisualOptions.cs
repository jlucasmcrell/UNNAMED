// UNNAMED Presentation - the visual overhaul's switches: which presentation systems draw this run (Phase B)
// Godot presentation only: every option chooses how the game is drawn, never what the simulation holds (D-11)

using Godot;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>--visual key=value,key=value</c>: the presentation systems a run draws with, so every Phase-B system is a reversible experiment
/// that the harnesses can photograph and measure against the Phase-A look (<c>--visual phase_a</c> turns every Phase-B system off).
/// Keys not given keep their defaults; an unknown key or value refuses to start, naming it, rather than silently drawing something else.
/// </summary>
public static class VisualOptions
{
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        ["terrain"] = new[] { "groundfield", "terrain3d" },
        ["sky"] = new[] { "classic", "sky3d", "hdri" },
        ["clouds"] = new[] { "none", "sunshine" },
        ["water"] = new[] { "none", "classic", "boujie" },
        ["plants"] = new[] { "classic", "models" },   // models: the scatter kinds drawn from prepared plant models (with wind)
        ["hour"] = Array.Empty<string>(),        // a number: the harness's time-of-day override, 0-24
        ["weather"] = Array.Empty<string>(),     // a weather state name, validated by the weather system
    };

    private static readonly Dictionary<string, string> Defaults = new()
    {
        ["terrain"] = "groundfield",
        ["sky"] = "classic",
        ["clouds"] = "none",
        ["water"] = "none",
        ["plants"] = "classic",
    };

    private static readonly Dictionary<string, string> Values = new(Defaults);

    public static string Terrain => Values["terrain"];
    public static string Sky => Values["sky"];
    public static string Clouds => Values["clouds"];
    public static string Water => Values["water"];
    public static string Plants => Values["plants"];

    /// <summary>The harness's time-of-day override in hours, or null to follow the world's clock.</summary>
    public static float? Hour => Values.TryGetValue("hour", out var h) ? float.Parse(h, System.Globalization.CultureInfo.InvariantCulture) : null;

    public static string? Weather => Values.GetValueOrDefault("weather");

    /// <summary>Everything in effect, for reports and screenshots' records.</summary>
    public static IReadOnlyDictionary<string, string> All => Values;

    public static void Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return;
        foreach (string part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part == "phase_a")
            {
                foreach (var (key, value) in Defaults)
                    Values[key] = value;
                continue;
            }
            string[] kv = part.Split('=', 2);
            if (kv.Length != 2 || !Allowed.TryGetValue(kv[0], out var values))
                throw new ArgumentException($"--visual: unknown option '{part}' (known: {string.Join(", ", Allowed.Keys)}, phase_a)");
            if (values.Length > 0 && !values.Contains(kv[1]))
                throw new ArgumentException($"--visual {kv[0]}: '{kv[1]}' is not one of {string.Join(", ", values)}");
            if (kv[0] == "hour" && (!float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hour)
                                    || hour < 0 || hour > 24))
                throw new ArgumentException($"--visual hour: '{kv[1]}' is not an hour from 0 to 24");
            Values[kv[0]] = kv[1];
        }
        GD.Print($"UNNAMED visual: {string.Join(", ", Values.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }
}
