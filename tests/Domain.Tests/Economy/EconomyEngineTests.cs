using System.Collections.Immutable;
using UNNAMED.Domain.Economy;
using Xunit;

namespace UNNAMED.Domain.Tests.Economy;

/// <summary>WP14's economy rules on a small two-town economy: the price formula and its DN6 bounds, a day's flows, the carrier, purses.</summary>
public class EconomyEngineTests
{
    private const string Ore = "item.material.ore";
    private const string Hide = "item.material.hide";

    private static MarketGood Good(string item, long target = 100, long min = 10, long max = 200, long production = 0, long consumption = 0,
        long imports = 0, int elasticity = 1000) => new MarketGood(item, target, min, max, production, consumption, imports, elasticity).Validated("test");

    private static EconomySetup Setup(IEnumerable<MarketGood> west, IEnumerable<MarketGood> east, params TradeLink[] links)
    {
        var markets = new[]
        {
            new MarketDefinition("market.west", ImmutableArray.Create("merchant.west"), west.ToImmutableSortedDictionary(g => g.ItemId, g => g, StringComparer.Ordinal)),
            new MarketDefinition("market.east", ImmutableArray.Create("merchant.east"), east.ToImmutableSortedDictionary(g => g.ItemId, g => g, StringComparer.Ordinal)),
        }.ToImmutableSortedDictionary(m => m.Id, m => m, StringComparer.Ordinal);
        var merchants = new[]
        {
            new MerchantEconomy("merchant.west", "market.west", markets["market.west"].Goods.Keys.ToImmutableArray(), 10, 100, 2),
            new MerchantEconomy("merchant.east", "market.east", markets["market.east"].Goods.Keys.ToImmutableArray(), 10, 100, 1),
        }.ToImmutableSortedDictionary(m => m.MerchantId, m => m, StringComparer.Ordinal);
        return new EconomySetup(markets, links.ToImmutableArray(), merchants, EconomyRules.Default);
    }

    private static (EconomyState, ImmutableSortedDictionary<string, long>) Step(EconomySetup setup, EconomyState state) =>
        EconomyEngine.Step(setup, state, state.Day + 1, _ => 10);

    private static EconomyState WithStock(EconomyState state, string market, string item, long stock) =>
        state with { Markets = state.Markets.SetItem(market, state.Markets[market] with { Stock = state.Markets[market].Stock.SetItem(item, stock) }) };

    [Theory]
    [InlineData(100, 1000)]   // at target: base
    [InlineData(50, 1500)]    // half short: 1 + 1.0 x 0.5
    [InlineData(0, 1600)]     // empty: capped at DN6's 1.6
    [InlineData(150, 600)]    // half over: 0.5, floored at DN6's 0.6
    [InlineData(120, 800)]
    public void ThePriceFactor_AnswersTheShortage_WithinDN6(long stock, int factor) =>
        Assert.Equal(factor, EconomyEngine.PriceFactor(Good(Ore, max: 300, min: 0), stock, EconomyRules.Default));

    [Fact]
    public void AnAsk_AtBase_IsTheOldPrice_AndAnOfferIsAlwaysBelowIt()
    {
        Assert.Equal(45, EconomyEngine.Ask(30, 1.5, 1000));
        Assert.Equal(9, EconomyEngine.Ask(8, 1.1, 1000));
        Assert.Equal(3, EconomyEngine.Offer(8, 0.4, 1000, 0, EconomyRules.Default, 8));
        Assert.Equal(1, EconomyEngine.Offer(8, 0.4, 1000, 40, EconomyRules.Default, 8));   // 40 remembered: a third
        Assert.Equal(4, EconomyEngine.Offer(10, 0.9, 1600, 0, EconomyRules.Default, 5));    // never at or above the ask
        Assert.Equal(0, EconomyEngine.Offer(1, 0.4, 1000, 0, EconomyRules.Default, 1));
    }

    [Fact]
    public void ADay_MakesImportsUsesRelaxesAndClamps()
    {
        var setup = Setup(new[] { Good(Ore, production: 5, consumption: 20, imports: 3) }, new[] { Good(Hide) });
        var state = WithStock(EconomyEngine.Reconcile(setup, null, 0), "market.west", Ore, 50);
        var (next, income) = Step(setup, state);
        // 50 + 5 made, + 3 brought in (short), - 20 used = 38; then a tenth of the 62 short: + 6.
        Assert.Equal(44, next.Markets["market.west"].Stock[Ore]);
        Assert.Equal(1, next.Day);
        Assert.Equal(20 * 15, income["merchant.west"]);   // 20 used at 10 x 1.5 (50 of 100)

        // A small distance still closes, one a day; and nothing leaves its bounds.
        var near = WithStock(state, "market.west", Ore, 199);
        var setupNear = Setup(new[] { Good(Ore) }, new[] { Good(Hide) });
        Assert.Equal(190, Step(setupNear, near).Item1.Markets["market.west"].Stock[Ore]);
        Assert.Equal(99, Step(setupNear, WithStock(state, "market.west", Ore, 98)).Item1.Markets["market.west"].Stock[Ore]);
        Assert.Throws<ArgumentOutOfRangeException>(() => EconomyEngine.Step(setup, state, 5, _ => 10));
    }

