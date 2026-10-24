using System.Collections.Immutable;
using UNNAMED.Content;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;

namespace UNNAMED.Content.Tests;

/// <summary>Owner finding 53: a reply that starts a quest is not offered once that quest is done, nor while it runs unless it advances it.</summary>
public class QuestOfferLintTests
{
    [Fact]
    public void HistoricalContentRemainsValidButNewAuthoringStillRequiresOfferGuards()
    {
        string root=Path.Combine(RepoPaths.Root(), "tests", "Persistence.Tests", "Fixtures", "content");
        var runtime=new ContentLoader();
        Assert.True(runtime.LoadAll(root), string.Join("\n",runtime.Errors));
        var authoring=new ContentLoader();
        Assert.False(authoring.LoadAll(root,authoring:true));
        Assert.Contains(authoring.Errors,e=>e.Code=="QST001" && e.Message.Contains("once it is completed"));
    }

    private const string Quest = "quest.test.errand";
    private const string Talk = "dialogue.test";

    private static readonly IReadOnlyDictionary<string, QuestDefinition> Quests = new Dictionary<string, QuestDefinition>
    {
        [Quest] = new(Quest, "Errand", "Errand.", null, "o_offer", ImmutableArray.Create(
            new ObjectiveDefinition("o_offer", "Hear the errand.", new TalkTo("npc.test", Talk, ImmutableArray.Create("errand")), ImmutableArray.Create("o_report")),
            new ObjectiveDefinition("o_report", "Report back.", new TalkTo("npc.test", Talk, ImmutableArray.Create("report")), ImmutableArray<string>.Empty)),
            ImmutableArray<ObjectiveCondition>.Empty, ImmutableArray<QuestReward>.Empty),
    };

    private static void Check(string next, bool once = false, params DialogueCondition[] conditions)
    {
        var reply = new DialogueChoice("take", "I'll do it.", conditions.ToImmutableArray(),
            ImmutableArray.Create<DialogueConsequence>(new StartQuestConsequence(Quest)), next);
        var hub = new DialogueNode("again", "Well?", ImmutableArray.Create(reply), null, once, null);
        var dialogue = new DialogueDefinition(Talk, ImmutableArray.Create("npc.test"), "again", ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal,
            new[] { "again", "errand", "report", "chat" }.Select(id => KeyValuePair.Create(id, id == "again" ? hub
                : new DialogueNode(id, id, ImmutableArray<DialogueChoice>.Empty, null, false, null)))));
        QuestContent.CheckQuestOffers(Quests, new Dictionary<string, DialogueDefinition> { [Talk] = dialogue });
    }

    private static QuestStateCondition Is(string state, bool not = false) => new(Quest, null, state, not);

    [Fact]
    public void AnUnguardedOfferIsStillOfferedWhenTheQuestIsDone() =>
        Assert.Contains("once it is completed", Assert.Throws<FormatException>(() => Check("chat")).Message);

    [Fact]
    public void HidingAnOfferOnlyWhileActiveLeavesItAfterCompletion() =>
        Assert.Contains("once it is completed", Assert.Throws<FormatException>(() => Check("errand", false, Is("active", not: true))).Message);

    [Fact]
    public void AnOfferThatDoesNotAdvanceTheQuestMustHideWhileItRuns() =>
        Assert.Contains("while it is active", Assert.Throws<FormatException>(() => Check("chat", false, Is("completed", not: true))).Message);

    [Fact]
    public void AReplyThatIsALaterTalkMayStayWhileTheQuestRuns() => Check("report", false, Is("completed", not: true));

    [Fact]
    public void AvailableGuardsAnOffer() => Check("chat", false, Is(QuestKeys.Available));

    [Fact]
    public void RepliesSaidOnceNeedNoQuestGuard()
    {
        Check("chat", once: true);
        Check("chat", false, new VisitedCondition("chat", Negated: true));
    }
}
