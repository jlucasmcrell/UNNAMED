// Fixed-place rendering benchmark. The ordinary world, input commands and 20 Hz session keep running.
using System.Globalization;
using System.Text;
using System.Text.Json;
using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation.Perf;

public sealed class FrontierPerf
{
    private const int HeadingCount = 8, HeadingDegrees = 360 / HeadingCount;
    private const double FirstWarmupSeconds = 10, TurnWarmupSeconds = 3, CaptureSeconds = 12;
    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly FrameStats _stats;
    private readonly Viewport _viewport;
    private readonly StringBuilder _renderCounts = new("segment,observed_frame,draw_calls,primitives,objects\n");
    private readonly long _startTick;
    private ulong _lastUsec;
    private double _elapsed;
    private int _heading;
    private bool _warming = true, _finished;

    public string Directory { get; }

    public static (long X, long Z) ParsePoint(string spec)
    {
        var parts = spec.Split(',');
        if (parts.Length != 2 || !double.TryParse(parts[0], CultureInfo.InvariantCulture, out double x)
            || !double.TryParse(parts[1], CultureInfo.InvariantCulture, out double z)
            || !double.IsFinite(x) || !double.IsFinite(z) || Math.Abs(x) > 1_000_000 || Math.Abs(z) > 1_000_000)
            throw new ArgumentException("--frontier-point requires x,z in metres");
        return ((long)Math.Round(x * 1000), (long)Math.Round(z * 1000));
    }

    public FrontierPerf(GameSession session, PlayerController controller, CameraRig camera, Viewport viewport, string directory)
    {
        _session = session; _controller = controller; _camera = camera;
        _viewport = viewport;
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
        _stats = new FrameStats(viewport) { Segment = "warmup_yaw_000" };
        _startTick = session.Simulation!.WorldTick;
        _lastUsec = Time.GetTicksUsec();
    }

    public void Screenshot() => _stats.SkipNext();

    public string? Update()
    {
        if (_finished) return "done";
        ulong now = Time.GetTicksUsec();
        double seconds = (now - _lastUsec) / 1_000_000.0;
        _lastUsec = now;
        int before = _stats.Count;
        _stats.Record(seconds); // Monotonic interval, including stalls that the engine's delta may clamp.
        if (_stats.Count > before)
            _renderCounts.Append(CultureInfo.InvariantCulture,
                $"{_stats.Segment},{_stats.Count - 1},{Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)}," +
                $"{Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame)},{Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame)}\n");
        _elapsed += seconds;
        _camera.Yaw = _heading * Mathf.Tau / HeadingCount;
        _camera.Pitch = -.12f;
        _camera.TargetDistance = 3.5f;
        _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
        if (_session.Simulation!.Combat.Defeated) return "failed";
        if (_warming)
        {
            if (_elapsed < (_heading == 0 ? FirstWarmupSeconds : TurnWarmupSeconds)) return null;
            _warming = false; _elapsed = 0;
            _stats.Segment = $"capture_yaw_{_heading * HeadingDegrees:000}";
            _stats.Mark($"steady observation starts at tick {_session.Simulation.WorldTick}");
            return null;
        }
        if (_elapsed < CaptureSeconds) return null;
        string shot = $"yaw_{_heading * HeadingDegrees:000}";
        var body = _controller.Authoritative;
        _stats.Mark($"{shot}: tick {_session.Simulation.WorldTick}, body {body.XMm},{body.YMm},{body.ZMm}");
        _finished = ++_heading == HeadingCount;
        _warming = true; _elapsed = 0;
        if (!_finished) _stats.Segment = $"warmup_yaw_{_heading * HeadingDegrees:000}";
        return shot;
    }

    public void Write()
    {
        File.WriteAllText(Path.Combine(Directory, "render_counts.csv"), _renderCounts.ToString());
        var lights = _viewport.GetTree().CurrentScene.FindChildren("*", "Light3D", true, false).OfType<Light3D>().Select(light => new
        {
            path = light.GetPath().ToString(), type = light.GetClass().ToString(),
            at = new[] { light.GlobalPosition.X, light.GlobalPosition.Y, light.GlobalPosition.Z },
            range = (light as OmniLight3D)?.OmniRange, energy = light.LightEnergy,
            shadows = light.ShadowEnabled, visible_in_tree = light.IsVisibleInTree(),
            distance_to_camera = light.GlobalPosition.DistanceTo(_camera.Camera.GlobalPosition),
        }).ToArray();
        File.WriteAllText(Path.Combine(Directory, "lights.json"), JsonSerializer.Serialize(lights, new JsonSerializerOptions { WriteIndented = true }));
        _stats.Write(Directory, new Dictionary<string, string>
        {
            ["route"] = "One fresh frontier fixture, eight gameplay camera headings; no relocation after new game",
            ["clock"] = "Normal GameSession.Frame(delta), 20 Hz simulation; monotonic measured frame intervals",
            ["warmup"] = "10 seconds first heading; 3 seconds after each turn. Warmup segments retained separately",
            ["capture"] = "12 seconds per heading; only the frame after screenshot readback is excluded",
            ["tier"] = VisualOptions.Tier,
            ["visual_options"] = string.Join(",", VisualOptions.All.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => $"{v.Key}={v.Value}")),
            ["region"] = _session.Setup.Layout.Id,
            ["seed"] = _session.Simulation!.World.WorldSeed.ToString(CultureInfo.InvariantCulture),
            ["ticks"] = $"{_startTick}..{_session.Simulation.WorldTick}",
            ["result"] = _session.Simulation.Combat.Defeated ? "FAILED: character defeated" : "complete",
        });
    }
}
