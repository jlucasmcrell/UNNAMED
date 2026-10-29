using System.Collections.Immutable;
using UNNAMED.Domain.Economy;
using UNNAMED.Domain.Items;
using Xunit.Abstractions;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP14's exploit and stability gates over the game's own economy content, run on the pure rules (<see cref="EconomyEngine"/>) the
/// simulation runs: the 100-run arbitrage harness (E-1: "the second run is worse than the first"; E-2: limited buys per restock), a
/// 183-day soak inside every market's bounds (RK-12), and the bounded money supply.
/// </summary>
public class EconomyHarnessTests
{
    private readonly ITestOutputHelper _output;

    public EconomyHarnessTests(ITestOutputHelper output) => _output = output;

    /// <summary>The economy and every purse, stepped a day at a time exactly as the markets and merchants are.</summary>
    private sealed class World
    {
        public World(GameSession session, double? sellRatio = null)
        {
            Setup = session.Setup.Economy;
            Catalog = session.Setup.Items.Catalog;
            Merchants = session.Setup.Items.Merchants;
            Pricing = sellRatio is { } ratio ? new Pricing(ratio) : session.Setup.Items.Pricing;
            State = EconomyEngine.Reconcile(Setup, null, 0);
            Purses = Setup.Merchants.Values.ToDictionary(m => m.MerchantId, m => EconomyEngine.Reconcile(m, null, 0), StringComparer.Ordinal);
        }

        public EconomySetup Setup { get; }
        public ItemCatalog Catalog { get; }
        public ImmutableSortedDictionary<string, Merchant> Merchants { get; }
        public Pricing Pricing { get; }
        public EconomyState State { get; private set; }
        public Dictionary<string, MerchantState> Purses { get; }

        public void Days(int days)
        {
            for (int i = 0; i < days; i++)
            {
                (State, var income) = EconomyEngine.Step(Setup, State, State.Day + 1, id => Catalog.Get(id).ValueBase);
                foreach (var merchant in Setup.Merchants.Values)
                    Purses[merchant.MerchantId] = EconomyEngine.Settle(merchant, Purses[merchant.MerchantId], State.Day, income.GetValueOrDefault(merchant.MerchantId));
            }
        }

        public string MarketOf(string merchant) => Setup.Merchants[merchant].MarketId!;

        public int Factor(string market, string item) =>
            Setup.Markets[market].Goods.TryGetValue(item, out var good) ? EconomyEngine.PriceFactor(good, State.Markets[market].Stock[item], Setup.Rules) : 1000;

        public long Ask(string merchant, string item) =>
            EconomyEngine.Ask(Catalog.Get(item).ValueBase, Merchants[merchant].Stock.FirstOrDefault(s => s.ItemId == item)?.PriceBias ?? 1.0,
                Factor(MarketOf(merchant), item));

        public long? Offer(string merchant, string item)
        {
            var definition = Catalog.Get(item);
            if (Pricing.SellPrice(definition, Merchants[merchant]) is null)
                return null;
            string market = MarketOf(merchant);
            return EconomyEngine.Offer(definition.ValueBase, Pricing.SellRatio, Factor(market, item),
                State.Markets[market].SoldByCharacter.GetValueOrDefault(item), Setup.Rules, Ask(merchant, item));
        }

        /// <summary>How many a merchant's shelf can sell after a restock: their stock line, while the market holds as many (E-2).</summary>
        public int Shelf(string merchant, string item) =>
            Merchants[merchant].Stock.Where(s => s.ItemId == item).Sum(s => EconomyEngine.Shelf(Setup.Markets[MarketOf(merchant)].Goods[item],
                State.Markets[MarketOf(merchant)].Stock[item], s.Count));

        public long Buy(string merchant, string item, int count)
        {
            long price = Ask(merchant, item) * count;
            Trade(merchant, item, count, 0);
            Purses[merchant] = EconomyEngine.Paid(Setup.Merchants[merchant], Purses[merchant], price);
            return price;
        }

        /// <summary>Sell as many as the purse covers; returns how many sold and for how much.</summary>
        public (int Sold, long Coin) Sell(string merchant, string item, int count)
        {
            long each = Offer(merchant, item) ?? 0;
            if (each <= 0)
                return (0, 0);
            int sold = (int)Math.Min(count, Purses[merchant].Purse / each);
            if (sold == 0)
                return (0, 0);
            Purses[merchant] = Purses[merchant] with { Purse = Purses[merchant].Purse - sold * each };
            Trade(merchant, item, 0, sold);
            return (sold, sold * each);
        }

