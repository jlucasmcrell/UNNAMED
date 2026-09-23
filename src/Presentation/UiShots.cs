// UNNAMED Presentation - scripted screenshots of the UI, to review it without playing (M3b)
// Godot presentation only

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Magic;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Ui;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>godot --path src/Presentation -- --ui-shots &lt;dir&gt;</c>: opens the inventory; (M3e) opens the longhouse, takes the
/// book from its shelf, reads it and works a ward, its tell and then its Strain on screen; (M3d) looks at each creature
/// archetype where it lives; walks out to the boar's wallow through the real command path until the boar notices, throws
/// a bolt at it, fights it to its death, mends, searches the carcass and takes what it holds; then walks to the valley
/// strays, wounds one and stands until it kills the character. A screenshot after each step, mid-fight, and of the death
/// recap. Windowed; exit code 0, or 1 when a step does not happen.
/// </summary>
public sealed class UiShots
{
    /// <summary>Each archetype where the content puts it: the spawner, and the picture's name.</summary>
    private static readonly (string Spawner, string Shot)[] Gallery =
    {
        ("spawn.hollow.charwood_hound", "creature_hound"),
        ("spawn.hollow.iron_shelf_husk", "creature_husk"),
        ("spawn.hollow.iron_shelf_armour", "creature_armour"),
        ("spawn.hollow.boar_wallow", "creature_boar"),
        ("spawn.hollow.spider_lair", "creature_spider"),
        ("spawn.hollow.den_pack", "creature_wolves"),
    };

    private static readonly (double X, double Z)[] ToTheDoor = { (56, 56), (55.5, 44) };
    private static readonly (double X, double Z)[] ToTheShelf = { (52.5, 44), (41, 45.6) };
    private static readonly (double X, double Z)[] OutOfTheLonghouse = { (52.5, 44), (56, 44), (56, 56) };
    private static readonly (double X, double Z)[] ToTheWallow = { (55, 70), (40, 74), (32, 73), (25, 72) };
    private static readonly (double X, double Z)[] ToTheStrays = { (40, 78), (60, 88), (88, 102) };
    private const string Boar = "spawn.hollow.boar_wallow#0";

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly InventoryPanel _inventory;

