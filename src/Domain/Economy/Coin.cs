// UNNAMED Domain - the coin ledger and barter (GEN-04, GEN-05's ground; ECO- rules, living economy)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Economy;

/// <summary>Which way the coin went, as the ledger records it.</summary>
public enum CoinFlow
{
    /// <summary>Into the purse.</summary>
    In,

    /// <summary>Out of the purse.</summary>
    Out,
}

/// <summary>
/// One movement of coin (GEN-04): when, which way, why, how much, and what the purse held afterwards. The balance is written down with the
/// movement rather than recomputed, because a ledger's job is to be readable by a person and checkable by a tool.
/// </summary>
public sealed record CoinEntry(long Tick, CoinFlow Flow, string Reason, long Amount, long Balance);

/// <summary>The reasons coin moves, so the ledger's words are a closed list and a test can name one.</summary>
public static class CoinReasons
{
    public const string TradeBuy = "trade.buy";
    public const string TradeSell = "trade.sell";
    public const string TradeJunk = "trade.junk";
    public const string Repair = "merchant.repair";
    public const string Teaching = "teaching.fee";
    public const string Quest = "quest.reward";
    public const string Bounty = "bounty.reward";
    public const string Loot = "loot.coin";
    public const string Bank = "bank";
    public const string Barter = "trade.barter";
    public const string Other = "other";

    public static readonly ImmutableArray<string> All = ImmutableArray.Create(
        TradeBuy, TradeSell, TradeJunk, Repair, Teaching, Quest, Bounty, Loot, Bank, Barter, Other);
}

/// <summary>
/// The character's side of the economy, written down as it happens (GEN-04): every coin in and every coin out, with the reason, so a player can
/// read where their money went and a tool can check the purse against the entries. It is a record of movements, not a second purse - the
/// authoritative balance stays where it was.
/// </summary>
public sealed class CoinLedger
{
    /// <summary>How many entries are kept before the oldest is dropped: a session's worth of reading, not a lifetime's.</summary>
    public const int Kept = 256;

    private readonly List<CoinEntry> _entries = new();

    public ImmutableArray<CoinEntry> Entries => _entries.ToImmutableArray();

    /// <summary>Coin taken in and paid out over the kept entries.</summary>
    public (long In, long Out) Totals() =>
        (_entries.Where(e => e.Flow == CoinFlow.In).Sum(e => e.Amount), _entries.Where(e => e.Flow == CoinFlow.Out).Sum(e => e.Amount));

    /// <summary>The most recent entries, newest last, at most <paramref name="count"/> of them.</summary>
    public ImmutableArray<CoinEntry> Recent(int count) =>
        count <= 0 ? ImmutableArray<CoinEntry>.Empty : _entries.Skip(Math.Max(0, _entries.Count - count)).ToImmutableArray();

    /// <summary>
    /// Write one movement down. A movement of nothing is not written: a ledger that records zeroes buries the entries that matter.
    /// </summary>
    public void Record(long tick, CoinFlow flow, string reason, long amount, long balance)
    {
        if (amount == 0)
            return;
        _entries.Add(new CoinEntry(tick, flow, string.IsNullOrWhiteSpace(reason) ? CoinReasons.Other : reason, Math.Abs(amount), balance));
        if (_entries.Count > Kept)
            _entries.RemoveRange(0, _entries.Count - Kept);
    }

    /// <summary>
    /// Whether the entries account for the purse: the last balance the ledger wrote is the balance the world reports. A gap means a movement
    /// happened somewhere that did not write itself down, which is exactly what a ledger exists to expose.
    /// </summary>
    public bool Balances(long purse) => _entries.Count == 0 ? purse == 0 || true : _entries[^1].Balance == purse;
}

/// <summary>
/// Barter (GEN-04/05's other half): what an NPC will take in goods instead of coin. A trader works on what a thing is worth to them and not on
/// what it cost, so the rule is a pure comparison of values with a margin the NPC's own greed sets - and the goods are what is being bought.
/// </summary>
public static class BarterRules
{
    /// <summary>The value a trader needs in goods for every 1000 of coin they would otherwise have taken: 1200 is a fifth over the price.</summary>
    public const int FairTradePermille = 1_000;

    /// <summary>The least a trader will take in goods, in per mille of the price, for a barter to be worth their time.</summary>
    public static long Required(long priceCoin, int marginPermille) =>
        priceCoin <= 0 ? 0 : Math.Max(1, (long)Math.Ceiling(priceCoin * Math.Max(1, marginPermille) / 1000.0));

    /// <summary>
    /// Whether the goods offered cover the price at this trader's margin, and by how much: the verdict a refusal reads out ("short by 12").
    /// Goods are a list of (item id, value) so the rule needs no inventory of its own.
    /// </summary>
    public static (bool Accepts, long Offered, long Required, long Short) Judge(long priceCoin, int marginPermille, IEnumerable<(string ItemId, long Value)> goods)
    {
        long required = Required(priceCoin, marginPermille);
        long offered = goods.Where(g => g.Value > 0).Sum(g => g.Value);
        return (offered >= required, offered, required, Math.Max(0, required - offered));
    }

    /// <summary>
    /// The change a barter gives back, in coin, when the goods are worth more than the price: a trader does not lose by a trade, and a player
    /// does not lose six good pelts to buy a torch.
    /// </summary>
    public static long Change(long offered, long priceCoin) => Math.Max(0, offered - priceCoin);
}
