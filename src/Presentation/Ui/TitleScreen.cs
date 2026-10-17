// UNNAMED Presentation - the title screen: the wordmark over the living hollow, and where the game is entered (PJ02 §5)
// Godot presentation only: it reads save summaries and asks Main to start, load or open a screen; nothing here holds game state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Persistence;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// At launch, over the start camera's slow drift across the hollow: a dark panel down the left third with the wordmark and one column
/// of entries - Continue (the newest save that loads, named, with the focus), New game (asked first when saves exist, since its saves
/// take the same slots), Load (every save and its kept copies, to restore one), Settings, Credits and Quit (no question: nothing is
/// unsaved here). A save passed over by Continue is said, never silently skipped. The version and the maker sit at its foot.
/// </summary>
public partial class TitleScreen : CanvasLayer
{
    /// <summary>The maker's line, as the owner set it (2026-10-17).</summary>
    public const string Maker = "“Rift Studios” — J.L. McRell";

    private const float Width = 600;

    private GameSession _session = null!;
    private VBoxContainer _column = null!;
    private Label _version = null!;
    private bool _confirming;
    private string? _failure;

    public void Bind(GameSession session) => _session = session;

    public Action<SaveSummary>? Continue { get; set; }
    public Action? NewGame { get; set; }
    public Action? Load { get; set; }
    public Action? Settings { get; set; }
    public Action? Credits { get; set; }
    public Action? Quit { get; set; }

    /// <summary>What the last run left when it closed unexpectedly (WP10's crash hooks): said at the head of the title until dismissed.</summary>
    public Diagnostics.CrashNotice? CrashNotice { get; set; }

    /// <summary>Opens the folder the crash log is in, on this machine.</summary>
    public Action? OpenLogs { get; set; }

    /// <summary>The build and its content, for the foot of the panel.</summary>
    public string Version { get; set; } = string.Empty;

    public override void _Ready()
    {
        Visible = false;
        Layer = 10;
        var root = new Control { Theme = UiKit.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var panel = new PanelContainer { AnchorBottom = 1, OffsetRight = Width, MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(HudStyle.Soot, 0.86f),
            BorderColor = HudStyle.Iron, BorderWidthRight = 1,
            ContentMarginLeft = 88, ContentMarginRight = 64, ContentMarginTop = 140, ContentMarginBottom = 48,
        });
        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 0);
        var mark = HudStyle.Text(HudStyle.Caps, 76, HudStyle.Bone, caps: true);
        mark.Text = "Otherreach";
        stack.AddChild(mark);
        var rule = new UiKit.TickRule { CustomMinimumSize = new Vector2(0, 10) };
        stack.AddChild(rule);
        stack.AddChild(new Control { CustomMinimumSize = new Vector2(0, 56) });
        _column = new VBoxContainer();
        _column.AddThemeConstantOverride("separation", 10);
        stack.AddChild(_column);
        stack.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        _version = UiKit.Words(string.Empty, 16, HudStyle.Muted);
        stack.AddChild(_version);
        panel.AddChild(stack);
        root.AddChild(panel);
        AddChild(root);
    }

    /// <summary>The title, with the focus on its first entry; <paramref name="failure"/> says why a Continue just failed.</summary>
    public void Open(string? failure = null)
    {
        _failure = failure;
        _confirming = false;
        Visible = true;
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Visible;
        Render();
    }

    public void Close() => Visible = false;

    /// <summary>The focus back on the title, as a screen opened from it closes.</summary>
    public void Refocus()
    {
        if (Visible)
            UiKit.FirstFocusable(_column)?.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>Press Continue as a click would (the resume harness). False when the title offers none.</summary>
    public bool PressContinue()
    {
        if (!Visible || _session.StartChoice().Continue is not { } next)
            return false;
        Continue?.Invoke(next);
        return true;
    }

    private void Render()
    {
        var choice = _session.StartChoice();
        _version.Text = $"{Maker}\n{Version}";
        UiKit.Clear(_column);
        if (_failure is not null)
            _column.AddChild(UiKit.Callout(HudStyle.Wound, _failure));
        if (CrashNotice is { } crash)
        {
            _column.AddChild(UiKit.Callout(HudStyle.Ember,
                $"Otherreach closed unexpectedly last time. What happened is in {System.IO.Path.GetFileName(crash.LogFile)}, kept on this computer only."));
            var answers = new HBoxContainer();
            answers.AddThemeConstantOverride("separation", 8);
            answers.AddChild(UiKit.Action("Open the log folder", () => OpenLogs?.Invoke(), id: "title.crash.open"));
            answers.AddChild(UiKit.Action("Dismiss", Deferred(() =>
            {
                CrashNotice = null;
                Render();
            }), id: "title.crash.dismiss"));
            _column.AddChild(answers);
        }
        foreach (var passed in choice.PassedOver)
            _column.AddChild(UiKit.Callout(HudStyle.Wound,
                $"{SavesPanel.Describe(passed.Slot, passed.Copy)} ({SavesPanel.When(passed)}) could not be continued: {passed.Problem}. Load lists its other copies."));
        Control? focus = null;
        if (choice.Continue is { } next)
        {
            var resume = Entry("Continue", () => Continue?.Invoke(next), UiKit.Kind.Primary,
                $"{SavesPanel.Describe(next.Slot, next.Copy)} · {SavesPanel.When(next)} · {SavesPanel.Played(next)}");
            focus = resume;
        }
        if (_confirming)
        {
            _column.AddChild(UiKit.Callout(HudStyle.Ember,
                "A new game uses the same save slots: its saves will replace these over time. Start a new game?"));
            var answers = new HBoxContainer();
            answers.AddThemeConstantOverride("separation", 8);
            answers.AddChild(UiKit.Action("Yes, start a new game", Deferred(() => NewGame?.Invoke()), UiKit.Kind.Destructive, id: "title.new.yes"));
            var no = UiKit.Action("No", Deferred(() =>
            {
                _confirming = false;
                Render();
            }), id: "title.new.no");
            answers.AddChild(no);
            _column.AddChild(answers);
            focus = no;
        }
        else
        {
            var fresh = Entry("New game", () =>
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
            focus ??= fresh;
        }
        if (!choice.Saves.IsEmpty)
            Entry("Load", () => Load?.Invoke());
        Entry("Settings", () => Settings?.Invoke());
        Entry("Credits", () => Credits?.Invoke());
        _column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });
        Entry("Quit", () => Quit?.Invoke(), UiKit.Kind.Destructive);
        focus?.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>One entry: a full-width button, words at its left, and a quiet line under it where there is one (Continue's save).</summary>
    private Button Entry(string words, Action pressed, UiKit.Kind kind = UiKit.Kind.Normal, string detail = "")
    {
        var button = UiKit.Action(words, Deferred(pressed), kind, id: "title." + words.ToLowerInvariant().Replace(' ', '_'));
        button.Alignment = HorizontalAlignment.Left;
        button.CustomMinimumSize = new Vector2(0, 52);
        button.AddThemeFontSizeOverride("font_size", HudStyle.Sized(22));
        _column.AddChild(button);
        if (detail.Length > 0)
        {
            var line = UiKit.Words(detail, 16, HudStyle.Ash);
            var indent = new MarginContainer();
            indent.AddThemeConstantOverride("margin_left", 14);
            indent.AddThemeConstantOverride("margin_top", -6);
            indent.AddChild(line);
            _column.AddChild(indent);
        }
        return button;
    }

    /// <summary>A press that runs after it is handled: what it does may rebuild the column that holds the button.</summary>
    private static Action Deferred(Action pressed) => () => Callable.From(pressed).CallDeferred();
}
