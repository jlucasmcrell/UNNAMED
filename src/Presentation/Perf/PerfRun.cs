// UNNAMED Presentation - the scripted camera path for the Phase-1 performance gate (PROTOTYPE.md §6.3 "Frame budget")
// Godot presentation only

using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation.Perf;

/// <summary>
/// Walks the player through the hollow through the real command path while the camera follows a script: a third-person
/// segment, a first-person segment, and a camera-obstruction segment inside and around the buildings (§23: "never only
/// the cheapest camera path"). Twelve animated stand-in bodies walk the hollow meanwhile - the prototype's entity budget
/// of 3 NPCs, 1 companion and 8 wolves - drawn by presentation only, since those systems arrive in M3d-M6.
/// </summary>
public sealed class PerfRun
{
    public const int ProxyActors = 12;

    private static readonly (double X, double Z)[] Valley =
    {
        (55, 62), (55, 80), (70, 110), (75, 135), (40, 160), (25, 168), (45, 150), (90, 130), (135, 95), (160, 60), (100, 75), (55, 75),
    };

    private static readonly (double X, double Z)[] Obstructed =
    {
        (56, 56), (55, 44), (46, 44), (40, 46), (40, 42), (50, 44), (56, 44), (58, 34), (65, 28), (72, 34), (65, 40), (56, 56),
    };

    private readonly List<Segment> _segments;
    private readonly List<(Avatar Body, Vector3 Centre, float Radius, float Speed, float Angle)> _proxies = new();
    private int _segment = -1;
    private double _elapsed;
    private int _waypoint;
    private bool _doorRequested;
    private float _cameraYaw;
    private bool _shot;

    public PerfRun(double segmentSeconds)
    {
        _segments = new List<Segment>
        {
            new("warmup", 5, 3.5f, Valley),
            new("third_person", segmentSeconds, 3.5f, Valley),
            new("first_person", segmentSeconds, 0f, Valley),
            new("obstruction", segmentSeconds, CameraRig.MaxDistance, Obstructed),
        };
    }

    public string SegmentName => _segment >= 0 && _segment < _segments.Count ? _segments[_segment].Name : "done";

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
            _waypoint = 0;
            _shot = false;
            stats.Segment = _segments[_segment].Name;
            camera.TargetDistance = _segments[_segment].CameraDistance;
        }
        _elapsed += delta;
        var segment = _segments[_segment];
        ScreenshotDue = !_shot && _elapsed >= segment.Seconds / 2;
        _shot |= ScreenshotDue;

        var body = controller.Authoritative;
        var (wx, wz) = segment.Route[_waypoint];
        var to = new Vector3((float)(wx - body.XMm / 1000.0), 0, (float)(wz - body.ZMm / 1000.0));
        if (to.Length() < 0.5f)
        {
            _waypoint = (_waypoint + 1) % segment.Route.Length;
            (wx, wz) = segment.Route[_waypoint];
            to = new Vector3((float)(wx - body.XMm / 1000.0), 0, (float)(wz - body.ZMm / 1000.0));
        }

        // The obstruction route passes through the longhouse: open its door the first time it is in reach.
        if (segment.Route == Obstructed && !_doorRequested && controller.FocusOn(camera) is { Kind: FocusKind.Door, Key: "door.longhouse" } door
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
