using System.Text.Json;
using Godot;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Greybox;

/// <summary>Absolute presentation profiles over WP7 fronts; no saved or frame-history-dependent weather.</summary>
public partial class SkyWeatherView : Node
{
    public const string ResourcePath = "res://Art/weather_states.json";
    private sealed record Property(GodotObject Target, StringName Name, Variant Baseline, Dictionary<string, Variant> Profiles)
    {
        public Variant Applied = Baseline;
    }
    private readonly List<Property> _properties = new();
    private readonly Dictionary<string, (float Fog, float Wind, float Gust)> _profiles = new(StringComparer.Ordinal);
    private WorldEnvironment _sky = null!;
    private float _fog, _volumetric;
    private float _fogScale = float.NaN, _cloudWind = float.NaN;
    private long _tick = long.MinValue, _front = long.MinValue;
    private string? _region;
    private WeatherState _previous;
    private WeatherView? _currentFront;
    private ulong _seed;
    public SkyWeatherBlend Current { get; private set; } = new("fair", "fair", 1);

    public void Bind(WorldEnvironment sky)
    {
        _sky = sky;
        if (!Godot.FileAccess.FileExists(ResourcePath)) return;
        using var json = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(ResourcePath));
        var root = json.RootElement;
        var states = root.GetProperty("states");
        var clouds = sky.GetNodeOrNull<Node>("SunshineCloudsDriver")?.Get("clouds_resource").AsGodotObject();
        var targets = new Dictionary<string, GodotObject?>
        {
            ["sky3d"] = sky, ["dome"] = sky.Get("sky").AsGodotObject(),
            ["environment"] = sky.Environment, ["clouds"] = clouds,
        };
        foreach (var (section, target) in targets)
        {
            if (target is null) continue;
            if (root.TryGetProperty("all", out var all) && all.TryGetProperty(section, out var defaults))
                foreach (var p in defaults.EnumerateObject()) target.Set(p.Name, Value(p.Value));
            var names = states.EnumerateObject().Where(s => s.Value.TryGetProperty(section, out _))
                .SelectMany(s => s.Value.GetProperty(section).EnumerateObject().Select(p => p.Name)).Distinct();
            foreach (string name in names)
            {
                Variant baseline = target.Get(name);
                var values = new Dictionary<string, Variant>(StringComparer.Ordinal);
                foreach (var state in states.EnumerateObject())
                    values[state.Name] = state.Value.TryGetProperty(section, out var settings) && settings.TryGetProperty(name, out var value)
                        ? Value(value) : baseline;
                _properties.Add(new Property(target, name, baseline, values));
            }
        }
        foreach (var state in states.EnumerateObject())
        {
            float fog = state.Value.TryGetProperty("fog_scale", out var f) ? f.GetSingle() : 1;
            float wind = state.Value.TryGetProperty("wind", out var w) ? w[0].GetSingle() : .35f;
            float gust = state.Value.TryGetProperty("wind", out w) ? w[1].GetSingle() : .55f;
            _profiles[state.Name] = (fog, wind, gust);
        }
        _fog = sky.Environment?.FogDensity ?? 0;
        _volumetric = sky.Environment?.VolumetricFogDensity ?? 0;
    }

    public void Advance(Simulation simulation, long tick)
    {
        if (_tick == tick || _profiles.Count == 0) return;
        _tick = tick;
        if (VisualOptions.Weather is { } pinned)
        {
            if (!_profiles.ContainsKey(pinned)) pinned = "fair";
            Current = new SkyWeatherBlend(pinned, pinned, 1);
        }
        else
        {
            var setup = simulation.Setup;
            var region = new CellKey(simulation.HeldCells[0].Region, 0, 0);
            WeatherView Front(long at)
            {
                long hour = (long)Math.Floor((at + (double)setup.Clock.StartTicks) * setup.Clock.HoursPerDay / setup.Clock.TicksPerDay);
                var f = Weather.At(simulation.World.WorldSeed, region, setup.Layout.Id, hour, setup.Weather);
                return new WeatherView(setup.Layout.Id, f.State, setup.Clock.TickOfHour(f.StartHour), setup.Clock.TickOfHour(f.EndHour));
            }
            if (_currentFront is null || tick < _currentFront.FrontStartTick || tick >= _currentFront.FrontEndTick
                || _currentFront.RegionId != setup.Layout.Id || _seed != simulation.World.WorldSeed)
                _currentFront = Front(tick);
            var front = _currentFront;
            if (_front != front.FrontStartTick || _region != front.RegionId || _seed != simulation.World.WorldSeed)
            {
                _seed = simulation.World.WorldSeed;
                _front = front.FrontStartTick;
                _region = front.RegionId;
                _previous = Front(front.FrontStartTick - 1).State;
            }
            Current = SkyWeatherBlend.At(tick, front, _previous, setup.Clock);
        }
        foreach (var p in _properties)
        {
            Variant from = p.Profiles.GetValueOrDefault(Current.From, p.Baseline);
            Variant to = p.Profiles.GetValueOrDefault(Current.To, p.Baseline);
            Variant value = p.Baseline.VariantType == Variant.Type.Color
                ? from.AsColor().Lerp(to.AsColor(), Current.Weight) : Mathf.Lerp(from.AsSingle(), to.AsSingle(), Current.Weight);
            // Some Sky3D setters restart a tween. Stable profiles must not allocate/restart it every tick.
            if (!value.Equals(p.Applied)) { p.Target.Set(p.Name, value); p.Applied = value; }
        }
        var a = _profiles.GetValueOrDefault(Current.From, _profiles["fair"]);
        var b = _profiles.GetValueOrDefault(Current.To, _profiles["fair"]);
        float scale = Mathf.Lerp(a.Fog, b.Fog, Current.Weight);
        if (_sky.Environment is { } air && scale != _fogScale)
        {
            _fogScale = scale;
            air.FogDensity = _fog * scale;
            air.VolumetricFogDensity = _volumetric * scale;
        }
        double days = (tick + (double)simulation.Setup.Clock.StartTicks) / simulation.Setup.Clock.TicksPerDay;
        float angle = .6435f + (float)(days % 20) * Mathf.Tau / 20;
        var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
        WindField.Set(direction, Mathf.Lerp(a.Wind, b.Wind, Current.Weight), Mathf.Lerp(a.Gust, b.Gust, Current.Weight));
        if (_sky.GetNodeOrNull<Node>("SunshineCloudsDriver") is { } driver)
        {
            driver.Set("wind_direction", direction);
            float speed = WindField.Strength / .35f;
            if (speed != _cloudWind)
            {
                _cloudWind = speed;
                driver.Set("extra_large_structures_wind_speed", 140 * speed);
                driver.Set("large_structures_wind_speed", 100 * speed);
                driver.Set("medium_structures_wind_speed", 40 * speed);
                driver.Set("small_structures_wind_speed", 12 * speed);
            }
        }
    }

    private static Variant Value(JsonElement value) => value.ValueKind == JsonValueKind.Array
        ? new Color(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle()) : value.GetSingle();
}