        /// <summary>Put a market's stock of a good somewhere: a glut or a shortage to start from.</summary>
        public void SetStock(string market, string item, long stock) =>
            State = State with
            {
                Markets = State.Markets.SetItem(market, State.Markets[market] with { Stock = State.Markets[market].Stock.SetItem(item, stock) }),
            };

        public void Trade(string merchant, string item, long bought, long sold)
        {
            string market = MarketOf(merchant);
            State = State with
            {
                Markets = State.Markets.SetItem(market, EconomyEngine.Traded(Setup.Markets[market], State.Markets[market], item, bought, sold)),
            };
        }

        public void AssertInBounds(string when)
        {
            foreach (var market in Setup.Markets.Values)
            {
                foreach (var good in market.Goods.Values)
                    Assert.True(State.Markets[market.Id].Stock[good.ItemId] >= good.Min && State.Markets[market.Id].Stock[good.ItemId] <= good.Max,
                        $"{when}: {market.Id} {good.ItemId} at {State.Markets[market.Id].Stock[good.ItemId]}, outside [{good.Min}, {good.Max}]");
            }
            foreach (var merchant in Setup.Merchants.Values.Where(m => m.HasPurse))
                Assert.InRange(Purses[merchant.MerchantId].Purse, 0, merchant.ReserveMax!.Value);
        }
    }

    /// <summary>
    /// The 100-run harness: a trader with 500 coin and room for 20 runs the Old East Road 100 times. Each run they buy whatever shelved good
    /// has the best margin on offer between the two smiths - even a losing one - as much as the shelf (E-2), their purse and their pack
    /// allow, walk the road (a day), sell what the other smith's purse will pay for, and walk back (a day). Returns each run's profit.
    /// </summary>
    private List<long> Runs(World world)
    {
        var smiths = world.Setup.Merchants.Values.Where(m => m.MarketId is "market.ashen_hollow" or "market.cairnwold")
            .Select(m => m.MerchantId).ToList();
        Assert.Equal(2, smiths.Count);
        long coin = 500;
        const int Pack = 20;
        var profits = new List<long>();
        for (int run = 0; run < 100; run++)
        {
            var best = smiths.SelectMany(from => smiths.Where(to => to != from).SelectMany(to => world.Setup.Merchants[from].Goods.Select(item =>
                    (From: from, To: to, Item: item, Margin: (world.Offer(to, item) ?? -1) - world.Ask(from, item)))))
                .Where(c => world.Offer(c.To, c.Item) is not null && world.Shelf(c.From, c.Item) > 0)
                .OrderByDescending(c => c.Margin).ThenBy(c => c.From, StringComparer.Ordinal).ThenBy(c => c.Item, StringComparer.Ordinal)
                .First();
            long before = coin;
            int count = (int)Math.Min(Math.Min(Pack, world.Shelf(best.From, best.Item)), coin / Math.Max(1, world.Ask(best.From, best.Item)));
            coin -= world.Buy(best.From, best.Item, count);
            world.Days(1);
            coin += world.Sell(best.To, best.Item, count).Coin;   // what the purse will not cover is left on the road
            world.Days(1);
            profits.Add(coin - before);
            world.AssertInBounds($"run {run + 1}");
        }
        _output.WriteLine("profit per run: " + string.Join(", ", profits));
        return profits;
    }

    /// <summary>E-1 under the game's own prices: buying at one smith to sell at the other never pays, and the second run loses more than the first.</summary>
    [Fact]
    public void ArbitrageOnTheOldEastRoad_NeverPays_AndTheSecondRunIsWorseThanTheFirst()
    {
        using var profile = new TempProfile();
        var profits = Runs(new World(Harness.Boot(profile)));
        Assert.True(profits[1] < profits[0], $"the second run made {profits[1]}, the first {profits[0]}");
        Assert.All(profits, p => Assert.True(p <= 0, $"a run made {p}"));
    }

