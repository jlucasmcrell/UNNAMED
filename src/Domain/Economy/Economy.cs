// UNNAMED Domain - the living economy: town markets, their daily flows, bounded prices and merchant purses
// (SYSTEMS_COMPLETENESS_PLAN_20261017.md §2.14, WP14; canon SYSTEMS.md S-36, ECONOMY.md stabilizers, GAMEPLAY_LOOPS.md E-1/E-2)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Economy;

/// <summary>
/// How far prices move and how fast markets settle (<c>config.economy</c>). Every ratio is in permille, so a day's step is integer
/// arithmetic and the same on every machine.
/// </summary>
/// <param name="PriceMinPermille">The cheapest a good gets against its base value (DN6: 0.6).</param>
/// <param name="PriceMaxPermille">The dearest a good gets against its base value (DN6: 1.6).</param>
/// <param name="RelaxPermille">How much of its distance from target a market's stock closes each day (at least one), so its price relaxes toward base (DN6: 10%).</param>
/// <param name="SellMemoryDecayPermille">How much of a town's memory of the character's sells fades each day.</param>
/// <param name="SellMemoryHalfUnits">How many remembered sells of a good halve what a town pays for the next (E-1, E-2).</param>
public sealed record EconomyRules(int PriceMinPermille, int PriceMaxPermille, int RelaxPermille, int SellMemoryDecayPermille, long SellMemoryHalfUnits)
{
    /// <summary>DN6 as accepted: 0.6-1.6x base, relaxing 10% a day; a town forgets a tenth of its memory of sells a day, and 20 halve the price.</summary>
    public static readonly EconomyRules Default = new(600, 1600, 100, 100, 20);

    public EconomyRules Validated()
    {
        if (PriceMinPermille <= 0 || PriceMinPermille > 1000 || PriceMaxPermille < 1000)
            throw new ArgumentException("A price may fall to no less than a sliver of base and no further than base, and rise to at least base");
        if (RelaxPermille is < 0 or > 1000 || SellMemoryDecayPermille is < 0 or > 1000 || SellMemoryHalfUnits < 1)
            throw new ArgumentException("Daily relaxation and memory decay are 0..1, and the sell memory halves at one sell or more");
        return this;
    }
}

/// <summary>
/// One good a town market keeps (<c>content/markets</c>): the stock it settles toward and the bounds it never leaves (RK-12), what the
/// town makes and uses of it a day, what is brought in while it is short, and how strongly its price answers a shortage.
/// </summary>
public sealed record MarketGood(string ItemId, long Target, long Min, long Max, long Production, long Consumption, long Imports, int ElasticityPermille)
{
    public MarketGood Validated(string marketId)
    {
        if (Min < 0 || Target < 1 || Min > Target || Target > Max)
            throw new ArgumentException($"{marketId} {ItemId}: stock bounds need 0 <= min <= target <= max and a target of at least 1");
        if (Production < 0 || Consumption < 0 || Imports < 0 || ElasticityPermille < 0)
            throw new ArgumentException($"{marketId} {ItemId}: production, consumption, imports and elasticity are never negative");
        return this;
    }
}

/// <summary>A settlement's market: the merchants who sell from it and the goods it keeps a stock of.</summary>
public sealed record MarketDefinition(string Id, ImmutableArray<string> Merchants, ImmutableSortedDictionary<string, MarketGood> Goods);

/// <summary>
/// A road a carrier works between two markets (the Old East Road): each day it takes what one has over its target to the other where
/// it is short, as much as it can carry each way, arriving <see cref="DelayDays"/> later.
/// </summary>
public sealed record TradeLink(string Id, string From, string To, int DelayDays, long CapacityPerDay);

/// <summary>
/// A merchant's place in the economy (MerchantProfile, DATA_MODEL.md §4.15): the market they sell from (none for a stub merchant), the
/// goods of theirs it keeps, their purse's <c>gold_reserve [min, max]</c> (no reserve: a bottomless purse), and how many days between
/// restocks (<c>restock_min</c>).
/// </summary>
public sealed record MerchantEconomy(string MerchantId, string? MarketId, ImmutableArray<string> Goods, long? ReserveMin, long? ReserveMax, int RestockDays)
{
    public bool HasPurse => ReserveMax is not null;
}

/// <summary>Everything the economy runs on, built from content at boot.</summary>
public sealed record EconomySetup(
    ImmutableSortedDictionary<string, MarketDefinition> Markets,
    ImmutableArray<TradeLink> Links,
    ImmutableSortedDictionary<string, MerchantEconomy> Merchants,
    EconomyRules Rules)
{
    public static EconomySetup Empty { get; } = new(
        ImmutableSortedDictionary.Create<string, MarketDefinition>(StringComparer.Ordinal), ImmutableArray<TradeLink>.Empty,
        ImmutableSortedDictionary.Create<string, MerchantEconomy>(StringComparer.Ordinal), EconomyRules.Default);

    /// <summary>The market a merchant sells from, or null.</summary>
    public MarketDefinition? MarketOf(string merchantId) =>
        Merchants.TryGetValue(merchantId, out var merchant) && merchant.MarketId is { } id ? Markets.GetValueOrDefault(id) : null;
}

