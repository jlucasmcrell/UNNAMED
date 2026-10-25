// UNNAMED Presentation - the title screen: the wordmark over the living hollow, and where the game is entered (PJ02 §5)
// Godot presentation only: it reads save summaries and asks Main to start, load or open a screen; nothing here holds game state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Persistence;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// At launch, over the start camera's slow drift across the hollow, as the opening leaves it: the name large across the top (the same
/// <see cref="Wordmark"/> the opening's hit puts there, so the hand-off moves nothing) and at the lower left a dark panel with one column
/// of entries - Continue (the newest save that loads, named, with the focus), New game (the creator: a new character saves in a folder of
/// its own, so nothing is asked), Load (every save and its kept copies, to restore one), Settings, Credits and Quit (no question: nothing
/// is unsaved here). A save passed over by Continue is said, never silently skipped. The version and the maker sit at its foot.
/// </summary>
public partial class TitleScreen : CanvasLayer
{
    /// <summary>The maker's line, as the owner set it (2026-10-17).</summary>
    public const string Maker = "“Rift Studios” — J.L. McRell";

    private const float Width = 600;

    /// <summary>The name's top, as a share of the screen's height.</summary>
    public const float MarkTop = 0.1f;

    /// <summary>As the opening's hit brings the name in: this much larger, and this share of the screen's height lower, than it rests.</summary>
    public const float RevealScale = 1.35f, RevealDrop = 0.24f;

    private Control _panel = null!;
    private Tween? _reveal;

    private GameSession _session = null!;
    private VBoxContainer _column = null!;
    private Label _version = null!;
    private string? _failure;
    private bool _syncDismissed;

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
        root.AddChild(Wordmark());
        var panel = new PanelContainer
        {
            AnchorTop = 1, AnchorBottom = 1, OffsetRight = Width, GrowVertical = Control.GrowDirection.Begin, MouseFilter = Control.MouseFilterEnum.Stop,
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(HudStyle.Soot, 0.8f),
            BorderColor = HudStyle.Iron, BorderWidthRight = 1, BorderWidthTop = 1,
            ContentMarginLeft = 88, ContentMarginRight = 64, ContentMarginTop = 40, ContentMarginBottom = 48,
        });
        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 0);
        _column = new VBoxContainer();
        _column.AddThemeConstantOverride("separation", 10);
        stack.AddChild(_column);
        stack.AddChild(new Control { CustomMinimumSize = new Vector2(0, 28) });
        _version = UiKit.Words(string.Empty, 16, HudStyle.Muted);
        stack.AddChild(_version);
        panel.AddChild(stack);
        root.AddChild(panel);
        _panel = panel;
        AddChild(root);
    }

    /// <summary>The name: large, across the top of the screen, over a tick rule.</summary>
    public static Control Wordmark()
    {
        var box = new VBoxContainer { AnchorRight = 1, AnchorTop = MarkTop, AnchorBottom = MarkTop, MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        var mark = HudStyle.Text(HudStyle.Caps, 132, HudStyle.Bone, caps: true);
        mark.Text = "Otherreach";
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        mark.AddThemeConstantOverride("shadow_offset_y", 3);
        mark.AddThemeConstantOverride("shadow_outline_size", 14);
        box.AddChild(mark);
        box.AddChild(new UiKit.TickRule { CustomMinimumSize = new Vector2(620, 10), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter });
        return box;
    }

    /// <summary>The title, with the focus on its first entry; <paramref name="failure"/> says why a Continue just failed.</summary>
    public void Open(string? failure = null)
    {
        _failure = failure;
        Visible = true;
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Visible;
        Render();
    }

    /// <summary>The title as the opening hands it over: the name already in place, the entries coming in under it.</summary>
    public void Reveal()
    {
        Open();
        _reveal?.Kill();
        _panel.Modulate = new Color(1, 1, 1, 0);
        _reveal = CreateTween();
        _reveal.TweenProperty(_panel, "modulate:a", 1f, 1.0);
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
        if (_session.CloudSyncRoot is { } sync && !_syncDismissed)
        {
            // §3.2 (RK-P06): a sync program can hold, bring back or half-download a save while it is written. Said once a run, never blocking.
            _column.AddChild(UiKit.Callout(HudStyle.Ember,
                $"Your saves are inside {sync}, a folder a cloud-sync program manages. Sync can hold or bring back save files while they are written; saves are safest kept outside it."));
            _column.AddChild(UiKit.Action("Understood", Deferred(() =>
            {
                _syncDismissed = true;
                Render();
            }), id: "title.sync.dismiss"));
        }
        foreach (var passed in choice.PassedOver)
            _column.AddChild(UiKit.Callout(HudStyle.Wound,
                $"{SavesPanel.Describe(passed.Slot, passed.Copy)} ({SavesPanel.When(passed)}) could not be continued. {SaveTrouble.OfProblem(passed.Problem!).Headline} Load lists its other copies."));
        Control? focus = null;
        if (choice.Continue is { } next)
        {
            var resume = Entry("Continue", () => Continue?.Invoke(next), UiKit.Kind.Primary,
                $"{SavesPanel.Describe(next.Slot, next.Copy)} · {SavesPanel.When(next)} · {SavesPanel.Played(next)}");
            focus = resume;
        }
        var fresh = Entry("New game", () => NewGame?.Invoke(), choice.Continue is null ? UiKit.Kind.Primary : UiKit.Kind.Normal);
        focus ??= fresh;
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
