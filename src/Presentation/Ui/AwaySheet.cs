// UNNAMED Presentation - "While you were away": what the world did while the character was logged off (WP11; SYSTEMS_COMPLETENESS_PLAN
// §2.12 item 7; the Field Instrument UI)
// Godot presentation only: it shows a login's away log in AwayWords' words; nothing here holds state (D-11)

using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The sheet a login opens when the world went on long enough to tell of: a SMALL sheet with the time away in days and tallies, the date
/// now, then the log's lines in their groups - the weather, the prices, the character's hirelings and their wages, respawns, the rest. It
/// is dismissed like any screen (Escape, or its one button), and like every screen it stops nothing: the world runs underneath it (the
/// no-pause ruling). Every line is in the journal's Recent list too, so a group longer than the sheet shows says where the rest are.
/// </summary>
public partial class AwaySheet : CanvasLayer
{
    /// <summary>The most lines a group shows here; the journal has them all.</summary>
    public const int LinesShown = 6;

    private Sheet _sheet = null!;
    private Button? _done;

    /// <summary>Back to the world: Main closes the sheet as it closes any screen.</summary>
    public Action? Dismissed { get; set; }

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "While you were away", null, UiKit.Small);
        _sheet.SetHint("The world went on without you.");
        _sheet.OnShown(() => _done?.GrabFocus());
    }

    /// <summary>Fill the sheet with one absence's words; <see cref="CanvasLayer.Visible"/> is Main's to set.</summary>
    public void Show(AwaySummary summary)
    {
        UiKit.Clear(_sheet.Body);
        var head = new VBoxContainer();
        head.AddThemeConstantOverride("separation", 4);
        head.AddChild(UiKit.Words(summary.Head, 20, HudStyle.Bone));
        head.AddChild(UiKit.Words(summary.Now, 17, HudStyle.Ash));
        _sheet.Body.AddChild(head);
        if (summary.Groups.IsEmpty)
            _sheet.Body.AddChild(UiKit.Quiet("Nothing you would know of changed."));
        foreach (var group in summary.Groups)
        {
            var section = UiKit.Section(group.Heading);
            section.AddThemeConstantOverride("separation", 6);
            foreach (string line in group.Lines.Take(LinesShown))
                section.AddChild(UiKit.Words(line, 18, HudStyle.Bone));
            if (group.Lines.Length > LinesShown)
                section.AddChild(UiKit.Quiet($"And {group.Lines.Length - LinesShown} more, in the journal's Recent list."));
            _sheet.Body.AddChild(section);
        }
        _sheet.Body.AddChild(UiKit.Callout(HudStyle.Brass, "All of this is in the journal's Recent list."));
        _done = UiKit.Action("Back to the world", () => Dismissed?.Invoke(), UiKit.Kind.Primary, id: "away.done");
        _done.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        _sheet.Body.AddChild(_done);
    }
}
