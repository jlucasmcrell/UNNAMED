// UNNAMED tests - GEN-08, an item's era as a price.

using System.Collections.Immutable;
using UNNAMED.Domain.Items;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// An era is identity, never a filter: it changes what a piece costs and what a trader gives for it, and it never hides anything from a list.
/// The important property is the one the economy depends on - old work is dearer both ways, so buying to resell still loses money.
/// </summary>
public class ItemEraTests
{
    private static ItemDefinition Item(ItemEra era) => new(
        ItemEra.Modern == era ? "item.test.modern" : "item.test.old",
        "weapon",
        1,
        1_000,
        100,
        "common",
        false,
        false,
        EquipSlot.MainHand,
        Requirements.None,
        null,
        0)
    {
        Era = era,
    };

    private static readonly Pricing Default = new(SellRatio: 0.4);

    [Fact]
    public void AnUntaggedItemIsModern()
    {
        var item = new ItemDefinition("item.test.plain", "misc", 1, 100, 50, "common", false, false, null,
            Requirements.None, null, 0);
        Assert.Equal(ItemEra.Modern, item.Era);
        Assert.Equal(1.0, Default.EraFactor(item.Era));
    }

    [Theory]
    [InlineData(ItemEra.Modern, 1.0)]
    [InlineData(ItemEra.Older, 1.4)]
    [InlineData(ItemEra.Ancient, 2.2)]
    public void EveryEraHasItsMultiplier(ItemEra era, double factor) =>
        Assert.Equal(factor, Default.EraFactor(era));

    [Fact]
    public void AnOlderPieceIsDearerToBuy_AndDearerToSell_SoResaleStillLoses()
    {
        var modern = Item(ItemEra.Modern);
        var ancient = Item(ItemEra.Ancient);
        var stock = new MerchantStock("item.test.old", 1, 1.0);
        var merchant = new Merchant("merchant.test", ImmutableArray.Create(stock), ImmutableArray.Create("weapon"));

        Assert.Equal(100, Default.Value(modern));
        Assert.Equal(220, Default.Value(ancient));
        Assert.Equal(100, Default.BuyPrice(modern, stock));
        Assert.Equal(221, Default.BuyPrice(ancient, stock));   // 100 x 2.2 is 220.00000000000003 in binary, and a price rounds up
        Assert.Equal(40, Default.SellPrice(modern, merchant));
        Assert.Equal(88, Default.SellPrice(ancient, merchant));

        // and the property that makes the economy work: what a trader gives is always under what they ask
        Assert.True(Default.SellPrice(ancient, merchant) < Default.BuyPrice(ancient, stock));
    }

    [Fact]
    public void AQualityAndAMakerMarkStillApplyOnTopOfTheEra()
    {
        var ancient = Item(ItemEra.Ancient);
        Assert.Equal(220, Default.Value(ancient));
        Assert.Equal(275, Default.Value(ancient, quality: 1));                      // +25%
        Assert.Equal(242, Default.Value(ancient, makerMarked: true));               // +10%
    }

    [Theory]
    [InlineData(ItemEra.Modern, "modern")]
    [InlineData(ItemEra.Older, "older")]
    [InlineData(ItemEra.Ancient, "ancient")]
    public void EraKeysRoundTrip(ItemEra era, string key)
    {
        Assert.Equal(key, ItemEras.Key(era));
        Assert.Equal(era, ItemEras.Parse(key));
    }

    [Fact]
    public void AnUnknownEraIsNotAnEra()
    {
        Assert.Null(ItemEras.Parse("elder"));
        Assert.Null(ItemEras.Parse(""));
        Assert.Equal(3, ItemEras.All.Length);
    }
}
