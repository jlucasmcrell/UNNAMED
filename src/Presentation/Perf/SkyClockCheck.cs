using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Character;
using UNNAMED.Presentation.Greybox;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Perf;

public partial class SkyClockCheck : Node3D
{
    private GameSession? _session;
    public override async void _Ready()
    {
        try
        {
            VisualOptions.Parse("tier=high,clouds=none");
            string content = OS.GetEnvironment("UNNAMED_CONTENT_ROOT");
            string output = OS.GetEnvironment("UNNAMED_SKY_CHECK_OUT");
            _session = GameSession.Boot(new GameOptions(content, Path.Combine(output, $"profile-{System.Environment.ProcessId}")));
            var sim = _session.NewGame(CharacterSpec.Quick("Sky Check", null), Playthrough.Seed);
            var air = new Godot.Environment { FogEnabled = true, FogDensity = .01f, VolumetricFogDensity = .02f };
            var sky = SkyView.BuildSky3D(this, air, new DirectionalLight3D(), out string? why)
                ?? throw new Exception(why);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var clock = sim.Setup.Clock;
            var region = new CellKey(sim.HeldCells[0].Region, 0, 0);
            var samples = new Dictionary<WeatherState, long>();
            for (int day = 1; day < 366 && samples.Count < 5; day++)
            {
                long tick = clock.TickOfHour(day * clock.HoursPerDay + 12);
                var front = Weather.At(sim.World.WorldSeed, region, sim.Setup.Layout.Id, clock.Hour(tick), sim.Setup.Weather);
                if (front.State != WeatherState.Fold) samples.TryAdd(front.State,
                    Math.Max(tick, clock.TickOfHour(front.StartHour) + clock.TicksPerDay / (clock.HoursPerDay * 6)));
            }
            if (samples.Count != 5) throw new Exception("Missing WP7 weather");
            foreach (var (state, tick) in samples)
            {
                SkyView.Advance(sim, tick);
                if (SkyView.Weather.Amount(UNNAMED.World.Runtime.WeatherStates.Key(state)) != 1)
                    throw new Exception($"Wrong projected weather at {state}");
                GD.Print($"SKY WEATHER CHECK: {state}, fog {air.FogDensity}, wind {WindField.Strength}");
            }
            long rainTick = samples[WeatherState.Rain];
            var rain = Weather.At(sim.World.WorldSeed, region, sim.Setup.Layout.Id, clock.Hour(rainTick), sim.Setup.Weather);
            long mid = clock.TickOfHour(rain.StartHour) + clock.TicksPerDay / (clock.HoursPerDay * 12);
            SkyView.Advance(sim, mid);
            var blend = SkyView.Weather;
            float fog = air.FogDensity, energy = sky.Get("sun_energy").AsSingle(), wind = WindField.Strength;
            SkyView.Advance(sim, samples[WeatherState.Fog]);
            SkyView.Advance(sim, mid);
            if (SkyView.Weather != blend || fog != air.FogDensity || energy != sky.Get("sun_energy").AsSingle() || wind != WindField.Strength)
                throw new Exception("Mid-front reload/seek depends on rendered history");
            RemoveChild(sky); sky.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            air = new Godot.Environment { FogEnabled = true, FogDensity = .01f, VolumetricFogDensity = .02f };
            sky = SkyView.BuildSky3D(this, air, new DirectionalLight3D(), out why) ?? throw new Exception(why);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            SkyView.Advance(sim, mid);
            if (SkyView.Weather != blend || fog != air.FogDensity || energy != sky.Get("sun_energy").AsSingle() || wind != WindField.Strength)
                throw new Exception("Fresh sky at the same mid-front tick differs from the live sky");
            float Altitude(long day)
            {
                SkyView.Advance(sim, clock.TickOfHour(day * clock.HoursPerDay + 12));
                return sky.Get("sky").AsGodotObject()!.Get("sun_altitude").AsSingle();
            }
            float winter = Altitude(0), summer = Altitude(sim.Setup.Calendar.DaysPerReckoning / 2);
            if (Math.Abs(winter - summer) < .2) throw new Exception("Season did not move the sun");
            var preview = new SubViewport { OwnWorld3D = true };
            AddChild(preview);
            var previewKey = new DirectionalLight3D { LightEnergy = 100 };
            preview.AddChild(previewKey);
            SkyView.Advance(sim, clock.TickOfHour(clock.HoursPerDay));
            if (SkyView.DaylightLevel > .01f || SkyView.KeyLight is not { LightEnergy: > 0 } nightKey
                || nightKey == previewKey || nightKey == sky.Get("sun").AsGodotObject())
                throw new Exception("Night/Foldscar key does not follow the world sky");
            SkyView.Advance(sim, samples[WeatherState.Fair]);
            if (Math.Abs(air.FogDensity - .01f) > .000001) throw new Exception("Fog compounded instead of restoring baseline");
            if (sim.WorldTick != 0) throw new Exception("Sky projection advanced the simulation");
            GD.Print("SKY CLOCK CHECK PASS: all WP7 states, mid-front seek and fresh sky rebuild, season, absolute fog, world night key, world tick unchanged");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    public override void _ExitTree() => _session?.Dispose();
}
