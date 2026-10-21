// UNNAMED Presentation - the reading panel: a book or a note open in the world (spec 41, owner question 2)
// Godot presentation only: it reads the lore content and the pages the character has filed (D-11)

using Godot;
using System.Collections.Immutable;
using UNNAMED.Application;
using UNNAMED.Domain.Knowledge;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// What something written says, read where it stands: the entry its content names, one page at a time, with the page it opens on and how much
/// of it there is. A book turns its pages; a carved panel is one page and says so. Reading is what files the page, so the panel is opened by the
/// read, and it never changes anything itself.
/// </summary>
public partial class ReadingPanel : CanvasLayer
{
    private Sheet _sheet = null!;
    private GameSession _session = null!;
    private string? _entryId;
    private int _page;
    private string? _drawn;

    public void Bind(GameSession session) => _session = session;

    /// <summary>The entry open, or null.</summary>
    public string? EntryId => _entryId;

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "Read", "what is written there");
        _sheet.OnShown(() =>
        {
            _drawn = null;
            Callable.From(() => UiKit.FirstFocusable(_sheet.Root)?.GrabFocus()).CallDeferred();
        });
    }

    /// <summary>Open an entry at the page just read, or at its first page.</summary>
    public void Open(string entryId, int page = 1)
    {
        _entryId = entryId;
        _page = page;
        _drawn = null;
        Visible = true;
        Refresh();
    }

    public void Close()
    {
        Visible = false;
        _entryId = null;
    }

    public override void _Process(double delta)
    {
        if (Visible)
            Refresh();
    }

    private void Refresh()
    {
        if (!Visible || _entryId is null || !_session.Setup.Lore.TryGetValue(_entryId, out var entry))
            return;
        var pages = _session.Simulation?.Lore ?? ImmutableArray<LorePageRecord>.Empty;
        string signature = $"{entry.Id}|{_page}|{pages.Length}";
        if (signature == _drawn)
            return;
        _drawn = signature;
        string? focused = UiKit.FocusedId(_sheet.Root);
        UiKit.Clear(_sheet.Body);
        _sheet.Shape(UiKit.Standard, dense: false);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 14);
        _sheet.Body.AddChild(body);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 12);
        var title = HudStyle.Text(HudStyle.Caps, 34, HudStyle.Bone, caps: true);
        title.Text = entry.Title;
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        top.AddChild(title);
        body.AddChild(top);

        var page = entry.PageAt(Math.Clamp(_page, 1, entry.PageCount));
        if (page is { } reading)
        {
            var words = UiKit.Words(reading.Text, 20);
            body.AddChild(words);
            body.AddChild(UiKit.Quiet(entry.PageCount == 1
                ? "The whole of it."
                : $"Page {reading.Page} of {entry.PageCount}"));
        }

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 10);
        if (_page > 1)
            actions.AddChild(UiKit.Action("Previous page", () => Turn(-1), compact: true, id: "read.previous"));
        if (_page < entry.PageCount)
            actions.AddChild(UiKit.Action("Turn the page", () => Turn(1), UiKit.Kind.Primary, compact: true, id: "read.next"));
        actions.AddChild(UiKit.Action("Close", Close, compact: true, id: "read.close"));
        body.AddChild(actions);
        if (!UiKit.Refocus(_sheet.Root, focused))
            UiKit.FirstFocusable(_sheet.Root)?.GrabFocus();
    }

    private void Turn(int by)
    {
        _page = Math.Max(1, _page + by);
        _drawn = null;
        Refresh();
    }
}