    /// <summary>
    /// E-1's mechanism, stressed: were merchants to pay 90% of value (not content's 40%), with Cairnwold glutted with wolf hides and Ashen
    /// Hollow out of them, the first run would pay - and the second is worse, no run beats the first, and the moved stock, the sell memory
    /// and the road's own carrier close the gap rather than let the hundred compound into a fortune.
    /// </summary>
    [Fact]
    public void ArbitrageWithGenerousMerchants_TheSecondRunIsWorseThanTheFirst_AndNoRunBeatsIt()
    {
        using var profile = new TempProfile();
        var world = new World(Harness.Boot(profile), sellRatio: 0.9);
        world.SetStock("market.cairnwold", "item.material.wolf_hide", 40);
        world.SetStock("market.ashen_hollow", "item.material.wolf_hide", 0);
        var profits = Runs(world);
        Assert.True(profits[0] > 0, $"the first run made {profits[0]}: the stress needs an opportunity to close");
        Assert.True(profits[1] < profits[0], $"the second run made {profits[1]}, the first {profits[0]}");
        Assert.All(profits.Skip(1), p => Assert.True(p <= profits[0], $"a later run made {p}, more than the first's {profits[0]}"));
        Assert.True(profits.Skip(1).Average() < profits[0] / 2.0, $"the later runs averaged {profits.Skip(1).Average()} against the first's {profits[0]}");
    }

    /// <summary>
    /// RK-12: half a year of the character trading hard and at random in both towns - buying out shelves, selling heaps - never takes a
    /// market's stock outside its authored bounds or a purse outside its reserve.
    /// </summary>
    [Fact]
    public void A183DaySoak_StaysInsideEveryMarketsBounds()
    {
        using var profile = new TempProfile();
        var world = new World(Harness.Boot(profile));
        var smiths = world.Setup.Merchants.Values.Where(m => m.MarketId is not null).Select(m => m.MerchantId).ToList();
        uint draw = 2463534242;
        uint Next() => draw = draw * 1664525 + 1013904223;
        for (int day = 0; day < 183; day++)
        {
            for (int trade = 0; trade < 4; trade++)
            {
                string merchant = smiths[(int)(Next() % (uint)smiths.Count)];
                var goods = world.Setup.Markets[world.MarketOf(merchant)].Goods.Keys.ToList();
                string item = goods[(int)(Next() % (uint)goods.Count)];
                int count = (int)(Next() % 60) + 1;
                if (Next() % 2 == 0)
                    world.Buy(merchant, item, (int)Math.Min(count, world.Shelf(merchant, item)));
                else
                    world.Sell(merchant, item, count);
                world.AssertInBounds($"day {day}, trade {trade}");
            }
            world.Days(1);
            world.AssertInBounds($"day {day}");
        }
    }

    /// <summary>
    /// The money supply: whatever the character does, each town's merchants never hold more than their reserves' tops together, never go
    /// below nothing, and with the character gone they hold at least their floors again after a restock.
    /// </summary>
    [Fact]
    public void EachTownsMoneySupply_IsBoundedByItsMerchantsReserves()
    {
        using var profile = new TempProfile();
        var world = new World(Harness.Boot(profile));
        var towns = world.Setup.Markets.Values.ToDictionary(m => m.Id, m => m.Merchants.Where(id => world.Setup.Merchants[id].HasPurse).ToList());
        long Supply(string town) => towns[town].Sum(id => world.Purses[id].Purse);
        long Top(string town) => towns[town].Sum(id => world.Setup.Merchants[id].ReserveMax!.Value);
        long Floor(string town) => towns[town].Sum(id => world.Setup.Merchants[id].ReserveMin!.Value);
        Assert.All(towns.Keys, t => Assert.True(Top(t) > 0, $"{t} has no merchant with a purse"));

        // The character drains every purse they can, selling everything the smiths buy, every day for sixty days.
        for (int day = 0; day < 60; day++)
        {
            foreach (var (town, merchants) in towns)
            {
                foreach (string merchant in merchants)
                {
                    foreach (string item in world.Setup.Markets[town].Goods.Keys)
                        world.Sell(merchant, item, 1000);
                }
                Assert.InRange(Supply(town), 0, Top(town));
            }
            world.Days(1);
            foreach (string town in towns.Keys)
                Assert.InRange(Supply(town), Floor(town), Top(town));   // a restock brings every purse back to its floor
        }
        // And with the character gone a month, the purses fill from the towns' custom, never past their tops.
        world.Days(30);
        foreach (string town in towns.Keys)
        {
            _output.WriteLine($"{town}: {Supply(town)} of {Top(town)}");
            Assert.InRange(Supply(town), Floor(town), Top(town));
        }
    }
}
