// UNNAMED Presentation - the start screen and the saves list (the Phase-1 technical audit, B-01; the Field Instrument UI)
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
/// A STANDARD sheet: problems first, then a row a slot with its copies indented under it, and how saving happens in the hint.
/// </summary>
public partial class SavesPanel : CanvasLayer
{
    private Sheet _sheet = null!;
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
        _sheet = UiKit.Sheet(this, "Saves", "saves", UiKit.Standard);
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
        _sheet.Head(_atStart ? "Otherreach" : "Saves", _atStart ? null : "saves", closable: !_atStart);
        _sheet.SetHint(_atStart ? "" : $"{HelpPanel.Key("quicksave")} quicksaves · {HelpPanel.Key("quickload")} quickloads · autosaves are kept as you play");
        UiKit.Clear(_sheet.Body);
        Control? focus = null;

        if (_failure is not null)
            _sheet.Body.AddChild(UiKit.Callout(HudStyle.Wound, _failure));
        foreach (var passed in choice.PassedOver)
            _sheet.Body.AddChild(UiKit.Callout(HudStyle.Wound,
                $"{Describe(passed.Slot, passed.Copy)} ({When(passed)}) is not offered to Continue: {passed.Problem}. Its other copies are listed below."));

        if (_atStart)
        {
            var actions = new VBoxContainer();
            actions.AddThemeConstantOverride("separation", 8);
            if (choice.Continue is { } next)
            {
                var resume = Action($"Continue   {Describe(next.Slot, next.Copy)}, {When(next)}, {Played(next)}", () => Load?.Invoke(next.Slot, next.Copy), UiKit.Kind.Primary);
                resume.Alignment = HorizontalAlignment.Left;
                actions.AddChild(resume);
                focus = resume;
            }
            if (_confirming)
            {
                actions.AddChild(UiKit.Callout(HudStyle.Ember,
                    "A new game uses the same save slots. Its autosaves and quicksaves will replace these over time. Start a new game?"));
                var answers = new HBoxContainer();
                answers.AddThemeConstantOverride("separation", 8);
                answers.AddChild(Action("Yes, start a new game", () => NewGame?.Invoke(), UiKit.Kind.Destructive));
                var no = Action("No", () =>
                {
                    _confirming = false;
                    Render();
                });
                answers.AddChild(no);
                actions.AddChild(answers);
                focus = no;
            }
            else
            {
                var fresh = Action("New game", () =>
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
                }, choice.Continue is null ? UiKit.Kind.Primary : UiKit.Kind.Normal);
                fresh.Alignment = HorizontalAlignment.Left;
                actions.AddChild(fresh);
                focus ??= fresh;
            }
            var quit = Action("Quit", () => Quit?.Invoke(), UiKit.Kind.Destructive);
            quit.Alignment = HorizontalAlignment.Left;
            actions.AddChild(quit);
            _sheet.Body.AddChild(actions);
        }

        var list = UiKit.Section("Saves", choice.Saves.IsEmpty ? "" : $"{choice.Saves.Length}, newest first");
        list.AddThemeConstantOverride("separation", 4);
        if (choice.Saves.IsEmpty)
            list.AddChild(UiKit.EmptyState("No saves yet", "No saves yet.", "Quicksave", "quicksave"));
        bool newest = true;
        foreach (var save in choice.Saves)
        {
            var load = Row(list, save, newest && save.Problem is null);
            if (newest && !_atStart)
                focus ??= load;
            newest = false;
            foreach (var copy in _session.OtherCopies(save.Slot))
                Row(list, copy, false);
        }
        _sheet.Body.AddChild(list);
        if (!_atStart)
        {
            var back = Action("Back to the game", () => Closed?.Invoke());
            back.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            _sheet.Body.AddChild(back);
            focus ??= back;
        }
        if (focus is not null)
            focus.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>A save's row: its name, when it was written and how long was played; Load at the right (primary on the newest).</summary>
    private Button? Row(VBoxContainer list, SaveSummary save, bool primary)
    {
        bool copy = save.Copy != SaveCopy.Current;
        var row = UiKit.Row(null, Describe(save.Slot, save.Copy), $"{When(save)} · {Played(save)}", copy ? HudStyle.Ash : HudStyle.Bone, copy ? 32 : 40);
        if (copy)
            row.Root.AddThemeConstantOverride("separation", 12);
        Button? load = null;
        // A damaged save may still give up what survives (§7.2: partial recovery beats none); one this build cannot read, nothing.
        if (save.Problem is null || save.Problem.StartsWith("it is damaged", StringComparison.Ordinal))
        {
            load = Action(save.Problem is null ? "Load" : "Load what survives", () => Load?.Invoke(save.Slot, save.Copy),
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

    private static string When(SaveSummary save) =>
        save.WrittenAt == DateTimeOffset.MinValue ? "time unknown" : save.WrittenAt.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture);

    private static string Played(SaveSummary save)
    {
        int minutes = (int)(save.PlaytimeSeconds / 60);
        return minutes >= 60 ? $"{minutes / 60} h {minutes % 60:00} min played" : $"{minutes} min played";
    }

    /// <summary>A button whose press runs after the press is handled: what it does may rebuild the list that holds it.</summary>
    private static Button Action(string text, Action pressed, UiKit.Kind kind = UiKit.Kind.Normal, bool compact = false) =>
        UiKit.Action(text, () => Callable.From(pressed).CallDeferred(), kind, compact, id: text);
}
