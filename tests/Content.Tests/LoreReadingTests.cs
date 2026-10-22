using System.Collections.Immutable;
using UNNAMED.Domain.Knowledge;
using Xunit;

namespace UNNAMED.Content.Tests;

/// <summary>
/// Spec 41: the words the game shows about a beast, and the words it reads back at night, are the pages themselves. Nothing is composed at the
/// moment of showing - these are the two lookups the bestiary plate and the inn's rest snippet make.
/// </summary>
public class LoreReadingTests
{
    private static LoreEntryDefinition Entry(string id, LoreCategory category, params (int Page, string Text, LoreSourceKind How, string Ref)[] pages) =>
        new(id, "The Ember Hounds", category, pages.Select(p => new LorePageDefinition(p.Page, p.Text,
            ImmutableArray.Create(new LoreSource(p.How, p.Ref)))).ToImmutableArray());

    private static readonly ImmutableSortedDictionary<string, LoreEntryDefinition> Pack = new[]
    {
        Entry("lore.ember_hounds", LoreCategory.Creatures,
            (1, "The hound comes and it keeps coming. No guard turns the bite.", LoreSourceKind.Studied, "creature.beast.ash_ember_hound"),
            (2, "She has seen what the cinder sac does in a salve and will not write it down.", LoreSourceKind.Told,
                "dialogue.ashen_hollow.sel_arien")),
        Entry("lore.wayhold", LoreCategory.Peoples, (1, "The Wayhold keeps the road. Its seat is the waystation.", LoreSourceKind.Visited, "location.outpost")),
    }.ToImmutableSortedDictionary(e => e.Id, e => e, System.StringComparer.Ordinal);

    [Fact]
    public void AStudiedBeast_SaysWhatItsKnownPageSays()
    {
        var known = ImmutableArray.Create(new LorePageRecord("lore.ember_hounds", 1, LoreSourceKind.Studied, "creature.beast.ash_ember_hound", 10));
        Assert.Equal("The hound comes and it keeps coming.", LoreRules.Known(Pack, known, "creature.beast.ash_ember_hound"));
    }

    [Fact]
    public void ABeastNobodyHasStudied_SaysNothing()
    {
        Assert.Equal("", LoreRules.Known(Pack, ImmutableArray<LorePageRecord>.Empty, "creature.beast.ash_ember_hound"));
        // Studied somewhere else in the pack, but with no page of it known, is the same nothing.
        var other = ImmutableArray.Create(new LorePageRecord("lore.wayhold", 1, LoreSourceKind.Visited, "location.outpost", 10));
        Assert.Equal("", LoreRules.Known(Pack, other, "creature.beast.ash_ember_hound"));
        Assert.Equal("", LoreRules.Known(Pack, other, "creature.beast.grey_wolf"));
    }

    [Fact]
    public void TheNightReading_IsTheNewestPage()
    {
        var known = ImmutableArray.Create(
            new LorePageRecord("lore.ember_hounds", 1, LoreSourceKind.Studied, "creature.beast.ash_ember_hound", 10),
            new LorePageRecord("lore.wayhold", 1, LoreSourceKind.Visited, "location.outpost", 40));
        Assert.Equal("The Wayhold keeps the road.", LoreRules.Latest(Pack, known));
        Assert.Equal("", LoreRules.Latest(Pack, ImmutableArray<LorePageRecord>.Empty));
        // Before New Game there is no Simulation: the loading screen passes default.
        Assert.Equal("", LoreRules.Latest(Pack, default));
    }

    [Fact]
    public void APageThatIsNotThere_ReadsAsNothing()
    {
        var missing = ImmutableArray.Create(new LorePageRecord("lore.ember_hounds", 9, LoreSourceKind.Studied, "creature.beast.ash_ember_hound", 10));
        Assert.Equal("", LoreRules.Latest(Pack, missing));
    }
}
