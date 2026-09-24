// UNNAMED Presentation - the start screen and the saves list (the Phase-1 technical audit, B-01)
// Godot presentation only: it reads save summaries and asks Main to start or load; nothing here holds game state (D-11)

using System.Globalization;
using Godot;
using UNNAMED.Application;
using UNNAMED.Persistence;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// At launch: Continue - the newest save that loads, autosaves included - New Game, confirmed when saves exist, and every save with its
/// backups and the save it displaced, each loadable by choice. In the game (L, or a failed quickload) the same list, and a way back.
/// A save that cannot be loaded says why, and the copies of it that can be loaded are listed beneath it: nothing falls back silently.
/// </summary>
public partial class SavesPanel : CanvasLayer
{
    private readonly Label _title = new();
    private readonly Label _message = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly VBoxContainer _actions = new();
    private readonly VBoxContainer _list = new();
    private GameSession _session = null!;
    private bool _atStart;
    private bool _confirming;
    private string? _failure;

    public void Bind(GameSession session) => _session = session;

    /// <summary>What the player chose: Main starts or loads, and closes the panel once a world runs.</summary>
    public Action? NewGame { get; set; }

    public Action<string, SaveCopy>? Load { get; set; }

    public Action? Quit { get; set; }

    /// <summary>In the game: back to it. Main closes the panel.</summary>
    public Action? Closed { get; set; }

