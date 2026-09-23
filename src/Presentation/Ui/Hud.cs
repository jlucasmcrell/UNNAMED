// UNNAMED Presentation - the heads-up display and the debug overlay (PROTOTYPE.md §4.1 UI panels; WORLD_ARCHITECTURE.md §6.2)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// One HUD for every camera distance (CAMERA_PERSPECTIVE_AND_PRESENTATION.md §19): pools and level, the interaction
/// prompt, short-lived notices, a crosshair in first person, and the F3 overlay with frame timing and every cell's tier.
/// </summary>
public partial class Hud : CanvasLayer
{
    private readonly Label _status = Text(18);
    private readonly Label _prompt = Text(22);
    private readonly Label _debug = Text(15);
    private readonly Label _crosshair = Text(22);
    private readonly VBoxContainer _toasts = new();
    private readonly List<(Label Label, double Expires)> _live = new();
    private double _clock;

    public override void _Ready()
    {
        _status.Position = new Vector2(24, 20);
        AddChild(_status);

        _prompt.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _prompt.HorizontalAlignment = HorizontalAlignment.Center;
        _prompt.Position = new Vector2(-300, -140);
        _prompt.Size = new Vector2(600, 40);
        AddChild(_prompt);

        _toasts.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _toasts.Position = new Vector2(-300, 60);
        _toasts.Size = new Vector2(600, 200);
        AddChild(_toasts);

        _crosshair.Text = "+";
        _crosshair.SetAnchorsPreset(Control.LayoutPreset.Center);
        _crosshair.Position = new Vector2(-6, -16);
        AddChild(_crosshair);

        _debug.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _debug.Position = new Vector2(-520, 20);
        _debug.Size = new Vector2(500, 600);
        _debug.Visible = false;
        AddChild(_debug);
    }

    public bool DebugVisible
    {
        get => _debug.Visible;
        set => _debug.Visible = value;
    }

    public void SetStatus(string text) => _status.Text = text;

    public void SetPrompt(string? text) => _prompt.Text = text ?? string.Empty;

    public void SetCrosshair(bool visible) => _crosshair.Visible = visible;

    public void SetDebug(string text) => _debug.Text = text;

    public void Toast(string text, double seconds = 4)
    {
        var label = Text(20);
        label.Text = text;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        _toasts.AddChild(label);
        _live.Add((label, _clock + seconds));
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var (label, expires) = _live[i];
            if (_clock < expires)
            {
                label.Modulate = new Color(1, 1, 1, (float)Math.Clamp(expires - _clock, 0, 1));
                continue;
            }
            label.QueueFree();
            _live.RemoveAt(i);
        }
    }

    private static Label Text(int size)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 4);
        return label;
    }
}
