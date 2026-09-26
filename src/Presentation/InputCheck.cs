// UNNAMED Presentation - the input gate, checked through the real input path (the Phase-1 technical audit, M-03 and L-27)
// Godot presentation only

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>godot --path src/Presentation -- --input-check</c>: presses keys as a player does - through the input map, read by the same input
/// code a player's run uses - with each panel open in turn: a conversation, the inventory, the character sheet, the saves list. While
/// one is open no gameplay key reaches the world: nothing that moves, fights, works, uses, interacts, jumps, crouches, orders, saves or
/// loads is submitted. The mouse is the pointer's, and the game's again once the panel closes. A load with a conversation open leaves no
/// panel on screen, and the click that takes the mouse back is not also a swing. Run it in a window: the mouse is part of what it
/// checks. Exit code 0 on success, 1 on failure.
/// </summary>
public sealed class InputCheck
{
    private const string Sel = "npc.ashen_hollow.sel_arien";

    /// <summary>Every gameplay key, held or pressed at once.</summary>
    private static readonly string[] Gameplay =
    {
        "move_forward", "sprint", "attack", "guard", "dodge", "use", "interact", "jump", "crouch", "cast_1", "companion_order", "quicksave",
        "quickload", "first_person", "build_mode", "build_place", "build_dismantle", "build_repair", "work_order", "build_rotate", "build_piece_1",
    };

    // From the Ashen Waystone to Sel's table, by the lodge's south side.
    private static readonly (double X, double Z)[] ToSel = { (44, 138), (54.5, 134), (62, 128), (69.8, 123.6) };

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly Func<bool> _modal;
    private readonly Func<string, LoadResult?> _load;
    private readonly (DialoguePanel Dialogue, InventoryPanel Inventory, CharacterPanel Character, SavesPanel Saves) _panels;
    private readonly BuildMode _build;
    private readonly List<string> _passed = new();
    private int _step;
    private int _frames;
    private int _waypoint;
    private int _wait;
    private int _logMark;
    private Simulation? _world;

    public InputCheck(GameSession session, PlayerController controller, CameraRig camera, Func<bool> modal, Func<string, LoadResult?> load,
        DialoguePanel dialogue, InventoryPanel inventory, CharacterPanel character, SavesPanel saves, BuildMode build)
    {
        _build = build;
        _session = session;
        _controller = controller;
        _camera = camera;
        _modal = modal;
        _load = load;
        _panels = (dialogue, inventory, character, saves);
    }

    /// <summary>Whether the player's input code reads the keys this frame (not while the check walks the character somewhere).</summary>
    public bool Reading { get; private set; }

    private bool Windowed => DisplayServer.GetName() != "headless";

