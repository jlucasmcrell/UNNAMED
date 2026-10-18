using System.Text.Json;
using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Player;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Perf;

/// <summary>Photographs sky projections in the real scene; never advances or edits the world.</summary>
public sealed class SkyShots
{
    private sealed record Sample(string Name, long Tick, float Yaw = 0, float Pitch = .7f);
    private readonly List<Sample> _samples = new();
    private readonly List<object> _observations = new();
    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly Camera3D _skyCamera;
    private readonly bool _calendarOnly;
    private int _index, _frames;
    private double _elapsed;
    public string Directory { get; }
    public long ProjectionTick => _samples[Math.Min(_index, _samples.Count - 1)].Tick;

    public SkyShots(GameSession session, PlayerController controller, CameraRig camera, string directory, bool calendarOnly = false)
    {
        _session = session; _controller = controller; _camera = camera; Directory = directory;
        _calendarOnly = calendarOnly;
        _skyCamera = new Camera3D { Name = "SkyStudyCamera", Fov = 80, Far = camera.Camera.Far };
        camera.GetParent().AddChild(_skyCamera);
        System.IO.Directory.CreateDirectory(directory);
        var sim = session.Simulation!;
        var setup = sim.Setup;
        var clock = setup.Clock;
        var region = new CellKey(sim.HeldCells[0].Region, 0, 0);
        var found = new HashSet<WeatherState>();
        for (int day = 1; !calendarOnly && day < setup.Calendar.DaysPerReckoning * 2 && found.Count < 5; day++)
        {
            long tick = clock.TickOfHour(day * clock.HoursPerDay + 12);
            var front = Weather.At(sim.World.WorldSeed, region, setup.Layout.Id, clock.Hour(tick), setup.Weather);
            if (front.State == WeatherState.Fold || !found.Add(front.State)) continue;
            string key = UNNAMED.World.Runtime.WeatherStates.Key(front.State);
            long start = clock.TickOfHour(front.StartHour);
            long transition = Math.Max(1, clock.TicksPerDay / (clock.HoursPerDay * 6));
            _samples.Add(new Sample($"weather_{key}_before", start - 1));
            _samples.Add(new Sample($"weather_{key}_mid", start + transition / 2));
            _samples.Add(new Sample($"weather_{key}_settled", start + transition));
            _samples.Add(new Sample($"weather_{key}_noon", Math.Max(tick, start + transition)));
        }
        if (!calendarOnly && found.Count != 5) throw new InvalidOperationException("Sky study did not find every WP7 weather state");
        for (int hour = 0; hour <= 24; hour += 2)
            _samples.Add(new Sample($"day_{hour:00}", clock.TickOfHour(clock.HoursPerDay + hour)));
        for (int season = 0; season < 4; season++)
            _samples.Add(new Sample($"season_{season}", clock.TickOfHour((long)(setup.Calendar.DaysPerReckoning * season / 4) * clock.HoursPerDay + 12)));
        foreach (int day in new[] { 0, 7, 14, 21, 28, 42, 182, 183 })
        {
            for (int heading = 0; heading < 360; heading += 90)
                _samples.Add(new Sample($"moon_day_{day:000}_yaw_{heading:000}",
                    clock.TickOfHour((long)day * clock.HoursPerDay + 22), Mathf.DegToRad(heading)));
            _samples.Add(new Sample($"moon_day_{day:000}_zenith",
                clock.TickOfHour((long)day * clock.HoursPerDay + 22), 0, 1.2f));
        }
    }

    public string? Update(double delta)
    {
        if (_index >= _samples.Count) return "done";
        _camera.Yaw = _samples[_index].Yaw; _camera.Pitch = .15f; _camera.TargetDistance = 3.5f;
        bool elevated = _calendarOnly || _samples[_index].Name.StartsWith("moon_", StringComparison.Ordinal);
        _skyCamera.Current = elevated;
        if (elevated)
        {
            var feet = _controller.Authoritative;
            _skyCamera.GlobalPosition = new Vector3(feet.XMm / 1000f, feet.YMm / 1000f + 30, feet.ZMm / 1000f);
            _skyCamera.Rotation = new Vector3(_samples[_index].Name.StartsWith("moon_", StringComparison.Ordinal)
                ? _samples[_index].Pitch : .35f, _samples[_index].Yaw, 0);
        }
        else _camera.Camera.Current = true;
        _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
        if (++_frames <= 2) return null;
        _elapsed += Math.Min(delta, .1);
        if (_elapsed < 2 || _frames < 30) return null;
        var sample = _samples[_index];
        var sim = _session.Simulation!;
        _observations.Add(new { sample.Name, projection_tick = sample.Tick, real_world_tick = sim.WorldTick,
            date = Calendar.Of(sample.Tick, sim.Setup.Clock, sim.Setup.Calendar),
            cycle = SkyCycle.At(sample.Tick, sim.Setup.Clock, sim.Setup.Calendar), blend = SkyView.Weather,
            wind = new { direction = new[] { WindField.Direction.X, WindField.Direction.Z }, WindField.Strength, WindField.Gust },
            key_light = SkyView.KeyLight?.Name.ToString(), key_energy = SkyView.KeyLight?.LightEnergy });
        _index++; _elapsed = 0; _frames = 0;
        return sample.Name;
    }

    public void Write() => File.WriteAllText(Path.Combine(Directory, "sky_study.json"), JsonSerializer.Serialize(new
    {
        purpose = "Presentation-only WP7 projections; simulation state was never replaced by preview dates",
        calendar_only = _calendarOnly, calendar_weather_override = VisualOptions.Weather,
        camera_note = "Moon study (and calendar-only mode): camera 30 m over the same feet, 80-degree FOV; no camera collisions",
        samples = _observations,
    }, new JsonSerializerOptions { WriteIndented = true }));
}
