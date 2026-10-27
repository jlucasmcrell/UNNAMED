using UNNAMED.Domain.Quests;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui.Models;

public sealed record QuestTrackerRow(string Id, string Title, string Step, bool Expanded);
public sealed record QuestTrackerList(IReadOnlyList<QuestTrackerRow> Rows, int More);

public static class QuestPresentation
{
    public static string Step(QuestView quest) => string.Join("; ", quest.Objectives
        .Where(o => o.Status == ObjectiveStatus.Active)
        .Select(o => o.Description + (o.Progress is { } p ? $" ({p})" : "")));

    public static QuestTrackerList Tracker(IReadOnlyList<QuestView> quests, string? tracked)
    {
        var active = quests.Where(q => q.Status == QuestStatus.Active)
            .OrderBy(q => q.Id == tracked ? 0 : 1).ThenBy(q => q.StartedTick).ThenBy(q => q.Id, StringComparer.Ordinal).ToArray();
        return new(active.Take(5).Select((q, i) => new QuestTrackerRow(q.Id, q.Title, Step(q), i == 0)).ToArray(), Math.Max(0, active.Length - 5));
    }
}

public sealed record QuestCard(string QuestId, long Tick, string Title, string Giver, string Summary, string Next, bool Started);

/// <summary>Presentation time only: cards never hold or advance the simulation.</summary>
public sealed class QuestCardQueue
{
    private readonly Queue<QuestCard> _pending = new();
    public QuestCard? Current { get; private set; }
    public bool HasCards => Current is not null || _pending.Count > 0;
    public double Age { get; private set; }
    public const double Seconds = 7.5;
    public double Opacity => Math.Clamp((Seconds - Age) / 0.6, 0, 1);

    public void Add(QuestCard card)
    {
        // Start and its entry activation arrive together; parallel objectives at one tick share one card.
        if (Current is { } current && current.QuestId == card.QuestId && current.Tick == card.Tick
            || _pending.Any(p => p.QuestId == card.QuestId && p.Tick == card.Tick))
            return;
        _pending.Enqueue(card);
    }

    public void Dismiss() { Current = null; Age = 0; }
    public void Clear() { _pending.Clear(); Dismiss(); }

    public void Advance(double seconds)
    {
        if (Current is not null && (Age += Math.Max(0, seconds)) >= Seconds)
            Dismiss();
        if (Current is null && _pending.TryDequeue(out var next))
        {
            Current = next;
            Age = 0;
        }
    }
}
