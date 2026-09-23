// UNNAMED Presentation - the heads-up display and the debug overlay (PROTOTYPE.md §4.1 UI panels; WORLD_ARCHITECTURE.md §6.2)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// One HUD for every camera distance (CAMERA_PERSPECTIVE_AND_PRESENTATION.md §19): pools and level, the interaction
/// prompt, short-lived notices, a crosshair in first person, and the F3 overlay with frame timing and every cell's tier.
/// Combat (M3c) adds health and stamina bars, active effects, the target's health, a short combat log, and a death recap
/// that names what killed the character (ROADMAP.md M3c: a tester can name what killed them).
/// </summary>
public partial class Hud : CanvasLayer
{
    private readonly Label _status = Text(18);
    private readonly Label _prompt = Text(22);
    private readonly Label _debug = Text(15);
    private readonly Label _crosshair = Text(22);
    private readonly VBoxContainer _toasts = new();
    private readonly List<(Label Label, double Expires)> _live = new();
    private readonly ProgressBar _health = Bar(new Color(0.75f, 0.16f, 0.14f));
    private readonly ProgressBar _stamina = Bar(new Color(0.85f, 0.7f, 0.2f));
    private readonly Label _effects = Text(17);
    private readonly Label _targetName = Text(18);
    private readonly ProgressBar _target = Bar(new Color(0.7f, 0.2f, 0.18f));
    private readonly Label _log = Text(16);
    private readonly Queue<string> _logLines = new();
    private readonly PanelContainer _death = new();
    private readonly Label _deathText = Text(20);
    private double _deathUntil;
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

        var vitals = new VBoxContainer();
        vitals.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        vitals.Position = new Vector2(24, -150);
        vitals.AddChild(_effects);
        vitals.AddChild(_health);
        vitals.AddChild(_stamina);
        AddChild(vitals);

        var target = new VBoxContainer();
        target.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        target.Position = new Vector2(-160, 16);
        _targetName.HorizontalAlignment = HorizontalAlignment.Center;
        _targetName.CustomMinimumSize = new Vector2(320, 0);
        target.AddChild(_targetName);
        target.AddChild(_target);
        AddChild(target);

        _log.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _log.Position = new Vector2(-560, -190);
        _log.Size = new Vector2(540, 170);
        _log.HorizontalAlignment = HorizontalAlignment.Right;
        _log.VerticalAlignment = VerticalAlignment.Bottom;
        AddChild(_log);

        _death.SetAnchorsPreset(Control.LayoutPreset.Center);
        _death.Position = new Vector2(-330, -150);
        _death.CustomMinimumSize = new Vector2(660, 0);
        _deathText.HorizontalAlignment = HorizontalAlignment.Center;
        _deathText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _death.AddChild(_deathText);
        _death.Visible = false;
        AddChild(_death);
    }

    public void SetVitals(int health, int maxHealth, int stamina, int maxStamina)
    {
        _health.MaxValue = maxHealth;
        _health.Value = health;
        _stamina.MaxValue = maxStamina;
        _stamina.Value = stamina;
        _health.TooltipText = $"{health}/{maxHealth}";
    }

    public void SetEffects(string text) => _effects.Text = text;

    /// <summary>The creature the character is fighting or facing, or nothing.</summary>
    public void SetTarget(string? name, int health, int maxHealth)
    {
        _targetName.Visible = _target.Visible = name is not null;
        _targetName.Text = name ?? string.Empty;
        _target.MaxValue = Math.Max(1, maxHealth);
        _target.Value = health;
    }

    public void Log(string line)
    {
        _logLines.Enqueue(line);
        while (_logLines.Count > 7)
            _logLines.Dequeue();
        _log.Text = string.Join("\n", _logLines);
    }

    public void ShowDeath(string text, double seconds = 8)
    {
        _deathText.Text = text;
        _death.Visible = true;
        _deathUntil = _clock + seconds;
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
        if (_death.Visible && _clock >= _deathUntil)
            _death.Visible = false;
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

    private static ProgressBar Bar(Color fill)
    {
        var bar = new ProgressBar { CustomMinimumSize = new Vector2(320, 22), ShowPercentage = false, Step = 1 };
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = fill });
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.55f) });
        return bar;
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