    public override void _Ready()
    {
        Visible = false;
        var screen = new Control();
        screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        screen.MouseFilter = Control.MouseFilterEnum.Stop;   // the world behind takes no clicks
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.55f) };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        screen.AddChild(shade);
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(820, 0) };
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 18);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        _title.AddThemeFontSizeOverride("font_size", 30);
        _message.AddThemeFontSizeOverride("font_size", 17);
        _message.AddThemeColorOverride("font_color", new Color(1f, 0.78f, 0.55f));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 420), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _list.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_list);
        column.AddChild(_title);
        column.AddChild(_message);
        column.AddChild(_actions);
        column.AddChild(scroll);
        margin.AddChild(column);
        panel.AddChild(margin);
        center.AddChild(panel);
        screen.AddChild(center);
        AddChild(screen);
    }

    /// <summary>The start screen: nothing runs until the player chooses.</summary>
    public void OpenStart()
    {
        _atStart = true;
        Open(null);
    }

    /// <summary>The saves list in the game; <paramref name="failure"/> says why a load just failed.</summary>
    public void OpenInGame(string? failure = null)
    {
        _atStart = false;
        Open(failure);
    }

    /// <summary>Press Continue as a click would (the resume harness). False when the start screen offers none.</summary>
    public bool PressContinue()
    {
        if (!Visible || !_atStart || _session.StartChoice().Continue is not { } next)
            return false;
        Load?.Invoke(next.Slot, next.Copy);
        return true;
    }

    /// <summary>A load chosen here failed: say so, and list again - the failed save's other copies are beneath it.</summary>
    public void ShowFailure(string what) => Open(what);

    public void Close()
    {
        Visible = false;
        _failure = null;
        _confirming = false;
    }

    private void Open(string? failure)
    {
        _failure = failure;
        _confirming = false;
        Visible = true;
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Visible;
        Render();
    }

    private void Render()
    {
        var choice = _session.StartChoice();
        _title.Text = _atStart ? "Otherreach" : $"Load a save   [{HelpPanel.Key("saves")}] or Escape: back to the game";
        var notes = new List<string>();
        if (_failure is not null)
            notes.Add(_failure);
        foreach (var passed in choice.PassedOver)
            notes.Add($"{Describe(passed.Slot, passed.Copy)} ({When(passed)}) is not offered to Continue: {passed.Problem}. Its other copies are listed below.");
        _message.Text = string.Join("\n", notes);
        _message.Visible = notes.Count > 0;

        Clear(_actions);
        if (_atStart)
        {
            if (choice.Continue is { } next)
                Action(_actions, $"Continue   {Describe(next.Slot, next.Copy)}, {When(next)}, {Played(next)}", () => Load?.Invoke(next.Slot, next.Copy), focus: true);
            if (_confirming)
            {
                var warning = new Label
                {
                    Text = "A new game uses the same save slots. Its autosaves and quicksaves will replace these over time. Start a new game?",
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                };
                warning.AddThemeFontSizeOverride("font_size", 17);
                _actions.AddChild(warning);
                Action(_actions, "Yes, start a new game", () => NewGame?.Invoke(), focus: true);
                Action(_actions, "No", () =>
                {
                    _confirming = false;
                    Render();
                });
            }
            else
            {
                Action(_actions, "New game", () =>
                {
                    if (choice.NewGameAsksFirst)
                    {
                        _confirming = true;
                        Render();
                    }
                    else
                    {
                        NewGame?.Invoke();
                    }
                }, focus: choice.Continue is null);
            }
            Action(_actions, "Quit", () => Quit?.Invoke());
        }
        else
        {
            Action(_actions, "Back to the game", () => Closed?.Invoke(), focus: true);
        }

        Clear(_list);
        if (choice.Saves.IsEmpty)
            Line(_list, "No saves yet.", 17);
        foreach (var save in choice.Saves)
        {
            Row(save);
            foreach (var copy in _session.OtherCopies(save.Slot))
                Row(copy);
        }
    }

    private void Row(SaveSummary save)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        bool copy = save.Copy != SaveCopy.Current;
        var label = new Label
        {
            Text = (copy ? "      " : "") + $"{Describe(save.Slot, save.Copy)}   {When(save)}   {Played(save)}" + (save.Problem is { } problem ? $"\n      {problem}" : ""),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        label.AddThemeFontSizeOverride("font_size", copy ? 16 : 17);
        row.AddChild(label);
        // A damaged save may still give up what survives (§7.2: partial recovery beats none); one this build cannot read, nothing.
        if (save.Problem is null || save.Problem.StartsWith("it is damaged", StringComparison.Ordinal))
            Action(row, save.Problem is null ? "Load" : "Load what survives", () => Load?.Invoke(save.Slot, save.Copy));
        _list.AddChild(row);
    }

    /// <summary>A slot as a player reads it: "Quick save", "Autosave 3", a manual save by its name; and which copy.</summary>
    public static string Describe(string slot, SaveCopy copy)
    {
        string name = slot == SaveSlots.Quick ? "Quick save"
            : slot.StartsWith("auto_", StringComparison.Ordinal) ? $"Autosave {int.Parse(slot[5..], CultureInfo.InvariantCulture)}"
            : slot.StartsWith("manual_", StringComparison.Ordinal) ? $"Save \"{slot[7..].Replace('_', ' ')}\""
            : slot;
        return copy switch
        {
            SaveCopy.Backup1 => $"{name}: backup 1",
            SaveCopy.Backup2 => $"{name}: backup 2",
            SaveCopy.Previous => $"{name}: the save before it (never loaded)",
            _ => name,
        };
    }

    private static string When(SaveSummary save) =>
        save.WrittenAt == DateTimeOffset.MinValue ? "time unknown" : save.WrittenAt.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture);

    private static string Played(SaveSummary save)
    {
        int minutes = (int)(save.PlaytimeSeconds / 60);
        return minutes >= 60 ? $"{minutes / 60} h {minutes % 60:00} min played" : $"{minutes} min played";
    }

    private static void Action(Container parent, string text, Action pressed, bool focus = false)
    {
        var button = new Button { Text = text, Alignment = HorizontalAlignment.Left };
        button.AddThemeFontSizeOverride("font_size", 18);
        // After the press has been handled: what it does may rebuild the list that holds the button.
        button.Pressed += () => Callable.From(pressed).CallDeferred();
        parent.AddChild(button);
        if (focus)
            button.CallDeferred(Control.MethodName.GrabFocus);
    }

    private static void Line(Container parent, string text, int size)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        parent.AddChild(label);
    }

    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
}