    private int _frame;
    private int _waypoint;
    private int _look;
    private int _step;
    private int _wait;
    private long _stepTick;
    private bool _foughtShot;
    private bool _asked;
    private bool _casting;
    private string? _released;
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
        _session.Subscribe<CastCompleted>(e => _released = e.FormulaId);
        _session.Subscribe<CastFizzled>(e => _released = e.FormulaId);
        _session.Subscribe<CastInterrupted>(e => _released = e.FormulaId);
    }

    public string Directory { get; }

    /// <summary>Where the camera looks from while the gallery runs; null follows the character.</summary>
    public Vector3? Viewpoint { get; private set; }

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
        var simulation = _session.Simulation!;
        var magic = _session.Setup.Magic;
        switch (_step)
        {
            case 0 when _frame > 30:
                _inventory.Open(null);
                Then("inventory");
                break;
            case 1:
                _inventory.Close();
                _waypoint = 0;
                _step++;
                break;
            case 2:
                // To the longhouse, and open its door.
                if (!Walk(ToTheDoor))
                    return Stalled(1_200, "the character never reached the longhouse");
                if (_controller.IsOpen("door.longhouse"))
                {
                    _waypoint = 0;
                    _step++;
                }
                else if (!_asked && _controller.FocusOn(_camera) is { Kind: FocusKind.Door } door)
                {
                    _controller.Interact(door.Key);
                    _asked = true;
                }
                return Stalled(1_400, "the longhouse door never opened");
            case 3:
                if (Walk(ToTheShelf))
                {
                    _inventory.Open(Shelf()?.Site.Key ?? "none");
                    Then("shelf");
                }
                return Stalled(1_200, "the character never reached the shelf");
            case 4:
            {
                var shelf = Shelf();
                if (shelf is null)
                    return Fail("no container holds a book that teaches");
                var book = shelf.Items.First(i => magic.Teaches.ContainsKey(i.DefId));
                _session.Submit(new MoveItemCommand(simulation.PlayerId, book.Ref, ItemPlace.In(shelf.Site.Key), ItemPlace.Carried, 1));
                _step++;
                break;
            }
            case 5:
            {
                if (simulation.Player.Inventory.FirstOrDefault(e => magic.Teaches.ContainsKey(e.DefId)) is not { } book)
                    return Fail("the book was not taken");
                _session.Submit(new UseItemCommand(simulation.PlayerId, book.ItemId));
                _inventory.Close();
                Then("learned");
                break;
            }
            case 6:
                // The ward: a picture in its tell, and one once it holds.
                if (_released is not null)
                {
                    Then("warded");
                    _casting = false;
                    _released = null;
                    _waypoint = 0;
                    break;
                }
                if (!_casting && simulation.Combat.Phase == CombatPhase.Idle)
                {
                    _controller.Cast(_controller.Formulas().First(f => magic.Formulas[f].Targeting == Targeting.Self));
                    _casting = true;
                }
                else if (_casting && simulation.Combat.Casting is not null && simulation.Combat.Phase == CombatPhase.Windup && _pending is null)
                {
                    _pending = "casting";
                    _wait = 1;
                }
                return Stalled(200, "the ward was never worked");
            case 7:
                if (Walk(OutOfTheLonghouse))
                {
                    _step++;
                    _stepTick = simulation.WorldTick;
                }
                return Stalled(1_200, "the character never left the longhouse");
            case 8:
                // The gallery: stand the camera a few metres in front of each archetype and let it settle.
                if (_look >= Gallery.Length)
                {
                    Viewpoint = null;
                    _waypoint = 0;
                    _step++;
                    _stepTick = simulation.WorldTick;
                    break;
                }
                var (spawner, name) = Gallery[_look++];
                var subject = simulation.Creatures.First(c => c.Key.StartsWith(spawner + "#", StringComparison.Ordinal));
                double facing = subject.Body.FacingMdeg / 1000.0 * Math.PI / 180;
                var at = new Vector3((float)(subject.Body.XMm / 1000.0 + Math.Sin(facing) * 2.5), subject.Body.YMm / 1000f,
                    (float)(subject.Body.ZMm / 1000.0 + Math.Cos(facing) * 2.5));
                Viewpoint = at;
                _camera.Yaw = Mathf.Atan2(-(subject.Body.XMm / 1000f - at.X), -(subject.Body.ZMm / 1000f - at.Z));
                _pending = name;
                _wait = 20;
                break;
            case 9:
                // Out through the gate to the boar's wallow, then straight at the boar, until it notices: a wandering
                // boar facing away may not see the character arrive, but it hears a run inside 8 m.
                var wallow = Boarish()!;
                if (wallow.Mind != CreatureMind.Unaware)
                {
                    _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
                    _pending = "noticed";
                    _wait = 2;
                    _step++;
                    _stepTick = simulation.WorldTick;
                    break;
                }
                if (Walk(ToTheWallow))
                {
                    var toward = ToCreature(wallow);
                    _controller.SteerWorld(toward.Normalized(), Gait.Run, _camera);
                    _camera.Yaw = Mathf.Atan2(-toward.X, -toward.Z);
                }
                return Stalled(1_200, "the boar never noticed the character");
            case 10:
            {
                // A bolt at the boar before it closes: face it, and work the projectile the book taught.
                var target = ToCreature(Boarish()!);
                _camera.Yaw = Mathf.Atan2(-target.X, -target.Z);
                _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera, faceCamera: true);
                if (_released is not null)
                {
                    Then("bolt");
                    _casting = false;
                    _released = null;
                    break;
                }
                if (!_casting && simulation.Combat.Phase == CombatPhase.Idle)
                {
                    _controller.Cast(_controller.Formulas().First(f => magic.Formulas[f].Targeting == Targeting.Projectile));
                    _casting = true;
                }
                return Stalled(200, "the bolt was never worked");
            }
            case 11:
                var boar = Boarish()!;
                if (!boar.Alive)
                {
                    Then("kill");
                    _waypoint = 0;
                    break;
                }
                if (_died)
                    return Fail("the boar killed the character");
                Engage(boar);
                var combat = simulation.Combat;
                if (!_foughtShot && boar.Health < boar.MaxHealth && combat.Health < combat.MaxHealth)
                {
                    _foughtShot = true;
                    _pending = "combat";
                    _wait = 1;
                }
                return Stalled(1_600, "the boar did not die");
            case 12:
                // Mend after the fight: the last self formula the book taught.
                if (_released is not null)
                {
                    Then("mending");
                    _casting = false;
                    _released = null;
                    break;
                }
                if (!_casting && simulation.Combat.Phase == CombatPhase.Idle)
                {
                    _controller.Cast(_controller.Formulas().Last(f => magic.Formulas[f].Targeting == Targeting.Self));
                    _casting = true;
                }
                return Stalled(400, "the mending was never worked");
            case 13:
                // Up to the carcass; facing it, the prompt offers to search it.
                var carcass = Boarish()!;
                if (Walk(new[] { (carcass.Body.XMm / 1000.0, carcass.Body.ZMm / 1000.0 - 1.0) }) || Close(carcass, 1.4))
                {
                    _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
                    var to = ToCreature(carcass);
                    _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
                    if (_controller.FocusOn(_camera) is not { Kind: FocusKind.Container } focus || focus.Key != carcass.CorpseKey)
                        return Stalled(300, "the carcass never came into focus");
                    Then("corpse_prompt");
                }
                break;
            case 14:
                _inventory.Open(Boarish()!.CorpseKey);
                Then("corpse");
                break;
            case 15:
                // Take everything: the emptied carcass is gone, and the panel's container column closes.
                string key = Boarish()!.CorpseKey;
                foreach (var item in simulation.Containers.Single(c => c.Site.Key == key).Items)
                    _session.Submit(new MoveItemCommand(simulation.PlayerId, item.Ref, ItemPlace.In(key), ItemPlace.Carried, item.Count));
                Then("after_take");
                break;
            case 16:
                if (Boarish()!.Condition != CreatureCondition.Gone)
                    return Fail("the emptied carcass is still there");
                _inventory.Close();
                _waypoint = 0;
                _step++;
                _stepTick = simulation.WorldTick;
                break;
            case 17:
                if (Walk(ToTheStrays))
                {
                    _step++;
                    _stepTick = simulation.WorldTick;
                }
                return Stalled(1_600, "the character never reached the strays");
            case 18:
                // Wound the nearer stray once, then stand and let it win: the death recap is the picture.
                var stray = Strays().First();
                if (stray.Health == stray.MaxHealth)
                {
                    Engage(stray);
                    return Stalled(800, "no stray was ever wounded");
                }
                _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
                _step++;
                _stepTick = simulation.WorldTick;
                break;
            case 19:
                if (_died)
                {
                    Then("death");
                    _wait = 30;
                    break;
                }
                return Stalled(1_800, "the character never died");
            case 20:
                return "done";
        }
        return null;
    }

    /// <summary>The container holding a book that teaches; nothing here names it.</summary>
    private ContainerView? Shelf() =>
        _session.Simulation!.Containers.FirstOrDefault(c => c.Items.Any(i => _session.Setup.Magic.Teaches.ContainsKey(i.DefId)));

    private CreatureView? Boarish() => _session.Simulation!.Creatures.FirstOrDefault(c => c.Key == Boar);

    private IEnumerable<CreatureView> Strays()
    {
        var body = _controller.Authoritative;
        return _session.Simulation!.Creatures
            .Where(c => c.Alive && c.Key.StartsWith("spawn.hollow.valley_strays#", StringComparison.Ordinal))
            .OrderBy(c => Math.Pow(c.Body.XMm - body.XMm, 2) + Math.Pow(c.Body.ZMm - body.ZMm, 2));
    }

    private Vector3 ToCreature(CreatureView creature) =>
        new((float)((creature.Body.XMm - _controller.Authoritative.XMm) / 1000.0), 0, (float)((creature.Body.ZMm - _controller.Authoritative.ZMm) / 1000.0));

    private bool Close(CreatureView creature, double metres) => ToCreature(creature).Length() <= metres;

    /// <summary>Look at a creature, close to reach, and swing whenever free - through the same commands the keys send.</summary>
    private void Engage(CreatureView creature)
    {
        var to = ToCreature(creature);
        _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
        var combat = _session.Simulation!.Combat;
        long radius = _session.Setup.Combat.Creatures[creature.DefId].RadiusMm;
        bool inReach = to.Length() <= (combat.Weapon.ReachMm + radius - 150) / 1000f;
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
