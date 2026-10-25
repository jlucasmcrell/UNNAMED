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
        ["aa"] = new[] { "msaa_taa", "taa", "msaa", "msaa2", "fxaa", "off", "smaa" },
        ["tier"] = new[] { "phase_a", "low", "medium", "high", "ultra", "quality", "performance" },
        ["wind"] = new[] { "none", "on" },
        ["vfx"] = new[] { "flipbooks", "recipes" },
        ["life"] = new[] { "none", "on" },
        ["fauna"] = new[] { "other", "earth" },  // earth: the WildMesh animals as scanned, without their otherfauna looks (before/after only)
        ["hooks"] = new[] { "none", "on" },
        ["body"] = new[] { "none", "modifiers" },
        ["clips"] = new[] { "procedural", "ext" },    // ext: the player moves with the retargeted pack clips (B12's catalogue)     // modifiers: feet planted on the ground, NPCs' heads turning to the player (B0.4 / B12)          // on: the environmental audio hooks (res://Art/audio_hooks.json)           // on: ambient life - birds, butterflies, fireflies (presentation only, B10)  // recipes: the particle recipes stand in for the flat flipbooks (B0.7 / B11)           // on: the trees (and every model bound with a wind stiffness) sway on the world wind  // a preset of the others (RenderTiers: the environment's cost)
        ["people"] = new[] { "phase_a", "production", "standard", "mpfb", "humgen" },  // production: the character-fidelity pass's models; standard: the character standard (charstd) demo cast
        // The player is a MetaHuman on the shared UE5-mannequin skeleton: the owner's blend bodies mh_veth_m / mh_veth_f, bound before the
        // creator chooses one. The MPFB player is retired (owner 2026-10-17).
        ["player"] = new[] { "metahuman_m", "metahuman_f" },
        ["hud"] = new[] { "classic", "production" },  // production: the remediation's HUD direction (Ui/Hud.cs, M04)
        ["audio"] = new[] { "v3", "proof", "proof_open" },  // proof_open: only proofs whose sources set no AI/ML restriction (reviewable by an AI)
        ["textures"] = new[] { "cache", "raw" },      // cache: the texture cache's BC7 copies where it has them (B1); raw: as loaded          // proof: the external-audio proof batch (assets/audio_proof/<id>.wav) over V3 where it has one  // msaa_taa: the project's Phase-A setting (MSAA 4x with TAA)  // proof: B0.6's fold without a hard silhouette and the heart as a staged artifact
        ["hour"] = Array.Empty<string>(),        // a number: the harness's time-of-day override, 0-24
        ["weather"] = Array.Empty<string>(),     // a weather state name, validated by the weather system
        // human: HumanShaders draws the skin (Art/HumanSkin.cs); engine: the same characters with the engine's own
        // subsurface scattering instead. There is no tier that changes this - it exists so ONE BUILD can photograph
        // both arms of the skin comparison in the same frame, which is what a blind A/B of it needs and what moving
        // the shader file aside could never give. Never set in a shipping run.
        // THE BISECT ARMS (2026-09-29). `human` and `engine` were the whole set, which could only ever answer
        // "is the shader's OUTPUT different from the engine's" - one bit, and the bit came back 0. To find WHICH
        // TERM moves the frame, one term is switched off at a time with everything else identical. Each value is
        // read at HumanSkin.cs and changes exactly one SetShaderParameter.
        //   no-sss         use_subsurface_scattering = false   (the term the shader is named for)
        //   no-ao          use_ambient_occlusion = false       (gates the AO, the specular AO and the light warp)
        //   no-microdetail use_micro_detail = false            <- ALREADY THE SHIPPED VALUE. This arm is
        //                  identical to `human` BY CONSTRUCTION and exists to say so out loud rather than to
        //                  leave a hole in the ladder. Measured: it renders the same frame, because it IS the
        //                  same frame. See docs/phase_b/SKIN_SHADER_BISECT.md.
        //   lightwarp      old_lightwarp_fallof = true         (swaps the three-lobe wrapped diffuse for the
        //                  lightwarp ramp - the largest single change available inside light())
        //   red            albedo forced to pure red           <- THE MUST-CHANGE POSITIVE CONTROL. If the skin
        //                  does not turn red, the ShaderMaterial is not reaching the rasteriser at all and no
        //                  other arm can mean anything. TRAP 10's identical-image control, inverted.
        // Same rule as `human`/`engine`: one build photographs every arm, and none of these is ever set in a
        // shipping run.
        ["skin"] = new[] { "human", "engine", "no-sss", "no-ao", "no-microdetail", "lightwarp", "red" },
        // flat: the air's light and ambient are the authored base scaled by a CLAMPED daylight, which reaches
        // 1.00 by 08:00 - so both read byte-identical from 08:00 to 16:00. Measured, fifteen hourly samples:
        // docs/phase_b/air_day, and the plateau is roughly 07:30-17:00.
        // sun: the same two endpoints, reached through the SUN'S ALTITUDE instead, which keeps moving all day.
        // MEASURED AND NOT ADOPTED - it buys a 1.6 % wider tonal range with THREE TIMES the crushed shadow
        // (2.05 % -> 6.09 % of the frame under 0.05), a trade this project has refused three times. Kept as the
        // arm it was built to be.
        // hue: the fix the measurement asks for - the SHIPPED scaling untouched, so the ambient inject stays
        // where passes 8 and 9 tuned it and the shadow floor cannot move, with only the fog's HUE following the
        // sun. No tint above 45 degrees of altitude, a warm cast as it drops, at very nearly equal luminance.
        // All three exist for the same reason as `skin`: ONE BUILD photographs every arm in the same session,
        // which is what a blind A/B of a lighting change needs (TRAP 12). Never set in a shipping run.
        ["air"] = new[] { "flat", "sun", "hue" },
        // Otherreach's own sky over Sky3D (Greybox/CelestialView.cs): mix is the owner's pick (2026-10-16); the three concepts stay for comparison.
        ["celestial"] = new[] { "none", "giant", "arc", "binary", "mix" },
        // 1, the default: the Barrowworks is in the run - its two entrances are usable and its interior loads on entry.
        // 0 is the debug switch that takes it out: no portal is registered anywhere, so EnterSpaceCommand cannot
        // succeed and the frontier plays as it did before the ring.
        //
        // This is the switch, but it is NOT where the gating happens. VisualOptions is Presentation and the
        // content gate is Application's, and Presentation -> Application is the direction the projects
        // reference, so a simulation could not read this even if it wanted to (D-11). Main passes it into
        // GameOptions.Barrowworks and that is what content reads. The spelling is --visual because the brief
        // asks for it; the structure is GameOptions because the layering requires it.
        ["barrowworks"] = new[] { "0", "1" },
        // How far from Earth the plants, rocks, water and air look (Greybox/Otherworld.cs).
        ["otherworld"] = new[] { "off", "subtle", "bold" },
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
        ["people"] = "humgen",
        ["player"] = "metahuman_m",
        ["hud"] = "classic",
        // THE AIR'S HUE FOLLOWS THE SUN, ITS INTENSITY DOES NOT. Adopted as the default 2026-09-29, on the
        // owner's deferral, after the alternative was measured and refused: the `sun` arm widened the tonal
        // range by 1.6 per cent and TRIPLED the crushed shadow (2.05 -> 6.09 per cent), so it was not taken.
        // This arm holds the intensity exactly where passes 8 and 9 tuned it - the ambient inject is
        // identical to four decimals - and moves ONLY the colour: fog (0.610, 0.628, 0.647) -> (0.697, 0.601,
        // 0.518) at 07:00, red-minus-blue +22.6 per cent at 07:00 and +12.9 at 08:00, mean luminance moving
        // 0.0002, deep shade 2.71 -> 2.74 per cent.
        //
        // WHY IT IS WORTH A DEFAULT CHANGE: the owner's priority 2 asked "is it still too static" and the
        // answer was yes - the air has exactly three states a day and one of them covers 07:30-17:00. This is
        // that finding's fix, and the only thing it can regress is colour, which was the thing being fixed.
        //
        // THE `sun` AND `flat` ARMS ARE KEPT, so a blind A/B of this default is one flag away:
        //     --visual air=flat   (what shipped before)   --visual air=sun   (the refused arm)
        // Frames of all three at 07:00 and 08:00: docs/phase_b/air_ab/AIR_DECISION.png.
        ["air"] = "hue",
        ["celestial"] = "mix",
        // The Barrowworks is in every run unless --visual barrowworks=0 takes it out (owner, 2026-10-23).
        ["barrowworks"] = "1",
        // The owner's pick (2026-10-17): subtle, the level of the village wildlife variants. off and bold stay for comparison.
        ["otherworld"] = "subtle",
    };

    /// <summary>Quality aliases High; Performance aliases Medium for same-art scale-down validation. Final preset tuning is deferred.</summary>
    private static readonly Dictionary<string, Dictionary<string, string>> Tiers = new()
    {
        ["low"] = new() { ["terrain"] = "terrain3d", ["sky"] = "sky3d", ["clouds"] = "none", ["plants"] = "models", ["foldscar"] = "proof", ["textures"] = "cache", ["wind"] = "on", ["water"] = "flow", ["vfx"] = "recipes", ["life"] = "on", ["hooks"] = "on", ["body"] = "modifiers", ["clips"] = "ext", ["people"] = "humgen", ["hud"] = "production", ["aa"] = "smaa" },
        ["medium"] = new() { ["terrain"] = "terrain3d", ["sky"] = "sky3d", ["clouds"] = "none", ["plants"] = "models", ["foldscar"] = "proof", ["textures"] = "cache", ["wind"] = "on", ["water"] = "flow", ["vfx"] = "recipes", ["life"] = "on", ["hooks"] = "on", ["body"] = "modifiers", ["clips"] = "ext", ["people"] = "humgen", ["hud"] = "production", ["aa"] = "taa" },
        ["high"] = new() { ["terrain"] = "terrain3d", ["sky"] = "sky3d", ["clouds"] = "sunshine", ["plants"] = "models", ["foldscar"] = "proof", ["textures"] = "cache", ["wind"] = "on", ["water"] = "flow", ["vfx"] = "recipes", ["life"] = "on", ["hooks"] = "on", ["body"] = "modifiers", ["clips"] = "ext", ["people"] = "humgen", ["hud"] = "production", ["aa"] = "taa" },
        ["ultra"] = new() { ["terrain"] = "terrain3d", ["sky"] = "sky3d", ["clouds"] = "sunshine", ["plants"] = "models", ["foldscar"] = "proof", ["textures"] = "cache", ["wind"] = "on", ["water"] = "flow", ["vfx"] = "recipes", ["life"] = "on", ["hooks"] = "on", ["body"] = "modifiers", ["clips"] = "ext", ["people"] = "humgen", ["hud"] = "production", ["aa"] = "taa" },
    };

    public static string Tier => Values["tier"] switch
    {
        "quality" => "high",
        "performance" => "medium",
        var tier => tier,
    };
    public static bool Wind => Values["wind"] == "on";
    public static bool Recipes => Values["vfx"] == "recipes";
    public static bool BodyModifiers => Values["body"] == "modifiers";
    public static bool ProductionHud => Values["hud"] == "production";

    /// <summary>The share of the prepared plant models' density this profile draws, times the player's plant density dial.</summary>
    public static float PlantDensity => PlantScale * Tier switch
    {
        "low" => 0.45f,
        "medium" => 0.70f,
        "high" => 1.00f,
        _ => 1.00f,
    };

    /// <summary>
    /// The largest side a cached texture is uploaded at, by tier, so Low and Medium fit an 8 GB card (public playtest gate C3): Low takes the
    /// world's maps to 1024 and the rigged bodies' (people and creatures, seen close) to 2048, Medium everything to 2048. High and Ultra
    /// upload the full chain, and so does the sky's HDR at every tier.
    /// </summary>
    public static int TextureMaxSize(string key, string role) => role == "hdr" ? int.MaxValue : Tier switch
    {
        "low" => key.StartsWith("rigged/", StringComparison.Ordinal) ? 2048 : 1024,
        "medium" => 2048,
        _ => int.MaxValue,
    };

    /// <summary>
    /// How many of a scatter kind's nearest levels cast shadows: Low none (ground scatter was the second cost after the sun's cascades in the
    /// 2026-10-25 attribution), Medium the nearest only, High and Ultra the two near levels as before.
    /// </summary>
    public static int ScatterShadowLevels => Tier switch { "low" => 0, "medium" => 1, _ => 2 };

    /// <summary>Settings' plant density and detail distance dials, read as the world is built (a player's run; 1 for a harness).</summary>
    public static float PlantScale { get; set; } = 1f;

    public static float ReachScale { get; set; } = 1f;

    private static readonly Dictionary<string, string> Values = new(Defaults);

    public static string Terrain => Values["terrain"];
    public static string Sky => Values["sky"];
    public static string Clouds => Values["clouds"];
    public static string Water => Values["water"];
    public static string Plants => Values["plants"];
    public static string Foldscar => Values["foldscar"];
    public static string AntiAliasing => Values["aa"];
    public static string Celestial => Values["celestial"];
    public static string Otherworld => Values["otherworld"];

    /// <summary>Which cast the people are drawn with; <c>mpfb</c> and <c>humgen</c> are the comparison builds' casts.</summary>
    public static string People => Values["people"];

    /// <summary>
    /// The comparison cast this run draws, or null for the production cast: <c>mpfb</c> (the MakeHuman arm)
    /// or <c>humgen</c> (the Human Generator arm). Each names its own cast table, its own offline motion
    /// libraries and its own body exports, so everything keyed on "a comparison cast is drawn" reads this
    /// rather than listing the two names at each site.
    /// </summary>
    public static string? ComparisonCast => People is "mpfb" or "humgen" ? People : null;

    /// <summary>The model-id prefix a comparison cast's bodies carry (<c>mpfb_</c>, <c>humgen_</c>), or null for the production cast.</summary>
    public static string? ComparisonBodyPrefix => ComparisonCast is { } cast ? cast + "_" : null;

    /// <summary>
    /// Whether this run includes the Barrowworks (default; <c>--visual barrowworks=0</c> takes it out). Read once at startup and
    /// passed into <c>GameOptions.Barrowworks</c>: this is the switch, and that record is the gate.
    /// </summary>
    public static bool Barrowworks => Values["barrowworks"] == "1";

    /// <summary>One option changed mid-run by a harness (the visual audit's per-shot hour and sky); refused as --visual refuses it.</summary>
    internal static void Pin(string key, string value)
    {
        if (!Allowed.TryGetValue(key, out var values) || (values.Length > 0 && !values.Contains(value)))
            throw new ArgumentException($"--visual: no option {key}={value}");
        Values[key] = value;
    }

    /// <summary>Whether the air's light follows the SUN'S ALTITUDE (<c>sun</c>) or a clamped daylight (<c>flat</c>).</summary>
    public static bool AirFollowsSun => Values["air"] == "sun";

    /// <summary>Whether the air's HUE follows the sun while its intensity stays exactly as shipped (<c>hue</c>).</summary>
    public static bool AirFollowsSunHue => Values["air"] == "hue";

    /// <summary>The options that are the viewport's own settings (the anti-aliasing), applied to the game's viewport.</summary>
    public static void Apply(Viewport viewport)
    {
        ApplyRenderScale(viewport, RenderScale, upscale: true);
        ApplyAntiAliasing(viewport, AntiAliasing);
        Greybox.RenderTiers.ApplyGlobal();
    }

    /// <summary>
    /// The share of the window's pixels the 3D picture is drawn at, by tier (the interface stays at full size): Low 0.67 and Medium 0.77,
    /// enlarged with FSR, are the lower presets' biggest GPU saving (public playtest gate C4). High and Ultra draw every pixel.
    /// </summary>
    public static float RenderScale => Tier switch { "low" => 0.67f, "medium" => 0.77f, _ => 1f };

    public static void ApplyRenderScale(Viewport viewport, float scale, bool upscale)
    {
        viewport.Scaling3DScale = scale;
        viewport.Scaling3DMode = scale < 0.999f && upscale ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
    }

    public static void ApplyAntiAliasing(Viewport viewport, string mode)
    {
        viewport.Msaa3D = mode switch { "msaa2" => Viewport.Msaa.Msaa2X, "msaa" or "msaa_taa" => Viewport.Msaa.Msaa4X, _ => Viewport.Msaa.Disabled };
        viewport.UseTaa = mode is "taa" or "msaa_taa";
        viewport.ScreenSpaceAA = mode switch { "fxaa" => Viewport.ScreenSpaceAAEnum.Fxaa, "smaa" => Viewport.ScreenSpaceAAEnum.Smaa, _ => Viewport.ScreenSpaceAAEnum.Disabled };
    }

    /// <summary>The harness's time-of-day override in hours, or null to follow the world's clock.</summary>
    public static float? Hour => Values.TryGetValue("hour", out var h) ? float.Parse(h, System.Globalization.CultureInfo.InvariantCulture) : null;

    public static string? Weather => Values.GetValueOrDefault("weather");

    /// <summary>Everything in effect, for reports and screenshots' records.</summary>
    public static IReadOnlyDictionary<string, string> All => Values;

    /// <summary>Owner builds start at Ultra (2026-10-24); saved settings or an explicit public hardware profile take precedence.</summary>
    public const string DefaultTier = "ultra";

    /// <summary>
    /// The run's options: the tier the player saved (<paramref name="savedTier"/>, player runs only) or <see cref="DefaultTier"/>, then
    /// whatever <c>--visual</c> names, then the tier's preset under everything the spec did not name. <c>phase_a</c> is every Phase-A value.
    /// </summary>
    public static void Parse(string? spec, string? savedTier = null)
    {
        Values["tier"] = savedTier is not null && Allowed["tier"].Contains(savedTier) ? savedTier : DefaultTier;
        var named = new HashSet<string>(StringComparer.Ordinal);
        string[] parts = (spec ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // `phase_a` is a whole-run preset, not an option, so where it sits in the list decides whether the
        // options written beside it survive it: `phase_a,air=flat` honours air=flat, while `air=flat,phase_a`
        // silently reverts to hue. Measured, not reasoned. That is a trap and not a feature, and this file's
        // contract is to refuse to start naming the fault rather than draw something other than what was asked.
        if (Array.IndexOf(parts, "phase_a") > 0)
            throw new ArgumentException("--visual: 'phase_a' is a whole-run preset and must come first in the list");
        foreach (string part in parts)
        {
            if (part == "phase_a")
            {
                foreach (var (key, value) in Defaults)
                {
                    Values[key] = value;
                    named.Add(key);
                }
                // `Defaults` carries two roles at once - the out-of-the-box value AND the Phase-A value - and
                // `air` is the one key where the two have parted company. Every other row is still both, but
                // `air`'s shipping default is the 2026-09-29 remedy (`hue`), whose own note above names `flat`
                // as "what shipped before". Copying Defaults alone therefore left a Phase-B change INSIDE the
                // control that exists to exclude Phase-B: measured, `--visual phase_a` gave air=hue, so a
                // phase_a A/B would have carried the air change in BOTH arms and could not have seen it.
                // Corrected here and deliberately NOT in `Defaults`: Defaults["air"] is the genuine default
                // for a player run, and moving it would undo the remedy rather than fix the control.
                Values["air"] = "flat";
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

