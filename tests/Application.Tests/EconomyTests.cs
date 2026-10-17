using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Economy;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP14: the living economy over the game's own content (SYSTEMS_COMPLETENESS_PLAN §2.14) - Ashen Hollow's and Cairnwold's markets,
/// their daily flows, bounded prices, Kera's and Durn's purses and restocks, the sell history, the Old East Road carrier, and the world
/// going on while the character is away (T-22).
/// </summary>
public class EconomyTests
{
    private const long Day = 57_600;
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string KeraWares = "merchant.ashen_hollow.kera_voss";
    private const string Hollow = "market.ashen_hollow";
    private const string Cairnwold = "market.cairnwold";
    private const string Arrows = "item.ammo.arrow_rough";
    private const string Hide = "item.material.wolf_hide";
    private const string Iron = "item.material.iron_ingot";
    private static readonly (double X, double Z) AtKera = (60.3, 140.3);

    private static Arena At(GameSession session, Func<PlayerRecord, PlayerRecord>? change = null, SimulationSetup? setup = null, long startTick = 0) =>
        Arena.OpenCreatures(session, setup ?? session.Setup, AtKera, 0, Array.Empty<(string, double, double, string)>(), change, startTick: startTick);

    private static PlayerRecord With(PlayerRecord r, long currency = 0, params InventoryEntry[] extra) =>
        new(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(extra), r.Progression, r.FacingMdeg, r.Discoveries,
            r.Equipment, currency, r.Effects, r.Relationships, r.Conversations);

    private static InventoryEntry Stack(string defId, int count) => new(EntityId.NewId(EntityKind.Item), defId, count) { Quality = Quality.Standard };

    private static MarketGoodView Good(Simulation simulation, string market, string item) =>
        simulation.Markets.Single(m => m.MarketId == market).Goods.Single(g => g.ItemId == item);

    private static int ShelfCount(Simulation simulation, string item) => simulation.Wares(Kera)!.Wares.Where(w => w.ItemId == item).Sum(w => w.Count);

    private static InventoryEntry Carried(Arena arena, string item) => arena.Simulation.Player.Inventory.First(e => e.DefId == item);

    /// <summary>
    /// Everything of the economy a player could see: every market's goods, what is on the road, and both smiths' purses. (Not the whole
    /// digest: each arena here is its own new character.)
    /// </summary>
    private static string Seen(Simulation simulation) =>
        string.Join(";", simulation.Markets.SelectMany(m => m.Goods.Select(g =>
            $"{m.MarketId}/{g.ItemId}:{g.Stock}:{g.PriceFactorPermille}")).Append($"incoming:{simulation.Markets.Sum(m => m.Incoming)}"))
        + "|" + simulation.Wares(Kera)!.Purse + "|" + simulation.Wares("npc.cairnwold.durn_skell")!.Purse;

