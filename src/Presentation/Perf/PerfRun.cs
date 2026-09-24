// UNNAMED Presentation - the scripted camera path for the Phase-1 performance gate (PROTOTYPE.md §6.3 "Frame budget")
// Godot presentation only

using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation.Perf;

/// <summary>
/// Walks the player through the hollow through the real command path while the camera follows a script: a camera-obstruction
/// segment inside and around the buildings (§23: "never only the cheapest camera path"), then a third-person and a first-person
/// segment round all four cells. Both loops keep clear of every creature's senses, so the capture measures the scene rather than a
/// fight; the creatures live, wander and patrol meanwhile. Twelve animated stand-in bodies walk the hollow too - the prototype's
/// entity budget of 3 NPCs, 1 companion and 8 wolves, drawn by presentation only before M3d-M6 built them, and kept as a margin.
/// </summary>
public sealed class PerfRun
{
    public const int ProxyActors = 12;

    // M6's layout: a loop through all four cells - the waystation, Blackvein's mouth and rim, the Foldscar's edge, Charwood's south -
    // and back, at least 45 m from the hound's home and 20-30 m beyond every other creature's sight, patrol or ground.
    private static readonly (double X, double Z)[] Valley =
    {
        (30, 158), (60, 156), (68, 150), (68, 128), (66, 112), (63, 98), (86, 78), (112, 74), (135, 62), (140, 85), (160, 106), (180, 118),
        (150, 100), (100, 100), (66, 112), (58, 130), (44, 138),
    };

    // Through the lodge, round the smithy, and through the narrow gap between its fence and its north-west corner (bible §26).
    private static readonly (double X, double Z)[] Obstructed =
    {
        (44, 138), (54.5, 134), (53, 128), (44, 128), (38, 130), (38, 126), (48, 128), (54, 128), (51.5, 136), (58, 136), (65, 136), (65, 148),
        (58, 148), (52.2, 148.5), (52.2, 141), (47, 137), (44, 138),
    };

    // Both loops pass here: a segment on the other loop joins it at this point, never by a straight line across the buildings.
    private static readonly (double X, double Z) Junction = (44, 138);

    private readonly List<Segment> _segments;
    private readonly List<(Avatar Body, Vector3 Centre, float Radius, float Speed, float Angle)> _proxies = new();
    private readonly Dictionary<string, int> _reached = new();
    private int _segment = -1;
    private double _elapsed;
    private (double X, double Z)[] _route = Obstructed;
    private (double X, double Z)[]? _next;
    private int _waypoint;
    private bool _doorRequested;
    private float _cameraYaw;
    private bool _shot;

    public PerfRun(double segmentSeconds)
    {
        _segments = new List<Segment>
        {
            new("warmup", 5, 3.5f, Obstructed),
            new("obstruction", segmentSeconds, CameraRig.MaxDistance, Obstructed),
            new("third_person", segmentSeconds, 3.5f, Valley),
            new("first_person", segmentSeconds, 0f, Valley),
        };
    }

    public string SegmentName => _segment >= 0 && _segment < _segments.Count ? _segments[_segment].Name : "done";

    /// <summary>Waypoints reached in each segment: a walker stuck on a wall shows here as a count that stopped.</summary>
    public string Progress => string.Join(", ", _segments.Select(s => $"{s.Name} {_reached.GetValueOrDefault(s.Name)} waypoints"));

    /// <summary>True once per segment, halfway through: the moment to keep a screenshot of what was being measured.</summary>
    public bool ScreenshotDue { get; private set; }

