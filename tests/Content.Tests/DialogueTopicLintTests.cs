using System.Collections.Immutable;
using UNNAMED.Content;
using UNNAMED.Domain.Social;

namespace UNNAMED.Content.Tests;

public class DialogueTopicLintTests
{
    private static DialogueChoice Reply(string id, string text, string? next = null, params DialogueConsequence[] consequences) =>
        new(id, text, [], consequences.ToImmutableArray(), next);
    private static DialogueNode Node(string id, params DialogueChoice[] choices) => new(id, id, choices.ToImmutableArray(), null, false, null);
    private static DialogueDefinition Dialogue(params DialogueNode[] nodes) =>
        new("dialogue.test", [], nodes[0].Id, nodes.ToImmutableSortedDictionary(n => n.Id, n => n));

    [Fact]
    public void AGeneratedTellingCannotDuplicateAHandWrittenOffer()
    {
        var dialogue = Dialogue(Node("greet", Reply("manual", "What happened?", null, new TellLoreConsequence("lore.fold")),
                Reply("generated", "Tell me about the fold.", "telling")),
            Node("telling", Reply("back", "Thanks.", "greet", new TellLoreConsequence("lore.fold"))));
        Assert.Contains("duplicate lore_ref", Assert.Throws<FormatException>(() => SocialContent.CheckTopicReplies(dialogue)).Message);
    }

    [Fact]
    public void IdenticalTextIsRejectedDespiteCaseOrTrailingPunctuation()
    {
        var dialogue = Dialogue(Node("greet", Reply("a", "Tell me about Old Late."), Reply("b", "tell me about old late")));
        Assert.Contains("duplicate reply text", Assert.Throws<FormatException>(() => SocialContent.CheckTopicReplies(dialogue)).Message);
    }

    [Fact]
    public void GoingBackToATopicMenuDoesNotOfferAllItsLore()
    {
        var dialogue = Dialogue(Node("greet", Reply("a", "One.", "menu"), Reply("b", "Two.", "menu")),
            Node("menu", Reply("topic", "What happened?", null, new TellLoreConsequence("lore.fold")), Reply("close", "Goodbye.")));
        SocialContent.CheckTopicReplies(dialogue);
    }
}
