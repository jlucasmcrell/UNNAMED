// UNNAMED Presentation - scripted screenshots of the UI, to review it without playing (M3b)
// Godot presentation only

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Magic;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Ui;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>godot --path src/Presentation -- --ui-shots &lt;dir&gt;</c>: opens the inventory; opens the longhouse and (M4) greets Renn,
/// then talks with Sel until she lends the primer; (M3e) reads it and works a ward, its tell and then its Strain on screen;
/// (M3d) looks at each creature
/// archetype where it lives; walks out to the boar's wallow through the real command path until the boar notices, throws
/// a bolt at it if the character knows one, fights it to its death, mends, searches the carcass and takes what it holds; (M3f) walks up to the iron
/// seam and strikes it until it is worked out, crosses to the ash stand and cuts a haft - fighting whatever hunts the
/// character on the way - comes home to the forge shed, (M4) asks Kera to teach the forge - (M5) which starts Iron Under Ash:
/// the journal and the quest debugger on screen - smelts a billet at the hearth, makes the spear at the anvil and takes it in
/// hand, (M4) trades the old sword to Kera for arrows, (M5) shows her the spear and completes the quest, and wounds a valley stray
/// with the spear; then walks up to the wolves' den, wounds one of the pack and stands
/// until the pack kills the character (`PROTOTYPE.md` §5 step 7's deliberate death: a wounded stray only flees, and a
/// character fighting back with the spear has cleared the den). A screenshot after each step, mid-fight, and of the death
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

    // M6's layout (the content bible's four cells): the lodge by the waystone, Sel's table outside it, the boar's wallow on
    // Blackvein's north-west rim.
    private static readonly (double X, double Z)[] ToTheDoor = Playthrough.Hollow((44, 138), (54.5, 134), (53.5, 128));
    private static readonly (double X, double Z)[] OutOfTheLonghouse = Playthrough.Hollow((50.5, 128), (53.5, 128), (60, 126));
    private static readonly (double X, double Z)[] ToTheWallow = Playthrough.Hollow((60, 118), (40, 104), (36, 92), (25, 78));
    private const string Boar = "spawn.hollow.boar_wallow#0";

    // M3f's loop, in M6's layout: down from the wallow into Blackvein Cut to the seam on its floor, struck from its south-west
    // face; up the quarry ramp and east through Charwood to the ash stand; home to the smithy's door. Each place to work is
    // walked to closely (Near): the reach is 1.6 m from the body.
    private const float Near = 0.2f;
    private static readonly (double X, double Z)[] ToTheSeam = Playthrough.Hollow((20, 62), (22, 50), (27.2, 44.2));
    private static readonly (double X, double Z)[] ToTheStand = Playthrough.Hollow((27, 50), (46, 60), (54, 74), (60, 90), (64, 104), (80, 118), (100, 125), (120, 135),
        (150, 140), (179, 138.3));
    private static readonly (double X, double Z)[] ToTheForgeDoor = Playthrough.Hollow((150, 140), (120, 140), (100, 142), (88, 145), (70, 135), (51.8, 136), (51.8, 142));
    private static readonly (double X, double Z)[] ToTheHearth = Playthrough.Hollow((54.5, 142), (59.5, 143.2));
    private static readonly (double X, double Z)[] ToTheAnvil = Playthrough.Hollow((58.2, 141.4));
    private static readonly (double X, double Z)[] OutOfTheForge = Playthrough.Hollow((54.5, 142), (51.8, 142), (47, 138));
    private static readonly (double X, double Z)[] ToTheStrays = Playthrough.Hollow((60, 128), (90, 124), (106, 126));
    private static readonly (double X, double Z)[] ToTheDen = Playthrough.Hollow((110, 140), (100, 158), (104, 166), (112, 170));

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly InventoryPanel _inventory;
    private readonly DialoguePanel _dialogue;
    private readonly JournalPanel _journal;
    private readonly QuestDebugPanel _questDebug;

    private int _frame;
    private int _waypoint;
    private (double X, double Z)[]? _way;
    private string? _wayTo;
    private Body _wayFrom = new(0, 0, 0, 0);
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
    private bool _prompted;
    private bool _mending;
    private long _mendTick = -10_000;
    private int _gathersAsked;
    private int _gathered;
    private int _craftsAsked;
    private int _crafted;
    private string? _made;
    private bool _sold;
    private bool _bought;
    private int _people;
    private string? _hireKey;
    private int _learnSizes;

    /// <summary>
    /// The inventory's own screens (inventory organisation), pictured first with <c>--pack-shots</c> over a profile's save (<c>--ui-shots-continue</c>
    /// with <c>--profile</c>) that stands the character where each is in reach: the pack, a stash, a bench working from the stash, a trader.
    /// </summary>
    private static readonly string[] PackScreens = { "inventory", "inventory_stash", "inventory_station", "inventory_trade", "" };

    /// <summary>
    /// The management screens pictured first, each opened by name through Main (the Field Instrument UI); "" closes them. Then a screen
    /// chosen on the System Menu, and Escape pressed on it: back to play, or back to the menu with <c>--menu-escape back</c>.
    /// </summary>
    private static readonly string[] Screens = (System.Environment.GetCommandLineArgs().Contains("--pack-shots") ? PackScreens : Array.Empty<string>()).Concat(new[]
    {
        "hud_stack", "scale_hud_stack", "large_hud_stack", "bars", "scale_hud_stack_bars", "large_hud_stack_bars", "subtitles", "large_subtitles", "compass", "", "bars_side", "", "menu", "character", "skills", "skills_formulas", "skills_recipes", "skills_map", "journal", "effects", "party", "chat", "chat_groups", "reputation", "away", "map",
        "large_menu", "large_reputation", "large_away", "large_map", "large_party", "large_chat", "text_menu", "text_reputation", "text_away", "text_map", "text_party", "text_chat", "help", "help_search", "saves",
        "settings", "settings_audio", "settings_controls", "settings_camera", "settings_interface", "credits", "report", "report_written", "",
        "announce", "announce_skill", "menu_character", "escape", "",
    }).ToArray();


    /// <summary>
    /// The screens pictured again once the route has filled them ("late_&lt;after&gt;_&lt;screen&gt;"): after the ward (an effect on, what is
    /// known) and after the trade (a quest, the spear and coin, and often a point to spend).
    /// </summary>
    private static readonly Dictionary<string, string[]> LatePasses = new(StringComparer.Ordinal)
    {
        ["warded"] = new[] { "effects", "character", "skills", "skills_formulas", "" },
        ["traded"] = new[] { "menu", "character", "skills", "skills_recipes", "skills_map", "inventory", "journal", "effects", "party", "chat", "reputation", "map",
            "large_menu", "large_reputation", "large_away", "large_map", "large_party", "large_chat", "text_menu", "text_reputation", "text_party", "text_chat", "" },
    };

    private readonly Action<string>? _screen;
    private readonly Queue<string> _late = new();
    private string _latePrefix = string.Empty;
    private int _screens;

    public UiShots(GameSession session, PlayerController controller, CameraRig camera, InventoryPanel inventory, DialoguePanel dialogue,
        JournalPanel journal, QuestDebugPanel questDebug, string outDirectory, Action<string>? screen = null)
    {
        _screen = screen;
        _session = session;
        _controller = controller;
        _camera = camera;
        _inventory = inventory;
        _dialogue = dialogue;
        _journal = journal;
        _questDebug = questDebug;
        Directory = outDirectory;
        _session.Subscribe<PlayerDied>(_ => _died = true);
        _session.Subscribe<CastCompleted>(e => _released = e.FormulaId);
        _session.Subscribe<CastFizzled>(e => _released = e.FormulaId);
        _session.Subscribe<CastInterrupted>(e => _released = e.FormulaId);
        _session.Subscribe<NodeGathered>(_ => _gathered++);
        _session.Subscribe<ItemCrafted>(e =>
        {
            _crafted++;
            _made = e.ItemId;
        });
        _session.Subscribe<ItemSold>(_ => _sold = true);
        _session.Subscribe<ItemBought>(_ => _bought = true);
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
                if (_screen is not null && LatePasses.TryGetValue(shot, out var pass))
                {
                    foreach (string late in pass)
                        _late.Enqueue(late);
                    _latePrefix = $"late_{shot}_";
                }
                return shot;
            }
            return null;
        }
        if (_screen is not null && _late.Count > 0)
        {
            string name = _late.Dequeue();
            _screen(name);
            if (name.Length > 0)
            {
                _pending = _latePrefix + name;
                _wait = 12;
            }
            return null;
        }
        if (_screen is not null && _frame > 30 && _screens < Screens.Length)
        {
            string name = Screens[_screens++];
            _screen(name);
            if (name.Length > 0)
            {
                _pending = "screen_" + name;
                _wait = name.StartsWith("announce", StringComparison.Ordinal) ? 40 : 12;
            }
            return null;
        }
        var simulation = _session.Simulation!;
        var magic = _session.Setup.Magic;
        if (_step == 5 && _screen is not null && _people < 2 * PeopleShots)
            return People(simulation);
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
                // A word with the steward on the way in: his greeting, and the panel it is on.
                if (Approach(Renn))
                {
                    _controller.Talk(Renn);
                    Then("renn");
                }
                return Stalled(1_200, "the character never reached Renn");
            case 4:
                // Sel keeps her books at her survey table outside (M6's layout): out of the lodge to her.
                if (_dialogue.Visible)
                    _dialogue.Leave();
                if (Walk(OutOfTheLonghouse))
                    Next();
                return Stalled(1_200, "the character never left the longhouse");
            case 5:
                // She lends the primer when asked.
                if (Approach(Sel))
                {
                    _controller.Talk(Sel);
                    Then("sel");
                }
                return Stalled(1_200, "the character never reached Sel");
            case 6:
                _dialogue.Answer("books");
                Then("sel_primer");
                break;
            case 7:
                if (!_asked)
                {
                    _dialogue.Answer("take");
                    _asked = true;
                }
                if (simulation.Player.Inventory.Any(e => magic.Teaches.ContainsKey(e.DefId)))
                {
                    _dialogue.Leave();
                    Next();
                }
                return Stalled(200, "Sel never lent the primer");
            case 8:
            {
                if (simulation.Player.Inventory.FirstOrDefault(e => magic.Teaches.ContainsKey(e.DefId)) is not { } book)
                    return Fail("the book was not taken");
                _session.Submit(new UseItemCommand(simulation.PlayerId, book.ItemId));
                _inventory.Close();
                Then("learned");
                break;
            }
            case 9:
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
            case 10:
                // (M3's longhouse held Sel, so the character left it here; in M6's layout it already has.)
                _step++;
                _stepTick = simulation.WorldTick;
                break;
            case 11:
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
                // A creature the content no longer spawns where the gallery expects it is left out of the gallery, not the run.
                if (simulation.Creatures.FirstOrDefault(c => c.Key.StartsWith(spawner + "#", StringComparison.Ordinal)) is not { } subject)
                    break;
                double facing = subject.Body.FacingMdeg / 1000.0 * Math.PI / 180;
                var at = new Vector3((float)(subject.Body.XMm / 1000.0 + Math.Sin(facing) * 2.5), subject.Body.YMm / 1000f,
                    (float)(subject.Body.ZMm / 1000.0 + Math.Cos(facing) * 2.5));
                Viewpoint = at;
                _camera.Yaw = Mathf.Atan2(-(subject.Body.XMm / 1000f - at.X), -(subject.Body.ZMm / 1000f - at.Z));
                _pending = name;
                _wait = 20;
                break;
            case 12:
                // Down the road and west along Blackvein's rim to the boar's wallow, then straight at the boar, until it notices: a wandering
                // boar facing away may not see the character arrive, but it hears a run inside 8 m.
                // Without the boar (content that spawns none at the wallow) the fight is skipped, and the run goes on to the seam.
                if (Boarish() is not { } wallow)
                {
                    _step = 20;
                    _waypoint = 0;
                    break;
                }
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
            case 13:
            {
                // A bolt at the boar before it closes: face it, and work a projectile the character knows. Sel's notes teach only a ward,
                // so a character who knows none opens the fight with the blade.
                if (!_controller.Formulas().Any(f => magic.Formulas[f].Targeting == Targeting.Projectile))
                {
                    Next();
                    break;
                }
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
            case 14:
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
            case 15:
            {
                // Mend after the fight: a self formula that lifts an effect when the character knows one, or else what the kit carries to use.
                if (_released is not null)
                {
                    Then("mending");
                    _casting = false;
                    _released = null;
                    break;
                }
                string? mending = _controller.Formulas().LastOrDefault(f => magic.Formulas[f].Targeting == Targeting.Self && !magic.Formulas[f].Removes.IsEmpty);
                if (mending is null && !_casting)
                {
                    if (_controller.UseConsumable())
                        Then("mending");
                    else
                        Next();
                    break;
                }
                if (!_casting && simulation.Combat.Phase == CombatPhase.Idle)
                {
                    _controller.Cast(mending!);
                    _casting = true;
                }
                return Stalled(400, "the mending was never worked");
            }
            case 16:
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
            case 17:
                _inventory.Open(Boarish()!.CorpseKey);
                Then("corpse");
                break;
            case 18:
                // Take everything: the emptied carcass is gone, and the panel's container column closes.
                string key = Boarish()!.CorpseKey;
                foreach (var item in simulation.Containers.Single(c => c.Site.Key == key).Items)
                    _session.Submit(new MoveItemCommand(simulation.PlayerId, item.Ref, ItemPlace.In(key), ItemPlace.Carried, item.Count));
                Then("after_take");
                break;
            case 19:
                if (Boarish()!.Condition != CreatureCondition.Gone)
                    return Fail("the emptied carcass is still there");
                _inventory.Close();
                Next();
                break;
            case 20:
                if (Travel(ToTheSeam, Near))
                    Next();
                return Survived("the iron seam") ?? Stalled(5_000, "the character never reached the iron seam");
            case 21:
            {
                // Face the seam - a picture with its prompt - then strike it, one strike at a time, until it is worked out.
                var seam = NodeNamed("iron_seam");
                if (Defend())
                    return Survived("the iron seam") ?? Stalled(3_000, "the fight at the seam never ended");
                Face(seam.XMm, seam.ZMm);
                if (!_prompted)
                {
                    _prompted = true;
                    _pending = "seam";
                    _wait = 12;
                    break;
                }
                if (_gathersAsked == _gathered)
                {
                    if (!seam.Ready)
                    {
                        Then("seam_worked");
                        break;
                    }
                    _controller.Gather(seam.Key);
                    _gathersAsked++;
                }
                return Stalled(3_000, "the seam was never worked out");
            }
            case 22:
                if (Travel(ToTheStand, Near))
                    Next();
                return Survived("the ash stand") ?? Stalled(6_000, "the character never reached the ash stand");
            case 23:
            {
                var stand = NodeNamed("ash_stand");
                if (Defend())
                    return Survived("the ash stand") ?? Stalled(3_000, "the fight at the stand never ended");
                Face(stand.XMm, stand.ZMm);
                if (_gathersAsked == _gathered)
                {
                    if (!stand.Ready)
                    {
                        Then("stand_cut");
                        break;
                    }
                    _controller.Gather(stand.Key);
                    _gathersAsked++;
                }
                return Stalled(3_000, "no haft was ever cut");
            }
            case 24:
                // Home through Charwood to the smithy, and in at its door.
                if (Travel(ToTheForgeDoor))
                {
                    if (_controller.IsOpen("door.forge_shed"))
                    {
                        Next();
                        break;
                    }
                    Face(453_200, 742_000);
                    if (!_asked && _controller.FocusOn(_camera) is { Kind: FocusKind.Door, Key: "door.forge_shed" } door)
                    {
                        _controller.Interact(door.Key);
                        _asked = true;
                    }
                }
                return Survived("the forge shed") ?? Stalled(8_000, "the character never got into the forge shed");
            case 25:
                // Nobody starts knowing the forge (M4): ask Kera.
                if (Approach(Kera))
                {
                    _controller.Talk(Kera);
                    Then("kera");
                }
                return Stalled(1_200, "the character never reached Kera");
            case 26:
                // Her Learn column beside the conversation at text 2x, then at the largest interface, then back to as it was.
                if (_screen is not null && _learnSizes < 3)
                {
                    string size = new[] { "text_", "large_", "" }[_learnSizes++];
                    _screen(size);
                    if (size.Length > 0)
                    {
                        _pending = $"kera_{size.TrimEnd('_')}";
                        _wait = 12;
                    }
                    break;
                }
                _dialogue.Answer("teach");
                Then("lesson");
                break;
            case 27:
                if (_dialogue.Visible)
                    _dialogue.Leave();
                Next();
                break;
            case 28:
                // The quest the lesson started (M5): the ore and the shelf are behind the character already (bible §32), so it
                // asks for a billet now - the tracker at the top right says so, and the journal and the quest debugger say more.
                _journal.Visible = true;
                _journal.Refresh(_session);
                Then("journal");
                break;
            case 29:
                _journal.Visible = false;
                // The quest debugger's picture only where the content lets it diagnose (the restored Oct 2 content does not).
                try
                {
                    _questDebug.Visible = true;
                    _questDebug.Refresh(_session, 0, now: true);
                    Then("quest_debug");
                }
                catch (InvalidOperationException e)
                {
                    GD.PushWarning($"UNNAMED ui shots: the quest debugger could not diagnose ({e.Message}); no picture of it");
                    _questDebug.Visible = false;
                    Next();
                }
                break;
            case 30:
                _questDebug.Visible = false;
                if (Walk(ToTheHearth, Near) && AtStation("forge") is { } hearth)
                {
                    _inventory.OpenAt(hearth);
                    Then("hearth");
                }
                return Stalled(1_200, "the hearth never came into focus");
            case 31:
                // Smelt one billet: the panel's Make button submits this same command.
                if (_craftsAsked == _crafted)
                {
                    if (_crafted > 0)
                    {
                        Then("smelted");
                        break;
                    }
                    _controller.Craft(_controller.Recipes("forge").First().Id);
                    _craftsAsked++;
                }
                return Stalled(200, "no billet was smelted");
            case 32:
                _inventory.Close();
                if (Walk(ToTheAnvil, Near) && AtStation("anvil") is { } anvil)
                {
                    _inventory.OpenAt(anvil);
                    Then("anvil");
                }
                return Stalled(1_200, "the anvil never came into focus");
            case 33:
                if (_craftsAsked == _crafted)
                {
                    if (_crafted > 1)
                    {
                        Then("forged");
                        break;
                    }
                    _controller.Craft(_controller.Recipes("anvil").First().Id);
                    _craftsAsked++;
                }
                return Stalled(200, "no spear was made");
            case 34:
            {
                // Take the spear in hand - the panel's Equip button submits this same command - and carry it outside.
                if (simulation.Combat.Weapon.Source != _made)
                {
                    if (!_asked && simulation.Player.Inventory.FirstOrDefault(e => e.DefId == _made) is { } spear)
                    {
                        _session.Submit(new EquipCommand(simulation.PlayerId, spear.ItemId));
                        _asked = true;
                    }
                    return Stalled(200, "the spear was never taken in hand");
                }
                _inventory.Close();
                Next();
                break;
            }
            case 35:
                // Back to Kera to trade: her wares open from her conversation, the panel's own buttons submit the trades.
                if (_inventory.OpenTrader == Kera)
                {
                    _asked = false;
                    Then("trade");
                    break;
                }
                if (Approach(Kera) && !_asked)
                {
                    _controller.Talk(Kera);
                    _dialogue.Answer("trade");
                    _asked = true;
                }
                return Stalled(1_200, "Kera's wares never opened");
            case 36:
            {
                // The old sword, now the spear is in hand, for as many arrows as it fetches.
                if (!_sold)
                {
                    if (!_asked && simulation.Player.Inventory.FirstOrDefault(e => e.DefId == OldSword) is { } sword)
                    {
                        _session.Submit(new SellCommand(simulation.PlayerId, Kera, sword.ItemId, 1));
                        _asked = true;
                    }
                    return Stalled(200, "the old sword was never sold");
                }
                if (!_bought)
                {
                    var arrows = simulation.Wares(Kera)?.Wares.FirstOrDefault(w => w.ItemId == Arrows);
                    if (arrows is null || arrows.Price > simulation.Player.Currency)
                        return Fail("no arrows the coin could buy");
                    if (_asked)
                    {
                        _session.Submit(new BuyCommand(simulation.PlayerId, Kera, arrows.Ref,
                            (int)Math.Min(arrows.Count, simulation.Player.Currency / arrows.Price)));
                        _asked = false;
                    }
                    return Stalled(200, "no arrows were bought");
                }
                Then("traded");
                break;
            }
            case 37:
                // Iron Under Ash's last step (M5): show Kera the spear - the reply on offer depends on how the spear came out.
                _inventory.Close();
                if (!_asked)
                {
                    _controller.Talk(Kera);
                    _asked = true;
                    break;
                }
                if (_dialogue.Visible && _dialogue.Replies.FirstOrDefault(r => r.StartsWith("show", StringComparison.Ordinal)) is { } show)
                {
                    _dialogue.Answer(show);
                    Then("shown");
                    break;
                }
                return Stalled(200, "Kera was never shown the spear");
            case 38:
                if (_dialogue.Visible)
                    _dialogue.Leave();
                if (simulation.Quests.Any(q => q.Status == QuestStatus.Completed))
                {
                    Then("quest_done");
                    break;
                }
                return Stalled(200, "the quest never completed");
            case 39:
                _journal.Visible = true;
                _journal.Refresh(_session);
                Then("journal_done");
                break;
            case 40:
                _inventory.Close();
                _journal.Visible = false;
                if (Walk(OutOfTheForge))
                {
                    _camera.Yaw = PlayerController.FacingRadians(_controller.Authoritative.FacingMdeg) + Mathf.Pi / 4;
                    Then("spear");
                }
                return Stalled(1_200, "the character never came out of the forge shed");
            case 41:
                _waypoint = 0;
                Next();
                break;
            case 42:
                if (Travel(ToTheStrays))
                    Next();
                return Survived("the strays") ?? Stalled(3_000, "the character never reached the strays");
            case 43:
            {
                // Wound the nearer stray with the spear: a picture of the thrust as it lands.
                var stray = Strays().First();
                if (stray.Health == stray.MaxHealth)
                {
                    Engage(stray);
                    return Stalled(800, "no stray was ever wounded");
                }
                _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
                Then("thrust");
                _wait = 1;
                break;
            }
            case 44:
                // Up to the den mouth, past whatever the wounded stray does, to take on the pack: the death recap is the
                // last picture.
                if (Walk(ToTheDen))
                    Next();
                return Survived("the den") ?? Stalled(2_400, "the character never reached the den");
            case 45:
                if (_died)
                {
                    Then("death");
                    _wait = 30;
                    break;
                }
                var pack = simulation.Creatures.Where(c => c.Alive && c.Key.StartsWith("spawn.hollow.den_pack#", StringComparison.Ordinal)).ToList();
                if (pack.All(c => c.Health == c.MaxHealth) && pack.OrderBy(c => ToCreature(c).Length()).FirstOrDefault() is { } wolf)
                    Engage(wolf);
                else
                    _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
                return Stalled(1_800, $"the character never died (health {simulation.Combat.Health}/{simulation.Combat.MaxHealth}; " +
                    string.Join(", ", simulation.Creatures.Where(c => c.Alive && ToCreature(c).Length() < 30)
                        .Select(c => $"{c.Key} {c.Mind} {c.Health}/{c.MaxHealth} at {ToCreature(c).Length():0.0} m")) + ")");
            case 46:
                return "done";
        }
        return null;
    }

    /// <summary>Per person hired: the prompt on them, the hiring sheet at standard size, at text 2x, at the largest interface, then closed.</summary>
    private const int PeopleShots = 5;

    /// <summary>
    /// The hiring sheet (PR #24), opened as interacting opens it, once out of the longhouse: at the nearest resident who hires on, then at
    /// the nearest adventurer in town, each with the prompt and its context line on them first. Someone the world does not hold is left out of the pictures, not the run.
    /// </summary>
    private string? People(Simulation simulation)
    {
        if (_dialogue.Visible)
            _dialogue.Leave();
        int phase = _people % PeopleShots;
        string who = _people < PeopleShots ? "resident" : "adventurer";
        if (phase == 0)
        {
            var adventurers = simulation.Adventurers.Select(a => a.Key).ToHashSet(StringComparer.Ordinal);
            var body = _controller.Authoritative;
            _hireKey ??= simulation.Npcs
                .Where(n => adventurers.Contains(n.Id) == (who == "adventurer") && simulation.Hiring.Hireability(n.Id) is { } hire
                    && (hire.Hireable || who == "adventurer"))
                .OrderByDescending(n => simulation.Hiring.Hireability(n.Id)!.Hireable)
                .ThenBy(n => Math.Pow(n.Body.XMm - body.XMm, 2) + Math.Pow(n.Body.ZMm - body.ZMm, 2))
                .FirstOrDefault()?.Id;
            if (_hireKey is null)
            {
                GD.PushWarning($"UNNAMED ui shots: no {who} to hire; no picture of hiring one");
                _people += PeopleShots;
                _stepTick = simulation.WorldTick;
                return null;
            }
            if (!Approach(_hireKey))
                return Stalled(2_400, $"the character never reached {_hireKey} to hire");
            _pending = $"hire_{who}_prompt";
        }
        else if (phase < 4)
        {
            string size = new[] { "", "", "text_", "large_" }[phase];
            _screen!(size + "hire:" + _hireKey);
            _pending = $"hire_{who}{(size.Length > 0 ? "_" + size.TrimEnd('_') : "")}";
        }
        else
        {
            _screen!("");
            _hireKey = null;
            _waypoint = 0;
            _stepTick = simulation.WorldTick;
        }
        _wait = _pending is null ? 0 : 12;
        _people++;
        return null;
    }

    private void Next()
    {
        _step++;
        _stepTick = _session.Simulation!.WorldTick;
        _waypoint = 0;
        _asked = false;
    }

    private NodeView NodeNamed(string name) => _session.Simulation!.Nodes.Single(n => n.Name == name);

    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Sel = "npc.ashen_hollow.sel_arien";
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string OldSword = "item.weapon.rusted_sword";
    private const string Arrows = "item.ammo.arrow_rough";

    /// <summary>
    /// Walk up to within a hand's reach of an NPC, from wherever the character stands, wherever their schedule has them (WP12), and face them.
    /// </summary>
    private bool Approach(string npcId)
    {
        var npc = _session.Simulation!.Npcs.Single(n => n.Id == npcId);
        var body = _controller.Authoritative;
        if (_way is null || _wayTo != npcId || Math.Abs(_wayFrom.XMm - npc.Body.XMm) + Math.Abs(_wayFrom.ZMm - npc.Body.ZMm) > 1_000)
        {
            // Round walls and in through doors (Playthrough.WayTo): planned once, again only if they moved.
            (_way, _wayTo, _wayFrom, _waypoint) = (Playthrough.WayTo(_session.Simulation!, body, npc.Body), npcId, npc.Body, 0);
        }
        if (!_controller.InTalkReach(npc) && !Walk(_way, Near))
            return false;
        // Stand still in front of them: reach can come mid-stride, and a conversation or sheet closes once its person is out of reach.
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
        _way = null;
        Face(npc.Body.XMm, npc.Body.ZMm);
        return true;
    }

    /// <summary>Turn the camera to a point, so what is there comes into focus.</summary>
    private void Face(long xMm, long zMm)
    {
        var body = _controller.Authoritative;
        _camera.Yaw = Mathf.Atan2(-(xMm - body.XMm) / 1000f, -(zMm - body.ZMm) / 1000f);
    }

    /// <summary>The station of this kind the character faces within reach, found the way the E key finds it.</summary>
    private StationSite? AtStation(string kind)
    {
        var station = _session.Setup.Layout.Stations.First(s => s.Kind == kind);
        Face(station.XMm, station.ZMm);
        return _controller.FocusOn(_camera) is { Kind: FocusKind.Station } focus && focus.Key == station.Key ? station : null;
    }

    /// <summary>
    /// Walk a route - but first fight whatever hunts the character (the loop's walks cross the husk's shelf and the east
    /// pack's woods), and mend between fights. True once at the route's end.
    /// </summary>
    private bool Travel((double X, double Z)[] route, float arrive = 0.5f) => !Defend() && !Mend() && Walk(route, arrive);

    /// <summary>
    /// Fight the nearest creature hunting the character, through the same commands the keys send. A sentinel is left alone:
    /// it keeps its post, and the way round it is to stay out of its reach. True while a fight is on.
    /// </summary>
    private bool Defend()
    {
        var threat = _session.Simulation!.Creatures
            .Where(c => c.Alive && c.Hostile && c.RoleId != "sentinel" && ToCreature(c).Length() < 25)
            .OrderBy(c => ToCreature(c).Length())
            .FirstOrDefault();
        if (threat is null)
            return false;
        Engage(threat);
        return true;
    }

    /// <summary>Below half health and out of a fight, work the last self formula the book taught - once in a while. True while it is worked.</summary>
    private bool Mend()
    {
        var simulation = _session.Simulation!;
        var combat = simulation.Combat;
        if (_mending)
        {
            if (_released is null && simulation.WorldTick - _mendTick < 80)
                return true;
            _mending = false;
            _released = null;
            return false;
        }
        var magic = _session.Setup.Magic;
        string? mend = _controller.Formulas().LastOrDefault(f => magic.Formulas[f].Targeting == Targeting.Self);
        if (mend is null || combat.Health * 2 >= combat.MaxHealth || combat.Phase != CombatPhase.Idle
            || combat.Focus < magic.Formulas[mend].FocusCost || simulation.WorldTick - _mendTick < 300)
            return false;
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
        _controller.Cast(mend);
        _mending = true;
        _released = null;
        _mendTick = simulation.WorldTick;
        return true;
    }

    private string? Survived(string where) => _died ? Fail($"the character died on the way to {where}") : null;

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

    /// <summary>Walk a route's waypoints in turn; a waypoint is reached within <paramref name="arrive"/> metres. True at the end.</summary>
    private bool Walk((double X, double Z)[] route, float arrive = 0.5f)
    {
        if (_waypoint >= route.Length)
        {
            _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
            return true;
        }
        var body = _controller.Authoritative;
        var (x, z) = route[_waypoint];
        var to = new Vector3((float)(x - body.XMm / 1000.0), 0, (float)(z - body.ZMm / 1000.0));
        if (to.Length() < arrive)
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
        _waypoint = 0;
        _pending = shot;
        _wait = 12;
    }
}
