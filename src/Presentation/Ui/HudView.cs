// UNNAMED Presentation - the production HUD: the classic HUD's information, laid out and finished (Phase B, the HUD finish)
// Godot presentation only: no gameplay state lives here (D-11)

using System.Text.RegularExpressions;
using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The status the classic HUD prints as its top-left text, as values, so the production HUD can lay it out as widgets rather than
/// parse a sentence. <see cref="Main"/> composes both from the same view in the same frame; neither holds state.
/// </summary>
public sealed record HudSheet(
    string Name, int Level, bool LevelCapped, long Xp, long XpToNext, long XpDebt, string? PointKey, bool Crouched,
    long Focus, long FocusMax, long Strain, long StrainTolerance, long Resonance,
    string Wielded, string? WieldedId, bool Guarding, long Coin, int Armor, double CarriedKg, double CarryLimitKg, string InventoryKey,
    string? Casting, IReadOnlyList<HudFormula> Formulas, bool Strained);

/// <summary>A formula on its key: its ID (for its icon), its name and its Focus cost.</summary>
public readonly record struct HudFormula(string Id, string Name, int FocusCost);

/// <summary>
/// The production HUD (visual option <c>hud=production</c>, which every quality tier selects). It shows what the classic HUD shows, at
/// the same moments - nothing is added to or taken from the information, only its presentation changes (the Player Journey design's
/// FE-2 information architecture, which decides what shows when, is not implemented here):
/// top left, the character plate (name, level, XP as a bar and in numbers, debt, a point to spend, crouched; the wielded item and
/// guarding, coin, armour, carried weight, the inventory key) and the companions; bottom left, the formulas on their keys with their
/// cost and the one being worked, the active effects, and the pools as gauges with their values (Strain hatched once Strained) and
/// Resonance; top centre, the target and the notices; top right, the compass and the tracked quest; bottom centre, the prompt with its
/// key; bottom right, the combat log; the death recap in the middle. Every piece sits in a soot panel so no word floats on bright ground.
/// </summary>
public partial class HudView : Control
{
    /// <summary>The safe-area margin from every screen edge, in the 1920 x 1080 layout.</summary>
    public const float Margin = 28;

    private const float PoolWidth = 232;
    private const float LogWidth = 452;
    private static readonly Regex Keyed = new(@"^\[([^\]]+)\] (.+)$", RegexOptions.Compiled);
    private static readonly Regex Companion = new(@"^(.*?)\s+\[([^\]]+)\] (.+)$", RegexOptions.Compiled);
    private static readonly Regex Seconds = new(@"^(.*) (\d+s)$", RegexOptions.Compiled);

    private HudIcons _icons = HudIcons.None;
    private double _clock;

    // Top left: the character plate.
    private readonly Label _name = HudStyle.Text(HudStyle.Caps, 23, HudStyle.Bone, caps: true);
    private readonly Label _level = HudStyle.Text(HudStyle.Caps, 17, HudStyle.Ember, caps: true);
    private readonly HBoxContainer _xpRow = new();
    private readonly Gauge _xp = new(HudStyle.XpFill, new Vector2(0, 5), quarters: false);
    private readonly Label _xpValue = HudStyle.Text(HudStyle.Strong, 15, HudStyle.Bone);
    private readonly HBoxContainer _tags = new();
    private readonly PanelContainer _debt = new();
    private readonly Label _debtText = HudStyle.Text(HudStyle.Body, 15, HudStyle.Wound);
    private readonly HBoxContainer _point = new();
    private readonly KeyCap _pointKey = new();
    private readonly PanelContainer _crouched = new();
    private readonly PanelContainer _weaponSlot = HudStyle.IconSlot(null, 18);
    private string? _weaponId;
    private readonly Label _weapon = HudStyle.Text(HudStyle.Strong, 17, HudStyle.Bone);
    private readonly PanelContainer _guarding = new();
    private readonly Label _coin = HudStyle.Text(HudStyle.Strong, 16, HudStyle.Bone);
    private readonly Label _armor = HudStyle.Text(HudStyle.Strong, 16, HudStyle.Bone);
    private readonly Label _carrying = HudStyle.Text(HudStyle.Strong, 16, HudStyle.Bone);
    private readonly KeyCap _inventoryKey = new();

    // Top left, under the plate: the companions.
    private readonly PanelContainer _companions = new();
    private readonly VBoxContainer _companionRows = new();
    private string _companionText = string.Empty;
    private string? _companionIcon;

