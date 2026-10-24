// UNNAMED Presentation - Report a problem: a note, then one local zip and where it is (public playtest gate E2)
// Godot presentation only (D-11): it asks Main to write the bundle and says where it went; nothing here holds game state or sends anything

using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The tester writes what happened and presses Write the report: Main gathers the log, the settings, the save in play, the screen as it
/// was when the panel opened, the build and the machine into a zip in the reports folder (<see cref="Diagnostics.ProblemReport"/>). The
/// panel then names the file, opens its folder on request, and says how to send it. The world runs on while it is open.
/// </summary>
public partial class ReportPanel : CanvasLayer
{
    /// <summary>How a tester sends a report: the game never does.</summary>
    public const string HowToSend =
        "Nothing has been sent. To send it, attach the zip to a comment on the Otherreach itch.io page, or use the feedback link on otherreach.com/playtest. " +
        "The zip holds your note, a picture of the screen, the game's log, your settings and your current save - nothing else from this computer.";

    private Sheet _sheet = null!;
    private TextEdit _note = null!;
    private VBoxContainer _result = null!;

    /// <summary>Write the bundle with the note; returns the zip's path, or throws with why it could not be written.</summary>
    public Func<string, string>? WriteReport { get; set; }

    public override void _Ready()
    {
        Visible = false;
        Layer = 12;
        _sheet = UiKit.Sheet(this, "Report a problem", "report_problem");
        _sheet.SetHint("written to a file on this computer; you choose whether to send it");
        var ask = UiKit.Section("What happened", "where you were, what you did, what you saw");
        _note = new TextEdit
        {
            CustomMinimumSize = new Vector2(0, 180), WrapMode = TextEdit.LineWrappingMode.Boundary,
            PlaceholderText = "For example: in Cairnwold, talking to Kera, the reply list went blank after I chose to buy.",
        };
        ask.AddChild(_note);
        _sheet.Body.AddChild(ask);
        var write = UiKit.Action("Write the report", Write, UiKit.Kind.Primary, id: "report.write");
        write.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _sheet.Body.AddChild(write);
        _result = new VBoxContainer();
        _result.AddThemeConstantOverride("separation", 8);
        _sheet.Body.AddChild(_result);
        _sheet.OnShown(() => _note.GrabFocus());
    }

    /// <summary>The note has the keys: its letters must not open screens.</summary>
    public bool Typing => Visible && _note.HasFocus();

    public void Open()
    {
        _note.Text = string.Empty;
        UiKit.Clear(_result);
        Visible = true;
    }

    private void Write()
    {
        UiKit.Clear(_result);
        try
        {
            string zip = WriteReport!(_note.Text);
            _result.AddChild(UiKit.Callout(HudStyle.Brass, $"The report is written: {zip}"));
            _result.AddChild(UiKit.Words(HowToSend, 17, HudStyle.Bone));
            var open = UiKit.Action("Open the folder", () => OS.ShellShowInFileManager(zip), id: "report.open");
            open.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            _result.AddChild(open);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _result.AddChild(UiKit.Callout(HudStyle.Wound, $"The report could not be written: {e.Message}"));
        }
    }
}
