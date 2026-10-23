using System.Collections.Immutable;
using UNNAMED.Domain.Social;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Tests;

/// <summary>A long list of replies folds to a few at the top level, with the topics behind one entry.</summary>
public class DialogueMenuTests
{
    private static (string, DialogueChoice?) Reply(string id, string? next = "n", params DialogueConsequence[] consequences) =>
        (id, new DialogueChoice(id, id, ImmutableArray<DialogueCondition>.Empty, consequences.ToImmutableArray(), next));

    [Fact]
    public void ALongList_FoldsToQuestsTradeLearnAskAndTheClosingLine()
    {
        var replies = new List<(string, DialogueChoice?)>
        {
            Reply("lore1"), Reply("trade", null, new OpenServiceConsequence("trade")), Reply("lore2"), Reply("lore3"),
            Reply("learn", "lesson", new TeachConsequence(ImmutableArray.Create("recipe.a"))), Reply("lore4"),
            Reply("quest", "q", new StartQuestConsequence("quest.a")), Reply("bye", null),
        };
        var top = DialogueMenu.Top(replies);
        Assert.Equal(new[] { "quest", "trade", "learn", DialogueMenu.Ask, "bye" }, top);
        Assert.Equal(new[] { "lore1", "lore2", "lore3", "lore4", DialogueMenu.Back }, DialogueMenu.Topics(replies));
    }

    [Fact]
    public void QuestArrivalStaysOutsideAsk_AndUnreadTopicsComeFirst()
    {
        var replies = new[] { Reply("read"), Reply("advance"), Reply("new", "q", new StartQuestConsequence("quest.a")),
            Reply("unread"), Reply("more"), Reply("extra"), Reply("bye", null) };
        DialogueMenu.Group Rank(string id) => id == "advance" ? DialogueMenu.Group.QuestAdvance : DialogueMenu.Classify(replies.First(r => r.Item1 == id).Item2);
        bool Read(string id) => id == "read";
        Assert.Equal(new[] { "advance", "new", DialogueMenu.Ask, "bye" }, DialogueMenu.Top(replies, Rank, Read));
        Assert.Equal(new[] { "unread", "more", "extra", "read", DialogueMenu.Back }, DialogueMenu.Topics(replies, Rank, Read));
    }

    [Fact]
    public void WorldFlagAndDeedRepliesAreNeverFolded()
    {
        Assert.Equal(DialogueMenu.Group.QuestAdvance, DialogueMenu.Classify(Reply("flag", "n", new SetWorldFlagConsequence("world.test", 1)).Item2));
        Assert.Equal(DialogueMenu.Group.QuestAdvance, DialogueMenu.Classify(Reply("act", "n", new ReportActConsequence("help", "a")).Item2));
    }

    [Fact]
    public void AShortList_StillPutsServicesBeforeLore()
    {
        var replies = new List<(string, DialogueChoice?)> { Reply("a"), Reply("trade", null, new OpenServiceConsequence("trade")), Reply("bye", null) };
        Assert.Equal(new[] { "trade", "a", "bye" }, DialogueMenu.Top(replies));
    }

    [Fact]
    public void TopicsArePagedFourAtATimeWithoutLosingAnyReply()
    {
        var replies = Enumerable.Range(0, 9).Select(i => Reply("topic" + i)).Append(Reply("bye", null)).ToList();
        Assert.Equal(new[] { DialogueMenu.Ask, "bye" }, DialogueMenu.Top(replies));
        Assert.Equal(new[] { "topic0", "topic1", "topic2", "topic3", DialogueMenu.More, DialogueMenu.Back }, DialogueMenu.Topics(replies));
        Assert.Equal(new[] { "topic4", "topic5", "topic6", "topic7", DialogueMenu.More, DialogueMenu.Back }, DialogueMenu.Topics(replies, page: 1));
        Assert.Equal(new[] { "topic8", DialogueMenu.Back }, DialogueMenu.Topics(replies, page: 2));
    }
}
