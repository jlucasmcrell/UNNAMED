// UNNAMED Presentation - the heads-up display and the debug overlay (PROTOTYPE.md §4.1 UI panels; WORLD_ARCHITECTURE.md §6.2)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// One HUD for every camera distance (CAMERA_PERSPECTIVE_AND_PRESENTATION.md §19): pools and level, the interaction
/// prompt, short-lived notices, a crosshair in first person, and the F3 overlay with frame timing and every cell's tier.
/// Combat (M3c) adds health and stamina bars, active effects, the target's health, a short combat log, and a death recap
/// that names what killed the character (ROADMAP.md M3c: a tester can name what killed them). Magic (M3e) adds Focus and
/// Strain bars - Strain marked once the character is Strained - and the formulas on keys 4 to 6, with the one being cast. The owner's M6
/// playtest adds the heading compass at the top right (the quest tracker moves below it) and the aiming reticle. The Phase-1 asset
/// integration adds the icon manifest's tiles: each pool's beside its bar, the formulas' on their keys, the active effects', and the
/// companion's order (<see cref="UseIcons"/>); without the asset workspace the text stands alone.
/// </summary>
public partial class Hud : CanvasLayer
{
    private readonly Label _status = Text(18);
    private readonly Label _prompt = Text(22);
    private readonly Label _debug = Text(15);
    private readonly Label _tracker = Text(18);
    private readonly Label _companions = Text(18);
    private readonly TextureRect _companionIcon = new() { CustomMinimumSize = new Vector2(28, 28), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
    private readonly HBoxContainer _formulaIcons = new();
    private readonly HBoxContainer _effectIcons = new();
    private readonly Dictionary<string, TextureRect> _poolIcons = new(StringComparer.Ordinal);
    private HudIcons _icons = HudIcons.None;
    private string _formulaKeys = string.Empty;
    private string _effectKeys = string.Empty;
    private string? _companionKey;
    private readonly Label _crosshair = Text(22);
    private readonly VBoxContainer _toasts = new();
    private readonly List<(Label Label, double Expires)> _live = new();
    private readonly ProgressBar _health = Bar(new Color(0.75f, 0.16f, 0.14f));
    private readonly ProgressBar _stamina = Bar(new Color(0.85f, 0.7f, 0.2f));
    private readonly ProgressBar _focus = Bar(new Color(0.25f, 0.45f, 0.85f));
    private readonly ProgressBar _strain = Bar(new Color(0.55f, 0.3f, 0.7f));
    private readonly Label _magic = Text(17);
    private readonly Label _effects = Text(17);
    private readonly Label _targetName = Text(18);
    private readonly ProgressBar _target = Bar(new Color(0.7f, 0.2f, 0.18f));
    private readonly Label _log = Text(16);
    private readonly Queue<string> _logLines = new();
    private readonly Compass _compass = new();
    private readonly Reticle _reticle = new();
    private readonly PanelContainer _death = new();
    private readonly Label _deathText = Text(20);
    private double _deathUntil;
    private double _clock;

    public override void _Ready()
    {
        _status.Position = new Vector2(24, 20);
        AddChild(_status);

        var companions = new HBoxContainer { Position = new Vector2(24, 112) };
        companions.AddChild(_companionIcon);
        companions.AddChild(_companions);
        AddChild(companions);

        _prompt.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _prompt.HorizontalAlignment = HorizontalAlignment.Center;
        _prompt.Position = new Vector2(-300, -140);
        _prompt.Size = new Vector2(600, 40);
        AddChild(_prompt);

        // Top right, so the notices and the target's bar keep their places above the panels.
        _compass.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _compass.Position = new Vector2(-Compass.Diameter - 44, 10);
        AddChild(_compass);

        _toasts.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _toasts.Position = new Vector2(-300, 60);
        _toasts.Size = new Vector2(600, 200);
        AddChild(_toasts);

        _crosshair.Text = "+";
        _crosshair.SetAnchorsPreset(Control.LayoutPreset.Center);
        _crosshair.Position = new Vector2(-6, -16);
        AddChild(_crosshair);
        AddChild(_reticle);

        _tracker.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _tracker.Position = new Vector2(-470, 134);
        _tracker.Size = new Vector2(450, 120);
        _tracker.HorizontalAlignment = HorizontalAlignment.Right;
        _tracker.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_tracker);

        _debug.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _debug.Position = new Vector2(-520, 20);
        _debug.Size = new Vector2(500, 600);
        _debug.Visible = false;
        AddChild(_debug);

        var vitals = new VBoxContainer();
        vitals.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        vitals.Position = new Vector2(24, -262);
        vitals.AddChild(_formulaIcons);
        vitals.AddChild(_magic);
        var effects = new HBoxContainer();
        effects.AddChild(_effectIcons);
        effects.AddChild(_effects);
        vitals.AddChild(effects);
        foreach (var (key, bar) in new[] { ("health", _health), ("stamina", _stamina), ("focus", _focus), ("strained", _strain) })
        {
            var row = new HBoxContainer();
            var icon = new TextureRect { CustomMinimumSize = new Vector2(20, 20), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Visible = false };
            _poolIcons[key] = icon;
            row.AddChild(icon);
            row.AddChild(bar);
            vitals.AddChild(row);
        }
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

    /// <summary>Focus, and Strain against its tolerance: past three-quarters the bar turns red, and the next working may cost health.</summary>
    public void SetMagicPools(int focus, int maxFocus, int strain, int tolerance, bool strained)
    {
        _focus.MaxValue = Math.Max(1, maxFocus);
        _focus.Value = focus;
        _strain.MaxValue = Math.Max(1, tolerance);
        _strain.Value = strain;
        _strain.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = strained ? new Color(0.9f, 0.2f, 0.3f) : new Color(0.55f, 0.3f, 0.7f) });
    }

    /// <summary>The icon manifest's tiles, through the art bindings: set once the workspace is known.</summary>
    public void UseIcons(HudIcons icons)
    {
        _icons = icons;
        foreach (var (key, icon) in _poolIcons)
        {
            icon.Texture = icons.For(key);
            icon.Visible = icon.Texture is not null;
        }
        _formulaKeys = _effectKeys = string.Empty;
        _companionKey = null;
    }

    /// <summary>The formulas on their keys (their icons, numbered from 4), and the one being cast.</summary>
    public void SetMagic(string text, IReadOnlyList<string> formulas)
    {
        _magic.Text = text;
        string keys = string.Join(",", formulas);
        if (keys == _formulaKeys)
            return;
        _formulaKeys = keys;
        foreach (var child in _formulaIcons.GetChildren())
            child.QueueFree();
        for (int i = 0; i < formulas.Count; i++)
        {
            if (_icons.For(formulas[i]) is null)
                continue;
            var tile = _icons.Tile(formulas[i], 40);
            tile.AddChild(new Label { Text = $"{i + 4}", Position = new Vector2(2, 20) });
            _formulaIcons.AddChild(tile);
        }
    }

    /// <summary>The active effects: their icons (by effect ID, and <c>strained</c>), then their names and time left.</summary>
    public void SetEffects(string text, IReadOnlyList<string> keys)
    {
        _effects.Text = text;
        string joined = string.Join(",", keys);
        if (joined == _effectKeys)
            return;
        _effectKeys = joined;
        foreach (var child in _effectIcons.GetChildren())
            child.QueueFree();
        foreach (string key in keys.Where(k => _icons.For(k) is not null))
            _effectIcons.AddChild(_icons.Tile(key, 26));
    }

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

    /// <summary>The compass: where the view faces, degrees clockwise from north.</summary>
    public void SetHeading(float degrees) => _compass.SetHeading(degrees);

    /// <summary>The pipeline's compass dial, when the asset workspace has one.</summary>
    public void UseCompassDial(Texture2D? dial) => _compass.UseDial(dial);

    /// <summary>The aiming reticle at a point on the screen, or hidden.</summary>
    public void SetReticle(Vector2? point, bool onCreature) => _reticle.Aim(point, onCreature);

    public void SetDebug(string text) => _debug.Text = text;

    /// <summary>The companion HUD (content bible §19): each companion's name, how they are, and follow or wait - nothing more.</summary>
    public void SetCompanions(string? text, string? icon = null)
    {
        _companions.Text = text ?? string.Empty;
        if (icon == _companionKey)
            return;
        _companionKey = icon;
        _companionIcon.Texture = _icons.For(icon);
        _companionIcon.Visible = _companionIcon.Texture is not null;
    }

    /// <summary>The quest tracker (content bible §19): optional and minimal, and out of the way of the debug overlay.</summary>
    public void SetTracker(string? text)
    {
        _tracker.Text = text ?? string.Empty;
        _tracker.Visible = !_debug.Visible;
    }

    public void Toast(string text, double seconds = 4)
    {
        // The same notice again while it still shows is the same notice: it stays up longer rather than stacking.
        int live = _live.FindIndex(t => t.Label.Text == text);
        if (live >= 0)
        {
            _live[live] = (_live[live].Label, Math.Max(_live[live].Expires, _clock + seconds));
            return;
        }
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
        var bar = new ProgressBar { CustomMinimumSize = new Vector2(320, 22), ShowPercentage = false, Step = 1, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
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
