// UNNAMED Presentation - the headless boot smoke (PROTOTYPE.md §6.3 "Boot smoke")
// Godot presentation only

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>godot --headless --path src/Presentation -- --smoke</c>: the project boots, content loads, the world is built and
/// ticks, the player walks to the longhouse through the real command path and opens its door, and a quicksave loads
/// back to the identical state. Exit code 0 on success, 1 on failure; the scratch save profile is removed either way.
/// </summary>
public sealed class Smoke
{
    private static readonly (double X, double Z)[] Route = { (56, 56), (55, 44) };

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly string _profile;
    private int _frames;
    private int _waypoint;
    private bool _asked;

    public Smoke(GameSession session, PlayerController controller, CameraRig camera, string profile)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        _profile = profile;
    }

    /// <summary>Advance one frame; returns the exit code when the smoke is over.</summary>
    public int? Update()
    {
        if (++_frames > 3000)
            return Fail($"timed out at tick {_session.Simulation!.WorldTick}");

        if (_waypoint < Route.Length)
        {
            var body = _controller.Authoritative;
            var (x, z) = Route[_waypoint];
            var to = new Vector3((float)(x - body.XMm / 1000.0), 0, (float)(z - body.ZMm / 1000.0));
            if (to.Length() < 0.3f)
            {
                _waypoint++;
                _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
            }
            else
            {
                _controller.SteerWorld(to.Normalized(), Gait.Run, _camera);
            }
            return null;
        }

        if (!_controller.IsOpen("door.longhouse"))
        {
            if (!_asked && _controller.Focus(_camera) is { } door)
            {
                _controller.Interact(door);
                _asked = true;
            }
            return null;
        }

        var before = _session.Simulation!;
        string digest = before.StateDigest();
        long tick = before.WorldTick;
        _session.Save(SaveSlots.Quick);
        var loaded = _session.Load(SaveSlots.Quick);
        var after = _session.Simulation!;
        if (!loaded.IsComplete || after.StateDigest() != digest || after.WorldTick != tick
            || !after.Doors.Single(d => d.Site.Key == "door.longhouse").Open)
            return Fail($"the quicksave did not load back to the same state (digest {after.StateDigest()} vs {digest}, tick {after.WorldTick} vs {tick})");

        GD.Print($"UNNAMED smoke: PASS - {_frames} frames, world tick {tick}, door opened, save/load digest {digest[..23]}... identical");
        Cleanup();
        return 0;
    }

    private int Fail(string reason)
    {
        GD.PushError($"UNNAMED smoke: FAIL - {reason}");
        Cleanup();
        return 1;
    }

    private void Cleanup()
    {
        if (Directory.Exists(_profile))
            Directory.Delete(_profile, recursive: true);
    }
}