/// <summary>A market as it stands: its stock of each good, and how many of each the character has sold there lately (sell history).</summary>
public sealed record MarketState(string MarketId, ImmutableSortedDictionary<string, long> Stock, ImmutableSortedDictionary<string, long> SoldByCharacter);

/// <summary>Goods on the road: so many of one good from one market to another, arriving on a day.</summary>
public sealed record Shipment(string LinkId, string FromMarket, string ToMarket, string ItemId, long Count, long ArrivesDay);

/// <summary>Every market, what is on the road, and the last calendar day the economy has settled.</summary>
public sealed record EconomyState(long Day, ImmutableSortedDictionary<string, MarketState> Markets, ImmutableArray<Shipment> Shipments);

/// <summary>
/// A merchant as they stand: their purse, the day they last restocked, the restock their shelf was last drawn for, and whether the
/// character has ever traded with them (what the away log may tell of).
/// </summary>
public sealed record MerchantState(string MerchantId, long Purse, long RestockedDay, long ShelfDay, bool Traded);

/// <summary>
/// The economy's rules as pure functions over its state (S-36; WP14). A day is one step: goods arrive off the road, the town makes,
/// imports and uses what it does, its stock relaxes toward target and is clamped to its bounds, its memory of the character's sells
/// fades, and carriers set out. Nothing reads a clock: the caller says which day it is, and a day's step depends only on the state and
/// that day, so many days stepped at once leave what the same days stepped one by one do.
/// </summary>
public static class EconomyEngine
{
    /// <summary>
    /// The economy as content describes it, carrying over what a save holds: every market and good content defines, at its saved stock or
    /// else its target; goods and markets content no longer defines are dropped, and so is anything on the road to or from them, or on a road
    /// between them content no longer defines.
    /// </summary>
    public static EconomyState Reconcile(EconomySetup setup, EconomyState? saved, long day)
    {
        var markets = setup.Markets.Values.Select(market =>
        {
            var was = saved?.Markets.GetValueOrDefault(market.Id);
            var stock = market.Goods.Values.ToImmutableSortedDictionary(g => g.ItemId,
                g => Clamp(g, was?.Stock.TryGetValue(g.ItemId, out long s) == true ? s : g.Target), StringComparer.Ordinal);
            var sold = (was?.SoldByCharacter ?? ImmutableSortedDictionary<string, long>.Empty).Where(kv => kv.Value > 0)
                .ToImmutableSortedDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            return new MarketState(market.Id, stock, sold);
        }).ToImmutableSortedDictionary(m => m.MarketId, m => m, StringComparer.Ordinal);
        var shipments = (saved?.Shipments ?? ImmutableArray<Shipment>.Empty)
            .Where(s => s.Count > 0 && setup.Markets.ContainsKey(s.FromMarket) && setup.Markets.TryGetValue(s.ToMarket, out var to)
                        && to.Goods.ContainsKey(s.ItemId) && setup.Links.Any(l => l.Id == s.LinkId
                            && ((l.From == s.FromMarket && l.To == s.ToMarket) || (l.From == s.ToMarket && l.To == s.FromMarket))))
            .ToImmutableArray();
        return new EconomyState(saved?.Day ?? day, markets, shipments);
    }

    /// <summary>A merchant's state carried over from a save, or as they start: a full purse, restocked today.</summary>
    public static MerchantState Reconcile(MerchantEconomy merchant, MerchantState? saved, long day) =>
        saved is null
            ? new MerchantState(merchant.MerchantId, merchant.ReserveMax ?? 0, day, day, false)
            : saved with { Purse = merchant.ReserveMax is { } max ? Math.Clamp(saved.Purse, 0, max) : 0 };

    /// <summary>
    /// A good's price against its base, in permille: <c>clamp(1 + k·(target − stock)/target, min, max)</c> (§2.14). Short, it is dear;
    /// glutted, cheap; at target, its base.
    /// </summary>
    public static int PriceFactor(MarketGood good, long stock, EconomyRules rules)
    {
        long factor = 1000 + (long)good.ElasticityPermille * (good.Target - stock) / good.Target;
        return (int)Math.Clamp(factor, rules.PriceMinPermille, rules.PriceMaxPermille);
    }

