// UNNAMED Presentation - scripted screenshots of the UI, to review it without playing (M3b)
// Godot presentation only

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>godot --path src/Presentation -- --ui-shots &lt;dir&gt;</c>: opens the inventory, walks to the den cache through the
/// real command path, opens it, takes the arrows; then (M3c) walks to the valley strays, fights one to its death, wounds
/// the other and stands until it kills the character. A screenshot after each step, mid-fight, and of the death recap.
/// Windowed; exit code 0, or 1 when a step does not happen.
/// </summary>
public sealed class UiShots
{
    private static readonly (double X, double Z)[] Route = { (55, 80), (70, 110), (75, 135), (40, 160), (25, 168), (25, 182) };

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly InventoryPanel _inventory;
    private static readonly (double X, double Z)[] ToTheStrays = { (40, 160), (75, 135), (90, 108) };

    private int _frame;
    private int _waypoint;
    private int _step;
    private int _wait;
    private long _stepTick;
    private bool _foughtShot;
    private string? _pending;
    private bool _died;

    public UiShots(GameSession session, PlayerController controller, CameraRig camera, InventoryPanel inventory, string outDirectory)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        _inventory = inventory;
        Directory = outDirectory;
        _session.Subscribe<PlayerDied>(_ => _died = true);
    }

    public string Directory { get; }

    /// <summary>Advance one frame. Returns the name of a screenshot to take now, "done" at the end, or null.</summary>
    public string? Update()
    {
        _frame++;
        if (_wait > 0)
        {
            // Let the panel lay out (and the take land) before the picture.
            if (--_wait == 0 && _pending is { } shot)
            {
                _pending = null;
                return shot;
            }
            return null;
        }
        switch (_step)
        {
            case 0 when _frame > 30:
                _inventory.Open(null);
                Then("inventory");
                break;
            case 1:
                _inventory.Close();
                _step++;
                break;
            case 2:
                if (_waypoint >= Route.Length)
                {
                    _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
                    _step++;
                    break;
                }
                var body = _controller.Authoritative;
                var (x, z) = Route[_waypoint];
                var to = new Vector3((float)(x - body.XMm / 1000.0), 0, (float)(z - body.ZMm / 1000.0));
                if (to.Length() < 0.3f)
                    _waypoint++;
                else
                    _controller.SteerWorld(to.Normalized(), Gait.Sprint, _camera);
                _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
                break;
            case 3:
                _inventory.Open("container.den_cache");
                Then("container");
                break;
            case 4:
                var arrows = _session.Simulation!.Containers.Single(c => c.Site.Key == "container.den_cache").Items
                    .First(i => i.DefId == "item.ammo.arrow_rough");
                _session.Submit(new MoveItemCommand(_session.Simulation.PlayerId, arrows.Ref, ItemPlace.In("container.den_cache"), ItemPlace.Carried, arrows.Count));
                Then("after_take");
                break;
            case 5:
                _inventory.Close();
                _waypoint = 0;
                _step++;
                _stepTick = _session.Simulation!.WorldTick;
                break;
            case 6:
                if (Walk(ToTheStrays))
                {
                    _step++;
                    _stepTick = _session.Simulation!.WorldTick;
                }
                break;
            case 7:
                // Fight the nearer stray with the sword until it dies; a picture once blows have been traded.
                var first = Strays().First();
                if (!first.Alive)
                {
                    Then("kill");
                    break;
                }
                Engage(first);
                var combat = _session.Simulation!.Combat;
                if (!_foughtShot && first.Health < first.MaxHealth && combat.Health < combat.MaxHealth)
                {
                    _foughtShot = true;
                    _pending = "combat";
                    _wait = 1;
                }
                return Stalled(1_200, "the first stray did not die");
            case 8:
                // Wound the other stray once, then stand and let it win: the death recap is the picture.
                var second = Strays().FirstOrDefault(s => s.Alive);
                if (second is null)
                    return Fail("the second stray is already dead");
                if (!second.Hostile)
                {
                    Engage(second);
                    return Stalled(600, "the second stray was never wounded");
                }
                _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
                _step++;
                _stepTick = _session.Simulation!.WorldTick;
                break;
            case 9:
                if (_died)
                {
                    Then("death");
                    _wait = 30;
                    break;
                }
                return Stalled(1_800, "the character never died");
            case 10:
                return "done";
        }
        return null;
    }

    private IEnumerable<CreatureView> Strays()
    {
        var body = _controller.Authoritative;
        return _session.Simulation!.Creatures
            .OrderBy(c => Math.Pow(c.Body.XMm - body.XMm, 2) + Math.Pow(c.Body.ZMm - body.ZMm, 2));
    }

    /// <summary>Look at a creature, close to reach, and swing whenever free - through the same commands the keys send.</summary>
    private void Engage(CreatureView creature)
    {
        var body = _controller.Authoritative;
        var to = new Vector3((float)((creature.Body.XMm - body.XMm) / 1000.0), 0, (float)((creature.Body.ZMm - body.ZMm) / 1000.0));
        _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
        var combat = _session.Simulation!.Combat;
        bool inReach = to.Length() <= (combat.Weapon.ReachMm + 300) / 1000f;
        _controller.SteerWorld(inReach ? Vector3.Zero : to.Normalized(), Gait.Run, _camera, faceCamera: true);
        if (inReach && combat.Phase == CombatPhase.Idle)
            _controller.Attack();
    }

    private bool Walk((double X, double Z)[] route)
    {
        if (_waypoint >= route.Length)
        {
            _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
            return true;
        }
        var body = _controller.Authoritative;
        var (x, z) = route[_waypoint];
        var to = new Vector3((float)(x - body.XMm / 1000.0), 0, (float)(z - body.ZMm / 1000.0));
        if (to.Length() < 0.5f)
            _waypoint++;
        else
            _controller.SteerWorld(to.Normalized(), Gait.Run, _camera);
        _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
        return false;
    }

    /// <summary>A step that takes more than this many simulated ticks has failed.</summary>
    private string? Stalled(int ticks, string what) => _session.Simulation!.WorldTick - _stepTick > ticks ? Fail(what) : null;

    private string Fail(string what)
    {
        GD.PushError($"UNNAMED ui shots: {what}");
        return "failed";
    }

    private void Then(string shot)
    {
        _step++;
        _stepTick = _session.Simulation!.WorldTick;
        _pending = shot;
        _wait = 12;
    }
}
