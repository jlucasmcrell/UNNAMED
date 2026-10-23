// UNNAMED Presentation - weather as presentation data (res://Art/weather_states.json) over Sky3D, the clouds, the fog, the wind and the ground
// Godot presentation only (D-11): the simulation decides the weather (World.Runtime.Weather); this draws it

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// A weather state applied to a Sky3D node (<see cref="SkyView"/>): its own properties and its dome's (named as Sky3D 2.1 declares them;
/// numbers, or [r, g, b] colours), the volumetric clouds' (<c>clouds</c>), the environment's, the fog densities scaled, the world wind and
/// how wet the ground is. The file's <c>all</c> applies first under every state. <see cref="Follow"/> moves to the simulation's state over
/// <see cref="FadeSeconds"/>, every number blended from where the sky stands now; a property no state names keeps the value it booted with.
/// </summary>
public static class WeatherStates
{
    public const string ResourcePath = "res://Art/weather_states.json";

    /// <summary>How long one weather takes to give way to the next (owner finding 55: a cross-fade, not a cut).</summary>
    public const float FadeSeconds = 40f;

    private sealed record Look(Dictionary<(string Target, string Property), Variant> Values, float FogScale, float Wind, float Gust, float Wet);

    /// <summary>The state being drawn, the one it is leaving, and how far the change has gone (0-1).</summary>
    public static string Now { get; private set; } = "fair";
    public static string Was { get; private set; } = "fair";
    public static float Blend { get; private set; } = 1f;

    /// <summary>Whether the camera is under the open sky: false inside a space, where no rain falls.</summary>
    public static bool UnderSky { get; set; } = true;

    private static JsonElement? _file;
    private static Look? _base, _from, _to;
    private static float _fogBase, _volumetricBase;
    private static float _wetDrawn = -1;
    private static (GodotObject Asset, Color Albedo, float Roughness)[] _ground = Array.Empty<(GodotObject, Color, float)>();

    /// <summary>Terrain3D's texture assets, whose colour and roughness the rain darkens and slicks (Terrain3DView).</summary>
    public static void WetGround(IEnumerable<GodotObject> assets)
    {
        _ground = assets.Select(a => (a, (Color)a.Get("albedo_color"), (float)a.Get("roughness"))).ToArray();
        _wetDrawn = -1;
    }

    /// <summary>Draw <paramref name="state"/> at once: the sky as a run builds it (<see cref="SkyView"/>).</summary>
    public static void Apply(WorldEnvironment sky, string state)
    {
        if (Of(sky, state) is not { } look)
            return;
        Now = Was = state;
        Blend = 1f;
        _from = _to = look;
        Write(sky, look);
        GD.Print($"UNNAMED weather: {state}");
    }

    /// <summary>Move toward <paramref name="state"/>, a frame at a time: a change starts a cross-fade from whatever is drawn now.</summary>
    public static void Follow(WorldEnvironment sky, string state, double delta)
    {
        if (_to is null)
            return;
        if (state != Now && Of(sky, state) is { } next)
        {
            _from = Lerp(_from!, _to, Smooth(Blend));
            _to = next;
            Was = Now;
            Now = state;
            Blend = 0f;
            GD.Print($"UNNAMED weather: {Was} -> {state}");
        }
        if (Blend >= 1f)
            return;
        Blend = Math.Min(1f, Blend + (float)delta / FadeSeconds);
        Write(sky, Lerp(_from!, _to, Smooth(Blend)));
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    private static JsonElement? File()
    {
        if (_file is null && Godot.FileAccess.FileExists(ResourcePath))
        {
            using var json = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(ResourcePath));
            _file = json.RootElement.Clone();
        }
        return _file;
    }