    // Bottom left: the effects, the formulas and the pools.
    private readonly HFlowContainer _effects = new();
    private string _effectText = string.Empty;
    private string _effectKeys = string.Empty;
    private readonly VBoxContainer _spells = new();
    private readonly HBoxContainer _casting = new();
    private readonly Label _castingName = HudStyle.Text(HudStyle.Strong, 16, HudStyle.Bone);
    private readonly HBoxContainer _slots = new();
    private readonly List<(PanelContainer Frame, string Name)> _slotFrames = new();
    private string _formulaKeys = string.Empty;
    private string? _castingNow;
    private readonly Dictionary<string, TextureRect> _poolIcons = new(StringComparer.Ordinal);
    private readonly Gauge _health = new(HudStyle.HealthFill, new Vector2(PoolWidth, 12));
    private readonly Gauge _stamina = new(HudStyle.StaminaFill, new Vector2(PoolWidth, 8));
    private readonly Gauge _focus = new(HudStyle.FocusFill, new Vector2(PoolWidth, 8));
    private readonly Gauge _strain = new(HudStyle.StrainFill, new Vector2(PoolWidth, 8));
    private readonly Label _healthValue = PoolValue();
    private readonly Label _staminaValue = PoolValue();
    private readonly Label _focusValue = PoolValue();
    private readonly Label _strainValue = PoolValue();
    private readonly Label _resonanceValue = PoolValue();
    private readonly Label _strainName = PoolName("Strain");
    private bool? _strainedShown;

    // Top centre: the target, then the notices.
    private readonly PanelContainer _targetBox = new();
    private readonly Label _targetName = HudStyle.Text(HudStyle.Caps, 18, HudStyle.Bone, caps: true);
    private readonly Gauge _target = new(HudStyle.HealthFill, new Vector2(320, 9));
    private readonly VBoxContainer _toasts = new();
    private readonly List<(Control Box, string Text, double Expires)> _live = new();

    // Top right, under the compass: the tracked quest.
    private readonly PanelContainer _tracker = new();
    private readonly Label _trackerTitle = HudStyle.Text(HudStyle.Caps, 17, HudStyle.Ember);
    private readonly VBoxContainer _objectives = new();
    private string _trackerText = string.Empty;

    // Bottom centre and bottom right: the prompt and the log.
    private readonly PanelContainer _prompt = new();
    private readonly KeyCap _promptKey = new();
    private readonly Label _promptText = HudStyle.Text(HudStyle.Strong, 19, HudStyle.Bone);
    private readonly PanelContainer _log = new();
    private readonly Label[] _logLines = new Label[7];
    private readonly Queue<string> _logQueue = new();

    // The middle: the crosshair and the death recap.
    private readonly Crosshair _crosshair = new();
    private readonly PanelContainer _death = new();
    private readonly Label _deathTitle = HudStyle.Text(HudStyle.Caps, 34, HudStyle.Bone, caps: true);
    private readonly Label _deathCause = HudStyle.Text(HudStyle.Strong, 19, HudStyle.Ash);
    private readonly Control _deathBlows = new VBoxContainer();
    private readonly Label _deathBlowsText = HudStyle.Text(HudStyle.Body, 17, HudStyle.Bone);
    private readonly Label _deathFooter = HudStyle.Text(HudStyle.Body, 16, HudStyle.Ash);
    private double _deathUntil;

    public override void _Ready()
    {
        Theme = HudStyle.Theme;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        BuildTopLeft();
        BuildBottomLeft();
        BuildTopCentre();
        BuildTracker();
        BuildPromptAndLog();
        BuildMiddle();
        Quiet(this);
    }

