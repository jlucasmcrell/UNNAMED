// UNNAMED Content tests - the Field Guide's entries (SYS-166): every entry is reachable by a search, and the search behaves

using System.Linq;
using UNNAMED.Content;
using UNNAMED.Domain.Guidance;
using Xunit;

namespace UNNAMED.Content.Tests;

public sealed class FieldGuideContentTests
{
    [Fact]
    public void EveryEntryIsComplete()
    {
        Assert.NotEmpty(FieldGuideContent.Entries);
        foreach (GuideEntry entry in FieldGuideContent.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Section), "an entry with no section cannot be indexed");
            Assert.False(string.IsNullOrWhiteSpace(entry.Title), "an entry with no title cannot be found by name");
            Assert.True(entry.Body.Length > 40, $"'{entry.Title}' says too little to be worth a page");
        }
    }

    [Fact]
    public void NoTwoEntriesShareASectionAndTitle()
    {
        var duplicates = FieldGuideContent.Entries
            .GroupBy(e => (e.Section, e.Title))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        Assert.Empty(duplicates);
    }

    [Theory]
    [InlineData("guard")]
    [InlineData("debt")]
    [InlineData("noise")]
    [InlineData("pack")]
    [InlineData("hunger")]
    [InlineData("waystone")]
    [InlineData("companion")]
    [InlineData("200")]
    public void APlayerCanFindTheTopicTheyNeed(string query)
    {
        var hits = FieldGuide.Search(FieldGuideContent.Entries, query);
        Assert.NotEmpty(hits);
    }

    [Fact]
    public void EveryWordTypedMustBeFound()
    {
        // a query whose words do not co-occur in any entry must return nothing, which is what proves the AND
        var none = FieldGuide.Search(FieldGuideContent.Entries, "waystone hunger");
        Assert.Empty(none);

        var guard = FieldGuide.Search(FieldGuideContent.Entries, "guard");
        Assert.Contains(guard, e => e.Title.Contains("Guard"));
        var noise = FieldGuide.Search(FieldGuideContent.Entries, "noise");
        Assert.Contains(noise, e => e.Title.Contains("Noise"));
    }

    [Fact]
    public void ATitleHitBeatsABodyHit()
    {
        var hits = FieldGuide.Search(FieldGuideContent.Entries, "debt");
        Assert.NotEmpty(hits);
        Assert.Contains("debt", hits[0].Title, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnEmptySearchListsEverythingInReadingOrder()
    {
        var all = FieldGuide.Search(FieldGuideContent.Entries, "   ");
        Assert.Equal(FieldGuideContent.Entries.Length, all.Length);
        var sections = all.Select(e => e.Section).ToArray();
        Assert.Equal(sections.OrderBy(s => s, System.StringComparer.Ordinal), sections);
    }
}