    /// <summary>What a merchant asks for one: base value times their bias times the market's factor, rounded up.</summary>
    public static long Ask(long valueBase, double bias, int factorPermille) =>
        CeilThousandths((long)Math.Round(valueBase * bias * factorPermille, MidpointRounding.AwayFromZero));

    /// <summary>
    /// What a merchant pays for one: base value times the sell ratio times the market's factor, less what the town already took from the
    /// character lately (<paramref name="remembered"/> sells halve it at <see cref="EconomyRules.SellMemoryHalfUnits"/>), rounded down -
    /// and always below what they ask for one (<paramref name="ask"/>), so buying back is never a profit (S-36).
    /// </summary>
    public static long Offer(long valueBase, double sellRatio, int factorPermille, long remembered, EconomyRules rules, long ask)
    {
        long scaled = (long)Math.Round(valueBase * sellRatio * factorPermille, MidpointRounding.AwayFromZero);
        long offer = scaled * rules.SellMemoryHalfUnits / (rules.SellMemoryHalfUnits + Math.Max(0, remembered)) / 1000;
        return Math.Max(0, Math.Min(offer, ask - 1));
    }

    /// <summary>
    /// One day: <paramref name="day"/> must be the day after the state's. Returns the economy after it, and what each merchant took that day
    /// selling the town what it used (their purse's income), by merchant. <paramref name="jobs"/> is what the towns' residents made at their
    /// jobs that day, by <c>market/item</c> (WP12's hook): a good's production is the larger of its floor and its jobs (WP12b) - the floor
    /// stands for the work of a town nobody simulates, and the jobs replace it where they make more, never add to it.
    /// </summary>
    public static (EconomyState State, ImmutableSortedDictionary<string, long> Income) Step(EconomySetup setup, EconomyState state, long day,
        Func<string, long> valueBase, IReadOnlyDictionary<string, long>? jobs = null)
    {
        if (day != state.Day + 1)
            throw new ArgumentOutOfRangeException(nameof(day), day, $"The economy steps one day at a time: it stands at day {state.Day}");
        var rules = setup.Rules;
        var income = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var stocks = state.Markets.ToDictionary(m => m.Key, m => new SortedDictionary<string, long>(m.Value.Stock, StringComparer.Ordinal),
            StringComparer.Ordinal);

        // What arrives today joins the market it was sent to; anything later stays on the road.
        foreach (var shipment in state.Shipments.Where(s => s.ArrivesDay <= day))
        {
            if (stocks.TryGetValue(shipment.ToMarket, out var into) && into.ContainsKey(shipment.ItemId))
                into[shipment.ItemId] += shipment.Count;
        }
        var road = state.Shipments.Where(s => s.ArrivesDay > day).ToList();

        foreach (var market in setup.Markets.Values)
        {
            var stock = stocks[market.Id];
            foreach (var good in market.Goods.Values)
            {
                long s = stock[good.ItemId];
                int factor = PriceFactor(good, Clamp(good, s), rules);
                // The town makes its floor, or what its residents' jobs make when that is more, and brings in what it is short of.
                s += Math.Max(good.Production, jobs?.GetValueOrDefault(JobKey(market.Id, good.ItemId)) ?? 0);
                if (s < good.Target)
                    s = Math.Min(good.Target, s + good.Imports);
                // It uses what it needs, never past its reserve: the merchants who sell it are paid for each.
                long used = Math.Max(0, Math.Min(good.Consumption, s - good.Min));
                s -= used;
                Pay(market, good, used * Ask(valueBase(good.ItemId), 1.0, factor), setup, income);
                // The stock settles a share of the way back toward its target - at least one, so a small market comes back - and never
                // leaves its bounds (RK-12).
                s += Relax(good.Target - s, rules.RelaxPermille);
                stock[good.ItemId] = Clamp(good, s);
            }
        }

        // The carriers set out: from where a good is over target to where it is short, counting what is already on its way.
        foreach (var link in setup.Links.OrderBy(l => l.Id, StringComparer.Ordinal))
        {
            foreach (var (from, to) in new[] { (link.From, link.To), (link.To, link.From) })
            {
                if (!setup.Markets.TryGetValue(from, out var source) || !setup.Markets.TryGetValue(to, out var destination))
                    continue;
                long room = link.CapacityPerDay;
                foreach (var good in source.Goods.Values)
                {
                    if (room <= 0 || !destination.Goods.TryGetValue(good.ItemId, out var wanted))
                        continue;
                    long surplus = stocks[from][good.ItemId] - good.Target;
                    long coming = road.Where(s => s.ToMarket == to && s.ItemId == good.ItemId).Sum(s => s.Count);
                    long deficit = wanted.Target - stocks[to][good.ItemId] - coming;
                    long carried = Math.Min(room, Math.Min(surplus, deficit));
                    if (carried <= 0)
                        continue;
                    stocks[from][good.ItemId] -= carried;
                    room -= carried;
                    road.Add(new Shipment(link.Id, from, to, good.ItemId, carried, day + Math.Max(1, link.DelayDays)));
                }
            }
        }

        var markets = state.Markets.Values.Select(m => new MarketState(m.MarketId,
                stocks[m.MarketId].ToImmutableSortedDictionary(StringComparer.Ordinal),
                m.SoldByCharacter.Select(kv => KeyValuePair.Create(kv.Key, kv.Value * (1000 - rules.SellMemoryDecayPermille) / 1000))
                    .Where(kv => kv.Value > 0).ToImmutableSortedDictionary(StringComparer.Ordinal)))
            .ToImmutableSortedDictionary(m => m.MarketId, m => m, StringComparer.Ordinal);
        return (new EconomyState(day, markets, road.ToImmutableArray()), income.ToImmutableSortedDictionary(StringComparer.Ordinal));
    }

