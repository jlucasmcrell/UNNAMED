// UNNAMED World - trade with NPCs: buy, sell and merchant wares (M3b's stub, opened by M4, priced by WP14's living economy)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Economy;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>Buy from a trader within reach: one of their wares (by its <see cref="WareView.Ref"/>), at their price.</summary>
public sealed record BuyCommand(EntityId Actor, string NpcId, string Ware, int Count) : GameCommand(Actor);

/// <summary>Sell a carried stack to a trader within reach who buys that kind of thing.</summary>
public sealed record SellCommand(EntityId Actor, string NpcId, EntityId Item, int Count) : GameCommand(Actor);

/// <summary>Have a trader within reach who repairs (WP3b) mend a carried piece, for their fee (<see cref="TradeSystem.RepairQuote"/>).</summary>
public sealed record MerchantRepairCommand(EntityId Actor, string NpcId, EntityId Item) : GameCommand(Actor);

/// <summary>What a trader would charge to mend a carried piece, and the quality it would come back at.</summary>
public sealed record RepairQuote(string NpcId, EntityId Item, long Fee, int Quality);

public sealed record ItemBought(string NpcId, string ItemId, int Count, long Price, long Tick);

public sealed record ItemSold(string NpcId, string ItemId, int Count, long Price, long Tick);

/// <summary>
/// A trader's wares as they stand, each at the price they ask for one; what their purse holds (null: bottomless), and the goods their
/// town is short of (WP14: "the smith is short of iron").
/// </summary>
public sealed record WaresView(string NpcId, string MerchantId, ImmutableArray<WareView> Wares)
{
    public long? Purse { get; init; }