    /// <summary>Advance one frame; the exit code once the check is over.</summary>
    public int? Update()
    {
        if (++_frames > 100 && _session.Simulation!.WorldTick > 3_000)   // bounded in game time: a window runs many frames a tick
            return Fail($"timed out at step {_step}, waypoint {_waypoint}, at ({_controller.Authoritative.XMm / 1000.0:0.0}, {_controller.Authoritative.ZMm / 1000.0:0.0})");
        if (_wait > 0)
        {
            _wait--;
            return null;
        }
        var simulation = _session.Simulation!;
        if (_tapped is not null && _step >= 28)
        {
            Release(_tapped);
            _tapped = null;
        }
        switch (_step)
        {
            case 0:
                // Walk to Sel with the keys unread: the check steers.
                Reading = false;
                if (_waypoint < ToSel.Length)
                {
                    if (Walk(ToSel[_waypoint]))
                        _waypoint++;
                    return null;
                }
                // Up to within a hand's reach of her, from where the character stands.
                var sel = simulation.Npcs.Single(n => n.Id == Sel).Body;
                var body = _controller.Authoritative;
                var away = new Vector3(body.XMm - sel.XMm, 0, body.ZMm - sel.ZMm).Normalized() * 1.3f;
                if (!Walk((sel.XMm / 1000.0 + away.X, sel.ZMm / 1000.0 + away.Z)))
                    return null;
                _controller.Talk(Sel);
                return Next(10);
            case 1:
                if (!_panels.Dialogue.Visible)
                    return Fail("Sel's conversation did not open");
                Reading = true;
                return Gate("the conversation", also: new[] { "inventory", "character", "saves" });
            case 2:
                return Judge("the conversation", Only(_panels.Dialogue));
            case 3:
                Press("release_mouse");   // Escape walks away
                return Next(6);
            case 4:
                Release("release_mouse");
                if (_panels.Dialogue.Visible || simulation.Conversation is not null)
                    return Fail("Escape did not walk away from the conversation");
                return Captured("after the conversation") ?? Next(0);
            case 5:
                Press("inventory");
                return Next(3);
            case 6:
                Release("inventory");
                return _panels.Inventory.Visible ? Gate("the inventory") : Fail("the inventory key did not open it");
            case 7:
                return Judge("the inventory", Only(_panels.Inventory));
            case 8:
                Press("inventory");
                return Next(4);
            case 9:
                Release("inventory");
                return _panels.Inventory.Visible ? Fail("the inventory key did not close it") : Captured("after the inventory") ?? Next(0);
            case 10:
                Press("character");
                return Next(3);
            case 11:
                Release("character");
                return _panels.Character.Visible ? Gate("the character sheet") : Fail("the character key did not open the sheet");
            case 12:
                return Judge("the character sheet", Only(_panels.Character));
            case 13:
                Press("character");
                return Next(4);
            case 14:
                Release("character");
                return _panels.Character.Visible ? Fail("the character key did not close the sheet") : Captured("after the sheet") ?? Next(0);
            case 15:
                Press("saves");
                return Next(3);
            case 16:
                Release("saves");
                return _panels.Saves.Visible ? Gate("the saves list") : Fail("the saves key did not open the list");
            case 17:
                return Judge("the saves list", Only(_panels.Saves));
            case 18:
                Press("release_mouse");
                return Next(4);
            case 19:
                Release("release_mouse");
                return _panels.Saves.Visible ? Fail("Escape did not close the saves list") : Captured("after the saves list") ?? Next(0);
            case 20:
                // With nothing open the same keys do reach the world: a quicksave is written.
                Press("quicksave");
                return Next(3);
            case 21:
                Release("quicksave");
                if (_session.StartChoice().Saves.All(s => s.Slot != SaveSlots.Quick))
                    return Fail("F5 with nothing open wrote no quick save");
                _passed.Add("F5 with nothing open saved");
                // M-03: a load with a conversation open.
                Reading = false;
                _controller.Talk(Sel);
                return Next(10);
            case 22:
                if (!_panels.Dialogue.Visible)
                    return Fail("Sel's conversation did not open again");
                if (_load(SaveSlots.Quick) is null)
                    return Fail("the quick save did not load");
                Reading = true;
                return Next(4);
            case 23:
                if (_panels.Dialogue.Visible || _panels.Inventory.Visible || _panels.Character.Visible || _modal())
                    return Fail("a panel stayed open after the load");
                _passed.Add("a load with a conversation open left no panel open (M-03)");
                return Captured("after the load") ?? Next(0);
            case 24:
                // Escape frees the mouse; the click that takes it back must not also swing.
                Press("release_mouse");
                return Next(3);
            case 25:
                Release("release_mouse");
                if (Windowed && Input.MouseMode != Input.MouseModeEnum.Visible)
                    return Fail("Escape did not free the mouse");
                _logMark = simulation.CommandLog.Count;
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(1500, 600) });
                return Next(3);
            case 26:
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(1500, 600) });
                if (simulation.CommandLog.Skip(_logMark).Any(c => c.Command is AttackCommand))
                    return Fail("the click that took the mouse back also swung");
                if (Windowed && Input.MouseMode != Input.MouseModeEnum.Captured)
                    return Fail("the click did not take the mouse back");
                _passed.Add("the click that took the mouse back did not swing");
                return Next(2);
            // Build mode (M7 design §8.19): every panel ends it, L's included; Esc leaves it before it frees the mouse; the click that takes
            // the mouse back never also places a piece.
            case 27:
                return Tap("build_mode");
            case 28:
                return !_build.Active ? Fail("B did not enter build mode") : Tap("inventory");
            case 29:
                if (_build.Active || !_panels.Inventory.Visible)
                    return Fail("the inventory did not end build mode");
                return Tap("inventory");
            case 30:
                return Tap("build_mode");
            case 31:
                return !_build.Active ? Fail("B did not enter build mode again") : Tap("character");
            case 32:
                if (_build.Active || !_panels.Character.Visible)
                    return Fail("the character sheet did not end build mode");
                return Tap("character");
            case 33:
                return Tap("build_mode");
            case 34:
                return !_build.Active ? Fail("B did not enter build mode again") : Tap("saves");
            case 35:
                if (_build.Active || !_panels.Saves.Visible)
                    return Fail("L did not end build mode");
                return Tap("release_mouse");
            case 36:
                return _panels.Saves.Visible ? Fail("Escape did not close the saves list") : Tap("build_mode");
            case 37:
                if (!_build.Active)
                    return Fail("B did not enter build mode again");
                Reading = false;
                _controller.Talk(Sel);
                return Next(10);
            case 38:
                if (!_panels.Dialogue.Visible)
                    return Fail("Sel's conversation did not open in build mode");
                if (_build.Active)
                    return Fail("the conversation did not end build mode");
                Reading = true;
                _passed.Add("every panel, L's included, ended build mode");
                return Tap("release_mouse");
            case 39:
                return _panels.Dialogue.Visible ? Fail("Escape did not walk away from the conversation") : Tap("build_mode");
            case 40:
                return !_build.Active ? Fail("B did not enter build mode again") : Tap("release_mouse");
            case 41:
                if (_build.Active)
                    return Fail("Escape did not leave build mode");
                if (Windowed && Input.MouseMode != Input.MouseModeEnum.Captured)
                    return Fail("the Escape that left build mode also freed the mouse");
                _passed.Add("Escape left build mode and kept the mouse");
                return Tap("release_mouse");
            case 42:
                if (Windowed && Input.MouseMode != Input.MouseModeEnum.Visible)
                    return Fail("the second Escape did not free the mouse");
                return Tap("build_mode");
            case 43:
                if (!_build.Active)
                    return Fail("B did not enter build mode with the mouse free");
                _logMark = simulation.CommandLog.Count;
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(1500, 600) });
                return Next(3);
            case 44:
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(1500, 600) });
                if (simulation.CommandLog.Skip(_logMark).Any(c => c.Command is PlacePieceCommand or AttackCommand))
                    return Fail("the click that took the mouse back also placed a piece");
                if (Windowed && Input.MouseMode != Input.MouseModeEnum.Captured)
                    return Fail("the click did not take the mouse back in build mode");
                _passed.Add("the click that took the mouse back did not place a piece");
                return Tap("build_mode");
            case 45:
                if (_build.Active)
                    return Fail("B did not leave build mode");
                GD.Print($"UNNAMED input check: PASS - {string.Join("; ", _passed)}" + (Windowed ? "" : " (headless: the mouse was not checked)"));
                return 0;
        }
        return null;
    }

    /// <summary>With a panel open: note the world and its command log, and press every gameplay key at once, held.</summary>
    private int? Gate(string panel, string[]? also = null)
    {
        _world = _session.Simulation;
        _logMark = _world!.CommandLog.Count;
        foreach (string action in Gameplay.Concat(also ?? Array.Empty<string>()))
            Press(action);
        if (Windowed && Input.MouseMode != Input.MouseModeEnum.Visible)
            return Fail($"the mouse is not the pointer's with {panel} open");
        return Next(8);
    }

    /// <summary>Let the keys go and look at what reached the world while they were held: nothing but standing still.</summary>
    private int? Judge(string panel, bool stillOpen)
    {
        foreach (string action in Gameplay.Concat(new[] { "inventory", "character", "saves" }))
            Release(action);
        if (!ReferenceEquals(_session.Simulation, _world))
            return Fail($"a load happened with {panel} open");
        var reached = _world!.CommandLog.Skip(_logMark).Select(c => c.Command)
            .Where(c => c is not MoveCommand { Intent.IsMoving: false } && c is not BlockCommand { Raised: false }).ToList();
        if (reached.Count > 0)
            return Fail($"with {panel} open, {string.Join(", ", reached.Select(c => c.GetType().Name))} reached the world");
        if (!stillOpen)
            return Fail($"{panel} was closed, or another panel opened over it, by a gameplay key");
        if (_session.StartChoice().Saves.Any(s => s.Slot == SaveSlots.Quick))
            return Fail($"F5 saved with {panel} open");
        _passed.Add($"{panel}: no gameplay key reached the world");
        return Next(2);
    }

    /// <summary>A moment after a panel closed, the mouse is the game's again (windowed only); null when it is.</summary>
    private int? Captured(string when)
    {
        if (Windowed && Input.MouseMode != Input.MouseModeEnum.Captured)
            return Fail($"the mouse was not the game's {when}");
        return null;
    }

    /// <summary>This panel open, and no other.</summary>
    private bool Only(CanvasLayer panel) =>
        new CanvasLayer[] { _panels.Dialogue, _panels.Inventory, _panels.Character, _panels.Saves }.All(p => p.Visible == (p == panel));

    private int? Next(int wait)
    {
        _step++;
        _wait = wait;
        return null;
    }

    private static void Press(string action) => Input.ActionPress(action);

    /// <summary>A key pressed for a frame and let go, as a player taps it; the next step reads what it did three frames on.</summary>
    private int? Tap(string action)
    {
        Press(action);
        _tapped = action;
        return Next(3);
    }

    private string? _tapped;

    private static void Release(string action) => Input.ActionRelease(action);

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
        foreach (string action in Gameplay.Concat(new[] { "inventory", "character", "saves", "release_mouse" }))
            Release(action);
        GD.PushError($"UNNAMED input check: FAIL - {reason} (passed so far: {string.Join("; ", _passed)})");
        return 1;
    }
}
