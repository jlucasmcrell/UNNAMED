// UNNAMED Presentation - the journal and the quest debugger's panel (PROTOTYPE.md §4.1 UI panels; SYSTEMS.md S-29; M5)
// Godot presentation only: both read views; nothing here changes state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Quests;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The journal (J): the quests by track - the main story ("the story so far", by act), side quests and bounties - each with its status, and
/// the chosen one's summary and the objectives the character knows of, with the HUD tracker's diamond bullets; the Recent list (the quests'
/// own lines and the away log's, newest first); the bounty board (open, held, or taken - by whom, and until when); and the player's notes.
/// A DENSE list and detail. It redraws from the session's views; the chosen row and the quest the tracker follows are all it keeps.
/// </summary>
public partial class JournalPanel : CanvasLayer
{
    private const string Recent = "page.recent";
    private const string Board = "page.board";
    private const string NotesPage = "page.notes";

    private Sheet _sheet = null!;
    private string? _chosen;
    private string _drawn = string.Empty;
    private LineEdit? _note;

    /// <summary>The quest the player chose to track, or null: the tracker then follows the main story, else the first active quest.</summary>
    public string? Tracked { get; private set; }

    /// <summary>The screen's own tile, as its System Menu row shows it.</summary>
    public void UseIcons(HudIcons icons) => _sheet.SetIcon(icons.Binds("panel.journal") ? icons.For("panel.journal") : null);

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "Journal", "journal", UiKit.Standard, scrolls: false);
        _sheet.OnShown(() =>
        {
            _drawn = string.Empty;
            UiKit.FirstFocusable(_sheet.Root)?.GrabFocus();
        });
    }

    /// <summary>Whether the notes' text field has the keys: the screens' own keys wait while the player writes.</summary>
    public bool Typing => Visible && _note is { } note && IsInstanceValid(note) && note.HasFocus();

    public void Refresh(GameSession session)
    {
        if (!Visible || session.Simulation is not { } simulation || session.Journal is not { } journal)
            return;
        var quests = journal.Quests;
        var bounties = simulation.Bounties;
        var notes = session.Notes?.All ?? System.Collections.Immutable.ImmutableArray<JournalNote>.Empty;
        string? tracked = simulation.TrackedQuest(Tracked);
        if (_chosen is null || _chosen is not (Recent or Board or NotesPage) && quests.All(q => q.Id != _chosen))
            _chosen = tracked ?? quests.FirstOrDefault()?.Id ?? Recent;
        string signature = _chosen + "|" + tracked + "|" + string.Join("|", quests.Select(q =>
                $"{q.Id}:{q.Status}:{q.LostTo}:" + string.Join(",", q.Objectives.Select(o => $"{o.Id}{o.Status}{o.Progress}"))))
            + "|" + string.Join("|", journal.Recent.Select(e => $"{e.Tick}{e.Text}")) + "|" + string.Join("|", bounties.Select(b => $"{b.QuestId}{b.Standing}{b.ClaimantId}{AgainMinutes(b, session)}"))
            + "|" + string.Join("|", notes.Select(n => n.Text));
        if (signature == _drawn)
            return;
        _drawn = signature;
        string? focused = UiKit.FocusedId(_sheet.Root);
        UiKit.Clear(_sheet.Body);
        _note = null;
        _sheet.Shape(UiKit.Wide, dense: true);
        var panes = UiKit.ListDetail();
        _sheet.Body.AddChild(panes.Root);

        foreach (var (track, heading, none) in new[]
                 {
                     (QuestTrack.Main, "The story so far", "No one has asked anything of you yet."), (QuestTrack.Side, "Side quests", "None taken up."),
                     (QuestTrack.Bounty, "Bounties", "No bounty taken."),
                 })
        {
            panes.List.AddChild(UiKit.Caption(heading, HudStyle.Brass));
            var group = (track switch { QuestTrack.Main => journal.Main, QuestTrack.Side => journal.Side, _ => journal.Bounties })
                .OrderBy(q => track == QuestTrack.Main || q.Status == QuestStatus.Active ? 0 : 1).ToList();   // the story keeps its order
            if (group.Count == 0)
                panes.List.AddChild(UiKit.Quiet(none));
            int? act = null;
            foreach (var quest in group)
            {
                if (track == QuestTrack.Main && quest.Act != act && quest.Act is { } a)
                    panes.List.AddChild(UiKit.Caption($"Act {Roman(a)}", HudStyle.Ash, 14));
                act = quest.Act;
                int done = quest.Objectives.Count(o => o.Status == ObjectiveStatus.Satisfied);
                var row = UiKit.ListRow(quest.Id, quest.Title, quest.Id == tracked ? "Tracked" : "",
                    nameColor: quest.Status == QuestStatus.Active ? HudStyle.Bone : HudStyle.Ash);
                row.Right.AddChild(quest.Status switch
                {
                    QuestStatus.Completed => UiKit.Chip("Done", HudStyle.Lichen),
                    QuestStatus.Failed => UiKit.Chip(quest.LostTo is null ? "Failed" : "Taken", HudStyle.Wound),
                    _ => UiKit.Chip($"{done} / {quest.Objectives.Length}", HudStyle.Iron, HudStyle.Brass),
                });
                Row(panes.List, row.Button, quest.Id, session);
            }
            panes.List.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        }
        panes.List.AddChild(UiKit.Caption("The record", HudStyle.Brass));
        var recent = UiKit.ListRow(Recent, "Recent", journal.Recent.FirstOrDefault()?.Text ?? "");
        Row(panes.List, recent.Button, Recent, session);
        var board = UiKit.ListRow(Board, "Bounty board", $"{bounties.Count(b => b.Standing == BountyStanding.Open)} open");
        Row(panes.List, board.Button, Board, session);
        var written = UiKit.ListRow(NotesPage, "Notes", notes.IsEmpty ? "" : notes[^1].Text);
        written.Right.AddChild(UiKit.Chip(notes.Length.ToString(), HudStyle.Iron, HudStyle.Brass));
        Row(panes.List, written.Button, NotesPage, session);

        switch (_chosen)
        {
            case Recent:
                RecentDetail(panes.Detail, journal);
                break;
            case Board:
                BoardDetail(panes.Detail, bounties, session);
                break;
            case NotesPage:
                NotesDetail(panes.Detail, notes, session);
                break;
            default:
                if (quests.FirstOrDefault(q => q.Id == _chosen) is { } chosen)
                    Detail(panes.Detail, chosen, chosen.Id == tracked, session);
                break;
        }
        if (!UiKit.Refocus(_sheet.Root, focused ?? _chosen))
            UiKit.FirstFocusable(_sheet.Root)?.GrabFocus();
    }

    private void Row(VBoxContainer list, Button button, string id, GameSession session)
    {
        UiKit.Select(button, id == _chosen);
        button.FocusEntered += () => Choose(id, session);
        button.Pressed += () => Choose(id, session);
        list.AddChild(button);
    }

    private void Choose(string id, GameSession session)
    {
        if (_chosen == id)
            return;
        _chosen = id;
        // After the focus or press has been handled: the redraw replaces the row that took it.
        Callable.From(() => Refresh(session)).CallDeferred();
    }

    private void Track(string? id, GameSession session)
    {
        Tracked = id;
        _drawn = string.Empty;
        Callable.From(() => Refresh(session)).CallDeferred();
    }

    /// <summary>The chosen quest: its title and status, its summary, and its objectives with the tracker's bullets and their counts.</summary>
    private void Detail(VBoxContainer detail, QuestView quest, bool tracked, GameSession session)
    {
        var head = new VBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 12);
        var title = HudStyle.Text(HudStyle.Caps, 30, HudStyle.Bone, caps: true);
        title.Text = quest.Title;
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        top.AddChild(title);
        top.AddChild(quest.Status switch
        {
            QuestStatus.Completed => UiKit.Chip("Done", HudStyle.Lichen),
            QuestStatus.Failed when quest.LostTo is { } who => UiKit.Chip($"Taken by {who}", HudStyle.Wound),
            QuestStatus.Failed => UiKit.Chip("Failed", HudStyle.Wound),
            _ => UiKit.Chip("Active", HudStyle.Ember),
        });
        if (quest.Status == QuestStatus.Active)
        {
            string id = quest.Id;
            top.AddChild(tracked && Tracked == id
                ? UiKit.Action("Untrack", () => Track(null, session), compact: true, id: "journal.track")
                : tracked ? UiKit.Chip("Tracked", HudStyle.Ember)
                : UiKit.Action("Track", () => Track(id, session), UiKit.Kind.Primary, compact: true, id: "journal.track"));
        }
        head.AddChild(top);
        head.AddChild(UiKit.Words(quest.Summary, 18));
        detail.AddChild(head);

        var objectives = UiKit.Section("Objectives");
        objectives.AddThemeConstantOverride("separation", 8);
        foreach (var objective in quest.Objectives)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            row.AddChild(new Bullet(objective.Status));
            var words = UiKit.Words(objective.Description, 18, objective.Status == ObjectiveStatus.Active ? HudStyle.Bone : HudStyle.Ash);
            row.AddChild(words);
            if (objective.Progress is { } progress)
            {
                var count = HudStyle.Text(HudStyle.Strong, 18, HudStyle.Brass);
                count.Text = progress;
                row.AddChild(count);
            }
            if (objective.Status == ObjectiveStatus.Failed)
                row.AddChild(UiKit.Chip("Failed", HudStyle.Wound));
            objectives.AddChild(row);
        }
        if (quest.Objectives.IsEmpty)
            objectives.AddChild(UiKit.Quiet("Nothing asked of you here yet."));
        detail.AddChild(objectives);
    }

    /// <summary>The Recent list: what happened lately, newest first - the quests' lines, the choices made, and the away log's.</summary>
    private static void RecentDetail(VBoxContainer detail, JournalView journal)
    {
        var section = UiKit.Section("Recent");
        section.AddThemeConstantOverride("separation", 6);
        foreach (var entry in journal.Recent)
            section.AddChild(UiKit.Words(entry.Text, 18, entry.Kind switch
            {
                JournalEntryKind.Choice => HudStyle.Ember,
                JournalEntryKind.Failed or JournalEntryKind.Claimed => HudStyle.Wound,
                JournalEntryKind.Away => HudStyle.Brass,
                _ => HudStyle.Bone,
            }));
        if (journal.Recent.IsEmpty)
            section.AddChild(UiKit.Quiet("Nothing yet."));
        detail.AddChild(section);
    }

    /// <summary>Whole minutes until a taken bounty is posted again, as the board shows them; null when it is not waiting.</summary>
    private static string? AgainMinutes(BountyView bounty, GameSession session) =>
        bounty.PostedAgainTick is { } at ? $"{Math.Max(0, at - session.Simulation!.WorldTick) * session.TickSeconds / 60:0}" : null;

    /// <summary>The bounty board: every bounty, by giver - open, held by the character, or taken, by whom, and when it is posted again.</summary>
    private static void BoardDetail(VBoxContainer detail, IReadOnlyList<BountyView> bounties, GameSession session)
    {
        var section = UiKit.Section("Bounty board", "Standing work. A competitive bounty goes to whoever finishes it first.");
        section.AddThemeConstantOverride("separation", 4);
        foreach (var bounty in bounties)
        {
            string giver = bounty.GiverId is { } g ? Names.Of(session, g) : "";
            string again = AgainMinutes(bounty, session) is { } minutes ? $"posted again in {minutes} min" : "";
            var row = UiKit.Row(null, bounty.Title, string.Join(" - ", new[] { giver, bounty.Competitive ? "competitive" : "", again }.Where(x => x.Length > 0)),
                bounty.Standing == BountyStanding.Open ? HudStyle.Bone : HudStyle.Ash);
            row.Root.AddChild(bounty.Standing switch
            {
                BountyStanding.Held => UiKit.Chip("Yours", HudStyle.Ember),
                BountyStanding.Claimed => UiKit.Chip(bounty.ClaimantId == session.Simulation.PlayerId.Value ? "Done" : $"Taken by {bounty.ClaimantName}",
                    HudStyle.Iron, HudStyle.Ash),
                _ => UiKit.Chip("Open", HudStyle.Lichen),
            });
            section.AddChild(row.Root);
        }
        detail.AddChild(section);
    }

    /// <summary>The player's notes, oldest first, each with a button to cross it out, and a line to write a new one.</summary>
    private void NotesDetail(VBoxContainer detail, System.Collections.Immutable.ImmutableArray<JournalNote> notes, GameSession session)
    {
        var section = UiKit.Section("Notes", "Your own words. Only you read them.");
        section.AddThemeConstantOverride("separation", 6);
        for (int i = 0; i < notes.Length; i++)
        {
            int index = i;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var words = UiKit.Words(notes[i].Text, 18);
            words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(words);
            row.AddChild(UiKit.Action("Cross out", () => Changed(session.RemoveNote(index), session), UiKit.Kind.Destructive, compact: true, id: $"note.{i}"));
            section.AddChild(row);
        }
        if (notes.IsEmpty)
            section.AddChild(UiKit.Quiet("Nothing written yet."));
        var write = new HBoxContainer();
        write.AddThemeConstantOverride("separation", 10);
        _note = new LineEdit { PlaceholderText = "Write a note", MaxLength = JournalNotes.MaxLength, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        UiKit.Id(_note, "note.text");
        var field = _note;
        void Add() => Changed(session.AddNote(field.Text), session);
        _note.TextSubmitted += _ => Add();
        write.AddChild(_note);
        write.AddChild(UiKit.Action("Write", Add, UiKit.Kind.Primary, compact: true, id: "note.write"));
        section.AddChild(write);
        detail.AddChild(section);
    }

    private void Changed(string? refused, GameSession session)
    {
        if (refused is not null)
            return;
        _drawn = string.Empty;
        Callable.From(() => Refresh(session)).CallDeferred();
    }

    private static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII", 8 => "VIII", _ => n.ToString() };

    /// <summary>The bible's minimal tracker (§19): the tracked quest's title and what it asks now, or nothing.</summary>
    public static string? Tracker(IReadOnlyList<QuestView> quests, string? tracked = null) =>
        (quests.FirstOrDefault(q => q.Id == tracked && q.Status == QuestStatus.Active) ?? quests.FirstOrDefault(q => q.Status == QuestStatus.Active)) is { } quest
            ? quest.Title.ToUpperInvariant() + "\n" + string.Join("\n", quest.Objectives.Where(o => o.Status == ObjectiveStatus.Active)
                .Select(o => o.Description + (o.Progress is { } p ? $" ({p})" : "")))
            : null;

    /// <summary>An objective's bullet, the HUD tracker's diamond: hollow while open, filled lichen once done, wound once failed.</summary>
    private sealed partial class Bullet : Control
    {
        private readonly ObjectiveStatus _status;

        public Bullet(ObjectiveStatus status)
        {
            _status = status;
            CustomMinimumSize = new Vector2(10, 24);
            SizeFlagsVertical = SizeFlags.ShrinkBegin;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            var c = new Vector2(5, 12);
            var points = new[] { c + new Vector2(0, -5), c + new Vector2(5, 0), c + new Vector2(0, 5), c + new Vector2(-5, 0) };
            switch (_status)
            {
                case ObjectiveStatus.Satisfied:
                    DrawColoredPolygon(points, HudStyle.Lichen);
                    break;
                case ObjectiveStatus.Failed:
                    DrawColoredPolygon(points, HudStyle.Wound);
                    break;
                default:
                    DrawPolyline(points.Append(points[0]).ToArray(), _status == ObjectiveStatus.Active ? HudStyle.Bone : HudStyle.Muted, 1.5f, true);
                    break;
            }
        }
    }
}