    public ImmutableArray<string> Short { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary>A ware: <see cref="Ref"/> is what a <see cref="BuyCommand"/> names it by.</summary>
public sealed record WareView(string Ref, string ItemId, int Count, int Quality, long Price)
{
    /// <summary>Where its price stands against base in the trader's town: 1 dearer, -1 cheaper, 0 at base (WP14's trend arrow).</summary>
    public int Trend { get; init; }

    /// <summary>Its wear (WP3b): a worn piece sold to a trader is as worn on their shelf.</summary>
    public ItemWear? Wear { get; init; }
}

/// <summary>
/// Owns no state. Trade with an NPC who offers it (M4, priced by the living economy since WP14): a trader's wares are a container at the
/// trader - their authored stock until the first sale, then a changed container like any other, drawn again from their town market at each
/// restock - so buying and selling are one move of a stack and the coin the other way, all or nothing. A trader asks each ware's value
/// times their bias times their town's price for it, and pays the sell ratio of that, less what the town already took from the character
/// lately, always below what they ask; they pay out of their purse, and buy nothing it cannot cover. Each trade moves the town's stock,
/// and the coin goes into or out of the purse.
/// </summary>
internal sealed class TradeSystem
{
    private readonly SystemContext _context;
    private readonly EntityId _player;
    private readonly Func<ContainerSite, ContainerView> _contents;
    private readonly MarketSystem _markets;
    private readonly MerchantSystem _merchants;

    public TradeSystem(SystemContext context, EntityId player, Func<ContainerSite, ContainerView> contents, MarketSystem markets, MerchantSystem merchants)
    {
        _context = context;
        _player = player;
        _contents = contents;
        _markets = markets;
        _merchants = merchants;
    }

    private RuntimeState State => _context.State;
    private ItemSetup Items => _context.Setup.Items;

    public string? Handle(BuyCommand command, long tick)
    {
        if (Trader(command.Actor, command.NpcId, out var npc, out var merchant) is { } refused)
            return refused;
        var ware = _contents(_context.WaresOf(npc)!).Items.FirstOrDefault(i => i.Ref == command.Ware);
        if (ware is null)
            return $"{npc.Definition.Name} has no such ware";
        if (command.Count < 1 || command.Count > ware.Count)
            return $"{npc.Definition.Name} has {ware.Count} of it";
        long price = AskFor(merchant, ware.DefId) * command.Count;
        if (price > State.Currency)
            return $"not enough coin: {price} asked, {State.Currency} carried";
        if (_context.Dispatch(new Trade(new MoveItemCommand(_player, ware.Ref, ItemPlace.In(merchant.Id), ItemPlace.Carried, command.Count), -price)) is { } failed)
            return failed;
        if (MarketOf(merchant) is { } market)
            _context.Dispatch(new MarketTrade(market, ware.DefId, command.Count, 0));
        _context.Dispatch(new PayMerchant(merchant.Id, price, ByCharacter: true));
        _context.Events.Publish(new ItemBought(npc.Definition.Id, ware.DefId, command.Count, price, tick));
        return null;
    }

    public string? Handle(SellCommand command, long tick)
    {
        if (Trader(command.Actor, command.NpcId, out var npc, out var merchant) is { } refused)
            return refused;
        if (State.Inventory.FirstOrDefault(e => e.ItemId == command.Item) is not { } entry)
            return $"{command.Item} is not carried";
        if (command.Count < 1 || command.Count > entry.Count)
            return $"only {entry.Count} carried";
        if (OfferFor(merchant, entry.DefId) is not { } each)
            return $"{npc.Definition.Name} does not buy that";
        if (each == 0)
            return $"{npc.Definition.Name} would give nothing for that";
        long price = each * command.Count;
        if (_merchants.Purse(merchant.Id) is { } purse && purse < price)
            return purse == 0 ? $"{npc.Definition.Name} has no coin left to buy with" : $"{npc.Definition.Name} cannot pay {price}: their purse holds {purse}";
        if (_context.Dispatch(new Trade(new MoveItemCommand(_player, entry.ItemId.Value, ItemPlace.Carried, ItemPlace.In(merchant.Id), command.Count), price)) is { } failed)
            return failed;
        _context.Dispatch(new PayFromPurse(merchant.Id, price));
        if (MarketOf(merchant) is { } market)
            _context.Dispatch(new MarketTrade(market, entry.DefId, 0, command.Count));
        _context.Events.Publish(new ItemSold(npc.Definition.Id, entry.DefId, command.Count, price, tick));
        return null;
    }

    /// <summary>
    /// A trader's repair (WP3b; C12:427, the first coin sink): no wear, and the piece back at the quality it has - or, worn out, at the quality it
    /// was made at but no better than a trader's hand gives (<c>config.inventory durability merchant_quality_cap</c>). The fee goes into their purse.
    /// </summary>
    public string? Handle(MerchantRepairCommand command, long tick)
    {
        if (Trader(command.Actor, command.NpcId, out var npc, out var merchant) is { } refused)
            return refused;
        if (Quote(npc, command.Item) is not { } quote)
            return Unrepairable(npc, command.Item);
        if (quote.Fee > State.Currency)
            return $"not enough coin: {quote.Fee} asked, {State.Currency} carried";
        if (_context.Dispatch(new RepairItem(command.Item, quote.Quality)) is { } failed)
            return failed;
        _context.Dispatch(new SpendCurrency(quote.Fee));
        _context.Dispatch(new PayMerchant(merchant.Id, quote.Fee, ByCharacter: true));
        var entry = State.Inventory.Single(e => e.ItemId == command.Item);
        _context.Events.Publish(new ItemRepaired(entry.ItemId, entry.DefId, entry.Quality, npc.Definition.Id, quote.Fee, tick));
        return null;
    }

    /// <summary>What a trader would charge to mend a carried piece now; null when they do not repair, or it needs none.</summary>
    public RepairQuote? RepairQuote(string npcId, EntityId item) =>
        State.Npcs.TryGetValue(npcId, out var npc) ? Quote(npc, item) : null;

    private RepairQuote? Quote(NpcState npc, EntityId item)
    {
        if (!npc.Definition.Offers(NpcServices.Repair) || State.Inventory.FirstOrDefault(e => e.ItemId == item) is not { } entry
            || Items.Catalog.Find(entry.DefId) is not { DurabilityMax: > 0 } definition || !Durability.NeedsRepair(entry.Quality, entry.Wear))
            return null;
        var rules = Items.Inventory.Durability;
        // A piece not yet worn out keeps the quality it has; a worn-out one comes back no finer than a trader's hand gives.
        int quality = Math.Min(Durability.MadeQuality(entry.Quality, entry.Wear), Math.Max(entry.Quality, rules.MerchantQualityCap));
        return new RepairQuote(npc.Definition.Id, item, Durability.MerchantFee(definition, entry.Wear, rules), quality);
    }

    private string Unrepairable(NpcState npc, EntityId item) =>
        !npc.Definition.Offers(NpcServices.Repair) ? $"{npc.Definition.Name} does not repair"
        : State.Inventory.FirstOrDefault(e => e.ItemId == item) is not { } entry ? $"{item} is not carried"
        : Items.Catalog.Find(entry.DefId) is not { DurabilityMax: > 0 } ? $"{entry.DefId} does not wear"
        : $"{entry.DefId} needs no repair";

    /// <summary>A trader's wares as they stand, at their price; null when this NPC does not trade.</summary>
    public WaresView? View(string npcId)
    {
        if (!State.Npcs.TryGetValue(npcId, out var npc) || _context.WaresOf(npc) is not { } site || !Items.Merchants.TryGetValue(site.Key, out var merchant))
            return null;
        string? market = MarketOf(merchant);
        return new WaresView(npcId, merchant.Id, _contents(site).Items
            .Select(i => new WareView(i.Ref, i.DefId, i.Count, i.Quality, AskFor(merchant, i.DefId)) { Trend = Math.Sign(_markets.Factor(market, i.DefId) - 1000), Wear = i.Wear })
            .ToImmutableArray())
        {
            Purse = _merchants.Purse(merchant.Id),
            Short = market is null ? ImmutableArray<string>.Empty : _markets.View(market)!.Goods.Where(g => g.Short).Select(g => g.ItemId).ToImmutableArray(),
        };
    }

    /// <summary>What a trader would pay for one of an item now; null when this NPC does not trade, or does not buy it.</summary>
    public long? Offer(string npcId, string itemId) =>
        State.Npcs.TryGetValue(npcId, out var npc) && npc.Definition.MerchantId is { } id && Items.Merchants.TryGetValue(id, out var merchant)
            && Items.Catalog.Find(itemId) is not null
            ? OfferFor(merchant, itemId)
            : null;

    /// <summary>What a trader asks for one: its value times their bias for it (1 for anything they did not stock) times their town's price.</summary>
    private long AskFor(Merchant merchant, string itemId) =>
        EconomyEngine.Ask(Items.Catalog.Get(itemId).ValueBase, merchant.Stock.FirstOrDefault(s => s.ItemId == itemId)?.PriceBias ?? 1.0,
            _markets.Factor(MarketOf(merchant), itemId));

    /// <summary>What a trader pays for one, or null when they do not buy it (S-36: always below what they ask).</summary>
    private long? OfferFor(Merchant merchant, string itemId)
    {
        var item = Items.Catalog.Get(itemId);
        if (Items.Pricing.SellPrice(item, merchant) is null)
            return null;
        string? market = MarketOf(merchant);
        return EconomyEngine.Offer(item.ValueBase, Items.Pricing.SellRatio, _markets.Factor(market, itemId), _markets.Remembered(market, itemId),
            _context.Setup.Economy.Rules, AskFor(merchant, itemId));
    }

    private string? MarketOf(Merchant merchant) => _context.Setup.Economy.MarketOf(merchant.Id)?.Id;

    private string? Trader(EntityId actor, string npcId, out NpcState npc, out Merchant merchant)
    {
        npc = null!;
        merchant = null!;
        if (actor != _player)
            return $"unknown actor {actor}";
        if (State.PlayerCombat.Defeated)
            return "dead";
        if (!State.Npcs.TryGetValue(npcId, out var found))
            return $"there is no one called {npcId} here";
        npc = found;
        if (!npc.Definition.Offers(NpcServices.Trade) || npc.Definition.MerchantId is not { } merchantId || !Items.Merchants.TryGetValue(merchantId, out var stock))
            return $"{npc.Definition.Name} does not trade";
        merchant = stock;
        return !_context.InTalkReach(npc.Body) ? $"{npc.Definition.Name} is out of reach" : null;
    }
}
