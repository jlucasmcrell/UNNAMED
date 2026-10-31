// UNNAMED Content tests - GEN-08, the era an item carries.

using UNNAMED.Domain.Items;

namespace UNNAMED.Content.Tests;

/// <summary>
/// The era is content: an item may name one of the three, an item that names none is modern, and a word that is not an era is a content error
/// rather than a silent modern. The shipped pack's old work is tagged and its everyday work is not.
/// </summary>
public class ItemEraContentTests
{
    private static ContentLoader Game()
    {
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(RepoPaths.Root(), "content"));
        return loader;
    }

    [Fact]
    public void TheShippedPacksHeritage_IsAncient_AndTheBarrowworksFindIsOlder()
    {
        var items = ItemContent.BuildCatalog(Game());

        Assert.Equal(ItemEra.Ancient, items.Get("item.heritage.cost_ledger").Era);
        Assert.Equal(ItemEra.Ancient, items.Get("item.heritage.record_stone").Era);
        Assert.Equal(ItemEra.Older, items.Get("item.quest.barrowworks_codex_fragment").Era);
    }

    [Fact]
    public void AnItemThatNamesNoEra_IsModern()
    {
        var items = ItemContent.BuildCatalog(Game());
        var modern = items.Definitions.Values.Count(i => i.Era == ItemEra.Modern);

        Assert.True(modern > 50, $"only {modern} items are modern: the default is not being applied");
    }

    [Fact]
    public void TheEconomy_PricesTheErasItShips()
    {
        var pricing = ItemContent.BuildPricing(Game());

        Assert.Equal(1.0, pricing.EraFactor(ItemEra.Modern));
        Assert.Equal(1.4, pricing.EraFactor(ItemEra.Older));
        Assert.Equal(2.2, pricing.EraFactor(ItemEra.Ancient));
        Assert.True(pricing.SellRatio < 1, "an era must not make resale profitable");
    }
}