/// <summary>
/// The quest debugger in the game (F4): for each quest content defines, the debugger's answer to "what is this quest waiting on
/// right now?" - the full diagnosis, with its recent trace, for a quest that is active; a line for the rest.
/// </summary>
public partial class QuestDebugPanel : CanvasLayer
{
    private readonly Label _text = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(1060, 0) };
    private double _since;

    public override void _Ready()
    {
        Visible = false;
        var panel = new PanelContainer { Position = new Vector2(40, 130), CustomMinimumSize = new Vector2(1100, 0) };
        _text.AddThemeFontSizeOverride("font_size", 15);
        panel.AddChild(_text);
        AddChild(panel);
    }

    /// <summary>Redraw a few times a second: a diagnosis walks the content, which is cheap but not free.</summary>
    public void Refresh(GameSession session, double delta, bool now = false)
    {
        if (!Visible || session.Simulation is not { } simulation)
            return;
        _since += delta;
        if (!now && _since < 0.25)
            return;
        _since = 0;
        _text.Text = Render(simulation, session.Setup.Quests);
    }

    public static string Render(Simulation simulation, QuestSetup quests)
    {
        var text = new System.Text.StringBuilder("QUEST DEBUGGER   [F4] close\n");
        foreach (var id in quests.Quests.Keys.OrderBy(id => simulation.Quests.Any(q => q.Id == id && q.Status == QuestStatus.Active) ? 0 : 1)
                     .ThenBy(id => id, StringComparer.Ordinal))
        {
            var diagnosis = simulation.Diagnose(id);
            text.Append('\n');
            if (diagnosis.Status == QuestKeys.Key(QuestStatus.Active))
                text.Append(diagnosis.ToText(traceEntries: 3));
            else
                text.Append(diagnosis.Title).Append(" - ").Append(diagnosis.Status).Append(": ").Append(diagnosis.Answer).Append('\n');
        }
        return text.ToString();
    }
}
