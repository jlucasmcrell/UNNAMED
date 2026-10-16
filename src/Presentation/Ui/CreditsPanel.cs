// UNNAMED Presentation - Credits: who made the game, and the notices and licences of everything it is built with (PJ02 §5.1)
// Godot presentation only (D-11): it reads the notices that ship beside the game; nothing here holds game state

using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The maker first, then the release's <c>THIRD_PARTY_NOTICES/NOTICE.txt</c> as it ships - the engine and runtime, the assets used under
/// licence with their credits, and the use of AI - each "== heading" a section, and every licence file in the folder named, with a way to
/// open the folder. The notices are read where the game finds them (beside an exported build; the release payload in the repository),
/// never rewritten here, so the screen cannot drift from what ships.
/// </summary>
public partial class CreditsPanel : CanvasLayer
{
    private Sheet _sheet = null!;

    /// <summary>The folder holding <c>NOTICE.txt</c> and the licence files.</summary>
    public string Folder { get; set; } = string.Empty;

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
        var maker = UiKit.Section("Otherreach");
        maker.AddChild(UiKit.Words(TitleScreen.Maker, 24, HudStyle.Bone, HudStyle.Strong));
        _sheet.Body.AddChild(maker);

        string notice = System.IO.Path.Combine(Folder, "NOTICE.txt");
        if (!File.Exists(notice))
        {
            _sheet.Body.AddChild(UiKit.Callout(HudStyle.Wound, $"The notices are missing: {notice} was not found."));
        }
        else
        {
            VBoxContainer? section = null;
            foreach (string raw in File.ReadAllLines(notice))
            {
                string line = raw.TrimEnd();
                if (line.StartsWith("== ", StringComparison.Ordinal))
                {
                    section = UiKit.Section(line[3..]);
                    section.AddThemeConstantOverride("separation", 6);
                    _sheet.Body.AddChild(section);
                    continue;
                }
                if (line.Length == 0)
                    continue;
                // The file's opening lines, before any heading, introduce it.
                var parent = section ?? _sheet.Body;
                bool item = line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("  ", StringComparison.Ordinal);
                var words = UiKit.Words(line.TrimStart(), 17, item ? HudStyle.Ash : HudStyle.Bone);
                if (item)
                {
                    var indent = new MarginContainer();
                    indent.AddThemeConstantOverride("margin_left", 20);
                    indent.AddChild(words);
                    parent.AddChild(indent);
                }
                else
                {
                    parent.AddChild(words);
                }
            }
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
}
