// UNNAMED World - merchants' purses and restocks: liquidity, and the shelf drawn from the town market (WP14)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Economy;
using UNNAMED.Domain.Social;

namespace UNNAMED.World.Runtime;

/// <summary>To <see cref="MerchantSystem"/>: the markets settled a day - each merchant's income from the town, and their restock if due.</summary>
internal sealed record SettleMerchants(long Day, ImmutableSortedDictionary<string, long> Income) : InternalCommand;

/// <summary>
/// To <see cref="MerchantSystem"/>: coin into a merchant's purse - the character buying (<see cref="ByCharacter"/>), or a wage or a repair
/// paid to them (WP3b, WP15's sinks). What would overfill the purse leaves the town.
/// </summary>
internal sealed record PayMerchant(string MerchantId, long Coin, bool ByCharacter) : InternalCommand;

/// <summary>To <see cref="MerchantSystem"/>: a merchant pays the character out of their purse for what they bought. Refused past the purse.</summary>
internal sealed record PayFromPurse(string MerchantId, long Coin) : InternalCommand;

/// <summary>One line of a restocked shelf: how many of a good, of standard make, the shelf holds now.</summary>
internal sealed record ShelfLine(string ItemId, int Count);

/// <summary>
/// To <see cref="InventorySystem"/>: a trader's shelf is drawn again (WP14) - each line's good, of standard make and with no record of its
/// own, is brought to exactly that count. Everything else on the shelf - what the character sold them, a finer piece - stays.
/// </summary>
internal sealed record RestockWares(string MerchantId, ImmutableArray<ShelfLine> Lines) : InternalCommand;

/// <summary>A trader's shelf was drawn from the town market as the character opened trade.</summary>
public sealed record WaresRestocked(string NpcId, string MerchantId, long Day, long Tick);

/// <summary>
/// Owns: <see cref="StateSlice.Merchants"/> - each merchant's purse, the day they last restocked, the restock their shelf was last drawn
/// for, and whether the character has traded with them (WP14, S-36). A purse holds its <c>gold_reserve</c>: the town's custom fills it
/// day by day and the character's buying, wages and repairs top it up, never past its top; buying from the character drains it, and an
/// empty purse buys nothing. Every <c>restock_min</c> days a merchant restocks: their purse comes back up to its floor, and their shelf is
/// drawn again from the town market - reified only when the character next opens trade with them (WORLD_ARCHITECTURE.md:389). A merchant
/// content gives no reserve keeps a bottomless purse, as before WP14. Stepped by the markets' day (<see cref="SettleMerchants"/>), so it
/// goes on while the character is away exactly as the markets do.
/// </summary>
internal sealed class MerchantSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly MarketSystem _markets;

    public MerchantSystem(SystemContext context, SliceOwner owner, MarketSystem markets)
    {
        _context = context;
        _owner = owner;
        _markets = markets;
    }

    private RuntimeState State => _context.State;
    private EconomySetup Economy => _context.Setup.Economy;

    /// <summary>Every merchant content gives a place in the economy, carried over from the world delta, or starting with a full purse.</summary>
    public void Populate()
    {
        long day = State.World.Economy?.Day ?? _context.Setup.Clock.Day(State.WorldTick);
        foreach (var merchant in Economy.Merchants.Values)
            State.SetMerchant(_owner, EconomyEngine.Reconcile(merchant, State.World.Merchant(merchant.MerchantId), day));
    }

    public void Listen(SimulationEvents events) =>
        events.Listen<ServiceOpened>(e =>
        {
            if (e.Service == NpcServices.Trade)
                DrawShelf(e.NpcId, e.Tick);
        });

    public string? Handle(SettleMerchants command)
    {
        foreach (var merchant in Economy.Merchants.Values)
        {
            if (State.World.Merchant(merchant.MerchantId) is { } state)
                State.SetMerchant(_owner, EconomyEngine.Settle(merchant, state, command.Day, command.Income.GetValueOrDefault(merchant.MerchantId)));
        }
        return null;
    }

    public string? Handle(PayMerchant command)
    {
        if (command.Coin < 0)
            return "a payment is never negative";
        if (!Economy.Merchants.TryGetValue(command.MerchantId, out var merchant) || State.World.Merchant(command.MerchantId) is not { } state)
            return null;   // a stub merchant outside the economy: the coin simply goes
        var paid = EconomyEngine.Paid(merchant, state, command.Coin);
        State.SetMerchant(_owner, command.ByCharacter ? paid with { Traded = true } : paid);
        return null;
    }

    public string? Handle(PayFromPurse command)
    {
        if (command.Coin < 0)
            return "a payment is never negative";
        if (!Economy.Merchants.TryGetValue(command.MerchantId, out var merchant) || State.World.Merchant(command.MerchantId) is not { } state)
            return null;
        if (!merchant.HasPurse)
        {
            State.SetMerchant(_owner, state with { Traded = true });
            return null;
        }
        if (state.Purse < command.Coin)
            return $"their purse holds {state.Purse}";
        State.SetMerchant(_owner, state with { Purse = state.Purse - command.Coin, Traded = true });
        return null;
    }

    /// <summary>What a merchant's purse holds now; null when it is bottomless.</summary>
    public long? Purse(string merchantId) =>
        Economy.Merchants.TryGetValue(merchantId, out var merchant) && merchant.HasPurse ? State.World.Merchant(merchantId)?.Purse ?? 0 : null;

    /// <summary>
    /// The character opened trade: a merchant who has restocked since their shelf was last drawn draws it now - each good the town market
    /// keeps, up to their stock line's count while the market holds as many. Goods the market does not keep are never restocked.
    /// </summary>
    private void DrawShelf(string npcId, long tick)
    {
        if (!State.Npcs.TryGetValue(npcId, out var npc) || npc.Definition.MerchantId is not { } merchantId
            || !_context.Setup.Items.Merchants.TryGetValue(merchantId, out var stock)
            || Economy.MarketOf(merchantId) is not { } market || State.World.Merchant(merchantId) is not { } state
            || state.ShelfDay >= state.RestockedDay)
            return;
        var lines = stock.Stock.Where(line => market.Goods.ContainsKey(line.ItemId))
            .Select(line => new ShelfLine(line.ItemId, EconomyEngine.Shelf(market.Goods[line.ItemId], _markets.Stock(market.Id, line.ItemId) ?? 0, line.Count)))
            .ToImmutableArray();
        if (!lines.IsEmpty && _context.Dispatch(new RestockWares(merchantId, lines)) is not null)
            return;
        State.SetMerchant(_owner, state with { ShelfDay = state.RestockedDay });
        _context.Events.Publish(new WaresRestocked(npcId, merchantId, state.RestockedDay, tick));
    }
}
