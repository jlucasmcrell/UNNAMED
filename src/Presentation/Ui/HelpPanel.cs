// UNNAMED Presentation - the controls overlay (the owner's M6 playtest; the Field Instrument UI)
// Godot presentation only: it reads the input map; nothing here holds state (D-11)

using Godot;
using UNNAMED.Presentation.Settings;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The controls (F1), read from the input map as it is bound - never a list that could drift from it - in the order a player needs
/// them, each key on the HUD's key cap. A WIDE sheet in three columns; the developer's keys stand apart, quieter, at the foot of the
/// third, and where the saves and the log lie closes the sheet.
/// </summary>
public partial class HelpPanel : CanvasLayer
{
    private static readonly (string Heading, (string Does, string[] Actions)[] Lines)[][] Columns =
    {
        new[]
        {
            ("Moving", new[]
            {
                ("Move", new[] { "move_forward", "move_left", "move_back", "move_right" }), ("Sprint", new[] { "sprint" }),
                ("Walk", new[] { "walk" }), ("Jump", new[] { "jump" }), ("Crouch", new[] { "crouch" }),
                ("Autorun, on or off", new[] { "auto_run" }),
            }),
            ("Looking", new[]
            {
                ("Look", new[] { "@Mouse" }), ("Zoom in and out, to first person", new[] { "@Mouse wheel" }),
                ("First person, and back", new[] { "first_person" }), ("Change shoulder", new[] { "shoulder_swap" }),
            }),
            ("Acting", new[]
            {
                ("Open, take, talk, work, help up", new[] { "interact" }), ("Attack, or shoot the bow", new[] { "attack" }),
                ("Guard, or aim the bow", new[] { "guard" }), ("Dodge", new[] { "dodge" }), ("Use a salve", new[] { "use" }),
                ("Use a quick slot: an item, a formula", new[] { "quick_1", "quick_12" }),
                ("Take all, from a container", new[] { "take_all" }),
            }),
        },
        new[]
        {
            ("Companion", new[] { ("Tell them to follow, or to wait", new[] { "companion_order" }) }),
            ("Conversation", new[] { ("Answer", new[] { "reply_1", "reply_9" }), ("Walk away", new[] { "release_mouse" }) }),
            ("Saving", new[]
            {
                ("Save now", new[] { "quicksave" }), ("Every save, to restore one", new[] { "saves" }),
            }),
        },
        new[]
        {
            ("Screens", new[]
            {
                ("System menu, or close a screen", new[] { "release_mouse" }), ("Inventory", new[] { "inventory" }), ("Journal", new[] { "journal" }),
                ("Map", new[] { "map" }), ("Party: orders, wages, gear", new[] { "party" }), ("Chat, and who is asking for company", new[] { "chat" }),
                ("Character", new[] { "character" }), ("Skills, and what you know", new[] { "skills" }), ("These controls", new[] { "help" }),
            }),
        },
    };

    private static readonly (string Does, string[] Actions)[] Developer =
    {
        ("Debug overlay (with the aim line)", new[] { "debug_overlay" }), ("Quest debugger", new[] { "quest_debug" }),
    };

    private Sheet _sheet = null!;

    /// <summary>Whether the developer keys are on (a debug build, or <c>--dev</c>): they are listed only then.</summary>
    public bool DeveloperKeys { get; init; }

