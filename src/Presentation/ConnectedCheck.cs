using System.Text.Json;
using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation;

/// <summary>Walks the real controller through both towns and the generated road.</summary>
public sealed class ConnectedCheck
{
    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly string _profile;
    private readonly (double X, double Z)[] _route;
    private int _waypoint = 1;
    private int _frames;
    private int _lastProgress;
    private double _lastDistance = double.PositiveInfinity;
    private bool _returning;
    private readonly string? _evidence;
    private readonly Perf.FrameStats? _stats;
    private readonly List<object> _trace = new();
    private ulong _lastFrame;

    public ConnectedCheck(GameSession session, PlayerController controller, CameraRig camera, string profile, string path)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        _profile = profile;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.GetProperty("region").GetString() != session.Setup.Layout.Id)
            throw new InvalidDataException("Route does not belong to the loaded region");
        _route = document.RootElement.GetProperty("route").EnumerateArray()
            .Select(point => (point[0].GetDouble(), point[1].GetDouble())).ToArray();
        if (_route.Length < 2)
            throw new InvalidDataException("Connected route needs at least two points");
        if (System.Environment.GetEnvironmentVariable("UNNAMED_CONNECTED_EVIDENCE") is { Length: > 0 } evidence)
        {
            _evidence = Path.GetFullPath(evidence);
            if (Directory.Exists(_evidence)) throw new InvalidOperationException("Refuse overwriting walking evidence");
            Directory.CreateDirectory(_evidence);
            _stats = new Perf.FrameStats(_camera.GetViewport());
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            Engine.MaxFps = 0;
        }
    }

    public int? Update()
    {
        if (++_frames > 18000)
            return Fail("time limit exceeded");
        var body = _controller.Authoritative;
        if (_stats is not null)
        {
            ulong now = Time.GetTicksUsec();
            _stats.Segment = _returning ? "return" : "outbound";
            if (_lastFrame != 0) _stats.Record((now - _lastFrame) / 1000000.0);
            _lastFrame = now;
            if (_frames % 30 == 0)
            {
                var scatter = _camera.GetParent().GetNodeOrNull<Greybox.ScatterView>("Scatter");
                var eye = _camera.Camera.GlobalPosition;
                _trace.Add(new { frame = _frames, tick = _session.Simulation!.WorldTick, returning = _returning,
                    player_mm = new[] { body.XMm, body.YMm, body.ZMm }, camera_m = new[] { eye.X, eye.Y, eye.Z },
                    resident = scatter?.ResidencyEnabled, ready = scatter?.ResidencyReady,
                    scatter_nodes = scatter?.GetChildCount(), worst_chunk_ms = scatter?.WorstChunkMs });
            }
        }
        var target = _route[_waypoint];
        double x = body.XMm / 1000.0, z = body.ZMm / 1000.0;
        var direction = new Vector3((float)(target.X - x), 0, (float)(target.Z - z));
        double distance = direction.Length();
        if (distance < .45)
        {
            GD.Print($"UNNAMED connected: {(_returning ? "return" : "outbound")} {_waypoint}/{_route.Length - 1} at ({x:0.0},{z:0.0}) tick {_session.Simulation!.WorldTick}");
            _lastDistance = double.PositiveInfinity;
            _lastProgress = _frames;
            if (!_returning && _waypoint == _route.Length - 1)
            {
                string digest = _session.Simulation!.StateDigest();
                _session.Save(SaveSlots.Quick);
                var loaded = _session.Load(SaveSlots.Quick);
                if (!loaded.IsComplete || _session.Simulation!.StateDigest() != digest)
                    return Fail("Cairnwold save/load did not preserve the state");
                _controller.Resync();
                GD.Print("UNNAMED connected: reached Cairnwold; save/load digest matches; returning to Ashen Hollow");
                _returning = true;
                _waypoint--;
            }
            else if (_returning && _waypoint == 0)
            {
                _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
                GD.Print($"UNNAMED connected: PASS, both crossings walked in one region; {_frames} ticks");
                WriteEvidence("PASS - both crossings and Cairnwold save/load digest", null);
                Cleanup();
                return 0;
            }
            else
                _waypoint += _returning ? -1 : 1;
            return null;
        }
        if (_frames % 80 == 0)
        {
            if (distance < _lastDistance - .5)
            {
                _lastDistance = distance;
                _lastProgress = _frames;
            }
            if (_frames - _lastProgress > 320)
                return Fail($"stalled at ({x:0.0},{z:0.0}), {distance:0.0} m from waypoint {_waypoint} ({target.X},{target.Z})");
        }
        _controller.SteerWorld(direction.Normalized(), distance < 3 ? Gait.Walk : Gait.Run, _camera);
        return null;
    }

    private int Fail(string reason)
    {
        GD.PushError($"UNNAMED connected: FAIL - {reason}");
        WriteEvidence("FAIL", reason);
        Cleanup();
        return 1;
    }

    private void Cleanup()
    {
        _session.Dispose();
        if (Directory.Exists(_profile))
            Directory.Delete(_profile, recursive: true);
    }

    private void WriteEvidence(string status, string? failure)
    {
        if (_evidence is null || _stats is null) return;
        _stats.Write(_evidence, new Dictionary<string, string>
        {
            ["status"] = status, ["failure"] = failure ?? "",
            ["clock"] = "One authoritative tick per rendered frame; same scripted route, not a real-time play-speed claim",
            ["visual"] = string.Join(",", VisualOptions.All.Select(p => p.Key + "=" + p.Value)),
            ["world_seed"] = _session.Simulation!.World.WorldSeed.ToString("x16"),
        });
        File.WriteAllText(Path.Combine(_evidence, "movement_trace.json"), JsonSerializer.Serialize(_trace,
            new JsonSerializerOptions { WriteIndented = true }));
        var image = _camera.GetViewport().GetTexture().GetImage();
        var viewport = _camera.GetViewport();
        var window = _camera.GetTree().Root;
        var clientSize = DisplayServer.WindowGetSize();
        var screen = DisplayServer.WindowGetCurrentScreen();
        var screenSize = DisplayServer.ScreenGetSize(screen);
        var viewportTexture = viewport.GetTexture();
        image?.SavePng(Path.Combine(_evidence, "return_arrival.png"));
        File.WriteAllText(Path.Combine(_evidence, "capture.json"), JsonSerializer.Serialize(new
        {
            status, failure, frames = _frames, image_size = new[] { image?.GetWidth() ?? 0, image?.GetHeight() ?? 0 },
            display_sizes_px = new
            {
                window_client = new[] { clientSize.X, clientSize.Y },
                root_window = new[] { window.Size.X, window.Size.Y },
                screen_id = screen,
                screen = new[] { screenSize.X, screenSize.Y },
                viewport_texture = new[] { viewportTexture.GetWidth(), viewportTexture.GetHeight() },
            },
            world_seed = _session.Simulation!.World.WorldSeed.ToString("x16"),
            limitation = "Screenshot taken after timing ends; simulation advances one fixed tick per render frame.",
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