    /// <summary>
    /// The character's trade at a market: <paramref name="bought"/> leaves its stock, <paramref name="sold"/> joins it and is remembered.
    /// A good the market does not keep moves no stock, but a sell of it is remembered all the same.
    /// </summary>
    public static MarketState Traded(MarketDefinition market, MarketState state, string itemId, long bought, long sold)
    {
        var stock = state.Stock;
        if (market.Goods.TryGetValue(itemId, out var good))
            stock = stock.SetItem(itemId, Clamp(good, stock[itemId] - bought + sold));
        var remembered = sold > 0 ? state.SoldByCharacter.SetItem(itemId, state.SoldByCharacter.GetValueOrDefault(itemId) + sold) : state.SoldByCharacter;
        return state with { Stock = stock, SoldByCharacter = remembered };
    }

    /// <summary>The key <see cref="Step"/>'s jobs are counted by: a market and a good.</summary>
    public static string JobKey(string marketId, string itemId) => $"{marketId}/{itemId}";

    /// <summary>Goods made in the town besides its floor (a resident's job, WP12): they join the stock, within its bounds.</summary>
    public static MarketState Produced(MarketDefinition market, MarketState state, string itemId, long count) =>
        market.Goods.TryGetValue(itemId, out var good) && count > 0
            ? state with { Stock = state.Stock.SetItem(itemId, Clamp(good, state.Stock[itemId] + count)) }
            : state;

    /// <summary>
    /// A merchant's day: what they took from the town comes into their purse, up to its reserve's top; on a restock day the purse is
    /// brought up to at least its reserve's floor (the restocking floor) and the merchant has new stock to draw.
    /// </summary>
    public static MerchantState Settle(MerchantEconomy merchant, MerchantState state, long day, long income)
    {
        long purse = merchant.ReserveMax is { } max ? Math.Min(max, state.Purse + Math.Max(0, income)) : 0;
        if (day - state.RestockedDay < Math.Max(1, merchant.RestockDays))
            return state with { Purse = purse };
        return state with { Purse = Math.Max(purse, merchant.ReserveMin ?? 0), RestockedDay = day };
    }

    /// <summary>Coin into a purse (the character buying, a wage, a repair), up to its reserve's top: the rest leaves the town.</summary>
    public static MerchantState Paid(MerchantEconomy merchant, MerchantState state, long coin) =>
        merchant.ReserveMax is { } max ? state with { Purse = Math.Min(max, state.Purse + Math.Max(0, coin)) } : state;

    /// <summary>How many of a good a merchant's shelf holds after a restock: what their stock line holds, while the market has as many.</summary>
    public static int Shelf(MarketGood good, long stock, int lineCount) => (int)Math.Clamp(Math.Min(lineCount, stock), 0, int.MaxValue);

    private static void Pay(MarketDefinition market, MarketGood good, long coin, EconomySetup setup, SortedDictionary<string, long> income)
    {
        var sellers = market.Merchants.Where(m => setup.Merchants.TryGetValue(m, out var e) && e.Goods.Contains(good.ItemId))
            .OrderBy(m => m, StringComparer.Ordinal).ToList();
        if (coin <= 0 || sellers.Count == 0)
            return;
        long share = coin / sellers.Count, rest = coin % sellers.Count;
        foreach (var (seller, i) in sellers.Select((s, i) => (s, i)))
            income[seller] = income.GetValueOrDefault(seller) + share + (i < rest ? 1 : 0);
    }

    private static long Relax(long distance, int permille) =>
        distance == 0 || permille == 0 ? 0 : Math.Sign(distance) * Math.Max(1, Math.Abs(distance) * permille / 1000);

    private static long Clamp(MarketGood good, long stock) => Math.Clamp(stock, good.Min, good.Max);

    private static long CeilThousandths(long scaled) => scaled >= 0 ? (scaled + 999) / 1000 : scaled / 1000;
}