    /// <summary>Where the saves and the log are (M-07): what a tester sends with a problem report.</summary>
    public string Files { get; set; } = string.Empty;

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "Controls", "help", UiKit.Wide);
    }

    /// <summary>The screen's own tile, as its System Menu row shows it.</summary>
    public void UseIcons(HudIcons icons) => _sheet.SetIcon(icons.Binds("panel.help") ? icons.For("panel.help") : null);

    /// <summary>Open or close; the lines are read from the input map each time it opens.</summary>
    public void Toggle()
    {
        Visible = !Visible;
        if (!Visible)
            return;
        UiKit.Clear(_sheet.Body);
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        for (int c = 0; c < Columns.Length; c++)
        {
            var column = new VBoxContainer { CustomMinimumSize = new Vector2(352, 0) };
            column.AddThemeConstantOverride("separation", 16);
            foreach (var (heading, lines) in Columns[c])
                column.AddChild(Section(heading, lines, HudStyle.Brass, HudStyle.Bone));
            if (DeveloperKeys && c == Columns.Length - 1)
                column.AddChild(Section("Developer", Developer, HudStyle.Muted, HudStyle.Ash));
            columns.AddChild(column);
        }
        _sheet.Body.AddChild(columns);
        // The three things a new player must be told, shown here when asked for rather than as a panel in the way (the world does not stop).
        var world = UiKit.Section("How this world works", "three things that are not what other games do", tone: HudStyle.Brass);
        foreach (var explainer in Models.Explainers.All)
        {
            world.AddChild(UiKit.Words(explainer.Title, 18, HudStyle.Ember));
            foreach (string paragraph in explainer.Paragraphs)
                world.AddChild(UiKit.Words(paragraph, 16, HudStyle.Bone));
        }
        _sheet.Body.AddChild(world);
        if (Files.Length > 0)
        {
            var support = UiKit.Section("Support", "what a tester sends with a problem report");
            support.AddChild(UiKit.Words(Files, 16, HudStyle.Ash));
            _sheet.Body.AddChild(support);
        }
    }

    private static VBoxContainer Section(string heading, (string Does, string[] Actions)[] lines, Color head, Color words)
    {
        var section = UiKit.Section(heading, tone: head);
        section.AddThemeConstantOverride("separation", 2);
        foreach (var (does, actions) in lines)
        {
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 26) };
            row.AddThemeConstantOverride("separation", 6);
            // Held or pressed on and off, as Settings has it (Controls: hold or toggle).
            string mode = actions.Length == 1 && ControlPrefs.Toggleable.ContainsKey(actions[0])
                ? SettingsStore.Current.Profile.Controls.IsToggle(actions[0]) ? " (on or off)" : " (hold)"
                : "";
            var label = UiKit.Words(does + mode, 17, words);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(label);
            foreach (var part in Caps(actions))
                row.AddChild(part);
            section.AddChild(row);
        }
        return section;
    }

    /// <summary>The keys bound to actions as key caps: a run of numbered actions reads [4]–[6], one action's two keys [TAB] or [I].</summary>
    private static IEnumerable<Control> Caps(string[] actions)
    {
        if (actions.Length == 1 && actions[0].StartsWith('@'))
        {
            yield return UiKit.Caption(actions[0][1..]);
            yield break;
        }
        if (actions.Length == 2 && actions[0].EndsWith("_1", StringComparison.Ordinal))
        {
            yield return new HudView.KeyCap(Key(actions[0]), 16);
            yield return UiKit.Caption("–");
            yield return new HudView.KeyCap(Key(actions[1]), 16);
            yield break;
        }
        var keys = actions.Length == 1 && InputMap.HasAction(actions[0])
            ? InputMap.ActionGetEvents(actions[0]).Select(KeyName).Distinct().ToList()
            : actions.Select(Key).ToList();
        for (int i = 0; i < keys.Count; i++)
        {
            if (i > 0 && actions.Length == 1)
                yield return UiKit.Caption("or");
            yield return new HudView.KeyCap(keys[i], 16);
        }
    }

    /// <summary>
    /// The key an action is on, as this keyboard prints it: the input map binds physical positions, so W's key reads Z on an AZERTY
    /// keyboard (the Phase-1 technical audit, L-27). For a prompt: "[E] Open the door".
    /// </summary>
    public static string Key(string action) =>
        InputMap.HasAction(action) && InputMap.ActionGetEvents(action).FirstOrDefault() is { } input ? KeyName(input) : "unbound";

    public static string KeyName(InputEvent input) => input switch
    {
        // The key left of 1 is "tilde" to a player (the owner's second-play brief §23), not the engine's "QuoteLeft".
        InputEventKey { PhysicalKeycode: Godot.Key.Quoteleft } or InputEventKey { Keycode: Godot.Key.Quoteleft } => "~",
        // A headless run has no keyboard layout to ask: the key's own name.
        InputEventKey key => OS.GetKeycodeString(key.Keycode != Godot.Key.None ? key.Keycode
            : DisplayServer.GetName() == "headless" ? key.PhysicalKeycode : DisplayServer.KeyboardGetKeycodeFromPhysical(key.PhysicalKeycode)),
        InputEventMouseButton { ButtonIndex: MouseButton.Left } => "Left mouse",
        InputEventMouseButton { ButtonIndex: MouseButton.Right } => "Right mouse",
        InputEventMouseButton mouse => $"Mouse {mouse.ButtonIndex}",
        _ => input.AsText(),
    };
}
