// UNNAMED Presentation - the hint line: one line of guidance low on the HUD, shown a while and gone, never in the way (WP10, §2.10 O1)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Presentation.Settings;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The HUD's hint line (PJ07: no modal tutorials). Non-modal: it takes no input and holds nothing up - the world goes on under it, as
/// it does under everything (no pause). One hint at a time; one that comes while another shows waits its turn, and only a few wait.
/// How long each stays follows the notice duration in Settings.
/// </summary>
public partial class HintLine : CanvasLayer
{
    /// <summary>How long a hint stays at the normal notice duration, in seconds.</summary>
    public const double Seconds = 9;

    private const int Waiting = 3;
    private const double FadeSeconds = 0.6;

    /// <summary>The line's foot above the screen's with no prompt showing, its half width, and its distance from what it keeps clear of.</summary>
    private const float Foot = 150, HalfWidth = 360, Gap = 12;

    private readonly Queue<string> _queue = new();
    private PanelContainer _panel = null!;
    private Label _words = null!;
    private double _left;

    public override void _Ready()
    {
        Layer = 5;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 1, AnchorBottom = 1,
            OffsetLeft = -HalfWidth, OffsetRight = HalfWidth,
            GrowHorizontal = Control.GrowDirection.Both, GrowVertical = Control.GrowDirection.Begin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = HudStyle.Soot, BorderColor = HudStyle.Brass, BorderWidthLeft = 3,
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 8, ContentMarginBottom = 8,
        });
        _words = UiKit.Words(string.Empty, 18, HudStyle.Bone);
        _words.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _words.HorizontalAlignment = HorizontalAlignment.Center;
        _words.MouseFilter = Control.MouseFilterEnum.Ignore;
        _panel.AddChild(_words);
        root.AddChild(_panel);
        AddChild(root);
    }

    /// <summary>The words now on the line, or null when it is empty (the smoke and the screenshot pass read it).</summary>
    public string? Showing => _panel.Visible ? _words.Text : null;

    /// <summary>Show a hint, or queue it behind the one showing; false when the queue is full and the hint is dropped.</summary>
    public bool Show(string words)
    {
        if (_panel.Visible)
        {
            if (_queue.Count >= Waiting)
                return false;
            _queue.Enqueue(words);
            return true;
        }
        _words.Text = words;
        _left = Seconds * SettingsStore.Current.Profile.Interface.NoticeDuration;
        _panel.Modulate = Colors.White;
        _panel.Visible = true;
        return true;
    }

    /// <summary>Everything off the line (a world ending, guidance turned off).</summary>
    public void Clear()
    {
        _queue.Clear();
        _panel.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!_panel.Visible)
            return;
        // While the HUD has stepped aside for a screen at an enlarged interface there is no room for the line: it waits, its time held.
        if (GetTree().Root.FindChild("HudView", true, false) is HudView { Aside: true })
        {
            _panel.Modulate = Colors.Transparent;
            return;
        }
        Place();
        _left -= delta;
        _panel.Modulate = new Color(1, 1, 1, (float)Math.Clamp(_left / FadeSeconds, 0, 1));
        if (_left > 0)
            return;
        _panel.Visible = false;
        if (_queue.TryDequeue(out string? next))
            Show(next);
    }

    /// <summary>
    /// Above the prompt and its context line at any text size (or low, where the prompt stands, while there is none), and clear of the
    /// HUD's bottom-left and bottom-right panels: narrowed beside whichever reaches into its row, as a sheet keeps clear of them.
    /// </summary>
    private void Place()
    {
        var canvas = _panel.GetParentControl().Size;
        var hud = GetTree().Root.FindChild("HudView", true, false) as HudView;
        bool shown = hud is { Visible: true } && hud.IsVisibleInTree();
        float bottom = shown && hud!.PromptRect is { } prompt ? prompt.Position.Y - Gap - canvas.Y : -Foot;
        _panel.OffsetBottom = _panel.OffsetTop = bottom;
        float centre = canvas.X / 2, left = centre - HalfWidth, right = centre + HalfWidth;
        if (shown)
        {
            float top = canvas.Y + bottom - _panel.Size.Y, foot = canvas.Y + bottom;
            foreach (var rect in hud!.Intrusions().Where(r => r.Position.Y < foot && r.End.Y > top))
            {
                if (rect.GetCenter().X < centre)
                    left = Math.Max(left, rect.End.X + Gap);
                else
                    right = Math.Min(right, rect.Position.X - Gap);
            }
        }
        _panel.OffsetLeft = left - centre;
        _panel.OffsetRight = right - centre;
    }
}
