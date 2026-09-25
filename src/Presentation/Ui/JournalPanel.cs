// UNNAMED Presentation - the journal and the quest debugger's panel (PROTOTYPE.md §4.1 UI panels; SYSTEMS.md S-29; M5)
// Godot presentation only: both read views; nothing here changes state (D-11)

using System.Text;
using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Quests;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The journal (J): every quest started, active first, each with its summary and the objectives the character knows of - text
/// directions, never a marker (charter §3). It redraws from the simulation's quest views; it holds no state of its own.
/// </summary>
public partial class JournalPanel : CanvasLayer
{
    private readonly Label _text = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(760, 0) };

    public override void _Ready()
    {
        Visible = false;
        var panel = new PanelContainer { Position = new Vector2(60, 140), CustomMinimumSize = new Vector2(800, 0) };
        _text.AddThemeFontSizeOverride("font_size", 18);
        panel.AddChild(_text);
        AddChild(panel);
    }

    public void Refresh(GameSession session)
    {
        if (!Visible || session.Simulation is not { } simulation)
            return;
        _text.Text = Render(simulation.Quests);
    }

    /// <summary>The journal as text: what the panel shows, and what a test can read.</summary>
    public static string Render(IReadOnlyList<QuestView> quests)
    {
        var text = new StringBuilder($"JOURNAL   [{HelpPanel.Key("journal")}] close\n");
        if (quests.Count == 0)
            return text.Append("\nNo one has asked anything of you yet.").ToString();
        foreach (var quest in quests)
        {
            text.Append('\n').Append(quest.Title.ToUpperInvariant());
            if (quest.Status != QuestStatus.Active)
                text.Append(quest.Status == QuestStatus.Completed ? "   (done)" : "   (failed)");
            text.Append('\n').Append(quest.Summary).Append('\n');
            foreach (var objective in quest.Objectives)
            {
                text.Append(objective.Status switch
                {
                    ObjectiveStatus.Satisfied => "  [x] ",
                    ObjectiveStatus.Failed => "  [!] ",
                    _ => "  [ ] ",
                }).Append(objective.Description);
                if (objective.Progress is { } progress)
                    text.Append(" (").Append(progress).Append(')');
                text.Append('\n');
            }
        }
        return text.ToString();
    }

    /// <summary>The bible's minimal tracker (§19): the active quest's title and what it asks now, or nothing.</summary>
    public static string? Tracker(IReadOnlyList<QuestView> quests) =>
        quests.FirstOrDefault(q => q.Status == QuestStatus.Active) is { } quest
            ? quest.Title.ToUpperInvariant() + "\n" + string.Join("\n", quest.Objectives.Where(o => o.Status == ObjectiveStatus.Active)
                .Select(o => o.Description + (o.Progress is { } p ? $" ({p})" : "")))
            : null;
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
        var text = new StringBuilder("QUEST DEBUGGER   [F4] close\n");
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
