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
    private readonly string? _diagnosticHide;
    private readonly StringBuilder _renderCounts = new("segment,observed_frame,draw_calls,primitives,objects\n");
    private readonly StringBuilder _processPhases = new("observed_frame,segment,phase,ms\n");
    private readonly StringBuilder _slowSimulation = new("observed_frame,segment,ms,world_tick,ticks_run,autosave_taken,gc_pause_ms\n");
    private readonly long _startTick;
    private ulong _lastUsec;
    private double _elapsed;
    private int _heading;
    private bool _warming = true, _finished;
    private readonly string[]? _arms;
    private readonly (Node3D Node, bool Visible)[] _visibility = [];
    private readonly (DirectionalLight3D Light, bool Shadows)[] _suns = [];
    private readonly (Godot.Environment Environment, bool Sdfgi)[] _environments = [];
    private readonly List<object> _armStates = [];
    private bool _applyArm;
    private string? _armScreenshot;

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

    public FrontierPerf(GameSession session, PlayerController controller, CameraRig camera, Viewport viewport, string directory, string? diagnosticHide = null)
    {
        _session = session; _controller = controller; _camera = camera;
        _viewport = viewport;
        _diagnosticHide = diagnosticHide;
        if (diagnosticHide == "attribution")
        {
            _arms = ["baseline", "ravine_off", "scatter_off", "sun_shadows_off", "npc_render_off", "sdfgi_off", "baseline_return"];
            var scene = viewport.GetTree().CurrentScene;
            _visibility = new[] { "Hollow/RavineDressing", "Scatter", "Npcs" }.Select(path =>
            {
                var node = scene.GetNodeOrNull<Node3D>(path) ?? throw new InvalidOperationException("Missing diagnostic component " + path);
                return (node, node.Visible);
            }).ToArray();
            _suns = scene.FindChildren("*", nameof(DirectionalLight3D), true, false).OfType<DirectionalLight3D>().Where(light => light.GetViewport() == viewport)
                .Select(light => (light, light.ShadowEnabled)).ToArray();
            _environments = scene.FindChildren("*", nameof(WorldEnvironment), true, false).OfType<WorldEnvironment>()
                .Where(node => node.GetViewport() == viewport && node.Environment is not null).Select(node => (node.Environment!, node.Environment!.SdfgiEnabled)).ToArray();
            _applyArm = true;
            GD.Print("FRONTIER DIAGNOSTIC ONLY: seven rendering arms at yaw270; no gameplay or quality acceptance");
        }
        else if (diagnosticHide is not null)
        {
            string path = diagnosticHide switch
            {
                "scatter" => "Scatter", "ravine" => "Hollow/RavineDressing",
                _ => throw new ArgumentException("--frontier-hide supports scatter or ravine, for diagnosis only"),
            };
            var hidden = viewport.GetTree().CurrentScene.GetNodeOrNull<Node3D>(path)
                ?? throw new InvalidOperationException($"Diagnostic component not found: {path}");
            hidden.Visible = false;
            GD.Print($"FRONTIER DIAGNOSTIC ONLY: hidden {diagnosticHide}; this run cannot pass acceptance");
        }
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
        _stats = new FrameStats(viewport) { Segment = "warmup_yaw_000" };
        _startTick = session.Simulation!.WorldTick;
        _lastUsec = Time.GetTicksUsec();
    }

    public void Screenshot() => _stats.SkipNext();

    // Work in this callback contributes to the next monotonic interval, not an asserted GPU frame.
    public void Phase(string phase, ulong started) => _processPhases.Append(CultureInfo.InvariantCulture,
        $"{_stats.Count - 1},{_stats.Segment},{phase},{(Time.GetTicksUsec() - started) / 1000.0:0.###}\n");

    public void SimulationPhase(ulong started, FrameResult frame, long gcPauseTicks)
    {
        double ms = (Time.GetTicksUsec() - started) / 1000.0;
        Phase("simulation", started);
        if (ms > 10)
            _slowSimulation.Append(CultureInfo.InvariantCulture,
                $"{_stats.Count - 1},{_stats.Segment},{ms:0.###},{_session.Simulation!.WorldTick},{frame.TicksRun},{frame.AutosaveTaken},{(GC.GetTotalPauseDuration().Ticks - gcPauseTicks) / (double)TimeSpan.TicksPerMillisecond:0.###}\n");
    }

    public string? Update()
    {
        if (_finished) return "done";
        if (_applyArm) { ApplyArm(); _applyArm = false; }
        ulong now = Time.GetTicksUsec();
        double seconds = (now - _lastUsec) / 1_000_000.0;
        _lastUsec = now;
        int before = _stats.Count;
        _stats.Record(seconds); // Monotonic interval, including stalls that the engine's delta may clamp.
        if (_stats.Count > before)
            _renderCounts.Append(CultureInfo.InvariantCulture,
                $"{_stats.Segment},{_stats.Count - 1},{Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)}," +
                $"{Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame)},{Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame)}\n");
        // The first Draw creates the NPC figures and can take longer than the entire
        // warm-up. Retain those two boot intervals, then warm for ten rendered seconds.
        // Screenshot readback must not consume the next heading's warm-up either.
        if (_heading == 0 && _warming && _stats.Count <= 2) _elapsed = 0;
        else if (_stats.Count > before) _elapsed += seconds;
        _camera.Yaw = _arms is not null ? Mathf.Pi * 1.5f : _heading * Mathf.Tau / HeadingCount;
        _camera.Pitch = -.12f;
        _camera.TargetDistance = 3.5f;
        _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
        if (_session.Simulation!.Combat.Defeated) return "failed";
        if (_warming)
        {
            if (_elapsed < (_heading == 0 || _arms?[_heading] == "baseline_return" ? FirstWarmupSeconds : TurnWarmupSeconds)) return null;
            _warming = false; _elapsed = 0;
            _stats.Segment = _arms is null ? $"capture_yaw_{_heading * HeadingDegrees:000}" : "capture_" + _arms[_heading];
            _stats.Mark($"steady observation starts at tick {_session.Simulation.WorldTick}");
            return null;
        }
        if (_elapsed < (_arms is null ? CaptureSeconds : 4)) return null;
        string shot = _arms is null ? $"yaw_{_heading * HeadingDegrees:000}" : "arm_" + _arms[_heading] + "_yaw270";
        var body = _controller.Authoritative;
        _armScreenshot = _arms?[_heading];
        _stats.Mark($"{shot}: tick {_session.Simulation.WorldTick}, body {body.XMm},{body.YMm},{body.ZMm}");
        if (_arms is not null)
            _armStates.Add(new { arm = _arms[_heading], world_tick = _session.Simulation.WorldTick,
                camera = _camera.Camera.GlobalTransform.ToString(), fov = _camera.Camera.Fov,
                visibility = _visibility.Select(v => new { path = v.Node.GetPath().ToString(), v.Node.Visible }).ToArray(),
                suns = _suns.Select(v => new { path = v.Light.GetPath().ToString(), v.Light.ShadowEnabled }).ToArray(),
                sdfgi = _environments.Select(v => v.Environment.SdfgiEnabled).ToArray() });
        _finished = ++_heading == (_arms?.Length ?? HeadingCount);
        _warming = true; _elapsed = 0;
        if (!_finished)
        {
            _stats.Segment = _arms is null ? $"warmup_yaw_{_heading * HeadingDegrees:000}" : "warmup_" + _arms[_heading];
            _applyArm = _arms is not null;
        }
        return shot;
    }

    private void ApplyArm()
    {
        foreach (var (node, visible) in _visibility) node.Visible = visible;
        foreach (var (sun, shadows) in _suns) sun.ShadowEnabled = shadows;
        foreach (var (environment, sdfgi) in _environments) environment.SdfgiEnabled = sdfgi;
        switch (_arms![_heading])
        {
            case "ravine_off": _visibility[0].Node.Visible = false; break;
            case "scatter_off": _visibility[1].Node.Visible = false; break;
            case "sun_shadows_off": foreach (var (sun, _) in _suns) sun.ShadowEnabled = false; break;
            case "npc_render_off": _visibility[2].Node.Visible = false; break;
            case "sdfgi_off": foreach (var (environment, _) in _environments) environment.SdfgiEnabled = false; break;
        }
        if (_heading == 0) _stats.Segment = "warmup_baseline";
    }

    // SkyView advances the ordinary sky after Update and restores sunlight shadows.
    // Reassert only this diagnostic arm after the normal world draw has finished.
    public void ApplyRenderOverride()
    {
        // Until the next Update applies its arm, the end screenshot still belongs to the previous one.
        if (_arms is not null && !_finished && _arms[Math.Max(0, _heading - (_applyArm ? 1 : 0))] == "sun_shadows_off")
            foreach (var (sun, _) in _suns) sun.ShadowEnabled = false;
        if (_armScreenshot is { } shot)
        {
            File.AppendAllText(Path.Combine(Directory, "attribution_draw_boundary.jsonl"), JsonSerializer.Serialize(new
            {
                arm = shot, world_tick = _session.Simulation!.WorldTick,
                visibility = _visibility.Select(v => new { path = v.Node.GetPath().ToString(), v.Node.Visible }).ToArray(),
                suns = _suns.Select(v => new { path = v.Light.GetPath().ToString(), v.Light.ShadowEnabled }).ToArray(),
                sdfgi = _environments.Select(v => v.Environment.SdfgiEnabled).ToArray(),
            }) + "\n");
            _armScreenshot = null;
        }
    }

    public void Write()
    {
        if (_arms is not null)
            File.WriteAllText(Path.Combine(Directory, "attribution_arms.json"), JsonSerializer.Serialize(_armStates, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(Directory, "render_counts.csv"), _renderCounts.ToString());
        File.WriteAllText(Path.Combine(Directory, "process_phases.csv"), _processPhases.ToString());
        File.WriteAllText(Path.Combine(Directory, "slow_simulation.csv"), _slowSimulation.ToString());
        // An inventory, not a visibility claim: shadow cameras and MultiMesh bounds can submit
        // geometry outside the player camera. Keep the authored ranges and bounds to investigate it.
        var meshes = _viewport.GetTree().CurrentScene.FindChildren("*", "GeometryInstance3D", true, false)
            .OfType<GeometryInstance3D>().Select(node =>
            {
                Mesh? mesh = (node as MeshInstance3D)?.Mesh ?? (node as MultiMeshInstance3D)?.Multimesh?.Mesh;
                int instances = node is MultiMeshInstance3D { Multimesh: { } multi }
                    ? (multi.VisibleInstanceCount < 0 ? multi.InstanceCount : multi.VisibleInstanceCount) : 1;
                long triangles = mesh is ArrayMesh array
                    ? Enumerable.Range(0, array.GetSurfaceCount()).Where(i => array.SurfaceGetPrimitiveType(i) == Mesh.PrimitiveType.Triangles)
                        .Sum(i => (long)Math.Max(array.SurfaceGetArrayIndexLen(i), array.SurfaceGetArrayLen(i)) / 3) : 0;
                Aabb bounds = node.GlobalTransform * (node.CustomAabb.Size == Vector3.Zero ? node.GetAabb() : node.CustomAabb);
                return new
                {
                    path = node.GetPath().ToString(), mesh = mesh?.ResourcePath, surfaces = mesh?.GetSurfaceCount(),
                    triangles_per_instance = triangles, instances, source_triangles = triangles * instances,
                    visible_in_tree = node.IsVisibleInTree(), shadows = node.CastShadow.ToString(), gi = node.GIMode.ToString(),
                    authored_shadows = node.HasMeta("shadow_lod_original") ? node.GetMeta("shadow_lod_original").AsString() : node.CastShadow.ToString(),
                    range_begin = node.VisibilityRangeBegin, range_end = node.VisibilityRangeEnd,
                    range_begin_margin = node.VisibilityRangeBeginMargin, range_end_margin = node.VisibilityRangeEndMargin,
                    fade_mode = node.VisibilityRangeFadeMode.ToString(),
                    bounds_min = new[] { bounds.Position.X, bounds.Position.Y, bounds.Position.Z },
                    bounds_size = new[] { bounds.Size.X, bounds.Size.Y, bounds.Size.Z },
                    distance_to_camera = bounds.GetCenter().DistanceTo(_camera.Camera.GlobalPosition),
                    viewport = node.GetViewport().GetPath().ToString(),
                };
            }).ToArray();
        File.WriteAllText(Path.Combine(Directory, "geometry.json"), JsonSerializer.Serialize(meshes));
        var occluders = _viewport.GetTree().CurrentScene.FindChildren("*", nameof(OccluderInstance3D), true, false)
            .OfType<OccluderInstance3D>().Select(n => new { path = n.GetPath().ToString(), n.Visible,
                vertices = n.Occluder?.GetVertices().Length, indices = n.Occluder?.GetIndices().Length }).ToArray();
        File.WriteAllText(Path.Combine(Directory, "occlusion.json"), JsonSerializer.Serialize(new
            { enabled = _viewport.UseOcclusionCulling, occluders }));
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
            ["route"] = _arms is null ? "One fresh frontier fixture, eight gameplay camera headings; no relocation after new game"
                : "DIAGNOSTIC: fixed yaw270; baseline/ravine/scatter/sun shadows/NPC render/SDFGI/baseline return; normal simulation remains active",
            ["clock"] = "Normal GameSession.Frame(delta), 20 Hz simulation; monotonic measured frame intervals",
            ["warmup"] = "Retain two boot intervals, then 10 seconds first heading; 3 seconds after each turn excluding screenshot readback. Warmup segments retained separately",
            ["capture"] = _arms is null ? "12 seconds per heading; only the frame after screenshot readback is excluded"
                : "4 seconds per arm; 10s initial/return warmup, 3s after each toggle; screenshot readback excluded; no frame edits or gameplay suppression",
            ["tier"] = VisualOptions.Tier,
            ["actual_msaa_3d"] = _viewport.Msaa3D.ToString(),
            ["actual_taa"] = _viewport.UseTaa.ToString(),
            ["actual_screen_space_aa"] = _viewport.ScreenSpaceAA.ToString(),
            ["actual_render_scale"] = _viewport.Scaling3DScale.ToString(CultureInfo.InvariantCulture),
            ["visual_options"] = string.Join(",", VisualOptions.All.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => $"{v.Key}={v.Value}")),
            ["region"] = _session.Setup.Layout.Id,
            ["seed"] = _session.Simulation!.World.WorldSeed.ToString(CultureInfo.InvariantCulture),
            ["ticks"] = $"{_startTick}..{_session.Simulation.WorldTick}",
            ["diagnostic_hide"] = _diagnosticHide ?? "none",
            ["result"] = _session.Simulation.Combat.Defeated ? "FAILED: character defeated"
                : _diagnosticHide is not null ? $"DIAGNOSTIC ONLY: hidden {_diagnosticHide}" : "complete",
        });
    }
}
