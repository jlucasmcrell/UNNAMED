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
        ["water"] = new[] { "none", "classic", "boujie", "flow" },  // flow: B6's river round the hollow on the ravine floors
        ["plants"] = new[] { "classic", "models" },   // models: the scatter kinds drawn from prepared plant models (with wind)
        ["foldscar"] = new[] { "classic", "proof" },
        ["aa"] = new[] { "msaa_taa", "taa", "msaa", "smaa" },
        ["tier"] = new[] { "phase_a", "low", "medium", "high", "ultra" },
        ["wind"] = new[] { "none", "on" },
        ["vfx"] = new[] { "flipbooks", "recipes" },
        ["life"] = new[] { "none", "on" },
        ["hooks"] = new[] { "none", "on" },
        ["body"] = new[] { "none", "modifiers" },
        ["clips"] = new[] { "procedural", "ext" },    // ext: the player moves with the retargeted pack clips (B12's catalogue)     // modifiers: feet planted on the ground, NPCs' heads turning to the player (B0.4 / B12)          // on: the environmental audio hooks (res://Art/audio_hooks.json)           // on: ambient life - birds, butterflies, fireflies (presentation only, B10)  // recipes: the particle recipes stand in for the flat flipbooks (B0.7 / B11)           // on: the trees (and every model bound with a wind stiffness) sway on the world wind  // a preset of the others (RenderTiers: the environment's cost)
        ["people"] = new[] { "phase_a", "production" },  // production: the character-fidelity pass's models (people_by_visual_option)
        ["audio"] = new[] { "v3", "proof", "proof_open" },  // proof_open: only proofs whose sources set no AI/ML restriction (reviewable by an AI)
        ["textures"] = new[] { "cache", "raw" },      // cache: the texture cache's BC7 copies where it has them (B1); raw: as loaded          // proof: the external-audio proof batch (assets/audio_proof/<id>.wav) over V3 where it has one  // msaa_taa: the project's Phase-A setting (MSAA 4x with TAA)  // proof: B0.6's fold without a hard silhouette and the heart as a staged artifact
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
        ["foldscar"] = "classic",
        ["aa"] = "msaa_taa",
        ["audio"] = "v3",
        ["textures"] = "raw",
        ["tier"] = "phase_a",
        ["wind"] = "none",
        ["vfx"] = "flipbooks",
        ["life"] = "none",
        ["hooks"] = "none",
        ["body"] = "none",
        ["clips"] = "procedural",
        ["people"] = "phase_a",
    };

    /// <summary>
    /// The quality tiers (B1) as presets of the other options, applied under whatever the spec names itself. Every tier draws the Phase-B
    /// world (Terrain3D, Sky3D, the fold, the texture cache); they differ in the costly systems: the volumetric clouds (Ultra), the plant
    /// models and their density, the anti-aliasing. High is the RTX 4070 Ti 1080p/60 target.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, string>> Tiers = new()
    {
        ["low"] = new() { ["terrain"] = "terrain3d", ["sky"] = "sky3d", ["clouds"] = "none", ["plants"] = "classic", ["foldscar"] = "proof", ["textures"] = "cache", ["wind"] = "on", ["water"] = "flow", ["vfx"] = "recipes", ["life"] = "on", ["hooks"] = "on", ["body"] = "modifiers", ["clips"] = "ext", ["people"] = "production", ["aa"] = "smaa" },
        ["medium"] = new() { ["terrain"] = "terrain3d", ["sky"] = "sky3d", ["clouds"] = "none", ["plants"] = "models", ["foldscar"] = "proof", ["textures"] = "cache", ["wind"] = "on", ["water"] = "flow", ["vfx"] = "recipes", ["life"] = "on", ["hooks"] = "on", ["body"] = "modifiers", ["clips"] = "ext", ["people"] = "production", ["aa"] = "taa" },
        ["high"] = new() { ["terrain"] = "terrain3d", ["sky"] = "sky3d", ["clouds"] = "none", ["plants"] = "models", ["foldscar"] = "proof", ["textures"] = "cache", ["wind"] = "on", ["water"] = "flow", ["vfx"] = "recipes", ["life"] = "on", ["hooks"] = "on", ["body"] = "modifiers", ["clips"] = "ext", ["people"] = "production", ["aa"] = "taa" },
        ["ultra"] = new() { ["terrain"] = "terrain3d", ["sky"] = "sky3d", ["clouds"] = "sunshine", ["plants"] = "models", ["foldscar"] = "proof", ["textures"] = "cache", ["wind"] = "on", ["water"] = "flow", ["vfx"] = "recipes", ["life"] = "on", ["hooks"] = "on", ["body"] = "modifiers", ["clips"] = "ext", ["people"] = "production", ["aa"] = "msaa_taa" },
    };

    public static string Tier => Values["tier"];
    public static bool Wind => Values["wind"] == "on";
    public static bool Recipes => Values["vfx"] == "recipes";
    public static bool BodyModifiers => Values["body"] == "modifiers";

    /// <summary>The share of the plant models' density this tier draws.</summary>
    public static float PlantDensity => Tier switch { "medium" => 0.55f, "high" => 0.8f, _ => 1f };

    private static readonly Dictionary<string, string> Values = new(Defaults);

    public static string Terrain => Values["terrain"];
    public static string Sky => Values["sky"];
    public static string Clouds => Values["clouds"];
    public static string Water => Values["water"];
    public static string Plants => Values["plants"];
    public static string Foldscar => Values["foldscar"];
    public static string AntiAliasing => Values["aa"];

    /// <summary>The options that are the viewport's own settings (the anti-aliasing), applied to the game's viewport.</summary>
    public static void Apply(Viewport viewport)
    {
        viewport.Msaa3D = AntiAliasing is "msaa_taa" or "msaa" ? Viewport.Msaa.Msaa4X : Viewport.Msaa.Disabled;
        viewport.UseTaa = AntiAliasing is "msaa_taa" or "taa";
        viewport.ScreenSpaceAA = AntiAliasing == "smaa" ? Viewport.ScreenSpaceAAEnum.Smaa : Viewport.ScreenSpaceAAEnum.Disabled;
        Greybox.RenderTiers.ApplyGlobal();
    }

    /// <summary>The harness's time-of-day override in hours, or null to follow the world's clock.</summary>
    public static float? Hour => Values.TryGetValue("hour", out var h) ? float.Parse(h, System.Globalization.CultureInfo.InvariantCulture) : null;

    public static string? Weather => Values.GetValueOrDefault("weather");

    /// <summary>Everything in effect, for reports and screenshots' records.</summary>
    public static IReadOnlyDictionary<string, string> All => Values;

    /// <summary>The tier a run draws with when nothing chooses one: Phase B's High (the 4070 Ti 1080p/60 target).</summary>
    public const string DefaultTier = "high";

    /// <summary>
    /// The run's options: the tier the player saved (<paramref name="savedTier"/>, player runs only) or <see cref="DefaultTier"/>, then
    /// whatever <c>--visual</c> names, then the tier's preset under everything the spec did not name. <c>phase_a</c> is every Phase-A value.
    /// </summary>
    public static void Parse(string? spec, string? savedTier = null)
    {
        Values["tier"] = savedTier is not null && Allowed["tier"].Contains(savedTier) ? savedTier : DefaultTier;
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (string part in (spec ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part == "phase_a")
            {
                foreach (var (key, value) in Defaults)
                {
                    Values[key] = value;
                    named.Add(key);
                }
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
            named.Add(kv[0]);
        }
        if (Tiers.TryGetValue(Tier, out var preset))
        {
            foreach (var (key, value) in preset)
            {
                if (!named.Contains(key))
                    Values[key] = value;
            }
        }
        GD.Print($"UNNAMED visual: {string.Join(", ", Values.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }
}
