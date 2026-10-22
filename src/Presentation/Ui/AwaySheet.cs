// UNNAMED Presentation - "While you were away": what the world did while the character was logged off (WP11; SYSTEMS_COMPLETENESS_PLAN
// §2.12 item 7; the Field Instrument UI)
// Godot presentation only: it shows a login's away log in AwayWords' words; nothing here holds state (D-11)

using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The sheet a login opens when the world went on long enough to tell of: a SMALL sheet with the time away in days and hours, the date
/// now, then the log's lines in their groups - the weather, the prices, the character's hirelings and their wages, respawns, the rest. It
/// is dismissed like any screen (Escape, or its one button), and like every screen it stops nothing: the world runs underneath it (the
/// no-pause ruling). The journal's Recent list keeps only the latest lines, so the sheet shows every one.
/// </summary>
public partial class AwaySheet : CanvasLayer
{
    private Sheet _sheet = null!;
    private Button? _done;
    private AwaySummary? _summary;
    private bool _expanded;

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
        _summary=summary;_expanded=false;Render();
    }
    private void Render()
    {
        if(_summary is not { } summary)return;
        var groups=_expanded && !summary.DetailGroups.IsDefaultOrEmpty ? summary.DetailGroups : summary.Groups;
        UiKit.Clear(_sheet.Body);
        var head = new VBoxContainer();
        head.AddThemeConstantOverride("separation", 4);
        head.AddChild(UiKit.Words(summary.Head, 20, HudStyle.Bone));
        head.AddChild(UiKit.Words(summary.Now, 17, HudStyle.Ash));
        _sheet.Body.AddChild(head);
        if (groups.IsEmpty)
            _sheet.Body.AddChild(UiKit.Quiet("Nothing you would know of changed."));
        foreach (var group in groups)
        {
            var section = UiKit.Section(group.Heading);
            section.AddThemeConstantOverride("separation", 6);
            foreach (string line in group.Lines)
                section.AddChild(UiKit.Words(line, 18, HudStyle.Bone));
            _sheet.Body.AddChild(section);
        }
        int total=summary.DetailGroups.Sum(g=>g.Lines.Length);
        if(total>summary.Groups.Sum(g=>g.Lines.Length))
            _sheet.Body.AddChild(UiKit.Action(_expanded ? "Show summary" : $"Show all events ({total})",()=>{_expanded=!_expanded;Render();UiKit.Refocus(_sheet.Root,"away.expand");},id:"away.expand"));
        _sheet.Body.AddChild(UiKit.Callout(HudStyle.Brass, "The journal's Recent list keeps the latest of this."));
        _done = UiKit.Action("Back to the world", () => Dismissed?.Invoke(), UiKit.Kind.Primary, id: "away.done");
        _done.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        _sheet.Body.AddChild(_done);
    }
}
