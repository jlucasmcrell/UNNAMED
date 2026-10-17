// UNNAMED Content - the living economy from content: town markets, the road trade link, merchant purses and price bounds (WP14)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Economy;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// Builds the domain's <see cref="EconomySetup"/> and lints what the reference pass cannot see (ECO001): a market's goods are items with
/// sane bounds, each merchant sells from one market at most, a trade link joins two defined markets, a purse's reserve is
/// <c>[min, max]</c> with min no more than max, and the price bounds hold base between them (DN6). <c>config.economy</c>'s price keys are
/// optional: a pack without them runs at DN6's defaults.
/// </summary>
public static class EconomyContent
{
    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        try
        {
            Build(loader);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(new ValidationError { SeverityLevel = ValidationError.Severity.Error, Code = "ECO001", Message = $"economy: {e.Message}" });
        }
        return errors;
    }

    public static EconomySetup Build(ContentLoader loader)
    {
        var economy = loader.Definitions.ContainsKey("config.economy") ? Config(loader, "config.economy") : new Dictionary<object, object>();
        var rules = BuildRules(economy);
        var catalog = ItemContent.BuildCatalog(loader);
        var stockLines = ItemContent.BuildMerchants(loader);

        var markets = loader.GetByKind("market").Values.Select(definition =>
        {
            string id = definition.Id;
            var map = Read(definition.YamlSource);
            var merchants = Rows(map, "merchants", id).Select(row => Text(row, "merchant_ref")).ToImmutableArray();
            if (merchants.Distinct(StringComparer.Ordinal).Count() != merchants.Length)
                throw new FormatException($"{id} names a merchant twice");
            var goods = Rows(map, "goods", id).Select(row =>
            {
                string item = Text(row, "item_ref");
                if (catalog.Find(item) is null)
                    throw new FormatException($"{id} keeps {item}, which is not an item");
                return new MarketGood(item, Whole(row, "target"), Whole(row, "min"), Whole(row, "max"), Whole(row, "production", 0),
                    Whole(row, "consumption", 0), Whole(row, "imports", 0), Permille(row, "elasticity", 1.0)).Validated(id);
            }).ToList();
            if (goods.Count == 0)
                throw new FormatException($"{id} keeps no goods");
            if (goods.Select(g => g.ItemId).Distinct(StringComparer.Ordinal).Count() != goods.Count)
                throw new FormatException($"{id} keeps a good twice");
            return new MarketDefinition(id, merchants, goods.ToImmutableSortedDictionary(g => g.ItemId, g => g, StringComparer.Ordinal));
        }).ToImmutableSortedDictionary(m => m.Id, m => m, StringComparer.Ordinal);

        var marketOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var market in markets.Values)
        {
            foreach (string merchant in market.Merchants)
            {
                if (!stockLines.ContainsKey(merchant))
                    throw new FormatException($"{market.Id} names {merchant}, which is not a merchant");
                if (!marketOf.TryAdd(merchant, market.Id))
                    throw new FormatException($"{merchant} sells from both {marketOf[merchant]} and {market.Id}");
            }
        }

        var links = (economy.ContainsKey("trade_links") ? Rows(economy, "trade_links", "config.economy") : Enumerable.Empty<Dictionary<object, object>>())
            .Select(row =>
            {
                var link = new TradeLink(Text(row, "id"), Text(row, "from_market_ref"), Text(row, "to_market_ref"), Int(row, "delay_days"), Whole(row, "capacity_per_day"));
                if (!markets.ContainsKey(link.From) || !markets.ContainsKey(link.To) || link.From == link.To)
                    throw new FormatException($"trade link {link.Id} must join two different defined markets");
                if (link.DelayDays < 1 || link.CapacityPerDay < 1)
                    throw new FormatException($"trade link {link.Id} takes at least a day and carries at least one a day");
                return link;
            }).ToImmutableArray();
        if (links.Select(l => l.Id).Distinct(StringComparer.Ordinal).Count() != links.Length)
            throw new FormatException("config.economy names a trade link twice");

        var merchantEconomy = stockLines.Values.Select(merchant =>
        {
            var map = Read(loader.Definitions[merchant.Id].YamlSource);
            long? min = null, max = null;
            if (map.ContainsKey("gold_reserve"))
            {
                var reserve = List(map, "gold_reserve");
                if (reserve.Count != 2)
                    throw new FormatException($"{merchant.Id} gold_reserve is [min, max]");
                min = WholeOf(reserve[0], $"{merchant.Id} gold_reserve min");
                max = WholeOf(reserve[1], $"{merchant.Id} gold_reserve max");
                if (min > max)
                    throw new FormatException($"{merchant.Id} gold_reserve's min is above its max");
            }
            int restock = map.ContainsKey("restock_min") ? Int(map, "restock_min") : 1;
            if (restock < 1)
                throw new FormatException($"{merchant.Id} restock_min is at least one day");
            string? market = marketOf.GetValueOrDefault(merchant.Id);
            var goods = market is null
                ? ImmutableArray<string>.Empty
                : merchant.Stock.Select(s => s.ItemId).Where(markets[market].Goods.ContainsKey).Distinct(StringComparer.Ordinal).ToImmutableArray();
            return new MerchantEconomy(merchant.Id, market, goods, min, max, restock);
        }).ToImmutableSortedDictionary(m => m.MerchantId, m => m, StringComparer.Ordinal);

        return new EconomySetup(markets, links, merchantEconomy, rules);
    }

    /// <summary>DN6's bounds and relaxation, and the sell memory (E-1, E-2): <c>config.economy</c>'s, or the defaults for a key it omits.</summary>
    private static EconomyRules BuildRules(Dictionary<object, object> map)
    {
        var d = EconomyRules.Default;
        return new EconomyRules(
            Permille(map, "price_min_factor", d.PriceMinPermille / 1000.0),
            Permille(map, "price_max_factor", d.PriceMaxPermille / 1000.0),
            Permille(map, "daily_relaxation", d.RelaxPermille / 1000.0),
            Permille(map, "sell_memory_daily_decay", d.SellMemoryDecayPermille / 1000.0),
            map.ContainsKey("sell_memory_half_units") ? Whole(map, "sell_memory_half_units") : d.SellMemoryHalfUnits).Validated();
    }

    private static int Permille(Dictionary<object, object> map, string key, double otherwise) =>
        (int)Math.Round((map.ContainsKey(key) ? Number(map, key) : otherwise) * 1000, MidpointRounding.AwayFromZero);

    private static long Whole(Dictionary<object, object> map, string key, long? otherwise = null) =>
        map.ContainsKey(key) || otherwise is null ? WholeOf(map.GetValueOrDefault(key), key) : otherwise.Value;

    private static long WholeOf(object? value, string what) =>
        long.TryParse(value as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) && n >= 0
            ? n
            : throw new FormatException($"'{what}' must be a whole number of at least 0");
}
