// UNNAMED World - the town markets: daily supply and demand, the road carrier, and the world going on while away (WP14)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Economy;

namespace UNNAMED.World.Runtime;

/// <summary>To <see cref="MarketSystem"/>: the character bought or sold at a market (WP14) - its stock moves, and a sell is remembered.</summary>
internal sealed record MarketTrade(string MarketId, string ItemId, long Bought, long Sold) : InternalCommand;

/// <summary>To <see cref="MarketSystem"/>: goods made in a town besides its floor - a resident's job output (WP12's hook).</summary>
internal sealed record ProduceGoods(string MarketId, string ItemId, long Count) : InternalCommand;

/// <summary>
/// What the towns' residents make at their jobs on a calendar day (WP12's job hook): a good's production that day is the larger of its floor
/// and its jobs (WP12b). A pure function of the day and of state that does not depend on when the markets ask, so a long absence and the same absence
/// in pieces settle alike (T-22).
/// </summary>
internal interface IJobOutput
{
    IEnumerable<(string MarketId, string ItemId, long Count)> On(long day);
}

/// <summary>A good in a town market as it stands, and its price against base in permille.</summary>
public sealed record MarketGoodView(string ItemId, long Stock, long Target, long Min, long Max, int PriceFactorPermille)
{
    /// <summary>A price this far above base or further reads as a shortage: "the smith is short of iron".</summary>
    public const int ShortPermille = 1200;

    /// <summary>Where the price stands against base: 1 dearer, -1 cheaper, 0 at base - the trade column's arrow.</summary>
    public int Trend => Math.Sign(PriceFactorPermille - 1000);

    public bool Short => PriceFactorPermille >= ShortPermille;
}

/// <summary>A town market: who sells from it, its goods, and what is on the road to it.</summary>
public sealed record MarketView(string MarketId, ImmutableArray<string> Merchants, ImmutableArray<MarketGoodView> Goods, long Incoming);

/// <summary>The markets settled one or more days (WP14): prices may have moved.</summary>
public sealed record MarketsSettled(long FromDay, long ToDay, long Tick);

/// <summary>The kinds of <see cref="AdvanceLine"/> the economy writes, for the away sheet (§2.14 "price arrows and news").</summary>
public static class MarketAdvanceKinds
{
    /// <summary>A good's price moved while away; <see cref="AdvanceLine.Ref"/> is <c>market/item</c>, the amount the change in permille of base.</summary>
    public const string PriceMoved = "market.price";

    /// <summary>A market is short of a good now; <see cref="AdvanceLine.Ref"/> is <c>market/item</c>, the amount its price in permille of base.</summary>
    public const string Short = "market.short";

    /// <summary>The smallest change in a price, in permille of base, the away sheet tells of.</summary>
    public const int NotablePermille = 50;
}

