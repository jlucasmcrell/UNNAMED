// UNNAMED Presentation - Credits: who made the game, and the notices and licences of everything it is built with (PJ02 §5.1)
// Godot presentation only (D-11): it reads the credits data that ships with the content; nothing here holds game state

using Godot;
using UNNAMED.Content;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The studio line first, then every group of <c>content/credits.yaml</c> as it is written there: the third-party attributions in the
/// form each licence requires and the code notices; then every licence file in the notices folder named, with a way
/// to open the folder. The words are data (<see cref="CreditsContent"/>), never written here, so the screen cannot drift from the file.
/// The page scrolls, and its words are <see cref="UiKit"/>'s, so they follow the text size, the interface scale and high contrast.
/// </summary>
public partial class CreditsPanel : CanvasLayer
{
    private Sheet _sheet = null!;

    /// <summary>The folder holding the licence files.</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary><c>credits.yaml</c>, in the content root.</summary>
    public string CreditsFile { get; set; } = string.Empty;

    /// <summary>Back to wherever Credits was opened from.</summary>
    public Action? Closed { get; set; }

    public override void _Ready()
    {
        Visible = false;
        Layer = 12;
        _sheet = UiKit.Sheet(this, "Credits", null, UiKit.Wide);
        // The focus on Back at the top, and the page at its start: following the focus in has scrolled a long sheet down by a line.
        _sheet.OnShown(() =>
        {
            UiKit.FirstFocusable(_sheet.Root)?.GrabFocus();
            GetTree().CreateTimer(0.05).Timeout += () =>
            {
                if (_sheet.Scroll is { } scroll)
                    scroll.ScrollVertical = 0;
            };
        });
        VisibilityChanged += () =>
        {
            if (Visible)
                Build();
        };
    }

    private void Build()
    {
        UiKit.Clear(_sheet.Body);
        // Back first, so the focus opens at the top of a long page rather than scrolling it to the foot.
        var back = UiKit.Action("Back", () => Callable.From(() => Closed?.Invoke()).CallDeferred(), UiKit.Kind.Primary, id: "credits.back");
        back.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _sheet.Body.AddChild(back);
        CreditsContent? credits = null;
        try
        {
            credits = CreditsContent.Load(CreditsFile);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or YamlDotNet.Core.YamlException)
        {
            _sheet.Body.AddChild(UiKit.Callout(HudStyle.Wound, $"The credits are missing: {CreditsFile} could not be read ({e.Message})."));
        }
        if (credits is not null)
        {
            var maker = UiKit.Section("Otherreach");
            maker.AddChild(UiKit.Words(credits.Studio, 24, HudStyle.Bone, HudStyle.Strong));
            if (credits.StudioNote.Length > 0)
                maker.AddChild(UiKit.Words(credits.StudioNote, 17, HudStyle.Bone));
            _sheet.Body.AddChild(maker);
            foreach (var group in credits.Sections)
            {
                var section = UiKit.Section(group.Title, group.Note);
                section.AddThemeConstantOverride("separation", 6);
                foreach (var entry in group.Entries)
                {
                    section.AddChild(UiKit.Words(entry.Text, 17, HudStyle.Bone));
                    foreach (string line in entry.Lines)
                        section.AddChild(Indented(UiKit.Words(line, 17, HudStyle.Ash)));
                }
                _sheet.Body.AddChild(section);
            }
        }
        if (Directory.Exists(Folder))
        {
            var licences = UiKit.Section("Licence texts", "in full, beside the game");
            licences.AddChild(UiKit.Words(string.Join(" · ", Directory.EnumerateFiles(Folder).Select(System.IO.Path.GetFileName)
                .Where(f => f != "NOTICE.txt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)), 16, HudStyle.Ash));
            licences.AddChild(UiKit.Words(Folder, 16, HudStyle.Muted));
            var open = UiKit.Action("Open the folder", () => OS.ShellOpen(Folder), id: "credits.folder");
            open.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            licences.AddChild(open);
            _sheet.Body.AddChild(licences);
        }
    }

    private static MarginContainer Indented(Control child)
    {
        var indent = new MarginContainer();
        indent.AddThemeConstantOverride("margin_left", 20);
        indent.AddChild(child);
        return indent;
    }
}
