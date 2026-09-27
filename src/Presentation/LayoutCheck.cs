// UNNAMED Presentation - the panels at a small window, checked and pictured (the Phase-1 technical audit, M-06)
// Godot presentation only

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>godot --path src/Presentation --resolution 1366x768 -- --layout-check &lt;dir&gt; --content-root &lt;content with a full pack&gt;</c>: at
/// the window size it is given, opens each panel in turn - a full pack of 24 stacks, then the same pack scrolled to its last row, the
/// saves list, the controls, the character sheet, and a conversation with Sel - pictures each, and checks that every button on it lies on
/// the screen or in a list that scrolls and itself lies on the screen. Before M-06 a full pack's last rows, and a long conversation's
/// last replies, fell below the screen's edge. Exit code 0 when every check held.
/// </summary>
public sealed class LayoutCheck
{
    private const string Sel = "npc.ashen_hollow.sel_arien";
    private static readonly (double X, double Z)[] ToSel = { (44, 138), (54.5, 134), (62, 128), (69.8, 123.6) };

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly Viewport _viewport;
    private readonly InventoryPanel _inventory;
    private readonly DialoguePanel _dialogue;
    private readonly SavesPanel _saves;
    private readonly HelpPanel _help;
    private readonly CharacterPanel _character;
    private readonly BuildMode _build;
    private readonly Hud _hud;
    private readonly List<string> _passed = new();
    private int _step;
    private int _wait = 30;
    private int _waypoint;
    private string? _failure;

    public LayoutCheck(GameSession session, PlayerController controller, CameraRig camera, Viewport viewport, InventoryPanel inventory,
        DialoguePanel dialogue, SavesPanel saves, HelpPanel help, CharacterPanel character, BuildMode build, Hud hud, string directory)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        _viewport = viewport;
        _inventory = inventory;
        _dialogue = dialogue;
        _saves = saves;
        _help = help;
        _character = character;
        _build = build;
        _hud = hud;
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    public string Directory { get; }

    private string Size => $"{DisplayServer.WindowGetSize().X}x{DisplayServer.WindowGetSize().Y}";

    /// <summary>Advance one frame: the name of a picture to take now, "done", "failed", or null.</summary>
    public string? Update()
    {
        if (_failure is not null)
            return "failed";
        if (_wait > 0)
        {
            _wait--;
            return null;
        }
        var simulation = _session.Simulation!;
        switch (_step++)
        {
            case 0:
                int slots = _session.Setup.Items.Inventory.StackSlots;
                if (simulation.Player.Inventory.Length != slots)
                    return Fail($"the pack holds {simulation.Player.Inventory.Length} stacks, not a full {slots}: run it with the full-pack content");
                _inventory.Open(null);
                return Wait(10);
            case 1:
                return Check(_inventory, $"a full pack of {simulation.Player.Inventory.Length} stacks") ?? $"inventory_full_{Size}";
            case 2:
                var list = Find<ScrollContainer>(_inventory).First();
                list.ScrollVertical = (int)list.GetVScrollBar().MaxValue;
                return Wait(6);
            case 3:
            {
                var scroll = Find<ScrollContainer>(_inventory).First();
                var last = scroll.GetChild<VBoxContainer>(0).GetChildren().OfType<Control>().Last();
                if (!scroll.GetGlobalRect().Grow(1).Encloses(last.GetGlobalRect()))
                    return Fail("scrolled to its end, the pack's last row is still not in view");
                _passed.Add("the pack's last row scrolls into view");
                return $"inventory_full_scrolled_{Size}";
            }
            case 4:
                _inventory.Close();
                _saves.OpenInGame();
                return Wait(10);
            case 5:
                return Check(_saves, "the saves list") ?? $"saves_{Size}";
            case 6:
                _saves.Close();
                _help.Toggle();
                return Wait(10);
            case 7:
                return Check(_help, "the controls") ?? $"help_{Size}";
            case 8:
                // Build mode's panel (M7), top right under the tracker: on the screen at this size.
                _help.Toggle();
                if (!_build.Enter(_camera))
                    return Fail("build mode would not open: the pack has no pieces");
                return Wait(10);
            case 9:
            {
                var screen = _viewport.GetVisibleRect();
                var rect = _hud.BuildPanel.GetGlobalRect();
                if (!_hud.BuildPanel.IsVisibleInTree() || !screen.Grow(1).Encloses(rect))
                    return Fail($"the build panel at {rect} is not on the screen {screen}");
                _passed.Add("the build panel on screen");
                return $"build_{Size}";
            }
            case 10:
                _build.Exit(_camera);
                _character.Visible = true;
                _character.Refresh();
                return Wait(10);
            case 11:
                return Check(_character, "the character sheet") ?? $"character_{Size}";
            case 12:
                _character.Visible = false;
                if (!Approach())
                    _step--;   // walking to Sel: this step again next frame
                return null;
            case 13:
                if (!_dialogue.Visible)
                    return Fail("Sel's conversation did not open");
                return Check(_dialogue, "a conversation") ?? $"dialogue_{Size}";
            default:
                GD.Print($"UNNAMED layout check at {Size}: PASS - {string.Join("; ", _passed)}; pictures in {Directory}");
                return "done";
        }
    }

