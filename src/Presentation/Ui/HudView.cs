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
    string? Casting, bool Strained);

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

    private const float PoolWidth = 118;
    private const float LogWidth = 452;
    private static readonly Regex Keyed = new(@"^\[([^\]]+)\] (.+)$", RegexOptions.Compiled);
    private static readonly Regex Companion = new(@"^(.*?)\s+\[([^\]]+)\] (.+)$", RegexOptions.Compiled);
    private static readonly Regex Seconds = new(@"^(.*) (\d+s)$", RegexOptions.Compiled);

    private HudIcons _icons = HudIcons.None;
    private double _clock;

    // What steps aside for a sheet at a large interface (the plate and companions, the bottom-left panel, the log, the tracked quest),
    // and what stays in their place: health and stamina, so a screen never hides that the character is being hurt.
    private readonly Control _besides = new() { MouseFilter = MouseFilterEnum.Ignore };
    private bool _aside;
    private bool _sheetOpen;
    private bool _conversing;
    private bool _trackerWanted;

    // The regions (HudRegions): the top left and the right column are clipped to theirs, which stand clear of the dock and the subtitle band.
    private readonly Control _leftClip = new() { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
    private readonly VBoxContainer _leftColumn = new();
    private readonly VBoxContainer _plateBody = new();
    private readonly Control _bottomLeftClip = new() { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
    private readonly VBoxContainer _bottomLeftColumn = new();
    private readonly Control _bottomRightClip = new() { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
    private bool _chatWanted;
    private readonly Control _rightClip = new() { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
    private readonly VBoxContainer _rightColumn = new();
    private readonly Control _trackerHold = new() { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
    private const float RightColumnTop = 150;
    private const float ToastGap = 4;

    // Top left: the character plate.
    private readonly Label _name = HudStyle.Text(HudStyle.Caps, 23, HudStyle.Bone, caps: true);
    private readonly Label _level = HudStyle.Text(HudStyle.Caps, 17, HudStyle.Ember, caps: true);
    private readonly HBoxContainer _xpRow = new();
    private readonly Gauge _xp = new(HudStyle.XpFill, new Vector2(0, 5), quarters: false);
    private readonly Label _xpValue = HudStyle.Text(HudStyle.Strong, 16, HudStyle.Bone);
    private readonly HBoxContainer _tags = new();
    private readonly PanelContainer _debt = new();
    private readonly Label _debtText = HudStyle.Text(HudStyle.Body, 16, HudStyle.Wound);
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

    // Bottom left: the effects and the formula being worked. The vitals and the action bars are the bottom centre's (ActionBarsView).
    private readonly PanelContainer _lowerLeft = new();

    /// <summary>The action bars, at the bottom centre, with the vitals above the main bar.</summary>
    public ActionBarsView Bars { get; } = new() { Name = "ActionBars" };

    /// <summary>What is being said, as captions with the speaker, above the action bars.</summary>
    public SubtitleStrip Subtitles { get; } = new() { Name = "Subtitles" };
    private readonly HFlowContainer _effects = new();
    private string _effectText = string.Empty;
    private string _effectKeys = string.Empty;
    private readonly HBoxContainer _casting = new();
    private readonly Label _castingName = HudStyle.Text(HudStyle.Strong, 16, HudStyle.Bone);
    private readonly Dictionary<string, TextureRect> _poolIcons = new(StringComparer.Ordinal);
    private readonly Gauge _health = new(HudStyle.HealthFill, new Vector2(PoolWidth, 10));
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
    private readonly Label _promptContext = HudStyle.Text(HudStyle.Body, 16, HudStyle.Ash);
    private readonly PanelContainer _log = new();
    private readonly Label[] _logLines = new Label[7];
    private readonly Queue<string> _logQueue = new();

    // Bottom left, over the pools: the last few lines of chat (WP16), quiet; the Chat screen (T) holds the rest.
    private readonly PanelContainer _chat = new();
    private readonly Label[] _chatLines = new Label[4];
    private string _chatText = string.Empty;

    // Top centre, under the target: what just happened that the player should not miss - a level gained, a quest finished.
    private readonly PanelContainer _banner = new();
    private readonly PanelContainer _bannerIcon = HudStyle.IconSlot(null, 48);
    private readonly Label _bannerKind = HudStyle.Text(HudStyle.Caps, 17, HudStyle.Brass, caps: true);
    private readonly Label _bannerTitle = HudStyle.Text(HudStyle.Caps, 34, HudStyle.Bone, caps: true);
    private readonly VBoxContainer _bannerLines = new();
    private readonly List<Announcement> _announcements = new();
    private double _bannerSince;

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

        _besides.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_besides);
        BuildTopLeft();
        BuildBottomLeft();
        BuildTopCentre();
        BuildBanner();
        BuildTracker();
        BuildPromptAndLog();
        BuildMiddle();
        Quiet(this);
        AddChild(Subtitles);
        // After Quiet: the bars take the pointer when it is free, which is what dragging onto them needs.
        AddChild(Bars);
    }

    private void BuildTopLeft()
    {
        // Compact - three short rows ending above y 108 - because the journal, character sheet and Field Guide open at y 110-140 over
        // this corner, where the classic status text ended at y 105.
        var column = _leftColumn;
        column.AddThemeConstantOverride("separation", 8);
        _leftClip.AddChild(column);
        _besides.AddChild(_leftClip);

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
        var point = HudStyle.Text(HudStyle.Body, 16, HudStyle.Ember).Say("A point to spend");
        point.VerticalAlignment = VerticalAlignment.Center;
        point.SizeFlagsVertical = SizeFlags.Fill;
        _point.AddChild(point);
        _tags.AddChild(_point);
        _crouched.AddThemeStyleboxOverride("panel", HudStyle.ChipBox(HudStyle.Iron));
        _crouched.AddChild(HudStyle.Text(HudStyle.Caps, 16, HudStyle.Ash, caps: true).Say("Crouched"));
        _tags.AddChild(_crouched);
        title.AddChild(_tags);
        var inventory = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.End };
        inventory.AddThemeConstantOverride("separation", 6);
        inventory.AddChild(_inventoryKey);
        var inventoryWord = HudStyle.Text(HudStyle.Body, 16, HudStyle.Ash).Say("Inventory");
        inventoryWord.VerticalAlignment = VerticalAlignment.Center;
        inventoryWord.SizeFlagsVertical = SizeFlags.Fill;
        inventory.AddChild(inventoryWord);
        title.AddChild(inventory);
        rows.AddChild(title);

        // Experience: the bar across the plate, its numbers at the end.
        _plateBody.AddThemeConstantOverride("separation", 2);
        _xpRow.AddThemeConstantOverride("separation", 8);
        _xp.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _xp.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _xpRow.AddChild(_xp);
        var xp = HudStyle.Text(HudStyle.Caps, 16, HudStyle.Muted, caps: true).Say("XP");
        xp.VerticalAlignment = VerticalAlignment.Center;
        xp.SizeFlagsVertical = SizeFlags.Fill;
        _xpRow.AddChild(xp);
        _xpRow.AddChild(_xpValue);
        _plateBody.AddChild(_xpRow);

        // What is in hand, and what is carried.
        var gear = new HFlowContainer();
        gear.AddThemeConstantOverride("h_separation", 10);
        _weaponSlot.Visible = false;
        _weaponSlot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        gear.AddChild(_weaponSlot);
        _weapon.VerticalAlignment = VerticalAlignment.Center;
        _weapon.SizeFlagsVertical = SizeFlags.Fill;
        gear.AddChild(_weapon);
        _guarding.AddThemeStyleboxOverride("panel", HudStyle.ChipBox(HudStyle.Ember));
        _guarding.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _guarding.AddChild(HudStyle.Text(HudStyle.Caps, 16, HudStyle.Ember, caps: true).Say("Guarding"));
        gear.AddChild(_guarding);
        gear.AddChild(new Control { CustomMinimumSize = new Vector2(6, 0) });
        gear.AddChild(Pair("Coin", _coin));
        gear.AddChild(Pair("Armor", _armor));
        gear.AddChild(Pair("Carrying", _carrying));
        _plateBody.AddChild(gear);
        rows.AddChild(_plateBody);

        plate.AddChild(rows);
        column.AddChild(plate);

        _companions.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(12, 7));
        _companionRows.AddThemeConstantOverride("separation", 4);
        _companions.AddChild(_companionRows);
        _companions.Visible = false;
        column.AddChild(_companions);
    }

    /// <summary>
    /// The bottom-left panel: the active effects and the formula being worked, shown only while there are any. The pools stand with the
    /// action bars at the bottom centre. Kept short because a full pack's inventory list reaches down to y 838 over this corner.
    /// </summary>
    private void BuildBottomLeft()
    {
        var column = _bottomLeftColumn;
        _bottomLeftClip.AddChild(column);
        _besides.AddChild(_bottomLeftClip);

        _chat.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(12, 6, quiet: true));
        _chat.CustomMinimumSize = new Vector2(420, 0);
        _chat.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        var said = new VBoxContainer();
        said.AddThemeConstantOverride("separation", 1);
        for (int i = 0; i < _chatLines.Length; i++)
        {
            _chatLines[i] = HudStyle.Text(HudStyle.Body, 16, HudStyle.Ash);
            _chatLines[i].AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _chatLines[i].CustomMinimumSize = new Vector2(396, 0);
            _chatLines[i].Visible = false;
            said.AddChild(_chatLines[i]);
        }
        _chat.AddChild(said);
        _chat.Visible = false;
        column.AddChild(_chat);

        var panel = _lowerLeft;
        panel.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(12, 6));
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 4);

        _effects.AddThemeConstantOverride("h_separation", 6);
        _effects.AddThemeConstantOverride("v_separation", 4);
        _effects.Visible = false;
        rows.AddChild(_effects);

        _casting.AddThemeConstantOverride("separation", 8);
        _casting.AddChild(HudStyle.Text(HudStyle.Caps, 16, HudStyle.Ember, caps: true).Say("Casting"));
        _casting.AddChild(_castingName);
        _casting.Visible = false;
        rows.AddChild(_casting);

        // The pools stand with the action bars at the bottom centre (the vitals sit above the main bar, as they do in most such games), in two
        // blocks: health and stamina, then focus, strain and resonance.
        var vitals = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        vitals.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(10, 2));
        // Slim: the body's two pools on one row, and the working pools on a second that shows only while they matter.
        var blocks = new VBoxContainer();
        blocks.AddThemeConstantOverride("separation", 1);
        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 16);
        _magicRow = new HBoxContainer();
        _magicRow.AddThemeConstantOverride("separation", 16);
        var magic = _magicRow;
        magic.Visible = false;
        PoolRow(body, "health", PoolName("Health"), _health, _healthValue);
        PoolRow(body, "stamina", PoolName("Stamina"), _stamina, _staminaValue);
        PoolRow(magic, "focus", PoolName("Focus"), _focus, _focusValue);
        PoolRow(magic, "strained", _strainName, _strain, _strainValue);
        PoolRow(magic, "resonance", PoolName("Resonance"), new Control(), _resonanceValue);
        blocks.AddChild(body);
        blocks.AddChild(magic);
        vitals.AddChild(blocks);
        Quiet(vitals);
        Bars.VitalsHost.AddChild(vitals);

        panel.AddChild(rows);
        panel.Visible = false;
        column.AddChild(panel);
    }

    /// <summary>An enlarged interface: a canvas under the reference's 1080 lines, or text set larger. The HUD then steps aside for screens.</summary>
    public bool Crowded => Size.Y < 1079 || HudStyle.TextScale > 1;

    /// <summary>Whether the HUD has stepped aside for a sheet or a conversation (the hint line waits meanwhile).</summary>
    public bool Aside => _aside;

    /// <summary>
    /// While a sheet is open at an enlarged interface, the HUD steps aside for it rather than the sheet shrinking round it: the plate, the
    /// companions, the bottom-left panel, the log and the tracked quest go, and health and stamina stay at the bottom left.
    /// </summary>
    public void StepAside(bool aside)
    {
        _sheetOpen = aside;
        Apply();
    }

    /// <summary>A conversation is open: at an enlarged interface the HUD steps aside for it and its Learn column, as for a sheet.</summary>
    public void SetConversing(bool conversing)
    {
        _conversing = conversing;
        Apply();
    }

    private void Apply()
    {
        bool aside = _sheetOpen || _conversing && Crowded;
        if (aside == _aside)
            return;
        _aside = aside;
        _besides.Visible = !aside;
        _tracker.Visible = _trackerWanted && !aside;
    }

    private HBoxContainer? _magicRow;

    private void PoolRow(BoxContainer grid, string key, Label name, Control gauge, Label value)
    {
        var icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(18, 18), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, Material = HudStyle.IconMaterial, Visible = false,
            TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        _poolIcons[key] = icon;
        var cell = new Control { CustomMinimumSize = new Vector2(18, 18), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        cell.AddChild(icon);
        grid.AddChild(cell);
        name.CustomMinimumSize = Vector2.Zero;
        grid.AddChild(name);
        gauge.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        grid.AddChild(gauge);
        value.CustomMinimumSize = new Vector2(key == "resonance" ? 0 : 70, 0);
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
    /// The announcement: a soot panel at the top centre, under the target's bar - what kind of moment it is in brass capitals, the
    /// moment itself large, and a line or two of what it brings. It rises and fades in, holds, and fades out; a second waits its turn.
    /// </summary>
    private void BuildBanner()
    {
        _banner.AnchorLeft = _banner.AnchorRight = 0.5f;
        _banner.OffsetTop = _banner.OffsetBottom = BannerTop;
        _banner.GrowHorizontal = GrowDirection.Both;
        _banner.CustomMinimumSize = new Vector2(420, 0);
        _banner.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(28, 12));
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 4);
        // The moment's icon beside its words, the pair centred: a level gained, a quest finished.
        var head = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        head.AddThemeConstantOverride("separation", 16);
        _bannerIcon.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _bannerIcon.Visible = false;
        head.AddChild(_bannerIcon);
        var words = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        words.AddThemeConstantOverride("separation", 0);
        _bannerKind.HorizontalAlignment = HorizontalAlignment.Center;
        words.AddChild(_bannerKind);
        _bannerTitle.HorizontalAlignment = HorizontalAlignment.Center;
        words.AddChild(_bannerTitle);
        head.AddChild(words);
        rows.AddChild(head);
        rows.AddChild(HudStyle.Rule(6));
        _bannerLines.AddThemeConstantOverride("separation", 3);
        rows.AddChild(_bannerLines);
        _banner.AddChild(rows);
        _banner.Visible = false;
        AddChild(_banner);
    }

    /// <summary>
    /// Announce a moment (the owner's first playtest: a level gained or a quest completed showed nothing but a number changing). The
    /// same moment again while it shows or waits only adds its lines.
    /// </summary>
    public void Announce(string kind, string title, Color tone, params (string Text, string? Key)[] lines)
    {
        var same = _announcements.FirstOrDefault(a => a.Kind == kind && a.Title == title);
        if (same is null)
        {
            same = new Announcement(kind, title, tone);
            _announcements.Add(same);
            if (_announcements.Count == 1)
                ShowBanner();
        }
        foreach (var (text, key) in lines)
            AddLine(same, text, key);
    }

    /// <summary>Add what a moment brought ("+40 XP", "Iron Billet") to its announcement while it shows or waits; false when it is gone.</summary>
    public bool AnnounceMore(string kind, string title, string text)
    {
        if (_announcements.FirstOrDefault(a => a.Kind == kind && a.Title == title) is not { } announcement)
            return false;
        AddLine(announcement, text, null);
        return true;
    }

    private void AddLine(Announcement announcement, string text, string? key)
    {
        if (announcement.Lines.Any(l => l.Text == text))
            return;
        announcement.Lines.Add((text, key));
        if (_announcements[0] != announcement)
            return;
        _bannerLines.AddChild(BannerLine(text, key));
        _bannerSince = Math.Min(_bannerSince, BannerHold - 1.5);
    }

    private void ShowBanner()
    {
        var announcement = _announcements[0];
        _bannerKind.Text = announcement.Kind;
        _bannerKind.AddThemeColorOverride("font_color", announcement.Tone);
        _bannerTitle.Text = announcement.Title;
        var icon = MomentIcon(announcement.Kind);
        ((TextureRect)_bannerIcon.GetChild(0)).Texture = icon;
        _bannerIcon.Visible = icon is not null;
        foreach (var child in _bannerLines.GetChildren())
        {
            _bannerLines.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var (text, key) in announcement.Lines)
            _bannerLines.AddChild(BannerLine(text, key));
        _bannerSince = 0;
        _banner.Modulate = new Color(1, 1, 1, 0);
        _banner.Visible = true;
    }

    private static Control BannerLine(string text, string? key)
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        if (key is not null)
            row.AddChild(new KeyCap(key, 16));
        var words = HudStyle.Text(HudStyle.Body, 17, text.StartsWith('+') ? HudStyle.Brass : HudStyle.Ash).Say(text);
        words.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(words);
        return row;
    }

    /// <summary>A moment's icon (the 2026-10-15 set): a level gained, a quest begun or finished, something handed over; null for the rest.</summary>
    private Texture2D? MomentIcon(string? kind)
    {
        string? key = kind switch
        {
            "Level up" => "moment.level_up",
            "Quest complete" => "moment.quest_complete",
            "New quest" => "moment.quest_new",
            "Handed to" => "moment.handed_over",
            _ => null,
        };
        return _icons.Binds(key) ? _icons.For(key) : null;
    }

    private const float BannerTop = 92;
    private const double BannerHold = 4.5, BannerIn = 0.3, BannerOut = 0.8;

    private void AnimateBanner(double delta)
    {
        if (_announcements.Count == 0)
            return;
        _bannerSince += delta;
        double t = _bannerSince;
        double alpha = t < BannerIn ? t / BannerIn : t > BannerHold ? 1 - (t - BannerHold) / BannerOut : 1;
        _banner.Modulate = new Color(1, 1, 1, (float)Math.Clamp(alpha, 0, 1));
        _banner.OffsetTop = _banner.OffsetBottom = BannerTop + (t < BannerIn ? (float)(1 - t / BannerIn) * 10 : 0);
        if (t < BannerHold + BannerOut)
            return;
        _announcements.RemoveAt(0);
        _banner.Visible = false;
        if (_announcements.Count > 0)
            ShowBanner();
    }

    /// <summary>A moment to announce, and the lines it has gathered.</summary>
    private sealed record Announcement(string Kind, string Title, Color Tone)
    {
        public List<(string Text, string? Key)> Lines { get; } = new();
    }

    /// <summary>
    /// Where the HUD reaches into the middle of the screen (the companions under the plate, the tracked quest at the right, and from
    /// below the formulas, quick slots and pools at the left and the combat log at the right): the management sheets keep clear of
    /// these, so a screen never hides what tells the player they are being hurt.
    /// </summary>
    public IEnumerable<Rect2> Intrusions()
    {
        if (_companions.IsVisibleInTree())
            yield return _companions.GetGlobalRect();
        if (_tracker.IsVisibleInTree())
            yield return _tracker.GetGlobalRect().Intersection(_trackerHold.GetGlobalRect());
        if (_lowerLeft.IsVisibleInTree())
            yield return _lowerLeft.GetGlobalRect();
        foreach (var rect in Bars.Rects())
            yield return rect;
        if (_log.IsVisibleInTree())
            yield return _log.GetGlobalRect();
    }

    /// <summary>
    /// The right column, under the compass: the tracked quest, then the notices, right-aligned and newest last. The classic HUD put the
    /// notices at the top centre, where a long run of them reached down under the inventory, trade and journal panels (x 60-1190);
    /// here they never meet those panels, the conversation or the death recap.
    /// </summary>
    private void BuildTracker()
    {
        var column = _rightColumn;
        column.AddThemeConstantOverride("separation", 10);
        _rightClip.AddChild(column);
        AddChild(_rightClip);
        _tracker.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(14, 9));
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 5);
        rows.AddChild(_trackerTitle);
        _objectives.AddThemeConstantOverride("separation", 4);
        rows.AddChild(_objectives);
        _tracker.AddChild(rows);
        _tracker.Visible = false;
        _trackerHold.AddChild(_tracker);
        column.AddChild(_trackerHold);
        _toasts.AddThemeConstantOverride("separation", (int)ToastGap);
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
        // Under it, one quiet line of context: what a node gives, whether someone hires on, what a thing weighs.
        var stack = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        stack.AddThemeConstantOverride("separation", 2);
        stack.AddChild(row);
        _promptContext.HorizontalAlignment = HorizontalAlignment.Center;
        _promptContext.Visible = false;
        stack.AddChild(_promptContext);
        _prompt.AddChild(stack);
        _prompt.Visible = false;
        AddChild(_prompt);

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
        _bottomRightClip.AddChild(_log);
        _besides.AddChild(_bottomRightClip);
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
        var blowsTitle = HudStyle.Text(HudStyle.Caps, 16, HudStyle.Muted, caps: true).Say("The last blows");
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
        Bars.Icons = icons;
        foreach (var (key, icon) in _poolIcons)
        {
            icon.Texture = icons.For(key);
            icon.Visible = icon.Texture is not null;
        }
        _effectKeys = string.Empty;
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

        _casting.Visible = sheet.Casting is not null;
        _lowerLeft.Visible = _casting.Visible || _effects.Visible;
        _castingName.Text = sheet.Casting ?? string.Empty;
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
        // The working pools take a row only while they matter: focus spent, strain held, or strained.
        if (_magicRow is not null)
            _magicRow.Visible = maxFocus > 0 && (focus < maxFocus || strain > 0 || strained);
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
            var name = HudStyle.Text(HudStyle.Body, 16, HudStyle.Bone).Say(match.Success ? match.Groups[1].Value : parts[i]);
            name.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(name);
            if (match.Success)
            {
                var time = HudStyle.Text(HudStyle.Strong, 16, HudStyle.Brass).Say(match.Groups[2].Value);
                time.VerticalAlignment = VerticalAlignment.Center;
                row.AddChild(time);
            }
            chip.AddChild(row);
            _effects.AddChild(chip);
            Quiet(chip);
        }
        _effects.Visible = parts.Length > 0;
        _lowerLeft.Visible = _casting.Visible || _effects.Visible;
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

    /// <summary>Where the prompt stands while it shows, or null: the hint line sits above it.</summary>
    public Rect2? PromptRect => _prompt.IsVisibleInTree() ? _prompt.GetGlobalRect() : null;

    public void SetPrompt(string? text, string? context = null)
    {
        _prompt.Visible = !string.IsNullOrEmpty(text);
        if (text is null)
            return;
        var keyed = Keyed.Match(text);
        _promptKey.Visible = keyed.Success;
        _promptKey.Key = keyed.Success ? keyed.Groups[1].Value : string.Empty;
        _promptText.Text = keyed.Success ? keyed.Groups[2].Value : text;
        _promptContext.Visible = !string.IsNullOrEmpty(context);
        _promptContext.Text = context ?? string.Empty;
    }

    /// <summary>The chat feed's lines, oldest first (at most four); none hides it.</summary>
    public void SetChat(IReadOnlyList<string> lines)
    {
        string text = string.Join("\n", lines);
        if (text == _chatText)
            return;
        _chatText = text;
        var shown = lines.Skip(Math.Max(0, lines.Count - _chatLines.Length)).ToList();
        for (int i = 0; i < _chatLines.Length; i++)
        {
            _chatLines[i].Visible = i < shown.Count;
            _chatLines[i].Text = i < shown.Count ? shown[i] : string.Empty;
        }
        _chatWanted = shown.Count > 0;
        _chat.Visible = _chatWanted;
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
                var order = HudStyle.Text(HudStyle.Body, 16, HudStyle.Ash).Say(match.Groups[3].Value);
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
        _trackerWanted = shown && text.Length > 0;
        _tracker.Visible = _trackerWanted && !_aside;
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

    private PanelContainer? _reading;

    public void Toast(string text, double seconds, bool reading = false)
    {
        if (reading && _reading is not null)
        {
            _live.RemoveAll(t => t.Box == _reading);
            if (GodotObject.IsInstanceValid(_reading)) _reading.QueueFree();
            _reading = null;
        }
        // The same notice again while it still shows is the same notice: it stays up longer rather than stacking.
        int live = _live.FindIndex(t => t.Text == text);
        if (live >= 0)
        {
            _live[live] = (_live[live].Box, text, Math.Max(_live[live].Expires, _clock + seconds));
            return;
        }
        var box = ToastBox(text, reading);
        _toasts.AddChild(box);
        Quiet(box);
        _live.Add((box, text, _clock + seconds));
        if (reading) _reading = box;
    }

    /// <summary>
    /// A notice in its panel. Its words are the classic HUD's; a leading kind ("Quest complete:", "Learned", "Discovered:", "Done:")
    /// is set as a small capital label before the rest, and experience in brass.
    /// </summary>
    private PanelContainer ToastBox(string text, bool reading)
    {
        var box = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkEnd };
        box.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(12, 4));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var (kind, rest) = Headline(text);
        if (MomentIcon(kind ?? (text.StartsWith("Handed to ", StringComparison.Ordinal) ? "Handed to" : null)) is { } moment)
        {
            var slot = HudStyle.IconSlot(moment, 22);
            slot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(slot);
        }
        if (kind is not null)
        {
            var label = HudStyle.Text(HudStyle.Caps, 16, kind == "Done" ? HudStyle.Muted : HudStyle.Ember, caps: true).Say(kind);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.SizeFlagsVertical = SizeFlags.Fill;
            row.AddChild(label);
        }
        bool xp = rest.StartsWith('+') && rest.Contains(" XP", StringComparison.Ordinal);
        var words = HudStyle.Text(HudStyle.Strong, 18, xp ? HudStyle.Brass : HudStyle.Bone).Say(rest);
        // The notices stand in the right column, at most two-fifths of the screen wide: a longer notice wraps.
        float room = Math.Max(HudLayoutRules.MinWidth(18), GetViewportRect().Size.X * 0.4f - 70 - (kind is null ? 0 : HudStyle.Caps.GetStringSize(kind, HorizontalAlignment.Left, -1, HudStyle.Sized(16)).X));
        if (reading)
        {
            words.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            words.CustomMinimumSize = new Vector2(HudLayoutRules.ReadingWidth(GetViewportRect().Size.X, 120, Math.Min(560, room), 18), 0);
        }
        else if (HudStyle.Strong.GetStringSize(rest, HorizontalAlignment.Left, -1, HudStyle.Sized(18)).X > room)
        {
            words.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            words.CustomMinimumSize = new Vector2(room, 0);
        }
        row.AddChild(words);
        box.AddChild(row);
        return box;
    }

    private static (string? Kind, string Words) Headline(string text)
    {
        foreach (string kind in new[] { "Quest complete", "New quest", "Quest failed", "Discovered", "Done", "Skill" })
        {
            if (text.StartsWith(kind + ": ", StringComparison.Ordinal))
                return (kind, text[(kind.Length + 2)..]);
        }
        return text.StartsWith("Learned ", StringComparison.Ordinal) ? ("Learned", text["Learned ".Length..]) : (null, text);
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        // The prompt keeps above the vitals and the bars, however many of them are shown.
        _prompt.OffsetTop = _prompt.OffsetBottom = -(Bars.Height + 14);
        PlaceRegions();
        AnimateBanner(delta);
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

    /// <summary>
    /// Stand the status, the right column and the subtitle band in their regions (<see cref="HudRegions"/>) every frame. The tracker takes what
    /// the notices leave and is clipped to it; a notice that no longer fits, the oldest first, waits out of sight until there is room.
    /// </summary>
    private void PlaceRegions()
    {
        _objectives.Visible = true;
        _tracker.ResetSize();
        var trackerSize = _tracker.GetCombinedMinimumSize();
        _objectives.Visible = false;
        float titleHeight = _tracker.GetCombinedMinimumSize().Y;
        float trackerWanted = _tracker.Visible ? trackerSize.Y : 0;
        float toastsWanted = _live.Sum(t => t.Box.GetCombinedMinimumSize().Y + ToastGap) - (_live.Count > 0 ? ToastGap : 0);
        float columnWidth = Math.Max(_rightColumn.GetCombinedMinimumSize().X, trackerSize.X);
        float columnWanted = trackerWanted + toastsWanted + (trackerWanted > 0 && toastsWanted > 0 ? 10 : 0);
        _plateBody.Visible = true;
        var leftMin = _leftColumn.GetCombinedMinimumSize();
        var dock = Bars.DockSize;
        var regions = HudRegions.Of(Size.X, Size.Y, (dock.X, dock.Y), Subtitles.WantedHeight, (leftMin.X, leftMin.Y), (columnWidth, RightColumnTop - HudRegions.RightTop + columnWanted), Bars.SideWidth);
        Subtitles.Place(regions.Subtitles);

        _leftClip.Position = new Vector2(regions.TopLeft.X, regions.TopLeft.Y);
        _leftClip.Size = new Vector2(regions.TopLeft.Width, regions.TopLeft.Height);
        // Too little room for the whole plate and it shows its name row alone.
        _plateBody.Visible = regions.TopLeft.Height >= leftMin.Y;
        _leftColumn.Size = new Vector2(regions.TopLeft.Width, 0);

        float room = Math.Max(0, regions.TopRight.Bottom - RightColumnTop);
        float toastsKept = Math.Min(toastsWanted, room / 2);
        float trackerRoom = Math.Max(0, room - (toastsKept > 0 ? toastsKept + 10 : 0));
        // Too little room for the whole of it and the tracker shrinks to its title; too little for that and it waits.
        bool whole = trackerRoom >= trackerSize.Y;
        _objectives.Visible = whole;
        _tracker.ResetSize();
        _trackerHold.Visible = whole || trackerRoom >= titleHeight;
        _trackerHold.CustomMinimumSize = new Vector2(trackerSize.X, whole ? trackerSize.Y : titleHeight);
        _tracker.Position = new Vector2(_trackerHold.Size.X - _tracker.Size.X, 0);
        float toastRoom = room - (_trackerHold.Visible && _tracker.Visible ? _trackerHold.CustomMinimumSize.Y + 10 : 0);
        float used = 0;
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            float height = _live[i].Box.GetCombinedMinimumSize().Y;
            _live[i].Box.Visible = used + height <= toastRoom + 0.5f;
            used += height + ToastGap;
        }
        _rightClip.Position = new Vector2(regions.TopRight.X, RightColumnTop);
        _rightClip.Size = new Vector2(regions.TopRight.Width, room);
        _rightColumn.Size = new Vector2(regions.TopRight.Width, 0);

        // Beside the dock: the chat feed narrows to the room and waits when there is none; the combat log shows only when it fits whole.
        _bottomLeftClip.Position = new Vector2(regions.BottomLeft.X, regions.BottomLeft.Y);
        _bottomLeftClip.Size = new Vector2(regions.BottomLeft.Width, regions.BottomLeft.Height);
        float chatWidth = Math.Min(420, regions.BottomLeft.Width);
        _chat.Visible = _chatWanted && chatWidth >= 160;
        _chat.CustomMinimumSize = new Vector2(chatWidth, 0);
        foreach (var line in _chatLines)
            line.CustomMinimumSize = new Vector2(Math.Max(0, chatWidth - 24), 0);
        _bottomLeftColumn.Size = new Vector2(regions.BottomLeft.Width, 0);
        _bottomLeftColumn.Position = new Vector2(0, regions.BottomLeft.Height - _bottomLeftColumn.Size.Y);
        _bottomRightClip.Position = new Vector2(regions.BottomRight.X, regions.BottomRight.Y);
        _bottomRightClip.Size = new Vector2(regions.BottomRight.Width, regions.BottomRight.Height);
        _log.ResetSize();
        _bottomRightClip.Visible = _log.GetCombinedMinimumSize().X <= regions.BottomRight.Width;
        _log.Position = new Vector2(regions.BottomRight.Width - _log.Size.X, regions.BottomRight.Height - _log.Size.Y);
    }

    private static Label PoolName(string name)
    {
        var label = HudStyle.Text(HudStyle.Caps, 16, HudStyle.Muted, caps: true).Say(name);
        label.CustomMinimumSize = new Vector2(76, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }

    private static Label PoolValue()
    {
        var label = HudStyle.Text(HudStyle.Strong, 16, HudStyle.Bone);
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
        var label = HudStyle.Text(HudStyle.Caps, 16, HudStyle.Muted, caps: true).Say(name);
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
        private readonly Label _label = HudStyle.Text(HudStyle.Caps, 16, HudStyle.Ember, caps: true);

        public KeyCap()
            : this(string.Empty)
        {
        }

        public KeyCap(string key, int size = 16)
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
    /// edge; faint quarter marks (tenths for a value read exactly); and a diagonal hatch over the fill when marked (Strained).
    /// </summary>
    public sealed partial class Gauge : Control
    {
        private readonly int _marks;
        private double _value;
        private double _max = 1;
        private Color _fill;
        private bool _hatched;

        public Gauge()
            : this(HudStyle.HealthFill, new Vector2(100, 8))
        {
        }

        /// <summary>A gauge of a fill and size; <paramref name="marks"/> parts it (quarters by default, tenths where a value is read exactly).</summary>
        public Gauge(Color fill, Vector2 size, bool quarters = true, int marks = 4)
        {
            _fill = fill;
            _marks = quarters ? marks : 0;
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
            for (int q = 1; q < _marks; q++)
                DrawRect(new Rect2(Mathf.Round(inner.Position.X + inner.Size.X * q / _marks), inner.Position.Y, 1, inner.Size.Y), new Color(0, 0, 0, 0.3f));
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
