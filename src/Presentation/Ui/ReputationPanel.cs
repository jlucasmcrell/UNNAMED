// UNNAMED Presentation - how the factions regard the character, as far as they have let the character know (the Field Instrument UI,
// design record 06; WP6)
// Godot presentation only: it reads the faction views in StandingWords' words; nothing here holds state (D-11)

using Godot;
using UNNAMED.Application;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// Reputation, from the System Menu: a STANDARD sheet with a block a faction - its name, where it sits, who speaks for it and how it
/// regards the others; its regard in words with the tier's word as a chip; and what it has heard of the character's acts, and from whom.
/// Standing is report-only (PJ07): the sheet tells what the character has been told, never a number. While no faction knows anything of
/// the character the model's sentence is a callout. No faction at all is the empty state.
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
        _sheet.SetHint("How the factions regard you, as far as they have let you know.");
        _sheet.OnShown(() => _drawn = null);
    }

    public override void _Process(double delta)
    {
        if (!Visible || _session?.Simulation is not { } simulation)
            return;
        var reports = StandingWords.Of(simulation.Factions, simulation.Acts, id => Names.Of(_session, id), id => Names.Place(_session, id));
        string signature = string.Join("|", reports.Select(r => $"{r.Id}:{r.Word}:{string.Join(",", r.Heard)}"));
        if (signature == _drawn)
            return;
        _drawn = signature;
        Build(reports);
    }

    private void Build(IReadOnlyList<FactionReport> reports)
    {
        UiKit.Clear(_sheet.Body);
        if (reports.Count == 0)
        {
            _sheet.Body.AddChild(UiKit.EmptyState("No standing yet", "No faction knows you yet.",
                "A faction appears here once there is one to stand with."));
            return;
        }
        if (reports.All(r => !r.Known))
            _sheet.Body.AddChild(UiKit.Callout(HudStyle.Brass, StandingWords.NoneKnow));
        foreach (var report in reports)
        {
            var block = new VBoxContainer();
            block.AddThemeConstantOverride("separation", 6);
            var row = UiKit.Row(null, report.Name, report.About);
            var chip = UiKit.Chip(report.Word.ToUpperInvariant(), Tone(report), report.Known ? null : HudStyle.Ash);
            chip.CustomMinimumSize = new Vector2(112, 0);
            chip.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.Root.AddChild(chip);
            block.AddChild(row.Root);
            block.AddChild(UiKit.Words(report.Regard, 18, report.Known ? Tone(report) : HudStyle.Ash));
            if (report.Heard.Length > 0)
            {
                var heard = UiKit.Section("What they have heard");
                heard.AddThemeConstantOverride("separation", 4);
                foreach (string line in report.Heard)
                    heard.AddChild(UiKit.Words(line, 17, HudStyle.Bone));
                block.AddChild(heard);
            }
            _sheet.Body.AddChild(block);
        }
    }

    /// <summary>Good regard in lichen, ill in the wound's red, neutral or unknown in ash.</summary>
    private static Color Tone(FactionReport report) =>
        !report.Known ? HudStyle.Iron : report.Level > 0 ? HudStyle.Lichen : report.Level < 0 ? HudStyle.Wound : HudStyle.Ash;
}