    /// <summary>Every button the panel shows lies on the screen - or in a list that scrolls, and that list lies on the screen.</summary>
    private string? Check(Node panel, string what)
    {
        var screen = _viewport.GetVisibleRect();
        int buttons = 0;
        foreach (var button in Find<BaseButton>(panel).Where(b => b.IsVisibleInTree()))
        {
            buttons++;
            Control shown = Ancestor<ScrollContainer>(button) ?? (Control)button;
            var rect = shown.GetGlobalRect();
            if (!screen.Grow(1).Encloses(rect))
                return Fail($"{what}: the button '{(button as Button)?.Text}' {(shown == button ? "" : "(its list) ")}at {rect} is off the screen {screen}");
        }
        foreach (var box in panel.GetChildren().OfType<Control>().Where(c => c.IsVisibleInTree()))
        {
            if (!screen.Grow(1).Encloses(box.GetGlobalRect()))
                return Fail($"{what}: the panel at {box.GetGlobalRect()} is off the screen {screen}");
        }
        _passed.Add($"{what}: {buttons} buttons, all on screen");
        return null;
    }

    /// <summary>To within a hand's reach of Sel, then talk; true once asked.</summary>
    private bool Approach()
    {
        if (_waypoint < ToSel.Length)
        {
            if (Walk(ToSel[_waypoint]))
                _waypoint++;
            return false;
        }
        var sel = _session.Simulation!.Npcs.Single(n => n.Id == Sel).Body;
        var body = _controller.Authoritative;
        var away = new Vector3(body.XMm - sel.XMm, 0, body.ZMm - sel.ZMm).Normalized() * 1.3f;
        if (!Walk((sel.XMm / 1000.0 + away.X, sel.ZMm / 1000.0 + away.Z)))
            return false;
        _controller.Talk(Sel);
        _wait = 10;
        return true;
    }

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

    private string? Wait(int frames)
    {
        _wait = frames;
        return null;
    }

    private string Fail(string reason)
    {
        _failure = reason;
        GD.PushError($"UNNAMED layout check at {Size}: FAIL - {reason} (passed so far: {string.Join("; ", _passed)})");
        return $"failed_{Size}";
    }

    private static IEnumerable<T> Find<T>(Node node) where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T found)
                yield return found;
            foreach (var deeper in Find<T>(child))
                yield return deeper;
        }
    }

    private static T? Ancestor<T>(Node node) where T : Node
    {
        for (var parent = node.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is T found)
                return found;
        }
        return null;
    }
}
