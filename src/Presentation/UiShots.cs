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
/// real command path, opens it, takes the arrows, and saves a screenshot after each step. Windowed; exit code 0.
/// </summary>
public sealed class UiShots
{
    private static readonly (double X, double Z)[] Route = { (55, 80), (70, 110), (75, 135), (40, 160), (25, 168), (25, 182) };

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly InventoryPanel _inventory;
    private int _frame;
    private int _waypoint;
    private int _step;
    private int _wait;
    private string? _pending;

    public UiShots(GameSession session, PlayerController controller, CameraRig camera, InventoryPanel inventory, string outDirectory)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        _inventory = inventory;
        Directory = outDirectory;
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
                return "done";
        }
        return null;
    }

    private void Then(string shot)
    {
        _step++;
        _pending = shot;
        _wait = 12;
    }
}
