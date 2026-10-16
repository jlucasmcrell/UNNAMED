// UNNAMED World - NPCs, conversations, relationships and trade at run time (SYSTEMS.md S-24, S-26, S-28; M4)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

// ── commands ────────────────────────────────────────────────────────────────

/// <summary>Buy from a trader within reach: one of their wares (by its <see cref="WareView.Ref"/>), at their price.</summary>
public sealed record BuyCommand(EntityId Actor, string NpcId, string Ware, int Count) : GameCommand(Actor);

/// <summary>Sell a carried stack to a trader within reach who buys that kind of thing.</summary>
public sealed record SellCommand(EntityId Actor, string NpcId, EntityId Item, int Count) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

public sealed record ItemBought(string NpcId, string ItemId, int Count, long Price, long Tick);

public sealed record ItemSold(string NpcId, string ItemId, int Count, long Price, long Tick);

// ── views ───────────────────────────────────────────────────────────────────

/// <summary>A trader's wares as they stand, each at the price they ask for one.</summary>
public sealed record WaresView(string NpcId, string MerchantId, ImmutableArray<WareView> Wares);

/// <summary>A ware: <see cref="Ref"/> is what a <see cref="BuyCommand"/> names it by.</summary>
public sealed record WareView(string Ref, string ItemId, int Count, int Quality, long Price);

// ── state and internal commands ─────────────────────────────────────────────

// ── systems ─────────────────────────────────────────────────────────────────

/// <summary>
/// Owns no state. Trade with an NPC who offers it (M3b's economy stub, opened by M4's NPCs): a trader's wares are a container
/// at the trader - their authored stock until the first sale, then a changed container like any other - so buying and selling
/// are one move of a stack and the coin the other way, all or nothing. A trader asks each ware's value times their bias, and
/// pays a fraction of value for what their trade takes; what they buy joins their wares. Their purse is bottomless.
/// </summary>
internal sealed class TradeSystem
{
    private readonly SystemContext _context;
    private readonly EntityId _player;
    private readonly Func<ContainerSite, ContainerView> _contents;

    public TradeSystem(SystemContext context, EntityId player, Func<ContainerSite, ContainerView> contents)
    {
        _context = context;
        _player = player;
        _contents = contents;
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
        if (Items.Pricing.SellPrice(Items.Catalog.Get(entry.DefId), merchant) is not { } each)
            return $"{npc.Definition.Name} does not buy that";
        if (each == 0)
            return $"{npc.Definition.Name} would give nothing for that";
        long price = each * command.Count;
        if (_context.Dispatch(new Trade(new MoveItemCommand(_player, entry.ItemId.Value, ItemPlace.Carried, ItemPlace.In(merchant.Id), command.Count), price)) is { } failed)
            return failed;
        _context.Events.Publish(new ItemSold(npc.Definition.Id, entry.DefId, command.Count, price, tick));
        return null;
    }

    /// <summary>A trader's wares as they stand, at their price; null when this NPC does not trade.</summary>
    public WaresView? View(string npcId)
    {
        if (!State.Npcs.TryGetValue(npcId, out var npc) || _context.WaresOf(npc) is not { } site || !Items.Merchants.TryGetValue(site.Key, out var merchant))
            return null;
        return new WaresView(npcId, merchant.Id, _contents(site).Items
            .Select(i => new WareView(i.Ref, i.DefId, i.Count, i.Quality, AskFor(merchant, i.DefId)))
            .ToImmutableArray());
    }

    /// <summary>What a trader asks for one: its value times their bias for it (1 for anything they did not stock).</summary>
    private long AskFor(Merchant merchant, string itemId) =>
        Items.Pricing.BuyPrice(Items.Catalog.Get(itemId), merchant.Stock.FirstOrDefault(s => s.ItemId == itemId) ?? new MerchantStock(itemId, 0, 1.0));

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
