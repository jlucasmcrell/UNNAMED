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
    private readonly StringBuilder _renderCounts = new("segment,observed_frame,draw_calls,primitives,objects,gc_pause_ms\n");
    private long _lastGcPause = GC.GetTotalPauseDuration().Ticks;
    private readonly StringBuilder _processPhases = new("observed_frame,segment,phase,ms\n");
    private readonly StringBuilder _slowSimulation = new("observed_frame,segment,ms,world_tick,ticks_run,autosave_taken,gc_pause_ms\n");
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

    public FrontierPerf(GameSession session, PlayerController controller, CameraRig camera, Viewport viewport, string directory, string? diagnosticHide = null)
    {
        _session = session; _controller = controller; _camera = camera;
        _viewport = viewport;
        _diagnosticHide = diagnosticHide;
        if (diagnosticHide is not null)
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
        ulong now = Time.GetTicksUsec();
        double seconds = (now - _lastUsec) / 1_000_000.0;
        _lastUsec = now;
        int before = _stats.Count;
        _stats.Record(seconds); // Monotonic interval, including stalls that the engine's delta may clamp.
        long pause = GC.GetTotalPauseDuration().Ticks;
        double gcMs = (pause - _lastGcPause) / (double)TimeSpan.TicksPerMillisecond;
        _lastGcPause = pause;
        if (_stats.Count > before)
            _renderCounts.Append(CultureInfo.InvariantCulture,
                $"{_stats.Segment},{_stats.Count - 1},{Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)}," +
                $"{Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame)},{Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame)},{gcMs:0.###}\n");
        // The first Draw creates the NPC figures and can take longer than the entire
        // warm-up. Retain those two boot intervals, then warm for ten rendered seconds.
        // Screenshot readback must not consume the next heading's warm-up either.
        if (_heading == 0 && _warming && _stats.Count <= 2) _elapsed = 0;
        else if (_stats.Count > before) _elapsed += seconds;
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
        var scatter = _viewport.GetTree().CurrentScene.GetNodeOrNull<Greybox.ScatterView>("Scatter");
        File.WriteAllText(Path.Combine(Directory, "scatter_residency.json"), JsonSerializer.Serialize(new
        {
            enabled = scatter?.ResidencyEnabled, ready = scatter?.ResidencyReady,
            worst_chunk_ms = scatter?.WorstChunkMs, counts = scatter?.Counts,
        }));
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
            ["warmup"] = "Retain two boot intervals, then 10 seconds first heading; 3 seconds after each turn excluding screenshot readback. Warmup segments retained separately",
            ["capture"] = "12 seconds per heading; only the frame after screenshot readback is excluded",
            ["tier"] = VisualOptions.Tier,
            ["actual_msaa_3d"] = _viewport.Msaa3D.ToString(),
            ["actual_taa"] = _viewport.UseTaa.ToString(),
            ["actual_screen_space_aa"] = _viewport.ScreenSpaceAA.ToString(),
            ["actual_render_scale"] = _viewport.Scaling3DScale.ToString(CultureInfo.InvariantCulture),
            ["actual_mesh_lod_threshold"] = _viewport.MeshLodThreshold.ToString(CultureInfo.InvariantCulture),
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