    [Fact]
    public void ANewWorld_StartsEveryMarketAtTarget_AtBasePrices_WithFullPurses()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile));
        Assert.Equal(new[] { Hollow, Cairnwold }.OrderBy(m => m, StringComparer.Ordinal), arena.Simulation.Markets.Select(m => m.MarketId));
        Assert.All(arena.Simulation.Markets.SelectMany(m => m.Goods), g => Assert.Equal((g.Target, 1000, 0), (g.Stock, g.PriceFactorPermille, g.Trend)));
        var wares = arena.Simulation.Wares(Kera)!;
        Assert.Equal(400L, wares.Purse);
        Assert.Empty(wares.Short);
        Assert.Equal(1L, wares.Wares.First(w => w.ItemId == Arrows).Price);   // her authored shelf, at base
    }

    [Fact]
    public void Trading_MovesTheTownsStock_AndPrice_AndTheMerchantsPurse()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), r => With(r, 100, Stack(Hide, 4)));

        Assert.Equal(3L, arena.Simulation.Offer(Kera, Hide));   // 8 x 0.4
        Assert.Null(arena.Submit(new SellCommand(arena.Player, Kera, Carried(arena, Hide).ItemId, 2)));
        Assert.Equal(400L - 6, arena.Simulation.Wares(Kera)!.Purse);
        Assert.Equal(12L, Good(arena.Simulation, Hollow, Hide).Stock);
        Assert.True(arena.Simulation.Offer(Kera, Hide) < 3);   // glutted a little, and the town remembers the two

        var arrows = arena.Simulation.Wares(Kera)!.Wares.First(w => w.ItemId == Arrows);
        Assert.Null(arena.Submit(new BuyCommand(arena.Player, Kera, arrows.Ref, 20)));
        var good = Good(arena.Simulation, Hollow, Arrows);
        Assert.Equal((100L, 1100, 1), (good.Stock, good.PriceFactorPermille, good.Trend));   // 1000 + 600 x 20/120
        Assert.Equal(2L, arena.Simulation.Wares(Kera)!.Wares.First(w => w.ItemId == Arrows).Price);
        Assert.Equal(1, arena.Simulation.Wares(Kera)!.Wares.First(w => w.ItemId == Arrows).Trend);
        Assert.Equal(400L, arena.Simulation.Wares(Kera)!.Purse);   // 394 + 20, never past the top
    }

    [Fact]
    public void BuyIsAlwaysAboveSell_AtEveryStockAndMemory()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var economy = session.Setup.Economy;
        foreach (var market in economy.Markets.Values)
        {
            foreach (var good in market.Goods.Values)
            {
                long value = session.Setup.Items.Catalog.Get(good.ItemId).ValueBase;
                for (long stock = good.Min; stock <= good.Max; stock++)
                {
                    int factor = EconomyEngine.PriceFactor(good, stock, economy.Rules);
                    Assert.InRange(factor, 600, 1600);
                    long ask = EconomyEngine.Ask(value, 1.0, factor);
                    foreach (long memory in new long[] { 0, 1, 5, 20, 100, 10_000 })
                    {
                        long offer = EconomyEngine.Offer(value, session.Setup.Items.Pricing.SellRatio, factor, memory, economy.Rules, ask);
                        Assert.True(offer < ask || ask == 0, $"{market.Id} {good.ItemId} at {stock}: sells for {offer}, asks {ask}");
                        Assert.True(offer >= 0);
                    }
                }
            }
        }
    }

    [Fact]
    public void AnEmptyPurse_BuysNothing_AndTheNextRestockRefillsItsFloor()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var economy = session.Setup.Economy;
        var poor = session.Setup with
        {
            Economy = economy with { Merchants = economy.Merchants.SetItem(KeraWares, economy.Merchants[KeraWares] with { ReserveMin = 4, ReserveMax = 5 }) },
        };
        var arena = At(session, r => With(r, 0, Stack(Hide, 4)), poor);

        Assert.Equal("Kera Voss cannot pay 6: their purse holds 5", arena.Submit(new SellCommand(arena.Player, Kera, Carried(arena, Hide).ItemId, 2)));
        Assert.Equal(4, Carried(arena, Hide).Count);   // a refused sell moves nothing
        Assert.Null(arena.Submit(new SellCommand(arena.Player, Kera, Carried(arena, Hide).ItemId, 1)));
        Assert.Equal(2L, arena.Simulation.Wares(Kera)!.Purse);
        Assert.Null(arena.Submit(new SellCommand(arena.Player, Kera, Carried(arena, Hide).ItemId, 1)));   // 2: glutted, and remembered
        Assert.Equal(0L, arena.Simulation.Wares(Kera)!.Purse);
        Assert.Equal("Kera Voss has no coin left to buy with", arena.Submit(new SellCommand(arena.Player, Kera, Carried(arena, Hide).ItemId, 1)));

        arena.Simulation.Advance(Day);
        Assert.True(arena.Simulation.Wares(Kera)!.Purse >= 4);   // her restock's floor, and the town's custom
    }

    [Fact]
    public void SellingTheSameGoodAgain_PaysLess_AndTheTownForgets()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), r => With(r, 0, Stack("item.consumable.salve_minor", 6)));
        var offers = new List<long>();
        for (int i = 0; i < 6; i++)
        {
            offers.Add(arena.Simulation.Offer(Kera, "item.consumable.salve_minor")!.Value);
            Assert.Null(arena.Submit(new SellCommand(arena.Player, Kera, Carried(arena, "item.consumable.salve_minor").ItemId, 1)));
        }
        Assert.True(offers.Zip(offers.Skip(1)).All(p => p.Second <= p.First), string.Join(",", offers));
        Assert.True(offers[^1] < offers[0], string.Join(",", offers));

        arena.Simulation.Advance(30 * Day);
        Assert.Equal(offers[0], arena.Simulation.Offer(Kera, "item.consumable.salve_minor"));   // the market and its memory settled again
    }

    [Fact]
    public void TheShelf_IsDrawnFromTheMarket_WhenTradeOpensAfterARestock()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), r => With(r, 1000));
        var restocked = arena.Record<WaresRestocked>();
        // The first sale gives her shelf its identities, so each stack is found again.
        while (arena.Simulation.Wares(Kera)!.Wares.FirstOrDefault(w => w.ItemId == Arrows) is { } stack)
            Assert.Null(arena.Submit(new BuyCommand(arena.Player, Kera, stack.Ref, stack.Count)));
        Assert.Equal(0, ShelfCount(arena.Simulation, Arrows));
        Assert.Equal(60L, Good(arena.Simulation, Hollow, Arrows).Stock);

        // Opening trade before a restock draws nothing.
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Kera)));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "trade")));
        Assert.Empty(restocked);
        Assert.Equal(0, ShelfCount(arena.Simulation, Arrows));

        // A day later she has restocked: her shelf comes back to its line's 60 from a market that holds that many again.
        arena.Simulation.Advance(Day);
        Assert.Equal(0, ShelfCount(arena.Simulation, Arrows));   // reified only when trade opens
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Kera)));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "trade")));
        Assert.Equal(KeraWares, Assert.Single(restocked).MerchantId);
        Assert.Equal(60, ShelfCount(arena.Simulation, Arrows));
        Assert.Equal(1, arena.Simulation.Wares(Kera)!.Wares.Count(w => w.ItemId == "item.weapon.hunting_bow"));   // a good no market keeps stays as it was
    }

    [Fact]
    public void TheMarkets_SettleADay_OnTheTickThatCrossesMidnight()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        long midnight = session.Setup.Clock.TicksPerDay - session.Setup.Clock.StartTicks;
        long hour = session.Setup.Clock.TicksPerDay / 24;
        // Kera trades only in trading hours (WP12): bought from at 19:00, then the evening passes to five ticks before midnight.
        var arena = At(session, r => With(r, 100), startTick: midnight - 5 * hour);
        var settled = arena.Record<MarketsSettled>();
        var arrows = arena.Simulation.Wares(Kera)!.Wares.First(w => w.ItemId == Arrows);
        Assert.Null(arena.Submit(new BuyCommand(arena.Player, Kera, arrows.Ref, 20)));
        arena.Simulation.Advance(5 * hour - 5);
        Assert.Equal(100L, Good(arena.Simulation, Hollow, Arrows).Stock);

        arena.Tick(4);
        Assert.Empty(settled);
        arena.Tick(1);
        Assert.Equal((0L, 1L), (Assert.Single(settled).FromDay, settled[0].ToDay));
        Assert.Equal(102L, Good(arena.Simulation, Hollow, Arrows).Stock);   // 100 + max(20 floor, 4 the fletcher made) - 20 used (WP12b), then a tenth of the 20 short
    }

    [Fact]
    public void TheOldEastRoad_CarriesIronToCairnwold_WhereItIsShort()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile));
        arena.Simulation.Advance(20 * Day);

        var there = Good(arena.Simulation, Cairnwold, Iron);
        var here = Good(arena.Simulation, Hollow, Iron);
        Assert.True(there.Stock < there.Target && there.Trend == 1, $"Cairnwold's iron at {there.Stock}");
        var withoutRoad = At(Harness.Boot(profile), setup: arena.Simulation.Setup with
        {
            Economy = arena.Simulation.Setup.Economy with { Links = ImmutableArray<TradeLink>.Empty },
        });
        withoutRoad.Simulation.Advance(20 * Day);
        Assert.True(Good(withoutRoad.Simulation, Cairnwold, Iron).Stock < there.Stock, "the road brings iron Cairnwold would not have had");
        // and takes Ashen Hollow's surplus, which without it piles up and goes cheap.
        var piled = Good(withoutRoad.Simulation, Hollow, Iron);
        Assert.True(piled.Stock > here.Stock && piled.Trend == -1, $"Ashen Hollow's iron at {here.Stock}, {piled.Stock} without the road");
    }

    /// <summary>The markets, the road and the purses survive a save and load exactly, and go on from where they were.</summary>
    [Fact]
    public void TheEconomy_SurvivesASaveAndLoad_AndGoesOnTheSame()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, r => With(r, 500, Stack(Hide, 6)));
        var arrows = arena.Simulation.Wares(Kera)!.Wares.First(w => w.ItemId == Arrows);
        Assert.Null(arena.Submit(new BuyCommand(arena.Player, Kera, arrows.Ref, 20)));
        Assert.Null(arena.Submit(new SellCommand(arena.Player, Kera, Carried(arena, Hide).ItemId, 6)));
        arena.Simulation.Advance(5 * Day + 123);   // with goods on the road
        Assert.True(arena.Simulation.Markets.Sum(m => m.Incoming) > 0);

        var store = new Persistence.SaveStore(profile.Root);
        long tick = arena.Simulation.WorldTick;
        store.Save(Persistence.SaveSlots.Manual("market"), Persistence.SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content, tick, 0));
        var loaded = store.Load(Persistence.SaveSlots.Manual("market"),
            new Persistence.LoadContext(session.Generator, session.Content, new UNNAMED.EntityRegistry.EntityRegistry()));
        Assert.True(loaded.IsComplete);
        var resumed = Simulation.Start(arena.Simulation.Setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick, new EventBus());
        Assert.Equal(arena.Simulation.StateDigest(), resumed.StateDigest());
        Assert.Equal(Seen(arena.Simulation), Seen(resumed));

        arena.Simulation.Advance(9 * Day);
        resumed.Advance(9 * Day);
        Assert.Equal(arena.Simulation.StateDigest(), resumed.StateDigest());
    }

    /// <summary>
    /// T-22: the economy is stepped one calendar day at a time, so one Reckoning at once, 183 one-day advances, and the same span in uneven
    /// pieces that split days leave the same markets, road and purses - after the character has traded, so nothing sits at its start.
    /// </summary>
    [Fact]
    public void T22_AReckoningAtOnce_LeavesTheEconomyThatDailyAndUnevenPiecesDo()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        Arena Traded()
        {
            var arena = At(session, r => With(r, 500, Stack(Hide, 6)));
            var arrows = arena.Simulation.Wares(Kera)!.Wares.First(w => w.ItemId == Arrows);
            Assert.Null(arena.Submit(new BuyCommand(arena.Player, Kera, arrows.Ref, 20)));
            Assert.Null(arena.Submit(new SellCommand(arena.Player, Kera, Carried(arena, Hide).ItemId, 6)));
            return arena;
        }
        var once = Traded();
        var daily = Traded();
        var uneven = Traded();
        Assert.Equal(Seen(once.Simulation), Seen(daily.Simulation));

        var log = once.Simulation.Advance(183 * Day);
        for (int day = 0; day < 183; day++)
            daily.Simulation.Advance(Day);
        long left = 183 * Day;
        foreach (long piece in new long[] { 7, 39_601, Day - 1, 3 * Day + 17, 11 * Day })
        {
            uneven.Simulation.Advance(piece);
            left -= piece;
        }
        uneven.Simulation.Advance(left);

        Assert.Equal(Seen(once.Simulation), Seen(daily.Simulation));
        Assert.Equal(Seen(once.Simulation), Seen(uneven.Simulation));
        Assert.NotEqual(Seen(Traded().Simulation), Seen(once.Simulation));
        // The away log tells of the market the character traded at, and of no other.
        Assert.All(log.Lines.Where(l => l.Kind.StartsWith("market.", StringComparison.Ordinal)), l => Assert.StartsWith(Hollow + "/", l.Ref));
        Assert.Contains(log.Lines, l => l.Kind == MarketAdvanceKinds.Short && l.Ref == $"{Hollow}/{Iron}" || l.Kind == MarketAdvanceKinds.PriceMoved);
    }
}
