// UNNAMED Presentation - the controls overlay (the owner's M6 playtest)
// Godot presentation only: it reads the input map; nothing here holds state (D-11)

using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The controls (F1), read from the input map as it is bound - never a list that could drift from it - in the order a player needs
/// them. The developer's keys stand apart, under their own heading.
/// </summary>
public partial class HelpPanel : CanvasLayer
{
    private static readonly (string Heading, (string Does, string[] Actions)[] Lines)[] Sections =
    {
        ("MOVING", new[]
        {
            ("Move", new[] { "move_forward", "move_left", "move_back", "move_right" }), ("Sprint (hold)", new[] { "sprint" }),
            ("Walk (hold)", new[] { "walk" }), ("Jump", new[] { "jump" }), ("Crouch or stand", new[] { "crouch" }),
        }),
        ("LOOKING", new[]
        {
            ("Look", new[] { "@Mouse" }), ("Zoom in and out, to first person", new[] { "@Mouse wheel" }), ("First person, and back", new[] { "first_person" }),
            ("Change shoulder", new[] { "shoulder_swap" }),
        }),
        ("ACTING", new[]
        {
            ("Open, take, talk, work, help up", new[] { "interact" }), ("Attack, or shoot the bow", new[] { "attack" }),
            ("Guard, or aim the bow (hold)", new[] { "guard" }), ("Dodge", new[] { "dodge" }), ("Use a salve", new[] { "use" }),
            ("Work a formula", new[] { "cast_1", "cast_2", "cast_3" }), ("Take all, from a container", new[] { "take_all" }),
        }),
        ("BUILDING", new[]
        {
            ("Build mode, on or off", new[] { "build_mode" }), ("Choose a piece", new[] { "build_piece_1", "build_piece_7" }),
            ("Next or previous piece", new[] { "build_piece_next", "build_piece_prev" }), ("Turn the piece", new[] { "build_rotate" }),
            ("Place it (a room needs a doorway)", new[] { "build_place" }), ("Take down what you face (press twice)", new[] { "build_dismantle" }),
            ("Cancel: leave build mode, dropping the ghost and any armed take-down", new[] { "release_mouse" }),
        }),
        ("SCREENS", new[]
        {
            ("Inventory", new[] { "inventory" }), ("Journal", new[] { "journal" }), ("Character", new[] { "character" }),
            ("These controls", new[] { "help" }), ("Free the mouse", new[] { "release_mouse" }),
        }),
        ("COMPANION", new[] { ("Tell them to follow, or to wait", new[] { "companion_order" }) }),
        ("CONVERSATION", new[] { ("Answer", new[] { "reply_1", "reply_9" }), ("Walk away", new[] { "release_mouse" }) }),
        ("SAVING", new[]
        {
            ("Quicksave", new[] { "quicksave" }), ("Quickload", new[] { "quickload" }), ("Every save, backups too", new[] { "saves" }),
        }),
    };

    private static readonly (string Does, string[] Actions)[] Developer =
    {
        ("Debug overlay (with the aim line)", new[] { "debug_overlay" }), ("Quest debugger", new[] { "quest_debug" }),
        ("Structure debug (again: the navigation grid)", new[] { "build_debug" }),
        ("Faction debug", new[] { "faction_debug" }),
    };

    /// <summary>The sections in the left column; the rest, and the developer's keys, go in the right, so the whole list fits the screen.</summary>
    private const int LeftSections = 4;

    private readonly GridContainer _left = new() { Columns = 2 };
    private readonly GridContainer _right = new() { Columns = 2 };
    private readonly Label _files = new();

    /// <summary>Where the saves and the log are (M-07): what a tester sends with a problem report.</summary>
    public string Files
    {
        get => _files.Text;
        set => _files.Text = value;
    }

    public override void _Ready()
    {
        Visible = false;
        var panel = new PanelContainer { Position = new Vector2(60, 110) };
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 56);
        foreach (var grid in new[] { _left, _right })
        {
            grid.AddThemeConstantOverride("h_separation", 40);
            columns.AddChild(grid);
        }
        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 14);
        _files.AddThemeFontSizeOverride("font_size", 15);
        stack.AddChild(columns);
        stack.AddChild(_files);
        margin.AddChild(stack);
        panel.AddChild(margin);
        AddChild(panel);
    }

    /// <summary>Open or close; the lines are read from the input map each time it opens.</summary>
    public void Toggle()
    {
        Visible = !Visible;
        if (!Visible)
            return;
        foreach (var grid in new[] { _left, _right })
        {
            foreach (var child in grid.GetChildren())
            {
                grid.RemoveChild(child);
                child.QueueFree();
            }
        }
        Row(_left, "CONTROLS", "[F1] close", heading: true);
        Row(_right, "", "", heading: true);
        for (int i = 0; i < Sections.Length; i++)
        {
            var (heading, lines) = Sections[i];
            var grid = i < LeftSections ? _left : _right;
            Row(grid, heading, "", heading: true);
            foreach (var (does, actions) in lines)
                Row(grid, "  " + does, Keys(actions));
        }
        Row(_right, "DEVELOPER", "", heading: true);
        foreach (var (does, actions) in Developer)
            Row(_right, "  " + does, Keys(actions));
    }

    /// <summary>The keys bound to actions, as the input map has them: a run of numbered actions reads "4 - 6".</summary>
    public static string Keys(string[] actions)
    {
        if (actions.Length == 1 && actions[0].StartsWith('@'))
            return actions[0][1..];
        var names = actions.Select(a => InputMap.HasAction(a) ? InputMap.ActionGetEvents(a).Select(KeyName).FirstOrDefault() ?? "unbound" : "unbound").ToList();
        if (actions.Length == 2 && actions[0].EndsWith("_1", StringComparison.Ordinal))
            return $"{names[0]} - {names[1]}";
        var all = actions.Length == 1 && InputMap.HasAction(actions[0]) ? InputMap.ActionGetEvents(actions[0]).Select(KeyName).ToList() : names;
        return string.Join(actions.Length == 1 ? " or " : " ", all.Distinct());
    }

    /// <summary>
    /// The key an action is on, as this keyboard prints it: the input map binds physical positions, so W's key reads Z on an AZERTY
    /// keyboard (the Phase-1 technical audit, L-27). For a prompt: "[E] Open the door".
    /// </summary>
    public static string Key(string action) =>
        InputMap.HasAction(action) && InputMap.ActionGetEvents(action).FirstOrDefault() is { } input ? KeyName(input) : "unbound";

    private static string KeyName(InputEvent input) => input switch
    {
        InputEventKey key => OS.GetKeycodeString(key.Keycode != Godot.Key.None ? key.Keycode : DisplayServer.KeyboardGetKeycodeFromPhysical(key.PhysicalKeycode)),
        InputEventMouseButton { ButtonIndex: MouseButton.Left } => "Left mouse",
        InputEventMouseButton { ButtonIndex: MouseButton.Right } => "Right mouse",
        InputEventMouseButton mouse => $"Mouse {mouse.ButtonIndex}",
        _ => input.AsText(),
    };

    private static void Row(GridContainer grid, string does, string keys, bool heading = false)
    {
        foreach (string text in new[] { does, keys })
        {
            var label = new Label { Text = text };
            label.AddThemeFontSizeOverride("font_size", heading ? 18 : 17);
            if (heading)
                label.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.55f));
            grid.AddChild(label);
        }
    }
}
