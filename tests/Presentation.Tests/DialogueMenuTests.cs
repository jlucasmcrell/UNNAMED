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
    public void AShortList_ShowsFlat_InTheContentsOrder()
    {
        var replies = new List<(string, DialogueChoice?)> { Reply("a"), Reply("trade", null, new OpenServiceConsequence("trade")), Reply("bye", null) };
        Assert.Equal(new[] { "a", "trade", "bye" }, DialogueMenu.Top(replies));
    }
}