    private static Look? Of(WorldEnvironment sky, string state)
    {
        if (File() is not { } root)
            return null;
        var states = root.GetProperty("states");
        if (!states.TryGetProperty(state, out var chosen))
        {
            GD.PushWarning($"UNNAMED weather: no state '{state}' in {ResourcePath}; drawing fair weather");
            if (state == "fair" || !states.TryGetProperty("fair", out chosen))
                return null;
        }
        // The first time: what every property any state names stands at as the run built it, so leaving a state restores it.
        if (_base is null)
        {
            var values = new Dictionary<(string, string), Variant>();
            foreach (var s in states.EnumerateObject().Select(s => s.Value).Prepend(root.GetProperty("all")))
                foreach (var target in s.EnumerateObject())
                    if (Target(sky, target.Name) is { } on && target.Value.ValueKind == JsonValueKind.Object)
                        foreach (var p in target.Value.EnumerateObject())
                            values[(target.Name, p.Name)] = on.Get(p.Name);
            _fogBase = sky.Environment?.FogDensity ?? 0;
            _volumetricBase = sky.Environment?.VolumetricFogDensity ?? 0;
            _base = new Look(values, 1f, WindField.Strength, WindField.Gust, 0f);
        }
        var look = new Dictionary<(string, string), Variant>(_base.Values);
        foreach (var s in new[] { root.GetProperty("all"), chosen })
            foreach (var target in s.EnumerateObject())
                if (target.Value.ValueKind == JsonValueKind.Object)
                    foreach (var p in target.Value.EnumerateObject())
                        look[(target.Name, p.Name)] = p.Value.ValueKind == JsonValueKind.Array
                            ? new Color((float)p.Value[0].GetDouble(), (float)p.Value[1].GetDouble(), (float)p.Value[2].GetDouble())
                            : (float)p.Value.GetDouble();
        float Number(string key, float fallback) => chosen.TryGetProperty(key, out var n) ? (float)n.GetDouble() : fallback;
        var wind = chosen.TryGetProperty("wind", out var w) && w.GetArrayLength() == 2
            ? ((float)w[0].GetDouble(), (float)w[1].GetDouble()) : (_base.Wind, _base.Gust);
        return new Look(look, Number("fog_scale", 1f), wind.Item1, wind.Item2, Number("wet", 0f));
    }

    private static Look Lerp(Look a, Look b, float t) => new(
        b.Values.ToDictionary(v => v.Key, v => a.Values.TryGetValue(v.Key, out var from) ? Mix(from, v.Value, t) : v.Value),
        Mathf.Lerp(a.FogScale, b.FogScale, t), Mathf.Lerp(a.Wind, b.Wind, t), Mathf.Lerp(a.Gust, b.Gust, t), Mathf.Lerp(a.Wet, b.Wet, t));

    private static Variant Mix(Variant a, Variant b, float t) => b.VariantType switch
    {
        Variant.Type.Color => ((Color)a).Lerp((Color)b, t),
        Variant.Type.Float or Variant.Type.Int => Mathf.Lerp((float)a, (float)b, t),
        _ => t < 0.5f ? a : b,
    };

    private static GodotObject? Target(WorldEnvironment sky, string name) => name switch
    {
        "sky3d" => sky,
        "dome" => sky.Get("sky").AsGodotObject(),
        "environment" => sky.Environment,
        "clouds" => sky.Compositor?.CompositorEffects.FirstOrDefault(e => e.HasMethod("_render_callback") && e.Get("clouds_coverage").VariantType != Variant.Type.Nil),
        _ => null,
    };

    private static void Write(WorldEnvironment sky, Look look)
    {
        foreach (var ((target, property), value) in look.Values)
            Target(sky, target)?.Set(property, value);
        if (sky.Environment is { } environment)
        {
            environment.FogDensity = _fogBase * look.FogScale;
            environment.VolumetricFogDensity = _volumetricBase * look.FogScale;
        }
        WindField.Set(WindField.Direction, look.Wind, look.Gust);
        WindField.SetWet(look.Wet);
        // Wet ground is darker and glossier. Re-sent only when it has moved: each set re-packs Terrain3D's texture arrays.
        if (Math.Abs(look.Wet - _wetDrawn) >= 0.02f || look.Wet == 0 && _wetDrawn > 0)
        {
            _wetDrawn = look.Wet;
            foreach (var (asset, albedo, roughness) in _ground)
            {
                asset.Set("albedo_color", albedo.Darkened(0.35f * look.Wet));
                asset.Set("roughness", Mathf.Lerp(roughness, -0.6f, look.Wet));
            }
        }
    }
}
