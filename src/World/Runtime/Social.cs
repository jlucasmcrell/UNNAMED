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

/// <summary>The NPCs and their conversations, built from content at boot (M4). Where each stands is the region's.</summary>
public sealed record SocialSetup(ImmutableSortedDictionary<string, NpcDefinition> Npcs, ImmutableSortedDictionary<string, DialogueDefinition> Dialogues)
{
    /// <summary>How companions behave (M6); null in a pack where nobody can join.</summary>
    public CompanionTuning? Companions { get; init; }

    public static SocialSetup Empty { get; } = new(ImmutableSortedDictionary.Create<string, NpcDefinition>(StringComparer.Ordinal),
        ImmutableSortedDictionary.Create<string, DialogueDefinition>(StringComparer.Ordinal));
}

// ── commands ────────────────────────────────────────────────────────────────

/// <summary>Buy from a trader within reach: one of their wares (by its <see cref="WareView.Ref"/>), at their price.</summary>
public sealed record BuyCommand(EntityId Actor, string NpcId, string Ware, int Count) : GameCommand(Actor);

/// <summary>Sell a carried stack to a trader within reach who buys that kind of thing.</summary>
public sealed record SellCommand(EntityId Actor, string NpcId, EntityId Item, int Count) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

/// <summary>What an NPC thinks of the player moved on one dimension, for a named reason (S-26: every change is attributed).</summary>
public sealed record RelationshipChanged(string NpcId, string Dimension, int From, int To, string EventKey, long Tick);

public sealed record ItemBought(string NpcId, string ItemId, int Count, long Price, long Tick);

public sealed record ItemSold(string NpcId, string ItemId, int Count, long Price, long Tick);

// ── views ───────────────────────────────────────────────────────────────────

/// <summary>An NPC as presentation draws them: who, where, and whether the character is talking to them.</summary>
public sealed record NpcView(string Id, EntityId InstanceId, string Name, Body Body, bool Talking)
{
    /// <summary>A companion lying downed (M6).</summary>
    public bool Downed { get; init; }
}

/// <summary>A trader's wares as they stand, each at the price they ask for one.</summary>
public sealed record WaresView(string NpcId, string MerchantId, ImmutableArray<WareView> Wares);

/// <summary>A ware: <see cref="Ref"/> is what a <see cref="BuyCommand"/> names it by.</summary>
public sealed record WareView(string Ref, string ItemId, int Count, int Quality, long Price);

// ── state and internal commands ─────────────────────────────────────────────

/// <summary>An NPC's body as the simulation holds it: where they stand and which way they face. Transient in Phase 1.</summary>
internal sealed record NpcState(NpcDefinition Definition, EntityId InstanceId, NpcSite Site, Body Body);

/// <summary>To <see cref="RelationshipSystem"/>: move what an NPC thinks of the player on one dimension, for a reason.</summary>
internal sealed record ChangeRelationship(string NpcId, string Dimension, int Delta, string EventKey) : InternalCommand;

// ── systems ─────────────────────────────────────────────────────────────────

/// <summary>
/// Owns: <see cref="StateSlice.Npcs"/> - the NPCs' bodies (S-24). Each named NPC stands where the region puts them, turned
/// the way the region says, and turns to face whoever talks to them. Phase 1 has no schedules (the vertical slice) and no
/// simulation tiers for NPCs: the whole population is simulated in full. Identity is derived from the NPC's ID (D-10). A
/// companion's body (M6) is moved by <see cref="CompanionSystem"/> through <see cref="PlaceNpc"/> and saved with the companion;
/// everyone else stands where the region puts them, so nothing about their bodies needs saving.
/// </summary>
internal sealed class NpcSystem
{
    private const int TurnMdegPerTick = 18_000;   // 360 degrees a second at 20 Hz

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;

    public NpcSystem(SystemContext context, SliceOwner owner)
    {
        _context = context;
        _owner = owner;
    }

    private RuntimeState State => _context.State;

    /// <summary>A named NPC's instance ID: derived from their definition, so they are the same instance in every world and load.</summary>
    public static EntityId InstanceIdOf(string npcId)
    {
        using var h = new CanonicalHasher();
        return EntityId.Create(EntityKind.Npc, 1, h.Add("unnamed.npc/v1").Add(npcId).FinishBytes().AsSpan(0, 10));
    }

    public void Populate()
    {
        foreach (var site in _context.Setup.Layout.Npcs)
        {
            if (!_context.Setup.Social.Npcs.TryGetValue(site.NpcId, out var definition))
                continue;
            var id = InstanceIdOf(site.NpcId);
            if (!State.World.Registry.Exists(id))
                State.World.Registry.CreateEntity(DefinitionId.Parse(site.NpcId), id);
            long y = _context.Setup.Layout.Space.Terrain.HeightAtMm(site.XMm, site.ZMm);
            State.SetNpc(_owner, new NpcState(definition, id, site, new Body(site.XMm, y, site.ZMm, site.FacingMdeg)));
        }
    }

    public string? Handle(PlaceNpc command)
    {
        if (!State.Npcs.TryGetValue(command.NpcId, out var npc))
            return $"there is no one called {command.NpcId} here";
        State.SetNpc(_owner, npc with { Body = command.Body });
        return null;
    }

    public void Tick(long tick)
    {
        string? talking = State.Conversation?.NpcId;
        var player = State.Body;
        foreach (var npc in State.Npcs.Values.Where(n => !State.Companions.ContainsKey(n.Definition.Id)))
        {
            int wanted = npc.Definition.Id == talking
                ? CombatRules.FacingTowards(npc.Body.XMm, npc.Body.ZMm, player.XMm, player.ZMm)
                : npc.Site.FacingMdeg;
            int delta = (int)(((long)wanted - npc.Body.FacingMdeg + 540_000) % 360_000) - 180_000;
            if (delta == 0)
                continue;
            int facing = (int)(((long)npc.Body.FacingMdeg + Math.Clamp(delta, -TurnMdegPerTick, TurnMdegPerTick) + 360_000) % 360_000);
            State.SetNpc(_owner, npc with { Body = npc.Body with { FacingMdeg = facing } });
        }
    }

    public ImmutableArray<NpcView> Views() =>
        State.Npcs.Values.Select(n => new NpcView(n.Definition.Id, n.InstanceId, n.Definition.Name, n.Body, State.Conversation?.NpcId == n.Definition.Id)
        {
            Downed = State.Companions.TryGetValue(n.Definition.Id, out var companion) && companion.Condition == CompanionCondition.Downed,
        }).ToImmutableArray();
}

/// <summary>
/// Owns: <see cref="StateSlice.Relationships"/> - what each NPC thinks of the player, per dimension (S-26), saved with the
/// player. Every change names its reason; each value is held in [-100, 100].
/// </summary>
internal sealed class RelationshipSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;

    public RelationshipSystem(SystemContext context, SliceOwner owner)
    {
        _context = context;
        _owner = owner;
    }

    public string? Handle(ChangeRelationship command, long tick)
    {
        if (!Relationships.IsDimension(command.Dimension))
            return $"'{command.Dimension}' is not a relationship dimension";
        int from = _context.State.RelationshipOf(command.NpcId, command.Dimension);
        int to = Relationships.Clamp(from + command.Delta);
        _context.State.SetRelationship(_owner, command.NpcId, command.Dimension, to);
        _context.Events.Publish(new RelationshipChanged(command.NpcId, command.Dimension, from, to, command.EventKey, tick));
        return null;
    }
}

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
