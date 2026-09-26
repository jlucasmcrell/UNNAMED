// UNNAMED Presentation - scripted gameplay scenes for review video (Phase B remediation)
// Godot presentation only: every action goes through the player's own command path; nothing here changes a rule

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>--showcase &lt;dir&gt; --showcase-scene locomotion|sword|spells</c> (with <c>--profile</c> for a scene that needs a later save):
/// a fixed script of the player's own commands in real time - idle, walk, run, stop, turn, sprint, jump; the sword's ready stance,
/// attacks in a row, block and back to running; each formula cast - with the camera held behind or beside the direction of travel.
/// Recorded by Godot's Movie Maker (<c>--write-movie out.avi --fixed-fps 60</c> before <c>--</c>) it is a review clip; a picture
/// is taken at each step's middle. Exit code 0 when the script ran to its end.
/// </summary>
public sealed class Showcase
{
    private sealed record Step(string Name, double Seconds, Action<Showcase, double> Act, float CameraTurn = 0, float Pitch = -0.25f, float Distance = 3.5f);

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly List<Step> _steps;
    private int _index = -1;
    private double _clock;
    private double _stepStart;
    private bool _shot;
    private Vector3 _heading;

    public string Directory { get; }

    public Showcase(GameSession session, PlayerController controller, CameraRig camera, string directory, string scene)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
        _steps = scene switch
        {
            "locomotion" => Locomotion(),
            "sword" => Sword(),
            "spells" => Spells(),
            _ => throw new ArgumentException($"--showcase-scene: '{scene}' is not locomotion, sword or spells"),
        };
    }

    // Open ground at the fixed seed: the acceptance playthrough's own way out to the boar's wallow (UiShots.ToTheWallow), then on
    // down Blackvein's rim - no wall, stone or building in the way, with a right-angle turn at the first waypoint.
    private static readonly (double X, double Z)[] Route = { (60, 118), (40, 104), (36, 92), (25, 78), (22, 62) };

    private static List<Step> Locomotion() => new()
    {
        new("settle", 1.5, (s, _) => s.Hold()),
        new("idle", 3.0, (s, _) => s.Hold()),
        new("walk", 3.0, (s, _) => s.Go(Gait.Walk)),
        new("run", 4.0, (s, _) => s.Go(Gait.Run)),
        new("stop", 2.5, (s, _) => s.Hold()),
        new("run_turn", 4.0, (s, _) => s.Go(Gait.Run)),
        new("sprint", 3.0, (s, _) => s.Go(Gait.Sprint)),
        new("stop_from_sprint", 2.5, (s, _) => s.Hold()),
        new("run_side_view", 4.0, (s, _) => s.Go(Gait.Run), CameraTurn: 90, Pitch: -0.12f, Distance: 3.2f),
        new("stop_side_view", 2.0, (s, _) => s.Hold(), CameraTurn: 90, Pitch: -0.12f, Distance: 3.2f),
        new("run_jump", 3.0, (s, t) => { s.Go(Gait.Run); if (s.Once(t, 1.0)) s._controller.Jump(); }),
        new("stop_end", 2.5, (s, _) => s.Hold()),
    };

    private static List<Step> Sword() => new()
    {
        new("settle", 1.5, (s, _) => s.Hold(), CameraTurn: 135, Pitch: -0.15f, Distance: 3.0f),
        new("ready", 2.5, (s, _) => s.Hold(), CameraTurn: 135, Pitch: -0.15f, Distance: 3.0f),
        new("attacks", 6.0, (s, t) => { s.Hold(); if (s.Every(t, 1.2)) s._controller.Attack(); }, CameraTurn: 135, Pitch: -0.15f, Distance: 3.0f),
        new("recover", 2.0, (s, _) => s.Hold(), CameraTurn: 135, Pitch: -0.15f, Distance: 3.0f),
        new("block", 2.5, (s, t) => { s.Hold(); s._controller.Guard(t < 2.0); }, CameraTurn: 135, Pitch: -0.15f, Distance: 3.0f),
        new("attacks_side", 4.0, (s, t) => { s.Hold(); if (s.Every(t, 1.0)) s._controller.Attack(); }, CameraTurn: 90, Pitch: -0.12f, Distance: 3.0f),
        new("to_run", 3.0, (s, _) => s.Go(Gait.Run), CameraTurn: 0),
        new("stop_end", 2.0, (s, _) => s.Hold()),
    };

    private static List<Step> Spells() => new()
    {
        new("settle", 2.0, (s, _) => s.Hold(), CameraTurn: 150, Pitch: -0.18f, Distance: 3.6f),
        new("impulse_bolt", 4.0, (s, t) => { s.Hold(); if (s.Once(t, 0.3)) s.Cast("spell.force.impulse_bolt"); }, CameraTurn: 120, Pitch: -0.15f, Distance: 3.6f),
        new("brace_ward", 6.0, (s, t) => { s.Hold(); if (s.Once(t, 0.3)) s.Cast("spell.warding.brace_ward"); }, CameraTurn: 150, Pitch: -0.18f, Distance: 3.6f),
        new("mending_thread", 7.0, (s, t) => { s.Hold(); if (s.Once(t, 0.3)) s.Cast("spell.vital.mending_thread"); }, CameraTurn: 150, Pitch: -0.18f, Distance: 3.6f),
        new("mending_close", 4.0, (s, _) => s.Hold(), CameraTurn: 170, Pitch: -0.05f, Distance: 1.8f),
        new("mending_high", 4.0, (s, _) => s.Hold(), CameraTurn: 150, Pitch: -0.75f, Distance: 3.8f),
        new("walk_mending", 4.0, (s, _) => s.Go(Gait.Walk), CameraTurn: 30),
        new("end", 2.0, (s, _) => s.Hold()),
    };

    /// <summary>True on the first frame at or past <paramref name="at"/> seconds into the step.</summary>
    private bool Once(double t, double at) => _lastT < at && t >= at;

    /// <summary>True on the step's first frame and then every <paramref name="period"/> seconds.</summary>
    private bool Every(double t, double period) => Math.Floor(t / period) > Math.Floor(_lastT / period);

    private double _lastT = -1;

    private void Hold() => _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);

    private int _waypoint;

    /// <summary>Move along the route at a gait (the heading turns at each waypoint reached); in place at its end.</summary>
    private void Go(Gait gait)
    {
        var body = _controller.Authoritative;
        while (_waypoint < Route.Length)
        {
            var to = new Vector3((float)(Route[_waypoint].X - body.XMm / 1000.0), 0, (float)(Route[_waypoint].Z - body.ZMm / 1000.0));
            if (to.Length() > 1.5f)
            {
                _heading = to.Normalized();
                _controller.SteerWorld(_heading, gait, _camera);
                return;
            }
            _waypoint++;
        }
        Hold();
    }

    private void Cast(string formula)
    {
        if (!_controller.Formulas().Contains(formula))
            GD.PushWarning($"UNNAMED showcase: the character does not know {formula}");
        else
            _controller.Cast(formula);
    }

    /// <summary>This frame: null to keep going, a picture's name, "done", or "failed".</summary>
    public string? Update(double delta)
    {
        if (_index < 0)
        {
            // The camera starts behind the way to the first waypoint.
            var body = _controller.Authoritative;
            _heading = new Vector3((float)(Route[0].X - body.XMm / 1000.0), 0, (float)(Route[0].Z - body.ZMm / 1000.0)).Normalized();
            _index = 0;
            _stepStart = 0;
        }
        _clock += delta;
        var step = _steps[_index];
        double t = _clock - _stepStart;
        step.Act(this, t);
        _lastT = t;
        // The camera: behind the heading, turned by the step's angle (90 = beside, 180 = in front).
        float yaw = Mathf.Atan2(-_heading.X, -_heading.Z) + Mathf.DegToRad(step.CameraTurn);
        _camera.Yaw = Mathf.LerpAngle(_camera.Yaw, yaw, (float)Math.Min(1, delta * 3));
        _camera.Pitch = Mathf.Lerp(_camera.Pitch, step.Pitch, (float)Math.Min(1, delta * 3));
        _camera.TargetDistance = step.Distance;
        string? result = null;
        if (!_shot && t >= step.Seconds / 2)
        {
            _shot = true;
            result = $"{_index + 1:00}_{step.Name}";
        }
        if (t >= step.Seconds)
        {
            _index++;
            _stepStart = _clock;
            _shot = false;
            _lastT = -1;
            if (_index >= _steps.Count)
            {
                GD.Print($"UNNAMED showcase: {_steps.Count} steps in {_clock:0.0} s, written to {Directory}");
                return "done";
            }
        }
        return result;
    }
}