/// <summary>
/// Owns: <see cref="StateSlice.Markets"/> - each town market's stock and its memory of the character's sells, what the carriers have on
/// the road, and the last calendar day settled (WP14, §2.14). The economy steps once per calendar day, at midnight on the world clock:
/// while the world runs, on the tick that crosses it; after time passes at once (a Waystone journey, the offline catch-up), every day
/// crossed in turn. Each day's step depends only on the state and the day (<see cref="EconomyEngine.Step"/>), so one long absence leaves
/// the markets that the same absence in pieces does (T-22). Each day's merchant income goes to <see cref="MerchantSystem"/> by command.
/// </summary>
internal sealed class MarketSystem : IAbstractAdvance
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;

    public MarketSystem(SystemContext context, SliceOwner owner)
    {
        _context = context;
        _owner = owner;
    }

    /// <summary>Where the residents' job output comes from (WP12); none until composition gives one.</summary>
    public IJobOutput? Jobs { get; set; }

    /// <summary>What the adventurers trade here each day (WP13); none until composition gives it.</summary>
    public IAgentTrade? Agents { get; set; }

    private RuntimeState State => _context.State;
    private EconomySetup Economy => _context.Setup.Economy;
    private EconomyState Current => State.World.Economy ?? throw new InvalidOperationException("The markets have not been populated");

    /// <summary>The markets as content describes them, carrying over what the world delta holds; a new world's start at their targets today.</summary>
    public void Populate() =>
        State.SetEconomy(_owner, EconomyEngine.Reconcile(Economy, State.World.Economy, _context.Setup.Clock.Day(State.WorldTick)));

    /// <summary>One world tick: a midnight crossed settles the day, or every day a jump in the clock went past.</summary>
    public void Tick(long tick) => SettleTo(_context.Setup.Clock.Day(tick), tick);

    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
        var before = Factors();
        SettleTo(_context.Setup.Clock.Day(toTick), toTick);
        // Only what the character could know: the markets of the merchants they have traded with.
        foreach (var market in Economy.Markets.Values.Where(m => m.Merchants.Any(id => State.World.Merchant(id)?.Traded == true)))
        {
            foreach (var good in View(market.Id)!.Goods)
            {
                string key = $"{market.Id}/{good.ItemId}";
                int change = good.PriceFactorPermille - before.GetValueOrDefault(key, 1000);
                if (Math.Abs(change) >= MarketAdvanceKinds.NotablePermille)
                    log.Add(new AdvanceLine(toTick, MarketAdvanceKinds.PriceMoved, key, change));
                if (good.Short)
                    log.Add(new AdvanceLine(toTick, MarketAdvanceKinds.Short, key, good.PriceFactorPermille));
            }
        }
    }

    public string? Handle(MarketTrade command)
    {
        if (!Economy.Markets.TryGetValue(command.MarketId, out var market) || !Current.Markets.TryGetValue(command.MarketId, out var state))
            return $"there is no market {command.MarketId}";
        if (command.Bought < 0 || command.Sold < 0)
            return "a trade moves no negative count";
        Set(EconomyEngine.Traded(market, state, command.ItemId, command.Bought, command.Sold));
        return null;
    }

    public string? Handle(ProduceGoods command)
    {
        if (!Economy.Markets.TryGetValue(command.MarketId, out var market) || !Current.Markets.TryGetValue(command.MarketId, out var state))
            return $"there is no market {command.MarketId}";
        Set(EconomyEngine.Produced(market, state, command.ItemId, command.Count));
        return null;
    }

    // ── reads ───────────────────────────────────────────────────────────────

    /// <summary>A good's price against base at a market, in permille: base (1000) for a good the market does not keep, or no market.</summary>
    public int Factor(string? marketId, string itemId) =>
        marketId is not null && Economy.Markets.TryGetValue(marketId, out var market) && market.Goods.TryGetValue(itemId, out var good)
            && Current.Markets.TryGetValue(marketId, out var state)
            ? EconomyEngine.PriceFactor(good, state.Stock[itemId], Economy.Rules)
            : 1000;

    /// <summary>How many of a good the character has sold at a market lately, as the town remembers it.</summary>
    public long Remembered(string? marketId, string itemId) =>
        marketId is not null && Current.Markets.TryGetValue(marketId, out var state) ? state.SoldByCharacter.GetValueOrDefault(itemId) : 0;

    /// <summary>A market's stock as it stands; null for a good it does not keep.</summary>
    public long? Stock(string marketId, string itemId) =>
        Current.Markets.TryGetValue(marketId, out var state) && state.Stock.TryGetValue(itemId, out long stock) ? stock : null;

    public ImmutableArray<MarketView> Views() => Economy.Markets.Keys.Select(id => View(id)!).ToImmutableArray();

    public MarketView? View(string marketId)
    {
        if (!Economy.Markets.TryGetValue(marketId, out var market) || !Current.Markets.TryGetValue(marketId, out var state))
            return null;
        return new MarketView(market.Id, market.Merchants,
            market.Goods.Values.Select(g => new MarketGoodView(g.ItemId, state.Stock[g.ItemId], g.Target, g.Min, g.Max,
                EconomyEngine.PriceFactor(g, state.Stock[g.ItemId], Economy.Rules))).ToImmutableArray(),
            Current.Shipments.Where(s => s.ToMarket == marketId).Sum(s => s.Count));
    }

    // ── the day ─────────────────────────────────────────────────────────────

    private void SettleTo(long day, long tick)
    {
        var state = Current;
        long from = state.Day;
        while (state.Day < day)
        {
            // The adventurers' day here (WP13): what they sold joins the stock, what they bought or had made leaves it - never remembered
            // against the character - and what they paid goes to the merchants' purses.
            foreach (var (marketId, itemId, delta) in Agents?.Goods(state.Day + 1) ?? Enumerable.Empty<(string, string, long)>())
            {
                if (Economy.Markets.TryGetValue(marketId, out var market) && state.Markets.TryGetValue(marketId, out var stock))
                    state = state with { Markets = state.Markets.SetItem(marketId, delta >= 0
                        ? EconomyEngine.Produced(market, stock, itemId, delta)
                        : EconomyEngine.Traded(market, stock, itemId, -delta, 0)) };
            }
            foreach (var (merchantId, coin) in Agents?.Coin(state.Day + 1) ?? Enumerable.Empty<(string, long)>())
                _context.Dispatch(new PayMerchant(merchantId, coin, ByCharacter: false));
            // What the jobs made that day stands in for the floor where it is more (WP12b: max(floor, jobs)), never on top of it.
            var jobs = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var (marketId, itemId, count) in Jobs?.On(state.Day + 1) ?? Enumerable.Empty<(string, string, long)>())
                jobs[EconomyEngine.JobKey(marketId, itemId)] = jobs.GetValueOrDefault(EconomyEngine.JobKey(marketId, itemId)) + count;
            (state, var income) = EconomyEngine.Step(Economy, state, state.Day + 1, ValueBase, jobs);
            State.SetEconomy(_owner, state);
            _context.Dispatch(new SettleMerchants(state.Day, income));
        }
        if (state.Day > from)
            _context.Events.Publish(new MarketsSettled(from, state.Day, tick));
    }

    private long ValueBase(string itemId) => _context.Setup.Items.Catalog.Get(itemId).ValueBase;

    private void Set(MarketState market) => State.SetEconomy(_owner, Current with { Markets = Current.Markets.SetItem(market.MarketId, market) });

    private Dictionary<string, int> Factors() =>
        Views().SelectMany(m => m.Goods.Select(g => (Key: $"{m.MarketId}/{g.ItemId}", g.PriceFactorPermille)))
            .ToDictionary(x => x.Key, x => x.PriceFactorPermille, StringComparer.Ordinal);
}
