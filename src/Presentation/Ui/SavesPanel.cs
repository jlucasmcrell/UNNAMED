// UNNAMED Presentation - the start screen and the saves list (the Phase-1 technical audit, B-01; the Field Instrument UI)
// Godot presentation only: it reads save summaries and asks Main to start or load; nothing here holds game state (D-11)

using System.Globalization;
using Godot;
using UNNAMED.Application;
using UNNAMED.Persistence;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// From the title screen's Load (<see cref="TitleScreen"/>): every save with its backups and the save it displaced, each loadable by
/// choice - a restore. In the game (L, or a failed quickload) the same list, and a way back.
/// A save that cannot be loaded says why, and the copies of it that can be loaded are listed beneath it: nothing falls back silently.
/// A STANDARD sheet: problems first, then a row a slot with its copies indented under it, and how saving happens in the hint.
/// </summary>
public partial class SavesPanel : CanvasLayer
{
    private Sheet _sheet = null!;
    private GameSession _session = null!;
    private bool _atStart;
    private string? _failure;

    private Texture2D? _icon;

    public void Bind(GameSession session) => _session = session;

    /// <summary>The screen's own tile in the game, as its System Menu row shows it; the start screen has none.</summary>
    public void UseIcons(HudIcons icons) => _icon = icons.Binds("panel.saves") ? icons.For("panel.saves") : null;

    /// <summary>What the player chose: Main loads it from its own character's folder, and closes the panel once a world runs.</summary>
    public Action<SaveSummary>? Load { get; set; }

    /// <summary>Back: to the game, or to the title screen. Main closes the panel.</summary>
    public Action? Closed { get; set; }

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "Saves", "saves", UiKit.Standard);
    }

    /// <summary>From the title screen: nothing runs until the player chooses.</summary>
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

    /// <summary>A load chosen here failed: say so, and list again - the failed save's other copies are beneath it.</summary>
    public void ShowFailure(string what)
    {
        _atStart = true;
        Open(what);
    }

    public void Close()
    {
        Visible = false;
        _failure = null;
    }

    private void Open(string? failure)
    {
        _failure = failure;
        Visible = true;
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Visible;
        Render();
    }

    private void Render()
    {
        var choice = _session.StartChoice();
        _sheet.Head(_atStart ? "Load" : "Saves", _atStart ? null : "saves", closeWord: _atStart ? "Back" : "Close");
        _sheet.SetIcon(_icon);
        _sheet.SetHint(_atStart ? "A save, or one of the copies kept behind it, restores the world as it was then."
            : $"{HelpPanel.Key("quicksave")} quicksaves · {HelpPanel.Key("quickload")} quickloads · autosaves are kept as you play");
        UiKit.Clear(_sheet.Body);
        Control? focus = null;

        if (_failure is not null)
            _sheet.Body.AddChild(UiKit.Callout(HudStyle.Wound, _failure));
        foreach (var passed in choice.PassedOver)
            _sheet.Body.AddChild(UiKit.Callout(HudStyle.Wound,
                $"{Describe(passed.Slot, passed.Copy)} ({When(passed)}) is not offered to Continue: {passed.Problem}. Its other copies are listed below."));

        var list = UiKit.Section("Saves", choice.Saves.IsEmpty ? "" : $"{choice.Saves.Length}, newest first");
        list.AddThemeConstantOverride("separation", 4);
        if (choice.Saves.IsEmpty)
            list.AddChild(UiKit.EmptyState("No saves yet", "No saves yet.", "Quicksave", "quicksave"));
        // Each character keeps its own slots (OD-14): with more than one, a row says whose save it is.
        var whose = _session.CharacterSaves() is { Length: > 1 } characters
            ? characters.ToDictionary(c => c.Character ?? "", c => c.Name ?? "Unnamed character", StringComparer.Ordinal)
            : null;
        bool newest = true;
        foreach (var save in choice.Saves)
        {
            var load = Row(list, save, newest && save.Problem is null, whose?.GetValueOrDefault(save.Character ?? ""));
            if (newest)
                focus ??= load;
            newest = false;
            foreach (var copy in _session.OtherCopies(save))
                Row(list, copy, false, whose?.GetValueOrDefault(copy.Character ?? ""));
        }
        _sheet.Body.AddChild(list);
        var back = Action(_atStart ? "Back to the title" : "Back to the game", () => Closed?.Invoke());
        back.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _sheet.Body.AddChild(back);
        focus ??= back;
        if (focus is not null)
            focus.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>A save's row: its name, when it was written and how long was played; Load at the right (primary on the newest).</summary>
    private Button? Row(VBoxContainer list, SaveSummary save, bool primary, string? whose)
    {
        bool copy = save.Copy != SaveCopy.Current;
        var row = UiKit.Row(null, Describe(save.Slot, save.Copy), $"{(whose is null ? "" : whose + " · ")}{When(save)} · {Played(save)}", copy ? HudStyle.Ash : HudStyle.Bone, copy ? 32 : 40);
        if (copy)
            row.Root.AddThemeConstantOverride("separation", 12);
        Button? load = null;
        // A damaged save may still give up what survives (§7.2: partial recovery beats none); one this build cannot read, nothing.
        if (save.Problem is null || save.Problem.StartsWith("it is damaged", StringComparison.Ordinal))
        {
            load = Action(save.Problem is null ? "Load" : "Load what survives", () => Load?.Invoke(save),
                primary ? UiKit.Kind.Primary : UiKit.Kind.Normal, compact: copy);
            row.Root.AddChild(load);
        }
        if (save.Problem is { } problem)
        {
            row.Secondary.Text += $"\n{problem}";
            row.Secondary.AddThemeColorOverride("font_color", HudStyle.Wound);
        }
        if (copy)
        {
            var indent = new MarginContainer();
            indent.AddThemeConstantOverride("margin_left", 32);
            indent.AddChild(row.Root);
            list.AddChild(indent);
        }
        else
        {
            list.AddChild(row.Root);
        }
        return load;
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

    internal static string When(SaveSummary save) =>
        save.WrittenAt == DateTimeOffset.MinValue ? "time unknown" : save.WrittenAt.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture);

    internal static string Played(SaveSummary save)
    {
        int minutes = (int)(save.PlaytimeSeconds / 60);
        return minutes >= 60 ? $"{minutes / 60} h {minutes % 60:00} min played" : $"{minutes} min played";
    }

    /// <summary>A button whose press runs after the press is handled: what it does may rebuild the list that holds it.</summary>
    private static Button Action(string text, Action pressed, UiKit.Kind kind = UiKit.Kind.Normal, bool compact = false) =>
        UiKit.Action(text, () => Callable.From(pressed).CallDeferred(), kind, compact, id: text);
}
