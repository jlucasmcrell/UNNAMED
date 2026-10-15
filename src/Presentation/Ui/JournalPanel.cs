// UNNAMED Presentation - the journal and the quest debugger's panel (PROTOTYPE.md §4.1 UI panels; SYSTEMS.md S-29; M5)
// Godot presentation only: both read views; nothing here changes state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Quests;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The journal (J): every quest started, grouped Active, Done and Failed, and the chosen one's summary and the objectives the character
/// knows of - text directions, never a marker (charter §3), with the HUD tracker's diamond bullets. A STANDARD sheet with its empty
/// state until there is a quest, then the DENSE list and detail. It redraws from the simulation's quest views; the chosen quest is the
/// only thing it keeps.
/// </summary>
public partial class JournalPanel : CanvasLayer
{
    private Sheet _sheet = null!;
    private string? _chosen;
    private string _drawn = string.Empty;

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

    public void Refresh(GameSession session)
    {
        if (!Visible || session.Simulation is not { } simulation)
            return;
        var quests = simulation.Quests;
        if (_chosen is null || quests.All(q => q.Id != _chosen))
            _chosen = (quests.FirstOrDefault(q => q.Status == QuestStatus.Active) ?? quests.FirstOrDefault())?.Id;
        string signature = _chosen + "|" + string.Join("|", quests.Select(q =>
            $"{q.Id}:{q.Status}:" + string.Join(",", q.Objectives.Select(o => $"{o.Id}{o.Status}{o.Progress}"))));
        if (signature == _drawn)
            return;
        _drawn = signature;
        string? focused = UiKit.FocusedId(_sheet.Root);
        UiKit.Clear(_sheet.Body);
        if (quests.IsEmpty)
        {
            _sheet.Shape(UiKit.Standard);
            _sheet.Body.AddChild(UiKit.EmptyState("No quests", "No one has asked anything of you yet."));
            return;
        }
        _sheet.Shape(UiKit.Wide, dense: true);
        var panes = UiKit.ListDetail();
        _sheet.Body.AddChild(panes.Root);
        foreach (var (status, heading, none) in new[]
                 {
                     (QuestStatus.Active, "Active", "Nothing under way."), (QuestStatus.Completed, "Done", "None finished yet."),
                     (QuestStatus.Failed, "Failed", "None failed."),
                 })
        {
            panes.List.AddChild(UiKit.Caption(heading, HudStyle.Brass));
            var group = quests.Where(q => q.Status == status).ToList();
            if (group.Count == 0)
                panes.List.AddChild(UiKit.Quiet(none));
            foreach (var quest in group)
            {
                int done = quest.Objectives.Count(o => o.Status == ObjectiveStatus.Satisfied);
                var row = UiKit.ListRow(quest.Id, quest.Title, nameColor: status == QuestStatus.Active ? HudStyle.Bone : HudStyle.Ash);
                row.Right.AddChild(UiKit.Chip($"{done} / {quest.Objectives.Length}", HudStyle.Iron, HudStyle.Brass));
                UiKit.Select(row.Button, quest.Id == _chosen);
                string id = quest.Id;
                row.Button.FocusEntered += () => Choose(id, session);
                row.Button.Pressed += () => Choose(id, session);
                panes.List.AddChild(row.Button);
            }
            panes.List.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        }
        if (quests.FirstOrDefault(q => q.Id == _chosen) is { } chosen)
            Detail(panes.Detail, chosen);
        if (!UiKit.Refocus(_sheet.Root, focused ?? _chosen))
            UiKit.FirstFocusable(_sheet.Root)?.GrabFocus();
    }

    private void Choose(string id, GameSession session)
    {
        if (_chosen == id)
            return;
        _chosen = id;
        // After the focus or press has been handled: the redraw replaces the row that took it.
        Callable.From(() => Refresh(session)).CallDeferred();
    }

    /// <summary>The chosen quest: its title and status, its summary, and its objectives with the tracker's bullets and their counts.</summary>
    private static void Detail(VBoxContainer detail, QuestView quest)
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
            QuestStatus.Failed => UiKit.Chip("Failed", HudStyle.Wound),
            _ => UiKit.Chip("Active", HudStyle.Ember),
        });
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

    /// <summary>The bible's minimal tracker (§19): the active quest's title and what it asks now, or nothing.</summary>
    public static string? Tracker(IReadOnlyList<QuestView> quests) =>
        quests.FirstOrDefault(q => q.Status == QuestStatus.Active) is { } quest
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