    public string Summary =>
        $"segments: {string.Join(", ", _segments.Select(s => $"{s.Name} {s.Seconds:0} s at camera distance {s.CameraDistance:0.#} m"))}; " +
        $"{ProxyActors} presentation-only stand-in bodies for the prototype's entity budget";

    /// <summary>Add the stand-in bodies: walking circles spread over the hollow, clear of the buildings.</summary>
    public void SpawnProxies(Node3D parent, RegionLayout layout)
    {
        var random = new RandomNumberGenerator { Seed = 7 };
        var centres = new[] { (110f, 60f), (130f, 140f), (90f, 170f), (150f, 100f), (100f, 110f), (60f, 95f) };
        for (int i = 0; i < ProxyActors; i++)
        {
            var (x, z) = centres[i % centres.Length];
            var body = new Avatar { Name = $"Proxy{i:00}" };
            parent.AddChild(body);
            _proxies.Add((body, new Vector3(x, 0, z), 4f + random.Randf() * 6f, 1.2f + random.Randf() * 2.5f, random.Randf() * Mathf.Tau));
        }
    }

    /// <summary>Advance the script. Returns false once every segment has run.</summary>
    public bool Update(double delta, PlayerController controller, CameraRig camera, FrameStats stats, RegionLayout layout)
    {
        if (_segment < 0 || _elapsed >= _segments[_segment].Seconds)
        {
            if (++_segment >= _segments.Count)
                return false;
            _elapsed = 0;
            _shot = false;
            stats.Segment = _segments[_segment].Name;
            camera.TargetDistance = _segments[_segment].CameraDistance;
            if (_segments[_segment].Route != _route)
                _next = _segments[_segment].Route;
        }
        _elapsed += delta;
        var segment = _segments[_segment];
        ScreenshotDue = !_shot && _elapsed >= segment.Seconds / 2;
        _shot |= ScreenshotDue;

        var body = controller.Authoritative;
        var (wx, wz) = _route[_waypoint];
        var to = new Vector3((float)(wx - body.XMm / 1000.0), 0, (float)(wz - body.ZMm / 1000.0));
        if (to.Length() < 0.5f)
        {
            _reached[segment.Name] = _reached.GetValueOrDefault(segment.Name) + 1;
            if (_next is { } next && _route[_waypoint] == Junction)
            {
                _route = next;
                _next = null;
                _waypoint = Array.IndexOf(next, Junction);
            }
            _waypoint = (_waypoint + 1) % _route.Length;
            (wx, wz) = _route[_waypoint];
            to = new Vector3((float)(wx - body.XMm / 1000.0), 0, (float)(wz - body.ZMm / 1000.0));
        }

        // The obstruction route passes through the longhouse: open its door the first time it is in reach.
        if (_route == Obstructed && !_doorRequested && controller.FocusOn(camera) is { Kind: FocusKind.Door, Key: "door.longhouse" } door
            && !controller.IsOpen(door.Key))
        {
            controller.Interact(door.Key);
            _doorRequested = true;
        }

        var heading = to.Normalized();
        float targetYaw = Mathf.Atan2(-heading.X, -heading.Z);
        float sway = segment.Name == "obstruction"
            ? (float)(_elapsed * Mathf.Tau / 20)                         // a full orbit every 20 s, into walls and roofs
            : Mathf.Sin((float)_elapsed * 0.6f) * (segment.CameraDistance > 0 ? 0.8f : 0.3f);
        _cameraYaw = Mathf.LerpAngle(_cameraYaw, targetYaw, 1f - Mathf.Exp(-3f * (float)delta));
        camera.Yaw = Mathf.Wrap(_cameraYaw + sway, -Mathf.Pi, Mathf.Pi);
        camera.Pitch = segment.CameraDistance > 0 ? -0.28f : -0.1f + Mathf.Sin((float)_elapsed * 0.4f) * 0.25f;
        controller.SteerWorld(heading, Gait.Run, camera);

        foreach (var (proxy, centre, radius, speed, angle) in _proxies)
        {
            float a = angle + (float)(Time.GetTicksMsec() / 1000.0) * speed / radius;
            var feet = centre + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
            feet.Y = layout.Space.Terrain.HeightAtMm((long)(feet.X * 1000), (long)(feet.Z * 1000)) / 1000f;
            proxy.Pose(feet, Mathf.Atan2(-Mathf.Sin(a), Mathf.Cos(a)), speed, delta);
        }
        return true;
    }

    private sealed record Segment(string Name, double Seconds, float CameraDistance, (double X, double Z)[] Route);
}