    [Fact]
    public void TheCarrier_TakesSurplusToDeficit_WithinItsCapacity_ArrivingLater()
    {
        var link = new TradeLink("road.test", "market.west", "market.east", 2, 15);
        var setup = Setup(new[] { Good(Ore) }, new[] { Good(Ore) }, link) with { Rules = EconomyRules.Default with { RelaxPermille = 0 } };
        var state = WithStock(WithStock(EconomyEngine.Reconcile(setup, null, 0), "market.west", Ore, 160), "market.east", Ore, 70);

        var (day1, _) = Step(setup, state);
        Assert.Equal(145, day1.Markets["market.west"].Stock[Ore]);   // 15 set out: the cart's load
        Assert.Equal(new Shipment("road.test", "market.west", "market.east", Ore, 15, 3), Assert.Single(day1.Shipments));
        var (day2, _) = Step(setup, day1);
        Assert.Equal(130, day2.Markets["market.west"].Stock[Ore]);   // 15 more: 30 on the road of the 30 short
        var (day3, _) = Step(setup, day2);
        Assert.Equal((85L, 130L), (day3.Markets["market.east"].Stock[Ore], day3.Markets["market.west"].Stock[Ore]));   // the first load in; nothing more owed
        var (day4, _) = Step(setup, day3);
        Assert.Equal(100, day4.Markets["market.east"].Stock[Ore]);
        Assert.Empty(day4.Shipments);
    }

    [Fact]
    public void ManyDaysStepped_LeaveWhatTheSameDaysOneByOneDo()
    {
        var link = new TradeLink("road.test", "market.west", "market.east", 1, 7);
        var setup = Setup(new[] { Good(Ore, production: 9, consumption: 2), Good(Hide, consumption: 3) },
            new[] { Good(Ore, consumption: 6), Good(Hide, production: 8, consumption: 1, imports: 2) }, link);
        var start = EconomyEngine.Reconcile(setup, null, 0);
        var a = start;
        for (int i = 0; i < 200; i++)
            a = Step(setup, a).Item1;
        var b = start;
        for (int i = 0; i < 100; i++)
            b = Step(setup, b).Item1;
        var resumed = EconomyEngine.Reconcile(setup, b, 0);   // as a save carries it over
        for (int i = 0; i < 100; i++)
            resumed = Step(setup, resumed).Item1;
        Assert.Equal(a.Day, resumed.Day);
        Assert.Equal(a.Markets.Values.SelectMany(m => m.Stock), resumed.Markets.Values.SelectMany(m => m.Stock));
        Assert.Equal(a.Shipments, resumed.Shipments);
    }

    [Fact]
    public void TheSellMemory_FadesDaily_AndATradeOfAGoodNotKeptMovesNoStock()
    {
        var setup = Setup(new[] { Good(Ore) }, new[] { Good(Hide) });
        var market = setup.Markets["market.west"];
        var state = EconomyEngine.Reconcile(setup, null, 0);
        var traded = EconomyEngine.Traded(market, state.Markets["market.west"], Hide, 0, 30);
        Assert.Equal(100, traded.Stock[Ore]);
        Assert.False(traded.Stock.ContainsKey(Hide));
        Assert.Equal(30, traded.SoldByCharacter[Hide]);
        var next = Step(setup, state with { Markets = state.Markets.SetItem("market.west", traded) }).Item1;
        Assert.Equal(27, next.Markets["market.west"].SoldByCharacter[Hide]);
    }

    [Fact]
    public void APurse_FillsToItsTop_AndARestockBringsItToItsFloor_EveryRestockDays()
    {
        var setup = Setup(new[] { Good(Ore) }, new[] { Good(Hide) });
        var west = setup.Merchants["merchant.west"];
        var purse = EconomyEngine.Reconcile(west, null, 0);
        Assert.Equal(100, purse.Purse);
        purse = purse with { Purse = 0 };
        purse = EconomyEngine.Settle(west, purse, 1, 3);
        Assert.Equal((3L, 0L), (purse.Purse, purse.RestockedDay));   // restocks every 2 days
        purse = EconomyEngine.Settle(west, purse, 2, 0);
        Assert.Equal((10L, 2L), (purse.Purse, purse.RestockedDay));
        Assert.Equal(100, EconomyEngine.Settle(west, purse, 3, 1_000).Purse);
        Assert.Equal(100, EconomyEngine.Paid(west, purse, 5_000).Purse);
    }

    [Fact]
    public void Reconcile_DropsWhatContentNoLongerDefines_AndAddsWhatItNowDoes()
    {
        var setup = Setup(new[] { Good(Ore) }, new[] { Good(Hide) }, new TradeLink("road.test", "market.west", "market.east", 1, 10));
        var saved = new EconomyState(9,
            ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[]
            {
                KeyValuePair.Create("market.west", new MarketState("market.west",
                    ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new[] { KeyValuePair.Create(Ore, 500L), KeyValuePair.Create("item.gone", 3L) }),
                    ImmutableSortedDictionary<string, long>.Empty)),
                KeyValuePair.Create("market.gone", new MarketState("market.gone", ImmutableSortedDictionary<string, long>.Empty, ImmutableSortedDictionary<string, long>.Empty)),
            }),
            ImmutableArray.Create(
                new Shipment("road.test", "market.gone", "market.west", "item.gone", 4, 10),
                new Shipment("road.test", "market.gone", "market.east", Hide, 3, 10),     // its source is gone
                new Shipment("road.gone", "market.west", "market.east", Hide, 5, 10),     // its road is gone
                new Shipment("road.test", "market.west", "market.east", Hide, 2, 10)));
        var state = EconomyEngine.Reconcile(setup, saved, 0);
        Assert.Equal(9, state.Day);
        Assert.Equal(new[] { "market.east", "market.west" }, state.Markets.Keys);
        Assert.Equal(200, state.Markets["market.west"].Stock[Ore]);   // clamped into its bounds
        Assert.False(state.Markets["market.west"].Stock.ContainsKey("item.gone"));
        Assert.Equal(100, state.Markets["market.east"].Stock[Hide]);
        Assert.Equal(Hide, Assert.Single(state.Shipments).ItemId);
    }
}
