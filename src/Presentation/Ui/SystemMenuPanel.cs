// UNNAMED Presentation - the System Menu: every screen at once, grouped, with its key (the Field Instrument UI, design record 03)
// Godot presentation only: its rows call what Main gives them; nothing here holds game state (D-11)

using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// Escape with nothing open. It says four things: this is the root (every destination, grouped YOU / THE WORLD / SYSTEM, with its key
/// read live from the input map); the world goes on (the menu suspends the player's control, not time - a light scrim, the HUD live,
/// and one quiet pulsing line that says so); getting back is the first thing (Continue, primary, with the focus, and Escape does it);
/// and quitting is separate, set apart and quiet until pointed at. A destination replaces the menu; Escape from it returns to play.
/// </summary>
public partial class SystemMenuPanel : CanvasLayer
{
    /// <summary>A destination: its group, its words, the input action it is on (or none - menu only), and what choosing it does.</summary>
    public sealed record Entry(string Group, string Label, string? Key, Action Chosen);

    private static readonly string[] Groups = { "You", "The world", "System" };

    private Sheet _sheet = null!;
    private Button _continue = null!;
    private ColorRect _pulse = null!;
    private double _clock;

    /// <summary>The destinations, in each group's order; Main fills it before the menu first opens.</summary>
    public IReadOnlyList<Entry> Entries { get; set; } = Array.Empty<Entry>();

    /// <summary>Leave the game; set apart from the rest.</summary>
    public Action? Quit { get; set; }

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

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        // The one thing that moves on the menu, and it means what it says: a slow ember pulse, two seconds a cycle.
        _clock += delta;
        _pulse.Color = new Color(HudStyle.Ember, 0.7f + 0.3f * Mathf.Sin((float)(_clock * Math.PI)));
    }

    private void Build()
    {
        UiKit.Clear(_sheet.Body);
        var strip = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        strip.AddThemeConstantOverride("separation", 8);
        _pulse = new ColorRect { CustomMinimumSize = new Vector2(6, 6), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Color = HudStyle.Ember };
        strip.AddChild(_pulse);
        strip.AddChild(UiKit.Caption("The world goes on"));
        _sheet.Body.AddChild(strip);

        _continue = UiKit.Action("Continue", () => Visible = false, UiKit.Kind.Primary, id: "continue");
        _continue.CustomMinimumSize = new Vector2(0, 56);
        _continue.Alignment = HorizontalAlignment.Left;
        _sheet.Body.AddChild(_continue);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        var grid = new List<List<Button>>();
        foreach (string group in Groups)
        {
            var column = UiKit.Section(group);
            column.CustomMinimumSize = new Vector2(261, 0);
            column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var rows = new List<Button>();
            foreach (var entry in Entries.Where(e => e.Group == group))
            {
                var row = UiKit.ListRow(entry.Label, entry.Label, height: 48);
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
                var leave = UiKit.Action("Quit to desktop", quit, UiKit.Kind.Destructive, id: "quit");
                leave.CustomMinimumSize = new Vector2(0, 48);
                leave.Alignment = HorizontalAlignment.Left;
                column.AddChild(leave);
                rows.Add(leave);
            }
            columns.AddChild(column);
            grid.Add(rows);
        }
        _sheet.Body.AddChild(columns);
        Neighbours(grid);
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
