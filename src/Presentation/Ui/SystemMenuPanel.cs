// UNNAMED Presentation - the System Menu: every screen at once, grouped, with its key (the Field Instrument UI, design record 03)
// Godot presentation only: its rows call what Main gives them; nothing here holds game state (D-11)

using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// Escape with nothing open. It says four things: this is the root (every destination, grouped YOU / THE WORLD / SYSTEM, with its icon
/// and its key read live from the input map); the world goes on (the menu suspends the player's control, not time - a light scrim, the
/// HUD live, and one quiet pulsing line that says so); getting back is the first thing (Continue, primary, with the focus, and Escape
/// does it); and quitting is separate, set apart and quiet until pointed at. Across the top, who the character is and where the session
/// stands - the HUD plate's first row at menu scale, from what Main already shows there; no session reaches this panel (03's status
/// strip). A destination replaces the menu; Escape from it returns to play.
/// </summary>
public partial class SystemMenuPanel : CanvasLayer
{
    /// <summary>
    /// A destination: its group, its words, the input action it is on (or none - menu only), its icon key, what choosing it does, and
    /// whether it can be chosen yet (a disabled row is shown greyed). One line in Main's list adds a screen.
    /// </summary>
    public sealed record Entry(string Group, string Label, string? Key, string? Icon, Action Chosen, bool Enabled = true);

    /// <summary>
    /// The strip's facts, as Main has them for the HUD: the name, the level and the XP toward the next (exact), what is owed, the points
    /// waiting, the time played and the last save in words, and the calendar line - the date in numbered tallies and the weather now (WP7).
    /// </summary>
    public sealed record StatusLine(string Name, int Level, bool Capped, long Xp, long XpToNext, long XpDebt, int Points, string Played, string Saved,
        string Date = "");

    private static readonly string[] Groups = { "You", "The world", "System" };

    private Sheet _sheet = null!;
    private Button _continue = null!;
    private ColorRect _pulse = null!;
    private HudIcons _icons = HudIcons.None;
    private double _clock;
    private double _since;
    private string _shown = string.Empty;

    // The status strip, built with the menu and updated in place while it is open (the world, and the clock, run on).
    private Label _name = null!, _level = null!, _xpValue = null!, _debtWord = null!, _session = null!, _date = null!;
    private HBoxContainer _xp = null!, _points = null!, _strip = null!;
    private HudView.Gauge _xpGauge = null!;
    private PanelContainer _debt = null!;
    private PanelContainer? _pointsChip;

    /// <summary>The destinations, in each group's order; Main fills it before the menu first opens.</summary>
    public IReadOnlyList<Entry> Entries { get; set; } = Array.Empty<Entry>();

    /// <summary>Leave the game; set apart from the rest.</summary>
    public Action? Quit { get; set; }

