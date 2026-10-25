using UNNAMED.Domain.Guidance;

namespace UNNAMED.Domain.Tests;

/// <summary>1I's Field Guide search: every typed word must be found, title hits outrank body hits, and nothing typed lists the lot.</summary>
public class FieldGuideTests
{
    private static readonly GuideEntry[] Entries =
    {
        new("Screens", "Journal", "Controls: J"),
        new("Acting", "Open, take, talk", "Controls: E"),
        new("Guidance", "The pack is full - visit a station to empty it", "inventory: waits until pack full"),
        new("How this world works", "Living world", "The world does not stop while a screen is open."),
    };

    [Fact]
    public void NothingTyped_ListsEveryEntry_BySectionThenTitle()
    {
        Assert.Equal(new[] { "Open, take, talk", "The pack is full - visit a station to empty it", "Living world", "Journal" },
            FieldGuide.Search(Entries, "").Select(e => e.Title));
        Assert.Equal(4, FieldGuide.Search(Entries, null).Length);
        Assert.Equal(4, FieldGuide.Search(Entries, "   ").Length);
    }

    [Fact]
    public void OneWord_FindsItInTitleBodyOrSection_CaseDoesNotMatter()
    {
        Assert.Equal(new[] { "Journal" }, FieldGuide.Search(Entries, "journal").Select(e => e.Title));
        Assert.Equal(new[] { "Journal" }, FieldGuide.Search(Entries, "JOURNAL").Select(e => e.Title));
        Assert.Equal(new[] { "The pack is full - visit a station to empty it" }, FieldGuide.Search(Entries, "pack").Select(e => e.Title));
        Assert.Equal(new[] { "Open, take, talk" }, FieldGuide.Search(Entries, "acting").Select(e => e.Title));
    }

    [Fact]
    public void EveryWordMustBeFound_SoTwoWordsNarrowTheList()
    {
        Assert.Single(FieldGuide.Search(Entries, "pack station"));
        Assert.Empty(FieldGuide.Search(Entries, "pack journal"));
        Assert.Empty(FieldGuide.Search(Entries, "nothingmatchesthis"));
    }

    [Fact]
    public void ATitleHitOutranksABodyHit()
    {
        GuideEntry[] entries =
        {
            new("Guidance", "Open a door", "controls: E"),
            new("Acting", "Open, take, talk", "Controls: E"),
            new("Screens", "Chat", "Open it with T; the door is not here"),
        };
        // "open" is in the title of two entries and in the body of the third: the body hit comes last.
        var found = FieldGuide.Search(entries, "open").Select(e => e.Title).ToArray();
        Assert.Equal("Chat", found[^1]);
        Assert.Equal(new[] { "Open, take, talk", "Open a door" }, found[..^1]);
    }

    [Fact]
    public void AHintBecomesAGuideEntry_WithItsKeysSpelledOutAndItsMomentInTheBody()
    {
        var hint = new HintDefinition("hint.pack", "inventory", HintTriggers.PackFull, "The pack is full: {key:take_all} empties a chest.", 0);
        var entry = FieldGuide.Of(hint, action => action == "take_all" ? "R" : action);
        Assert.Equal("Guidance", entry.Section);
        Assert.Equal("The pack is full: R empties a chest.", entry.Title);
        Assert.Contains("inventory", entry.Body);
        Assert.Contains("pack full", entry.Body);
        // And it is findable by that spelled-out key.
        Assert.Single(FieldGuide.Search(new[] { entry }, "R empties"));
    }
}
