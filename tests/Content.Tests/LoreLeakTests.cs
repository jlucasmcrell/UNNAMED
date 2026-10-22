using System.IO;
using System.Linq;
using UNNAMED.Content;
using Xunit;

namespace UNNAMED.Content.Tests;

/// <summary>
/// Spec 41: the words a reader meets are the world's, never the notes the content was written from. A page that says "prototype", "player",
/// "design" or "quest" is the designer talking through the fiction, and the lore lint refuses it.
/// </summary>
public class LoreLeakTests
{
    [Theory]
    [InlineData("prototype", "It communicates that the world goes on past this prototype.")]
    [InlineData("player", "The player should notice the smoke before anything else.")]
    [InlineData("design", "That is a design decision, not a fact about the hollow.")]
    [InlineData("quest", "Finishing the quest opens the next page.")]
    [InlineData("slice", "This slice of the frontier is the only one written so far.")]
    public void TheTradeWords_AreRefused_OnAPage(string word, string sentence)
    {
        Assert.Equal(word, UNNAMED.Domain.Knowledge.LoreRules.Leak(sentence));
    }

    [Fact]
    public void TheWorldsWords_Pass()
    {
        foreach (string line in new[]
                 {
                     "Whatever was on the far side of it, nobody has found the way through.",
                     "The seam runs on into that wall and stops where the walling starts.",
                     "She has tried it eleven times and it has worked eleven times.",
                     "The Record has eleven lines on the Foldscar and four contradict the other seven.",
                 })
            Assert.Null(UNNAMED.Domain.Knowledge.LoreRules.Leak(line));
    }

    [Fact]
    public void TheGamesOwnLore_ReadsClean()
    {
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(RepoPaths.Root(), "content"));
        Assert.Empty(loader.Errors);
        var entries = LoreContent.Build(loader, check: true);
        Assert.NotEmpty(entries);
        foreach (var page in entries.Values.SelectMany(e => e.Pages))
            Assert.Null(UNNAMED.Domain.Knowledge.LoreRules.Leak(page.Text));
    }
}
