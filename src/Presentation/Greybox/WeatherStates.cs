// UNNAMED Presentation - weather as presentation data (res://Art/weather_states.json) over Sky3D, the fog and the wind (Phase B, B5)
// Godot presentation only (D-11): the simulation has no weather; a run picks a state for how the world is drawn

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// A weather state applied to a Sky3D node (<see cref="SkyView"/>): its own properties and its dome's (named as Sky3D 2.1 declares them;
/// numbers, or [r, g, b] colours), the environment's fog densities scaled, and the world wind set. The file's <c>all</c> applies first under
/// every state. An unknown state is reported and the fair weather drawn.
/// </summary>
public static class WeatherStates
{
    public const string ResourcePath = "res://Art/weather_states.json";

    public static void Apply(WorldEnvironment sky, string state)
    {
        if (!Godot.FileAccess.FileExists(ResourcePath))
            return;
        using var json = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(ResourcePath));
        var root = json.RootElement;
        if (root.TryGetProperty("all", out var all))
            Set(sky, all);
        if (!root.GetProperty("states").TryGetProperty(state, out var chosen))
        {
            GD.PushWarning($"UNNAMED weather: no state '{state}' in {ResourcePath}; drawing fair weather");
            return;
        }
        Set(sky, chosen);
        if (chosen.TryGetProperty("fog_scale", out var fog) && sky.Environment is { } environment)
        {
            float scale = (float)fog.GetDouble();
            environment.FogDensity *= scale;
            environment.VolumetricFogDensity *= scale;
        }
        if (chosen.TryGetProperty("wind", out var wind) && wind.GetArrayLength() == 2)
            WindField.Set(WindField.Direction, (float)wind[0].GetDouble(), (float)wind[1].GetDouble());
        GD.Print($"UNNAMED weather: {state}");
    }

    private static void Set(WorldEnvironment sky, JsonElement state)
    {
        if (state.TryGetProperty("sky3d", out var own))
            Properties(sky, own);
        if (state.TryGetProperty("dome", out var dome) && sky.Get("sky").AsGodotObject() is { } skyDome)
            Properties(skyDome, dome);
        if (state.TryGetProperty("environment", out var environment) && sky.Environment is { } world)
            Properties(world, environment);
    }

    private static void Properties(GodotObject target, JsonElement values)
    {
        foreach (var p in values.EnumerateObject())
        {
            Variant value = p.Value.ValueKind == JsonValueKind.Array
                ? new Color((float)p.Value[0].GetDouble(), (float)p.Value[1].GetDouble(), (float)p.Value[2].GetDouble())
                : (float)p.Value.GetDouble();
            target.Set(p.Name, value);
        }
    }
}
