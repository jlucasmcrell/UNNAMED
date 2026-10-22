using System.Linq;
using UNNAMED.Content;
using UNNAMED.Domain.Knowledge;
using Xunit;

namespace UNNAMED.Content.Tests;

/// <summary>
/// Spec 41, "lore that unlocks a hidden path": a page that tells the reader where something is puts that place on the map as told
/// (MAPS_CARTOGRAPHY_AND_WORLD_KNOWLEDGE.md §6 — the job did not change, the knowledge did). The page may only name a place the pack has, and
/// the place must be one the world actually lays out.
/// </summary>
public class LoreRevealTests
{
    private static ContentLoader Load()
    {
        var loader = new ContentLoader();
        loader.LoadAll(System.IO.Path.Combine(RepoPaths.Root(), "content"));
        Assert.Empty(loader.Errors);
        return loader;
    }

    [Fact]
    public void EveryPageThatTellsOfAPlace_NamesAPlaceThatExists()
    {
        var loader = Load();
        var entries = LoreContent.Build(loader);
        int revealing = 0;
        foreach (var page in entries.Values.SelectMany(e => e.Pages).Where(p => p.TellsOfAPlace))
        {
            revealing++;
            Assert.True(loader.Definitions.ContainsKey(page.Reveals), $"{page.Reveals} is not a definition the pack has");
            Assert.StartsWith("location.", page.Reveals);
        }
        Assert.True(revealing >= 10, $"only {revealing} pages tell of a place; the world knowledge pass expects more");
    }
}