    /// <summary>The character and session facts for the strip; null (or nothing returned) leaves only the world-goes-on cue.</summary>
    public Func<StatusLine?>? Status { get; set; }

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "System menu", null, UiKit.Standard);
        _sheet.Head("System menu", null, closeWord: "Continue");
        _sheet.OnShown(() => _continue.GrabFocus());
        VisibilityChanged += () =>
        {
            if (Visible)
                Build();
        };
    }

    /// <summary>The icon set: each row's tile.</summary>
    public void UseIcons(HudIcons icons) => _icons = icons;

    /// <summary>Choose a destination by its words, as pressing its row does (the UI shots harness); false when there is none.</summary>
    public bool Choose(string label)
    {
        if (Entries.FirstOrDefault(e => e.Label == label && e.Enabled) is not { } entry)
            return false;
        Visible = false;
        entry.Chosen();
        return true;
    }

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        // The one thing that moves on the menu, and it means what it says: a slow ember pulse, two seconds a cycle.
        _clock += delta;
        _pulse.Color = new Color(HudStyle.Ember, 0.7f + 0.3f * Mathf.Sin((float)(_clock * Math.PI)));
        _since += delta;
        if (_since >= 0.5)
        {
            _since = 0;
            ShowStatus();
        }
    }

    private void Build()
    {
        UiKit.Clear(_sheet.Body);
        _sheet.Body.AddChild(Strip());
        _shown = string.Empty;
        ShowStatus();

        _continue = UiKit.Action("Continue", () => Visible = false, UiKit.Kind.Primary, id: "continue");
        _continue.CustomMinimumSize = new Vector2(0, 56);
        _continue.Alignment = HorizontalAlignment.Left;
        _sheet.Body.AddChild(_continue);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        var grid = new List<List<Button>>();
        _pointsChip = null;
        foreach (string group in Groups)
        {
            var column = UiKit.Section(group);
            column.CustomMinimumSize = new Vector2(261, 0);
            column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var rows = new List<Button>();
            foreach (var entry in Entries.Where(e => e.Group == group))
            {
                var row = UiKit.ListRow(entry.Label, entry.Label, height: 48, left: UiKit.Icon(_icons, entry.Icon, 28));
                row.Button.Disabled = !entry.Enabled;
                if (entry.Key == "character")
                {
                    // A point to spend shows where it is spent: an ember count on the Character row, as the HUD plate's cap says.
                    _pointsChip = UiKit.Chip("0", HudStyle.Ember);
                    _pointsChip.Visible = false;
                    row.Right.AddChild(_pointsChip);
                }
                if (entry.Key is not null && UiKit.KeyCap(entry.Key) is { } cap)
                    row.Right.AddChild(cap);
                var chosen = entry.Chosen;
                row.Button.Pressed += () =>
                {
                    Visible = false;
                    chosen();
                };
                column.AddChild(row.Button);
                rows.Add(row.Button);
            }
            if (group == Groups[^1] && Quit is { } quit)
            {
                column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
                column.AddChild(HudStyle.Rule(4));
                var leave = QuitRow(quit);
                column.AddChild(leave);
                rows.Add(leave);
            }
            columns.AddChild(column);
            grid.Add(rows);
        }
        _sheet.Body.AddChild(columns);
        Neighbours(grid);
        ShowStatus();
    }

    /// <summary>
    /// The strip under the title: the name in capitals, the level in ember, the XP gauge with its exact numbers (tenths marked), a debt
    /// chip; under it the date and the weather; and under that the time played and the last save, with the point waiting at the right. The
    /// world-goes-on cue closes the first line. Without a status (no world yet) only the cue shows.
    /// </summary>
    private Control Strip()
    {
        var strip = new VBoxContainer();
        strip.AddThemeConstantOverride("separation", 2);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 12);
        _name = HudStyle.Text(HudStyle.Caps, 24, HudStyle.Bone, caps: true);
        _name.VerticalAlignment = VerticalAlignment.Center;
        top.AddChild(_name);
        _level = HudStyle.Text(HudStyle.Caps, 18, HudStyle.Ember, caps: true);
        _level.VerticalAlignment = VerticalAlignment.Center;
        _level.SizeFlagsVertical = Control.SizeFlags.Fill;
        top.AddChild(_level);
        _xp = new HBoxContainer();
        _xp.AddThemeConstantOverride("separation", 8);
        _xpGauge = UiKit.Gauge(HudStyle.XpFill, 220, 8, marks: 10);
        _xp.AddChild(_xpGauge);
        _xp.AddChild(UiKit.Caption("XP"));
        _xpValue = HudStyle.Text(HudStyle.Strong, 18, HudStyle.Brass);
        _xpValue.VerticalAlignment = VerticalAlignment.Center;
        _xp.AddChild(_xpValue);
        top.AddChild(_xp);
        _debt = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        _debt.AddThemeStyleboxOverride("panel", HudStyle.ChipBox(HudStyle.Wound));
        _debtWord = HudStyle.Text(HudStyle.Caps, 16, HudStyle.Wound, caps: true);
        _debt.AddChild(_debtWord);
        top.AddChild(_debt);
        var cue = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.End };
        cue.AddThemeConstantOverride("separation", 8);
        _pulse = new ColorRect { CustomMinimumSize = new Vector2(6, 6), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Color = HudStyle.Ember };
        cue.AddChild(_pulse);
        cue.AddChild(UiKit.Caption("The world goes on"));
        top.AddChild(cue);
        strip.AddChild(top);
        _date = HudStyle.Text(HudStyle.Body, 16, HudStyle.Bone);
        strip.AddChild(_date);

        _strip = new HBoxContainer();
        _strip.AddThemeConstantOverride("separation", 8);
        _session = HudStyle.Text(HudStyle.Body, 16, HudStyle.Muted);
        _session.VerticalAlignment = VerticalAlignment.Center;
        _strip.AddChild(_session);
        if (UiKit.KeyCap("quicksave") is { } save)
        {
            _strip.AddChild(save);
            _strip.AddChild(UiKit.Caption("Quicksave"));
        }
        _points = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.End };
        _points.AddThemeConstantOverride("separation", 6);
        if (UiKit.KeyCap("character") is { } key)
            _points.AddChild(key);
        var spend = HudStyle.Text(HudStyle.Body, 16, HudStyle.Ember);
        spend.Text = "A point to spend";
        spend.VerticalAlignment = VerticalAlignment.Center;
        _points.AddChild(spend);
        _strip.AddChild(_points);
        strip.AddChild(_strip);
        return strip;
    }

    /// <summary>Put the latest facts in the strip, in place; nothing is rebuilt unless they changed.</summary>
    private void ShowStatus()
    {
        var status = Status?.Invoke();
        string signature = status?.ToString() ?? string.Empty;
        if (signature == _shown)
            return;
        _shown = signature;
        bool known = status is not null;
        _name.Visible = _level.Visible = _strip.Visible = known;
        _xp.Visible = known && !status!.Capped;
        _debt.Visible = known && status!.XpDebt > 0;
        _date.Visible = known && status!.Date.Length > 0;
        if (_pointsChip is not null)
        {
            _pointsChip.Visible = known && status!.Points > 0;
            if (_pointsChip.GetChild(0) is Label count)
                count.Text = known ? $"{status!.Points}" : "0";
        }
        if (status is null)
            return;
        _name.Text = status.Name;
        _level.Text = status.Capped ? $"Level {status.Level} (the highest)" : $"Level {status.Level}";
        _xpGauge.Set(status.Xp, status.XpToNext);
        _xpValue.Text = $"{status.Xp} / {status.XpToNext}";
        _debtWord.Text = $"Debt {status.XpDebt}";
        _date.Text = status.Date;
        _session.Text = $"{status.Played} · {status.Saved}";
        _points.Visible = status.Points > 0;
    }

    /// <summary>Quit, set apart and quiet: ash words beside its icon, the wound colour only while the pointer or the focus is on it.</summary>
    private Button QuitRow(Action quit)
    {
        var row = UiKit.ListRow("quit", "Quit to desktop", nameColor: HudStyle.Ash, height: 48, left: UiKit.Icon(_icons, "panel.quit", 28));
        row.Button.AddThemeStyleboxOverride("hover", UiKit.Box(HudStyle.Coal, HudStyle.Wound));
        void Tone(bool hot) => row.Name.AddThemeColorOverride("font_color", hot ? HudStyle.Wound : HudStyle.Ash);
        row.Button.MouseEntered += () => Tone(true);
        row.Button.MouseExited += () => Tone(row.Button.HasFocus());
        row.Button.FocusEntered += () => Tone(true);
        row.Button.FocusExited += () => Tone(false);
        row.Button.Pressed += quit;
        return row.Button;
    }

    /// <summary>
    /// Arrow-grid focus, set explicitly so a controller needs no relayout: down from Continue to the top of YOU, up from any column's top
    /// back to it; up and down within a column; left and right to the same row in the next column, clamped.
    /// </summary>
    private void Neighbours(List<List<Button>> grid)
    {
        if (grid.All(c => c.Count == 0))
            return;
        _continue.FocusNeighborBottom = _continue.GetPathTo(grid.First(c => c.Count > 0)[0]);
        for (int c = 0; c < grid.Count; c++)
        {
            for (int r = 0; r < grid[c].Count; r++)
            {
                var button = grid[c][r];
                button.FocusNeighborTop = button.GetPathTo(r == 0 ? _continue : grid[c][r - 1]);
                button.FocusNeighborBottom = button.GetPathTo(grid[c][Math.Min(r + 1, grid[c].Count - 1)]);
                if (c > 0 && grid[c - 1].Count > 0)
                    button.FocusNeighborLeft = button.GetPathTo(grid[c - 1][Math.Min(r, grid[c - 1].Count - 1)]);
                if (c < grid.Count - 1 && grid[c + 1].Count > 0)
                    button.FocusNeighborRight = button.GetPathTo(grid[c + 1][Math.Min(r, grid[c + 1].Count - 1)]);
            }
        }
    }
}
