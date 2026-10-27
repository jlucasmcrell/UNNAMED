using System.Collections.Immutable;
using UNNAMED.Domain.Quests;
using UNNAMED.Presentation.Ui.Models;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Tests;

public class QuestPresentationTests
{
    private static QuestView Quest(string id, long tick = 0, QuestStatus status = QuestStatus.Active) =>
        new(id, "Title " + id, "Journal summary.", status, tick, null,
            ImmutableArray.Create(new ObjectiveView("step", "Find Renn", ObjectiveStatus.Active, "1/2")));

    [Fact]
    public void TrackedFirstExpanded_ActiveOnly_FiveRowsAndHonestRemainder()
    {
        var quests = Enumerable.Range(0, 8).Select(i => Quest("q" + i, i)).Append(Quest("done", status: QuestStatus.Completed)).ToArray();
        var result = QuestPresentation.Tracker(quests, "q7");
        Assert.Equal(new[] { "q7", "q0", "q1", "q2", "q3" }, result.Rows.Select(r => r.Id));
        Assert.Equal(3, result.More);
        Assert.True(result.Rows[0].Expanded);
        Assert.All(result.Rows.Skip(1), r => Assert.False(r.Expanded));
        Assert.Equal("Find Renn (1/2)", result.Rows[0].Step);
    }

    [Fact]
    public void StaleTrackingFallsBackStably_EmptyOrCompletedQuestsDisappear()
    {
        var result = QuestPresentation.Tracker(new[] { Quest("b"), Quest("a"), Quest("done", status: QuestStatus.Completed) }, "done");
        Assert.Equal(new[] { "a", "b" }, result.Rows.Select(r => r.Id));
        Assert.Equal(0, result.More);
        Assert.Empty(QuestPresentation.Tracker(new[] { Quest("done", status: QuestStatus.Completed) }, null).Rows);
    }

    [Fact]
    public void CardsQueueWithoutStacking_EntryActivationDoesNotRepeatAccept()
    {
        var cards = new QuestCardQueue();
        var accepted = new QuestCard("q", 1, "Title", "Renn", "Journal text", "Find Renn", true);
        cards.Add(accepted);
        Assert.True(cards.HasCards);
        cards.Add(accepted with { Started = false });
        cards.Add(accepted with { Tick = 2, Next = "Find Kera", Started = false });
        cards.Advance(0);
        Assert.Equal(accepted, cards.Current);
        cards.Advance(7);
        Assert.InRange(cards.Opacity, 0.8, 0.9);
        cards.Advance(0.5);
        Assert.Equal("Find Kera", cards.Current!.Next);
        cards.Dismiss();
        cards.Advance(0);
        Assert.Null(cards.Current);
        cards.Add(accepted);
        cards.Clear();
        Assert.False(cards.HasCards);
        cards.Advance(0);
        Assert.Null(cards.Current);
    }
}
