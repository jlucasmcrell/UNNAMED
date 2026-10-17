// UNNAMED Presentation - how the factions regard the character (the Field Instrument UI, design record 06; WP6)
// Godot presentation only: it reads the faction views; nothing here holds state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// Reputation, from the System Menu: a STANDARD sheet with a row a faction - its name, where it sits, who speaks for it and how it regards
/// the others, the character's signed points and the tier they make as a chip. While every standing is still 0 the model's sentence is a
/// callout: a faction learns of an act only when you tell one of its members. No faction at all is the empty state. A simple first pass;
/// the local lane polishes it.
/// </summary>
public partial class ReputationPanel : CanvasLayer
{
    private Sheet _sheet = null!;
    private GameSession _session = null!;
    private string? _drawn;

    public void Bind(GameSession session) => _session = session;

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "Reputation", null);
        _sheet.SetHint("How the factions regard you.");
        _sheet.OnShown(() => _drawn = null);
    }

    public override void _Process(double delta)
    {
        if (!Visible || _session?.Simulation is not { } simulation)
            return;
        var factions = simulation.Factions;
        string signature = string.Join(",", factions.Select(f => $"{f.Id}:{f.Points}"));
        if (signature == _drawn)
            return;
        _drawn = signature;
        Build(factions);
    }

    private void Build(IReadOnlyList<FactionView> factions)
    {
        UiKit.Clear(_sheet.Body);
        if (factions.Count == 0)
        {
            _sheet.Body.AddChild(UiKit.EmptyState("No standing yet", "No faction knows you yet.",
                "A faction appears here once there is one to stand with."));
            return;
        }
        if (factions.All(f => f.Points == 0))
            _sheet.Body.AddChild(UiKit.Callout(HudStyle.Brass,
                "A faction learns of an act only when you tell one of its members."));
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 8);
        foreach (var faction in factions)
        {
            var row = UiKit.Row(null, Capitalised(Names.Of(_session, faction.Id)), About(faction, factions));
            var points = HudStyle.Text(HudStyle.Strong, 20, Tone(faction.Level));
            points.Text = faction.Points == 0 ? "0" : faction.Points.ToString("+#;-#");
            points.CustomMinimumSize = new Vector2(72, 0);
            points.HorizontalAlignment = HorizontalAlignment.Right;
            points.VerticalAlignment = VerticalAlignment.Center;
            row.Root.AddChild(points);
            var chip = UiKit.Chip(faction.Tier.ToUpperInvariant(), Tone(faction.Level));
            chip.CustomMinimumSize = new Vector2(112, 0);
            row.Root.AddChild(chip);
            list.AddChild(row.Root);
        }
        _sheet.Body.AddChild(list);
    }

    /// <summary>Seat, members and regards in words: "At the outpost · Sel Arien · cordial to the Wayhold".</summary>
    private string About(FactionView faction, IReadOnlyList<FactionView> all)
    {
        var parts = new List<string> { "At " + Names.Place(_session, faction.SeatLocationId) };
        if (faction.Members.Length > 0)
            parts.Add(string.Join(", ", faction.Members.Select(m => Names.Of(_session, m))));
        parts.AddRange(faction.Relations.Select(r =>
            $"{r.Attitude} to {(all.FirstOrDefault(f => f.Id == r.FactionId) is { } other ? Names.Of(_session, other.Id) : Names.Of(_session, r.FactionId))}"));
        return string.Join(" · ", parts);
    }

    private static string Capitalised(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    /// <summary>Good regard in lichen, ill in the wound's red, neutral in ash.</summary>
    private static Color Tone(int level) => level > 0 ? HudStyle.Lichen : level < 0 ? HudStyle.Wound : HudStyle.Ash;
}