    private void BuildTopLeft()
    {
        // Compact - three short rows ending above y 108 - because the journal, character sheet and Field Guide open at y 110-140 over
        // this corner, where the classic status text ended at y 105.
        var column = new VBoxContainer { Position = new Vector2(Margin, 16) };
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        var plate = new PanelContainer { CustomMinimumSize = new Vector2(420, 0) };
        plate.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(14, 5));
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 2);

        // Name, level, what is owed or waiting, and the inventory key at the right.
        var title = new HBoxContainer();
        title.AddThemeConstantOverride("separation", 12);
        _name.VerticalAlignment = VerticalAlignment.Center;
        title.AddChild(_name);
        _level.VerticalAlignment = VerticalAlignment.Center;
        _level.SizeFlagsVertical = SizeFlags.Fill;
        title.AddChild(_level);
        _tags.AddThemeConstantOverride("separation", 8);
        _tags.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _debt.AddThemeStyleboxOverride("panel", HudStyle.ChipBox(new Color(HudStyle.Wound, 0.55f)));
        _debt.AddChild(_debtText);
        _tags.AddChild(_debt);
        _point.AddThemeConstantOverride("separation", 6);
        _point.AddChild(_pointKey);
        var point = HudStyle.Text(HudStyle.Body, 15, HudStyle.Ember).Say("A point to spend");
        point.VerticalAlignment = VerticalAlignment.Center;
        point.SizeFlagsVertical = SizeFlags.Fill;
        _point.AddChild(point);
        _tags.AddChild(_point);
        _crouched.AddThemeStyleboxOverride("panel", HudStyle.ChipBox(HudStyle.Iron));
        _crouched.AddChild(HudStyle.Text(HudStyle.Caps, 15, HudStyle.Ash, caps: true).Say("Crouched"));
        _tags.AddChild(_crouched);
        title.AddChild(_tags);
        var inventory = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.End };
        inventory.AddThemeConstantOverride("separation", 6);
        inventory.AddChild(_inventoryKey);
        var inventoryWord = HudStyle.Text(HudStyle.Body, 15, HudStyle.Ash).Say("Inventory");
        inventoryWord.VerticalAlignment = VerticalAlignment.Center;
        inventoryWord.SizeFlagsVertical = SizeFlags.Fill;
        inventory.AddChild(inventoryWord);
        title.AddChild(inventory);
        rows.AddChild(title);

        // Experience: the bar across the plate, its numbers at the end.
        _xpRow.AddThemeConstantOverride("separation", 8);
        _xp.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _xp.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _xpRow.AddChild(_xp);
        var xp = HudStyle.Text(HudStyle.Caps, 15, HudStyle.Muted, caps: true).Say("XP");
        xp.VerticalAlignment = VerticalAlignment.Center;
        xp.SizeFlagsVertical = SizeFlags.Fill;
        _xpRow.AddChild(xp);
        _xpRow.AddChild(_xpValue);
        rows.AddChild(_xpRow);

        // What is in hand, and what is carried.
        var gear = new HBoxContainer();
        gear.AddThemeConstantOverride("separation", 10);
        _weaponSlot.Visible = false;
        _weaponSlot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        gear.AddChild(_weaponSlot);
        _weapon.VerticalAlignment = VerticalAlignment.Center;
        _weapon.SizeFlagsVertical = SizeFlags.Fill;
        gear.AddChild(_weapon);
        _guarding.AddThemeStyleboxOverride("panel", HudStyle.ChipBox(HudStyle.Ember));
        _guarding.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _guarding.AddChild(HudStyle.Text(HudStyle.Caps, 15, HudStyle.Ember, caps: true).Say("Guarding"));
        gear.AddChild(_guarding);
        gear.AddChild(new Control { CustomMinimumSize = new Vector2(6, 0) });
        gear.AddChild(Pair("Coin", _coin));
        gear.AddChild(Pair("Armor", _armor));
        gear.AddChild(Pair("Carrying", _carrying));
        rows.AddChild(gear);

        plate.AddChild(rows);
        column.AddChild(plate);

        _companions.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(12, 7));
        _companionRows.AddThemeConstantOverride("separation", 4);
        _companions.AddChild(_companionRows);
        _companions.Visible = false;
        column.AddChild(_companions);
    }

    /// <summary>
    /// The bottom-left panel: the active effects, the formulas on their keys, and the pools. Kept short (under 205 px with one row of
    /// effects, so from about y 843 down) because a full pack's inventory list reaches down to y 838 over this corner.
    /// </summary>
    private void BuildBottomLeft()
    {
        var column = new VBoxContainer
        {
            AnchorTop = 1, AnchorBottom = 1, OffsetLeft = Margin, OffsetRight = Margin, OffsetTop = -Margin, OffsetBottom = -Margin,
            GrowVertical = GrowDirection.Begin,
        };
        AddChild(column);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(12, 6));
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 4);

        _effects.AddThemeConstantOverride("h_separation", 6);
        _effects.AddThemeConstantOverride("v_separation", 4);
        _effects.Visible = false;
        rows.AddChild(_effects);

        _spells.AddThemeConstantOverride("separation", 5);
        _casting.AddThemeConstantOverride("separation", 8);
        _casting.AddChild(HudStyle.Text(HudStyle.Caps, 15, HudStyle.Ember, caps: true).Say("Casting"));
        _casting.AddChild(_castingName);
        _casting.Visible = false;
        _spells.AddChild(_casting);
        _slots.AddThemeConstantOverride("separation", 14);
        _spells.AddChild(_slots);
        _spells.AddChild(HudStyle.Rule(3));
        _spells.Visible = false;
        rows.AddChild(_spells);

        var pools = new GridContainer { Columns = 4 };
        pools.AddThemeConstantOverride("h_separation", 9);
        pools.AddThemeConstantOverride("v_separation", 2);
        PoolRow(pools, "health", PoolName("Health"), _health, _healthValue);
        PoolRow(pools, "stamina", PoolName("Stamina"), _stamina, _staminaValue);
        PoolRow(pools, "focus", PoolName("Focus"), _focus, _focusValue);
        PoolRow(pools, "strained", _strainName, _strain, _strainValue);
        PoolRow(pools, "resonance", PoolName("Resonance"), new Control(), _resonanceValue);
        rows.AddChild(pools);

        panel.AddChild(rows);
        column.AddChild(panel);
    }

    private void PoolRow(GridContainer grid, string key, Label name, Control gauge, Label value)
    {
        var icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(18, 18), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, Material = HudStyle.IconMaterial, Visible = false,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        _poolIcons[key] = icon;
        var cell = new Control { CustomMinimumSize = new Vector2(18, 18), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        cell.AddChild(icon);
        grid.AddChild(cell);
        grid.AddChild(name);
        gauge.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        grid.AddChild(gauge);
        grid.AddChild(value);
    }

    private void BuildTopCentre()
    {
        _targetBox.AnchorLeft = _targetBox.AnchorRight = 0.5f;
        _targetBox.OffsetTop = _targetBox.OffsetBottom = 18;
        _targetBox.GrowHorizontal = GrowDirection.Both;
        _targetBox.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(16, 7));
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 5);
        _targetName.HorizontalAlignment = HorizontalAlignment.Center;
        rows.AddChild(_targetName);
        rows.AddChild(_target);
        _targetBox.AddChild(rows);
        _targetBox.Visible = false;
        AddChild(_targetBox);
    }

    /// <summary>
    /// The right column, under the compass: the tracked quest, then the notices, right-aligned and newest last. The classic HUD put the
    /// notices at the top centre, where a long run of them reached down under the inventory, trade and journal panels (x 60-1190);
    /// here they never meet those panels, the conversation or the death recap.
    /// </summary>
    private void BuildTracker()
    {
        var column = new VBoxContainer
        {
            AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -Margin, OffsetRight = -Margin, OffsetTop = 150, OffsetBottom = 150,
            GrowHorizontal = GrowDirection.Begin,
        };
        column.AddThemeConstantOverride("separation", 10);
        AddChild(column);
        _tracker.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        _tracker.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(14, 9));
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 5);
        rows.AddChild(_trackerTitle);
        _objectives.AddThemeConstantOverride("separation", 4);
        rows.AddChild(_objectives);
        _tracker.AddChild(rows);
        _tracker.Visible = false;
        column.AddChild(_tracker);
        _toasts.AddThemeConstantOverride("separation", 4);
        column.AddChild(_toasts);
    }

    private void BuildPromptAndLog()
    {
        _prompt.AnchorLeft = _prompt.AnchorRight = 0.5f;
        _prompt.AnchorTop = _prompt.AnchorBottom = 1;
        _prompt.OffsetTop = _prompt.OffsetBottom = -118;
        _prompt.GrowHorizontal = GrowDirection.Both;
        _prompt.GrowVertical = GrowDirection.Begin;
        _prompt.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(14, 7));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(_promptKey);
        _promptText.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_promptText);
        _prompt.AddChild(row);
        _prompt.Visible = false;
        AddChild(_prompt);

        _log.AnchorLeft = _log.AnchorRight = _log.AnchorTop = _log.AnchorBottom = 1;
        _log.OffsetLeft = _log.OffsetRight = -Margin;
        _log.OffsetTop = _log.OffsetBottom = -Margin;
        _log.GrowHorizontal = _log.GrowVertical = GrowDirection.Begin;
        _log.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(12, 7, quiet: true));
        var lines = new VBoxContainer();
        lines.AddThemeConstantOverride("separation", 1);
        for (int i = 0; i < _logLines.Length; i++)
        {
            _logLines[i] = HudStyle.Text(HudStyle.Body, 16, HudStyle.Ash);
            _logLines[i].HorizontalAlignment = HorizontalAlignment.Right;
            _logLines[i].Visible = false;
            lines.AddChild(_logLines[i]);
        }
        _log.AddChild(lines);
        _log.Visible = false;
        AddChild(_log);
    }

    private void BuildMiddle()
    {
        _crosshair.SetAnchorsPreset(LayoutPreset.Center);
        _crosshair.Position = new Vector2(-12, -12);
        _crosshair.Size = new Vector2(24, 24);
        _crosshair.Visible = false;
        AddChild(_crosshair);

        _death.AnchorLeft = _death.AnchorRight = _death.AnchorTop = _death.AnchorBottom = 0.5f;
        _death.OffsetTop = _death.OffsetBottom = -40;
        _death.GrowHorizontal = _death.GrowVertical = GrowDirection.Both;
        _death.CustomMinimumSize = new Vector2(640, 0);
        _death.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(28, 18));
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 6);
        foreach (var label in new[] { _deathTitle, _deathCause, _deathBlowsText, _deathFooter })
        {
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.CustomMinimumSize = new Vector2(584, 0);
        }
        rows.AddChild(_deathTitle);
        rows.AddChild(_deathCause);
        _deathBlows.AddThemeConstantOverride("separation", 4);
        _deathBlows.AddChild(HudStyle.Rule(14));
        var blowsTitle = HudStyle.Text(HudStyle.Caps, 15, HudStyle.Muted, caps: true).Say("The last blows");
        blowsTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _deathBlows.AddChild(blowsTitle);
        _deathBlows.AddChild(_deathBlowsText);
        _deathBlows.AddChild(HudStyle.Rule(14));
        rows.AddChild(_deathBlows);
        rows.AddChild(_deathFooter);
        _death.AddChild(rows);
        _death.Visible = false;
        AddChild(_death);
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
        _companionIcon = null;
        _weaponId = null;
    }

    /// <summary>The plate, the formulas and the pools' values: everything the classic status text says, as widgets.</summary>
    public void SetSheet(HudSheet sheet)
    {
        _name.Text = sheet.Name;
        _level.Text = sheet.LevelCapped ? $"Level {sheet.Level} (the highest)" : $"Level {sheet.Level}";
        _xpRow.Visible = !sheet.LevelCapped;
        if (!sheet.LevelCapped)
        {
            _xp.Set(sheet.Xp, sheet.XpToNext);
            _xpValue.Text = $"{sheet.Xp} / {sheet.XpToNext}";
        }
        _debt.Visible = sheet.XpDebt > 0;
        _debtText.Text = $"Debt {sheet.XpDebt}";
        _point.Visible = sheet.PointKey is not null;
        _pointKey.Key = sheet.PointKey ?? string.Empty;
        _crouched.Visible = sheet.Crouched;
        _tags.Visible = _debt.Visible || _point.Visible || _crouched.Visible;

        _weapon.Text = sheet.Wielded;
        _guarding.Visible = sheet.Guarding;
        if (sheet.WieldedId != _weaponId)
        {
            _weaponId = sheet.WieldedId;
            var icon = _icons.Binds(sheet.WieldedId) ? _icons.For(sheet.WieldedId) : null;
            ((TextureRect)_weaponSlot.GetChild(0)).Texture = icon;
            _weaponSlot.Visible = icon is not null;
        }
        _coin.Text = $"{sheet.Coin}";
        _armor.Text = $"{sheet.Armor}";
        _carrying.Text = $"{sheet.CarriedKg:0.#} / {sheet.CarryLimitKg:0.#} kg";
        _inventoryKey.Key = sheet.InventoryKey;

        _focusValue.Text = $"{sheet.Focus} / {sheet.FocusMax}";
        _strainValue.Text = $"{sheet.Strain} / {sheet.StrainTolerance}";
        _resonanceValue.Text = $"{sheet.Resonance}";
        if (sheet.Strained != _strainedShown)
        {
            _strainedShown = sheet.Strained;
            _strainName.Text = sheet.Strained ? "Strained" : "Strain";
            _strainName.AddThemeColorOverride("font_color", sheet.Strained ? HudStyle.Wound : HudStyle.Muted);
            _strainValue.AddThemeColorOverride("font_color", sheet.Strained ? HudStyle.Wound : HudStyle.Bone);
        }

        SetFormulas(sheet.Formulas, sheet.Casting);
    }

    private void SetFormulas(IReadOnlyList<HudFormula> formulas, string? casting)
    {
        _spells.Visible = formulas.Count > 0;
        _casting.Visible = casting is not null;
        _castingName.Text = casting ?? string.Empty;
        string keys = string.Join("|", formulas.Select(f => $"{f.Id}:{f.Name}:{f.FocusCost}"));
        if (keys != _formulaKeys)
        {
            _formulaKeys = keys;
            _castingNow = null;
            _slotFrames.Clear();
            foreach (var child in _slots.GetChildren())
                child.QueueFree();
            for (int i = 0; i < formulas.Count; i++)
            {
                // A slot: the framed icon with its key on the lower left corner, the name and the Focus cost beside it.
                var formula = formulas[i];
                var unit = new HBoxContainer();
                unit.AddThemeConstantOverride("separation", 8);
                var holder = new Control { CustomMinimumSize = new Vector2(38, 38), SizeFlagsVertical = SizeFlags.ShrinkCenter };
                var frame = HudStyle.IconSlot(_icons.For(formula.Id), 34);
                holder.AddChild(frame);
                var key = new KeyCap($"{i + 4}", 14) { Position = new Vector2(-6, 20) };
                holder.AddChild(key);
                unit.AddChild(holder);
                var words = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
                words.AddThemeConstantOverride("separation", -1);
                words.AddChild(HudStyle.Text(HudStyle.Body, 15, HudStyle.Bone).Say(formula.Name));
                words.AddChild(HudStyle.Text(HudStyle.Caps, 14, HudStyle.Muted, caps: true).Say($"{formula.FocusCost} Focus"));
                unit.AddChild(words);
                _slots.AddChild(unit);
                _slotFrames.Add((frame, formula.Name));
                Quiet(unit);
            }
        }
        if (casting == _castingNow)
            return;
        _castingNow = casting;
        foreach (var (frame, name) in _slotFrames)
            frame.AddThemeStyleboxOverride("panel", HudStyle.SlotBox(active: name == casting));
    }

    public void SetVitals(int health, int maxHealth, int stamina, int maxStamina)
    {
        _health.Set(health, maxHealth);
        _stamina.Set(stamina, maxStamina);
        _healthValue.Text = $"{health} / {maxHealth}";
        _staminaValue.Text = $"{stamina} / {maxStamina}";
    }

    /// <summary>Focus, and Strain against its tolerance: once Strained the gauge turns red and hatched, and its name says so.</summary>
    public void SetMagicPools(int focus, int maxFocus, int strain, int tolerance, bool strained)
    {
        _focus.Set(focus, Math.Max(1, maxFocus));
        _strain.Set(strain, Math.Max(1, tolerance));
        _strain.Mark(strained ? HudStyle.StrainedFill : HudStyle.StrainFill, strained);
    }

    /// <summary>The active effects (their text as the HUD writes it, "Weakened 60s   Bleeding x3 5s"), each a chip with its icon.</summary>
    public void SetEffects(string text, IReadOnlyList<string> keys)
    {
        string joined = string.Join(",", keys);
        if (text == _effectText && joined == _effectKeys)
            return;
        _effectText = text;
        _effectKeys = joined;
        foreach (var child in _effects.GetChildren())
            child.QueueFree();
        string[] parts = text.Length == 0 ? Array.Empty<string>() : text.Split("   ");
        for (int i = 0; i < parts.Length; i++)
        {
            var chip = new PanelContainer();
            var box = HudStyle.ChipBox(HudStyle.Iron);
            box.ContentMarginLeft = 2;
            box.ContentMarginTop = box.ContentMarginBottom = 1;
            chip.AddThemeStyleboxOverride("panel", box);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            var icon = i < keys.Count ? _icons.For(keys[i]) : null;
            if (icon is not null)
                row.AddChild(HudStyle.IconSlot(icon, 16));
            var match = Seconds.Match(parts[i]);
            var name = HudStyle.Text(HudStyle.Body, 15, HudStyle.Bone).Say(match.Success ? match.Groups[1].Value : parts[i]);
            name.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(name);
            if (match.Success)
            {
                var time = HudStyle.Text(HudStyle.Strong, 15, HudStyle.Brass).Say(match.Groups[2].Value);
                time.VerticalAlignment = VerticalAlignment.Center;
                row.AddChild(time);
            }
            chip.AddChild(row);
            _effects.AddChild(chip);
            Quiet(chip);
        }
        _effects.Visible = parts.Length > 0;
    }

    public void SetTarget(string? name, int health, int maxHealth)
    {
        _targetBox.Visible = name is not null;
        _targetName.Text = name ?? string.Empty;
        _target.Set(health, Math.Max(1, maxHealth));
    }

    public void Log(string line)
    {
        _logQueue.Enqueue(line);
        while (_logQueue.Count > _logLines.Length)
            _logQueue.Dequeue();
        int i = 0;
        foreach (string text in _logQueue)
        {
            var label = _logLines[i];
            label.Text = text;
            // As wide as its words, up to LogWidth; a longer line wraps rather than reaching under the conversation panel.
            bool wraps = HudStyle.Body.GetStringSize(text, HorizontalAlignment.Left, -1, 16).X > LogWidth;
            label.AutowrapMode = wraps ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off;
            label.CustomMinimumSize = new Vector2(wraps ? LogWidth : 0, 0);
            label.AddThemeColorOverride("font_color", LogTone(text, newest: i == _logQueue.Count - 1));
            label.Visible = true;
            i++;
        }
        _log.Visible = i > 0;
    }

    /// <summary>A log line's colour: what hurt the character warm red, the newest line brighter, the rest ash. The words carry it too.</summary>
    private static Color LogTone(string line, bool newest)
    {
        if (line.Contains(" hits your ", StringComparison.Ordinal) || line.StartsWith("Strain backlash", StringComparison.Ordinal)
            || line.StartsWith("Bleeding: -", StringComparison.Ordinal))
            return HudStyle.Wound;
        return newest ? HudStyle.Bone : HudStyle.Ash;
    }

    public void ShowDeath(string text, double seconds)
    {
        // The recap as the HUD writes it: "You died - killed by X.\n\nThe last blows:\n...\n\nXP debt ..."; laid out when it has that shape.
        string[] parts = text.Split("\n\n");
        bool shaped = parts.Length == 3 && parts[0].StartsWith("You died - ", StringComparison.Ordinal)
            && parts[1].StartsWith("The last blows:", StringComparison.Ordinal);
        _deathTitle.Visible = shaped;
        _deathBlows.Visible = shaped;
        _deathFooter.Visible = shaped;
        if (shaped)
        {
            _deathTitle.Text = "You died";
            string cause = parts[0]["You died - ".Length..];
            _deathCause.Text = cause.Length > 0 ? char.ToUpperInvariant(cause[0]) + cause[1..] : cause;
            _deathBlowsText.Text = parts[1]["The last blows:".Length..].Trim('\n');
            _deathFooter.Text = parts[2];
        }
        else
            _deathCause.Text = text;
        _death.Visible = true;
        _deathUntil = _clock + seconds;
    }

    public void SetPrompt(string? text)
    {
        _prompt.Visible = !string.IsNullOrEmpty(text);
        if (text is null)
            return;
        var keyed = Keyed.Match(text);
        _promptKey.Visible = keyed.Success;
        _promptKey.Key = keyed.Success ? keyed.Groups[1].Value : string.Empty;
        _promptText.Text = keyed.Success ? keyed.Groups[2].Value : text;
    }

    public void SetCrosshair(bool visible) => _crosshair.Visible = visible;

    /// <summary>The companions (each "Name - doing, standing   [G] order"): a row each, the first with its order's icon.</summary>
    public void SetCompanions(string? text, string? icon)
    {
        text ??= string.Empty;
        if (text == _companionText && icon == _companionIcon)
            return;
        _companionText = text;
        _companionIcon = icon;
        foreach (var child in _companionRows.GetChildren())
            child.QueueFree();
        string[] lines = text.Length == 0 ? Array.Empty<string>() : text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            if (i == 0 && _icons.For(icon) is { } texture)
                row.AddChild(HudStyle.IconSlot(texture, 22));
            var match = Companion.Match(lines[i]);
            var who = HudStyle.Text(HudStyle.Body, 16, HudStyle.Bone).Say(match.Success ? match.Groups[1].Value : lines[i]);
            who.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(who);
            if (match.Success)
            {
                row.AddChild(new KeyCap(match.Groups[2].Value));
                var order = HudStyle.Text(HudStyle.Body, 15, HudStyle.Ash).Say(match.Groups[3].Value);
                order.VerticalAlignment = VerticalAlignment.Center;
                row.AddChild(order);
            }
            _companionRows.AddChild(row);
            Quiet(row);
        }
        _companions.Visible = lines.Length > 0;
    }

    /// <summary>The tracked quest ("TITLE\nobjective\n..."), hidden while the debug overlay has the corner.</summary>
    public void SetTracker(string? text, bool shown)
    {
        text ??= string.Empty;
        _tracker.Visible = shown && text.Length > 0;
        if (text == _trackerText)
            return;
        _trackerText = text;
        string[] lines = text.Split('\n');
        _trackerTitle.Text = lines[0];
        foreach (var child in _objectives.GetChildren())
            child.QueueFree();
        foreach (string objective in lines.Skip(1))
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 9);
            row.AddChild(new Diamond());
            var words = HudStyle.Text(HudStyle.Body, 17, HudStyle.Bone).Say(objective);
            words.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            words.CustomMinimumSize = new Vector2(340, 0);
            row.AddChild(words);
            _objectives.AddChild(row);
            Quiet(row);
        }
    }

    public void Toast(string text, double seconds)
    {
        // The same notice again while it still shows is the same notice: it stays up longer rather than stacking.
        int live = _live.FindIndex(t => t.Text == text);
        if (live >= 0)
        {
            _live[live] = (_live[live].Box, text, Math.Max(_live[live].Expires, _clock + seconds));
            return;
        }
        var box = ToastBox(text);
        _toasts.AddChild(box);
        Quiet(box);
        _live.Add((box, text, _clock + seconds));
    }

    /// <summary>
    /// A notice in its panel. Its words are the classic HUD's; a leading kind ("Quest complete:", "Learned", "Discovered:", "Done:")
    /// is set as a small capital label before the rest, and experience in brass.
    /// </summary>
    private static PanelContainer ToastBox(string text)
    {
        var box = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkEnd };
        box.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(12, 4));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var (kind, rest) = Headline(text);
        if (kind is not null)
        {
            var label = HudStyle.Text(HudStyle.Caps, 15, kind == "Done" ? HudStyle.Muted : HudStyle.Ember, caps: true).Say(kind);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.SizeFlagsVertical = SizeFlags.Fill;
            row.AddChild(label);
        }
        bool xp = rest.StartsWith('+') && rest.Contains(" XP", StringComparison.Ordinal);
        row.AddChild(HudStyle.Text(HudStyle.Strong, 18, xp ? HudStyle.Brass : HudStyle.Bone).Say(rest));
        box.AddChild(row);
        return box;
    }

    private static (string? Kind, string Words) Headline(string text)
    {
        foreach (string kind in new[] { "Quest complete", "New quest", "Quest failed", "Discovered", "Done" })
        {
            if (text.StartsWith(kind + ": ", StringComparison.Ordinal))
                return (kind, text[(kind.Length + 2)..]);
        }
        return text.StartsWith("Learned ", StringComparison.Ordinal) ? ("Learned", text["Learned ".Length..]) : (null, text);
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_death.Visible && _clock >= _deathUntil)
            _death.Visible = false;
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var (box, _, expires) = _live[i];
            if (_clock < expires)
            {
                box.Modulate = new Color(1, 1, 1, (float)Math.Clamp(expires - _clock, 0, 1));
                continue;
            }
            box.QueueFree();
            _live.RemoveAt(i);
        }
    }

    private static Label PoolName(string name)
    {
        var label = HudStyle.Text(HudStyle.Caps, 15, HudStyle.Muted, caps: true).Say(name);
        label.CustomMinimumSize = new Vector2(76, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }

    private static Label PoolValue()
    {
        var label = HudStyle.Text(HudStyle.Strong, 15, HudStyle.Bone);
        label.CustomMinimumSize = new Vector2(78, 0);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }

    /// <summary>A small-capital label and its value: "COIN 25".</summary>
    private static HBoxContainer Pair(string name, Label value)
    {
        var pair = new HBoxContainer();
        pair.AddThemeConstantOverride("separation", 6);
        var label = HudStyle.Text(HudStyle.Caps, 15, HudStyle.Muted, caps: true).Say(name);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.SizeFlagsVertical = SizeFlags.Fill;
        pair.AddChild(label);
        value.VerticalAlignment = VerticalAlignment.Center;
        pair.AddChild(value);
        return pair;
    }

    /// <summary>The HUD never takes the mouse: every control under it lets clicks through to the world and the panels.</summary>
    private static void Quiet(Node node)
    {
        if (node is Control control)
            control.MouseFilter = MouseFilterEnum.Ignore;
        foreach (var child in node.GetChildren())
            Quiet(child);
    }

    /// <summary>A key cap: the key's name, in ember, on a raised dark key.</summary>
    public sealed partial class KeyCap : PanelContainer
    {
        private readonly Label _label = HudStyle.Text(HudStyle.Caps, 15, HudStyle.Ember, caps: true);

        public KeyCap()
            : this(string.Empty)
        {
        }

        public KeyCap(string key, int size = 15)
        {
            AddThemeStyleboxOverride("panel", HudStyle.KeyBox());
            MouseFilter = MouseFilterEnum.Ignore;
            SizeFlagsVertical = SizeFlags.ShrinkCenter;
            _label.AddThemeFontSizeOverride("font_size", size);
            _label.HorizontalAlignment = HorizontalAlignment.Center;
            _label.CustomMinimumSize = new Vector2(11, size + 6);
            _label.VerticalAlignment = VerticalAlignment.Center;
            AddChild(_label);
            Key = key;
        }

        public string Key
        {
            get => _label.Text;
            set => _label.Text = value;
        }
    }

    /// <summary>
    /// A pool's gauge: a soot track in a hairline iron frame; the fill in its colour with a lighter upper edge and a bright leading
    /// edge; faint quarter marks; and a diagonal hatch over the fill when marked (Strained).
    /// </summary>
    public sealed partial class Gauge : Control
    {
        private readonly bool _quarters;
        private double _value;
        private double _max = 1;
        private Color _fill;
        private bool _hatched;

        public Gauge()
            : this(HudStyle.HealthFill, new Vector2(100, 8))
        {
        }

        public Gauge(Color fill, Vector2 size, bool quarters = true)
        {
            _fill = fill;
            _quarters = quarters;
            CustomMinimumSize = size;
            MouseFilter = MouseFilterEnum.Ignore;
            TextureRepeat = TextureRepeatEnum.Enabled;
        }

        public void Set(double value, double max)
        {
            max = Math.Max(1, max);
            if (value == _value && max == _max)
                return;
            _value = value;
            _max = max;
            QueueRedraw();
        }

        public void Mark(Color fill, bool hatched)
        {
            if (fill == _fill && hatched == _hatched)
                return;
            _fill = fill;
            _hatched = hatched;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var whole = new Rect2(Vector2.Zero, Size);
            DrawRect(whole, HudStyle.Track);
            var inner = whole.Grow(-1);
            float share = (float)Math.Clamp(_value / _max, 0, 1);
            if (share > 0 && inner.Size.X > 0)
            {
                var fill = new Rect2(inner.Position, new Vector2(Mathf.Max(1, Mathf.Round(inner.Size.X * share)), inner.Size.Y));
                DrawRect(fill, _fill);
                DrawRect(new Rect2(fill.Position, new Vector2(fill.Size.X, Mathf.Max(1, Mathf.Round(fill.Size.Y * 0.4f)))), new Color(1, 1, 1, 0.13f));
                DrawRect(new Rect2(fill.Position.X, fill.End.Y - 1, fill.Size.X, 1), new Color(0, 0, 0, 0.22f));
                if (_hatched)
                    DrawTextureRect(HudStyle.Hatch, fill, true);
                DrawRect(new Rect2(fill.End.X - 1, fill.Position.Y, 1, fill.Size.Y), new Color(1, 1, 1, 0.4f));
            }
            if (_quarters)
            {
                for (int q = 1; q < 4; q++)
                    DrawRect(new Rect2(Mathf.Round(inner.Position.X + inner.Size.X * q / 4f), inner.Position.Y, 1, inner.Size.Y), new Color(0, 0, 0, 0.3f));
            }
            DrawRect(whole.Grow(-0.5f), HudStyle.Iron, false, 1);
        }
    }

    /// <summary>An objective's bullet: a small iron diamond.</summary>
    private sealed partial class Diamond : Control
    {
        public Diamond()
        {
            CustomMinimumSize = new Vector2(8, 22);
            SizeFlagsVertical = SizeFlags.ShrinkBegin;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            var c = new Vector2(4, 11);
            DrawColoredPolygon(new[] { c + new Vector2(0, -4), c + new Vector2(4, 0), c + new Vector2(0, 4), c + new Vector2(-4, 0) }, HudStyle.IronBright);
        }
    }

    /// <summary>The first-person crosshair: four short strokes round an open centre and a dot, dark-edged so it reads on sky or stone.</summary>
    private sealed partial class Crosshair : Control
    {
        public Crosshair() => MouseFilter = MouseFilterEnum.Ignore;

        public override void _Draw()
        {
            var c = Size / 2;
            foreach (var dir in new[] { Vector2.Up, Vector2.Down, Vector2.Left, Vector2.Right })
            {
                DrawLine(c + dir * 4, c + dir * 10, new Color(0, 0, 0, 0.7f), 4);
                DrawLine(c + dir * 4.5f, c + dir * 9.5f, HudStyle.Bone, 2);
            }
            DrawCircle(c, 2.2f, new Color(0, 0, 0, 0.7f));
            DrawCircle(c, 1.2f, HudStyle.Bone);
        }
    }
}

/// <summary>A little fluency for building labels in place.</summary>
internal static class LabelSay
{
    public static Label Say(this Label label, string text)
    {
        label.Text = text;
        return label;
    }
}
