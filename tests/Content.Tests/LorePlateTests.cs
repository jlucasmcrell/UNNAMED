using System.Collections.Immutable;
using System.IO;
using System.Linq;
using UNNAMED.Content;
using UNNAMED.Domain.Knowledge;
using Xunit;

namespace UNNAMED.Content.Tests;

/// <summary>
/// Spec 41, owner ruling 2026-10-22: the target plate says one thing a hunter can act on - a tactic or a weakness, ninety characters or fewer,
/// in the world's voice - and nothing at all when no page of the beast is known. A summary of a page is not a tactic; a plate is authored, not cut.
/// </summary>
public class LorePlateTests
{
    private static ContentLoader Load()
    {
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(RepoPaths.Root(), "content"));
        Assert.Empty(loader.Errors);
        return loader;
    }

    [Fact]
    public void EveryBeastThatCanBeStudied_HasAPlateLineOfNinetyOrFewer()
    {
        var entries = LoreContent.Build(Load(), check: true);
        var beasts = entries.Values.Where(e => e.Pages.Any(p => p.Sources.Any(s => s.Kind == LoreSourceKind.Studied))).ToList();
        Assert.NotEmpty(beasts);
        foreach (var entry in beasts)
        {
            Assert.True(entry.Plate.Length > 0, $"{entry.Id} can be studied and says nothing on the plate");
            Assert.True(entry.Plate.Length <= 90, $"{entry.Id} plate is {entry.Plate.Length} characters");
            Assert.Null(LoreRules.Leak(entry.Plate));
        }
    }

    [Fact]
    public void ABeastNobodyHasStudied_SaysNothingOnThePlate()
    {
        var entries = new[]
        {
            new LoreEntryDefinition("lore.ember_hounds", "The Ember Hounds", LoreCategory.Creatures,
                ImmutableArray.Create(new LorePageDefinition(1, "The hound comes on and does not stop.",
                    ImmutableArray.Create(new LoreSource(LoreSourceKind.Studied, "creature.beast.ash_ember_hound")))),
                "Ember-hot bite: no guard turns it. Watch the tell, be elsewhere when it lands."),
        }.ToImmutableSortedDictionary(e => e.Id, e => e, System.StringComparer.Ordinal);
        Assert.Equal("", LoreRules.Plate(entries, ImmutableArray<LorePageRecord>.Empty, "creature.beast.ash_ember_hound"));
        var known = ImmutableArray.Create(new LorePageRecord("lore.ember_hounds", 1, LoreSourceKind.Studied,
            "creature.beast.ash_ember_hound", 5));
        Assert.Equal("Ember-hot bite: no guard turns it. Watch the tell, be elsewhere when it lands.",
            LoreRules.Plate(entries, known, "creature.beast.ash_ember_hound"));
        // A beast with no entry of its own has nothing to say, known pages or not.
        Assert.Equal("", LoreRules.Plate(entries, known, "creature.beast.grey_wolf"));
    }
}
