// UNNAMED Presentation - the headless boot smoke (PROTOTYPE.md §6.3 "Boot smoke")
// Godot presentation only

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Magic;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>godot --headless --path src/Presentation -- --smoke</c>: the project boots, content loads, the world is built and
/// ticks, the player walks to the longhouse through the real command path and opens its door; (M3e) walks in to the
/// shelf, takes the book there, reads it and works the first self formula it taught; swings the sword at nothing (M3c:
/// the attack runs its phases and misses); and a quicksave loads back to the identical state. Exit code 0 on success, 1
/// on failure; the scratch save profile is removed either way.
/// </summary>
public sealed class Smoke
{
    private static readonly (double X, double Z)[] Route = { (56, 56), (55, 44) };
    private static readonly (double X, double Z)[] Inside = { (52.5, 44), (41, 45.6) };

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly string _profile;
    private int _frames;
    private int _waypoint;
    private int _inside;
    private bool _asked;
    private bool _taken;
    private bool _read;
    private bool _cast;
    private int _worked = -1;
    private bool _swung;
    private bool _started;
    private bool _missed;

    public Smoke(GameSession session, PlayerController controller, CameraRig camera, string profile)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        _profile = profile;
        _session.Subscribe<AttackStarted>(e => _started |= e.Attacker == _session.Simulation!.PlayerId);
        _session.Subscribe<AttackMissed>(e => _missed |= e.Attacker == _session.Simulation!.PlayerId);
        _session.Subscribe<CastCompleted>(e => _worked = e.Strain);
        _session.Subscribe<CastFizzled>(_ => _cast = false);
    }

    /// <summary>Advance one frame; returns the exit code when the smoke is over.</summary>
    public int? Update()
    {
        if (++_frames > 3000)
        {
            var at = _controller.Authoritative;
            return Fail($"timed out at tick {_session.Simulation!.WorldTick} at ({at.XMm / 1000.0:0.0}, {at.ZMm / 1000.0:0.0}): route {_waypoint}, " +
                        $"door {_controller.IsOpen("door.longhouse")}, inside {_inside}, taken {_taken}, read {_read}, cast {_cast}, worked {_worked}, " +
                        $"swung {_swung}, started {_started}, missed {_missed}");
        }

        if (_waypoint < Route.Length)
        {
            if (Walk(Route[_waypoint]))
                _waypoint++;
            return null;
        }

        if (!_controller.IsOpen("door.longhouse"))
        {
            if (!_asked && _controller.FocusOn(_camera) is { Kind: FocusKind.Door } door)
            {
                _controller.Interact(door.Key);
                _asked = true;
            }
            return null;
        }

        var simulation = _session.Simulation!;
        var magic = _session.Setup.Magic;
        if (_inside < Inside.Length)
        {
            if (Walk(Inside[_inside]))
                _inside++;
            return null;
        }
        if (!_taken)
        {
            // The shelf is whichever container holds a book that teaches; nothing here names it.
            var shelf = simulation.Containers.FirstOrDefault(c => c.Items.Any(i => magic.Teaches.ContainsKey(i.DefId)));
            if (shelf is null)
                return Fail("no container holds a book that teaches");
            var book = shelf.Items.First(i => magic.Teaches.ContainsKey(i.DefId));
            _session.Submit(new MoveItemCommand(simulation.PlayerId, book.Ref, ItemPlace.In(shelf.Site.Key), ItemPlace.Carried, 1));
            _taken = true;
            return null;
        }
        if (!_read)
        {
            if (simulation.Player.Inventory.FirstOrDefault(e => magic.Teaches.ContainsKey(e.DefId)) is not { } carried)
                return Fail("the book was not taken");
            _session.Submit(new UseItemCommand(simulation.PlayerId, carried.ItemId));
            _read = true;
            return null;
        }
        if (_worked < 0)
        {
            // A novice's working may fizzle; then it is worked again once the body is free.
            var formulas = _controller.Formulas();
            if (formulas.Length == 0)
                return Fail("reading the book taught nothing");
            if (!_cast && simulation.Combat.Phase == CombatPhase.Idle)
            {
                _controller.Cast(formulas.First(f => magic.Formulas[f].Targeting == Targeting.Self));
                _cast = true;
            }
            return null;
        }

        if (!_swung)
        {
            int spawned = _session.Setup.Combat.Spawns.Sum(s => s.Members.Length);
            if (simulation.Creatures.Count(c => c.Alive) != spawned)
                return Fail($"expected the {spawned} creatures the spawners place, found {simulation.Creatures.Count(c => c.Alive)} alive");
            if (simulation.Combat.Phase != CombatPhase.Idle)
                return null;
            _controller.Attack();
            _swung = true;
            return null;
        }
        if (!_started || !_missed)
            return null;

        var before = _session.Simulation!;
        string digest = before.StateDigest();
        long tick = before.WorldTick;
        int strain = before.Combat.Strain;
        _session.Save(SaveSlots.Quick);
        var loaded = _session.Load(SaveSlots.Quick);
        var after = _session.Simulation!;
        if (!loaded.IsComplete || after.StateDigest() != digest || after.WorldTick != tick
            || !after.Doors.Single(d => d.Site.Key == "door.longhouse").Open || after.Combat.Strain != strain || strain <= 0)
            return Fail($"the quicksave did not load back to the same state (digest {after.StateDigest()} vs {digest}, tick {after.WorldTick} vs {tick}, strain {after.Combat.Strain} vs {strain})");

        GD.Print($"UNNAMED smoke: PASS - {_frames} frames, world tick {tick}, {after.Creatures.Length} creatures placed, door opened, " +
                 $"{_controller.Formulas().Length} formulas read from a book and one worked (+{_worked} Strain), a swing ran and missed, " +
                 $"save/load digest {digest[..23]}... identical");
        Cleanup();
        return 0;
    }

    /// <summary>One frame of walking towards a point at a run; true once there.</summary>
    private bool Walk((double X, double Z) to)
    {
        var body = _controller.Authoritative;
        var direction = new Vector3((float)(to.X - body.XMm / 1000.0), 0, (float)(to.Z - body.ZMm / 1000.0));
        if (direction.Length() < 0.3f)
        {
            _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
            return true;
        }
        _controller.SteerWorld(direction.Normalized(), Gait.Run, _camera);
        return false;
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
