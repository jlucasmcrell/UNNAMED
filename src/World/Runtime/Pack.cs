// UNNAMED World - organising the pack: bags in bag slots, specialised bags, sorting, junk, locks, and the stash (owner request, 2026-10-18)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

// ── commands ────────────────────────────────────────────────────────────────

/// <summary>Equip a carried bag into an empty bag slot (1..4). An equipped bag adds its slots to the pack and takes none itself.</summary>
public sealed record EquipBagCommand(EntityId Actor, EntityId Item, int BagSlot) : GameCommand(Actor);

/// <summary>Take the bag in a bag slot out: it goes to the pack, and refused when what it holds would not then fit.</summary>
public sealed record UnequipBagCommand(EntityId Actor, int BagSlot) : GameCommand(Actor);

/// <summary>Put a carried stack in a slot of a bag (0 is the backpack); a stack lying there changes places with it if it can.</summary>
public sealed record PlaceStackCommand(EntityId Actor, EntityId Item, int Bag, int Slot) : GameCommand(Actor);

/// <summary>Sort the pack: partial stacks of a kind merge, and what is not locked is laid out in the order asked, each kind in the bag that takes it.</summary>
public sealed record SortPackCommand(EntityId Actor, PackSort Sort) : GameCommand(Actor);

/// <summary>Mark a carried stack as junk, or take the mark off: "sell junk" at a trader sells what is marked.</summary>
public sealed record MarkJunkCommand(EntityId Actor, EntityId Item, bool Junk) : GameCommand(Actor);

/// <summary>Lock a carried stack against sorting and selling, or unlock it.</summary>
public sealed record LockStackCommand(EntityId Actor, EntityId Item, bool Locked) : GameCommand(Actor);

/// <summary>Sell every stack marked as junk (and not locked) that a trader within reach buys.</summary>
public sealed record SellJunkCommand(EntityId Actor, string NpcId) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

public sealed record BagEquipped(EntityId Actor, int BagSlot, EntityId Item, long Tick);

public sealed record BagUnequipped(EntityId Actor, int BagSlot, EntityId Item, long Tick);

/// <summary>The pack was laid out again (a sort, a stack placed, a mark or a lock): whatever shows it draws it again.</summary>
public sealed record PackOrganised(EntityId Actor, string What, long Tick);

/// <summary>Junk was sold: how many stacks, and the coin they made.</summary>
public sealed record JunkSold(string NpcId, int Stacks, long Price, long Tick);

// ── views ───────────────────────────────────────────────────────────────────

/// <summary>
/// One container of the pack as a view shows it: the backpack (bag 0) or the bag in a bag slot, its capacity at its quality, what it takes
/// (empty: anything) and the stacks in it, by slot.
/// </summary>
public sealed record PackContainerView(int Bag, EntityId? BagItem, string? BagDefId, int Quality, int Capacity, ImmutableArray<string> Accepts,
    ImmutableArray<InventoryEntry> Stacks)
{
    public bool IsBackpack => Bag == 0;

    public bool Takes(string? kind) => Accepts.IsDefaultOrEmpty || (kind is not null && Accepts.Contains(kind));
}

/// <summary>The pack as laid out now: every container, the four bag slots (the bag in each, or null), and any stack that found no place.</summary>
public sealed record PackView(ImmutableArray<PackContainerView> Containers, ImmutableArray<EntityId?> BagSlots, ImmutableArray<InventoryEntry> Unplaced)
{
    public int Capacity => Containers.Sum(c => c.Capacity);

    public int Used => Containers.Sum(c => c.Stacks.Length);

    public PackContainerView? Holding(EntityId item) => Containers.FirstOrDefault(c => c.Stacks.Any(s => s.ItemId == item));
}

/// <summary>
/// The pack, in the inventory system (it owns <see cref="StateSlice.PlayerInventory"/>): where each stack lies, and the commands that move
/// them about. Every one is a command judged here and changes nothing when it is refused; the same moves in the same order always give the
/// same pack (identity order breaks every tie).
/// </summary>
internal sealed partial class InventorySystem
{
    /// <summary>An equipped bag is worn, not carried in a slot; it cannot be spent, sold or put anywhere while it is.</summary>
    private bool Worn(EntityId id) => State.Equipment.ContainsValue(id) || State.Bags.Contains(id);

    private string? Kind(InventoryEntry entry) => Items.Catalog.Find(entry.DefId) is { } definition ? PackKinds.Of(definition) : null;

    /// <summary>Each bag slot that holds a bag the entries really carry, with that bag's capacity at its quality.</summary>
    private ImmutableArray<PackContainer> ContainersOf(IReadOnlyList<InventoryEntry> entries, ImmutableArray<EntityId?> bags)
    {
        var found = new List<(int Bag, int Capacity, ImmutableArray<string> Accepts)>();
        for (int i = 0; i < bags.Length; i++)
        {
            if (bags[i] is { } id && entries.FirstOrDefault(e => e.ItemId == id) is { } entry && Items.Catalog.Find(entry.DefId)?.Bag is { } spec)
                found.Add((i + 1, Items.Inventory.Bags.Capacity(spec, entry.Quality), spec.Accepts));
        }
        return PackRules.Containers(Items.Inventory.StackSlots, found);
    }

    /// <summary>The bag slots, with any that name a bag no longer carried (or no bag) emptied.</summary>
    private ImmutableArray<EntityId?> CleanBags(IReadOnlyList<InventoryEntry> entries, ImmutableArray<EntityId?> bags) =>
        bags.Select(b => b is { } id && entries.FirstOrDefault(e => e.ItemId == id) is { } entry && Items.Catalog.Find(entry.DefId)?.Bag is not null ? b : null)
            .ToImmutableArray();

    private PackItem PackItemOf(InventoryEntry e) =>
        new(e.ItemId.Value, Kind(e), e.Slot >= 0 && e.Bag >= 0 ? new PackSlot(e.Bag, e.Slot) : null, e.Locked);

    /// <summary>Every entry with the place it keeps or is given; an equipped bag lies in none, and a stack with no room keeps no slot.</summary>
    private (List<InventoryEntry> Entries, ImmutableArray<EntityId?> Bags, PackPlacement Placement) PlaceAll(IReadOnlyList<InventoryEntry> entries,
        ImmutableArray<EntityId?> bagsIn, Func<PackItem, bool>? keeps = null, IReadOnlyList<string>? order = null)
    {
        var bags = CleanBags(entries, bagsIn);
        var worn = bags.OfType<EntityId>().ToHashSet();
        var placement = PackRules.Place(entries.Where(e => !worn.Contains(e.ItemId)).Select(PackItemOf), ContainersOf(entries, bags), keeps, order);
        var placed = entries.Select(e =>
            worn.Contains(e.ItemId) ? e with { Bag = -1, Slot = -1 }
            : placement.Placed.TryGetValue(e.ItemId.Value, out var at) ? e with { Bag = at.Bag, Slot = at.Slot }
            : e with { Bag = 0, Slot = -1 }).ToList();
        return (placed, bags, placement);
    }

    /// <summary>Set the carried stacks, each in its place: what has none is put where its kind goes.</summary>
    private void Commit(IEnumerable<InventoryEntry> entries)
    {
        var (placed, bags, _) = PlaceAll(entries.ToList(), State.Bags);
        if (!bags.SequenceEqual(State.Bags))
            State.SetBags(_owner, bags);
        State.SetInventory(_owner, placed);
    }

    /// <summary>
    /// Why <paramref name="count"/> more of an item cannot be carried, or null when they can: the stacks they would make must each find a slot
    /// in a container that takes them. <paramref name="merge"/> false: they join no stack already carried (a split).
    /// </summary>
    private string? PackRoom(IReadOnlyList<InventoryEntry> entries, ItemDefinition definition, int quality, int count, bool merge = true)
    {
        int free = merge && definition.Stacks
            ? entries.Where(e => e.DefId == definition.Id && e.Quality == quality && !e.Junk).Sum(e => Math.Max(0, definition.StackMax - e.Count))
            : 0;
        int needed = Stacks(Math.Max(0, count - free), definition.StackMax).Count();
        if (needed == 0)
            return null;
        var bags = CleanBags(entries, State.Bags);
        var worn = bags.OfType<EntityId>().ToHashSet();
        var carried = entries.Where(e => !worn.Contains(e.ItemId)).Select(PackItemOf);
        return PackRules.Fits(carried, Enumerable.Repeat(PackKinds.Of(definition), needed).ToList(), ContainersOf(entries, bags))
            ? null
            : $"no room: {needed} more stack(s) would not fit in the pack";
    }

    /// <summary>The pack as it lies now, read without changing anything.</summary>
    public PackView Pack()
    {
        var entries = State.Inventory;
        var (placed, bags, placement) = PlaceAll(entries, State.Bags);
        var containers = ContainersOf(placed, bags);
        var views = containers.Select(c =>
        {
            var bagItem = c.Bag == 0 ? null : bags[c.Bag - 1];
            var bagEntry = bagItem is { } id ? placed.First(e => e.ItemId == id) : null;
            return new PackContainerView(c.Bag, bagItem, bagEntry?.DefId, bagEntry?.Quality ?? 0, c.Capacity, c.Accepts,
                placed.Where(e => e.Bag == c.Bag && e.Slot >= 0).OrderBy(e => e.Slot).ToImmutableArray());
        }).ToImmutableArray();
        return new PackView(views, bags, placed.Where(e => placement.Unplaced.Contains(e.ItemId.Value)).ToImmutableArray());
    }

    private string? Carried(EntityId actor, EntityId item, out InventoryEntry entry, out ItemDefinition definition)
    {
        entry = null!;
        definition = null!;
        if (actor != _player)
            return $"unknown actor {actor}";
        if (State.Inventory.FirstOrDefault(e => e.ItemId == item) is not { } found)
            return $"{item} is not carried";
        if (Items.Catalog.Find(found.DefId) is not { } known)
            return $"{found.DefId} is not an item this build knows";
        entry = found;
        definition = known;
        return null;
    }

    public string? Handle(EquipBagCommand command, long tick)
    {
        if (Carried(command.Actor, command.Item, out var entry, out var definition) is { } refused)
            return refused;
        if (definition.Bag is null)
            return $"{definition.Id} is not a bag";
        if (command.BagSlot is < 1 or > PackLimits.BagSlots)
            return $"there is no bag slot {command.BagSlot}";
        if (State.Bags.Contains(command.Item))
            return $"{definition.Id} is already in a bag slot";
        if (State.Bags[command.BagSlot - 1] is { } there)
            return $"bag slot {command.BagSlot} holds {State.Inventory.First(e => e.ItemId == there).DefId}: take it out first";
        State.SetBags(_owner, State.Bags.SetItem(command.BagSlot - 1, command.Item));
        // A specialised bag takes in what is already carried that it takes, out of the general bags and any bag that takes more kinds than it.
        var entries = State.Inventory.ToList();
        var containers = ContainersOf(entries, State.Bags);
        var added = containers.FirstOrDefault(c => c.Bag == command.BagSlot);
        Func<PackItem, bool>? keeps = null;
        if (added is { Accepts.IsDefaultOrEmpty: false })
            keeps = item => item.Locked || !added.Takes(item.Kind) || item.At is not { } at
                || containers.FirstOrDefault(c => c.Bag == at.Bag) is not { } current || (!current.Accepts.IsDefaultOrEmpty && current.Accepts.Length <= added.Accepts.Length);
        var (placed, bags, _) = PlaceAll(entries, State.Bags, keeps);
        State.SetBags(_owner, bags);
        State.SetInventory(_owner, placed);
        _context.Events.Publish(new BagEquipped(_player, command.BagSlot, command.Item, tick));
        _context.Events.Publish(new PackOrganised(_player, "bag", tick));
        return null;
    }

    public string? Handle(UnequipBagCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (command.BagSlot is < 1 or > PackLimits.BagSlots)
            return $"there is no bag slot {command.BagSlot}";
        if (State.Bags[command.BagSlot - 1] is not { } bag)
            return $"bag slot {command.BagSlot} is empty";
        var bags = State.Bags.SetItem(command.BagSlot - 1, null);
        // What lay in the bag goes back into the pack's other containers; the bag itself becomes a stack there.
        var entries = State.Inventory.Select(e => e.Bag == command.BagSlot ? e with { Slot = -1 } : e).ToList();
        var (placed, cleaned, placement) = PlaceAll(entries, bags);
        if (!placement.Unplaced.IsEmpty)
        {
            string name = State.Inventory.First(e => e.ItemId == bag).DefId;
            return $"{name} holds more than the rest of the pack has room for: {placement.Unplaced.Length} stack(s) would not fit";
        }
        State.SetBags(_owner, cleaned);
        State.SetInventory(_owner, placed);
        _context.Events.Publish(new BagUnequipped(_player, command.BagSlot, bag, tick));
        _context.Events.Publish(new PackOrganised(_player, "bag", tick));
        return null;
    }

    public string? Handle(PlaceStackCommand command, long tick)
    {
        if (Carried(command.Actor, command.Item, out var entry, out _) is { } refused)
            return refused;
        if (State.Bags.Contains(command.Item))
            return "a bag in a bag slot lies in no slot";
        var pack = Pack();
        if (pack.Containers.FirstOrDefault(c => c.Bag == command.Bag) is not { } target)
            return $"there is no bag {command.Bag}";
        if (command.Slot < 0 || command.Slot >= target.Capacity)
            return $"slot {command.Slot} is outside {(target.IsBackpack ? "the backpack" : $"bag {target.Bag}")}";
        var kind = Kind(entry);
        if (!target.Takes(kind))
            return $"{(target.IsBackpack ? "the backpack" : $"bag {target.Bag}")} takes only {string.Join(", ", target.Accepts)}";
        var current = pack.Holding(command.Item)?.Stacks.First(s => s.ItemId == command.Item);
        var (fromBag, fromSlot) = current is null ? (-1, -1) : (current.Bag, current.Slot);
        if (fromBag == command.Bag && fromSlot == command.Slot)
            return "it is already there";
        // The stacks as the pack lays them out now (a bag in a bag slot included, so it stays worn), then this one moved.
        var laid = pack.Containers.SelectMany(c => c.Stacks).Concat(pack.Unplaced).ToDictionary(e => e.ItemId);
        var entries = State.Inventory.Select(e => laid.GetValueOrDefault(e.ItemId) ?? e).ToList();
        int index = entries.FindIndex(e => e.ItemId == command.Item);
        var occupant = entries.FindIndex(e => e.Bag == command.Bag && e.Slot == command.Slot);
        if (occupant >= 0)
        {
            var other = entries[occupant];
            var home = pack.Containers.FirstOrDefault(c => c.Bag == fromBag);
            if (fromSlot < 0 || home is null || !home.Takes(Kind(other)))
                return "that slot is taken, and what lies there cannot take this one's place";
            entries[occupant] = other with { Bag = fromBag, Slot = fromSlot };
        }
        entries[index] = entries[index] with { Bag = command.Bag, Slot = command.Slot };
        Commit(entries);
        _context.Events.Publish(new PackOrganised(_player, "place", tick));
        return null;
    }

    public string? Handle(SortPackCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (!Enum.IsDefined(command.Sort))
            return "there is no such order";
        var entries = State.Inventory.ToList();
        // Partial stacks of a kind merge first, into as few stacks as hold them; a locked stack, a marked one and an unmarked one stay apart.
        foreach (var group in entries.Where(e => !e.Locked && !Worn(e.ItemId) && Items.Catalog.Find(e.DefId) is { Stacks: true } && e.Notable is null && e.Wear is null)
                     .GroupBy(e => (e.DefId, e.Quality, e.Junk)).Where(g => g.Count() > 1).ToList())
        {
            int stackMax = Items.Catalog.Get(group.Key.DefId).StackMax;
            int total = group.Sum(e => e.Count);
            foreach (var stack in group.OrderBy(e => e.ItemId.Value, StringComparer.Ordinal))
            {
                int keep = Math.Min(total, stackMax);
                total -= keep;
                if (keep == 0)
                {
                    entries.Remove(stack);
                    Retire(stack.ItemId);
                }
                else
                {
                    entries[entries.IndexOf(stack)] = stack with { Count = keep };
                }
            }
        }
        var rows = entries.Where(e => !e.Locked && !State.Bags.Contains(e.ItemId)).Select(e =>
        {
            var definition = Items.Catalog.Get(e.DefId);
            return new SortRow(e.ItemId.Value, definition.Category, definition.Name ?? definition.Id, definition.ValueBase * e.Count, definition.WeightGrams * e.Count, e.Quality);
        }).ToList();
        var order = PackSorting.Order(rows, command.Sort);
        // Only the locked keep their slots; everything else is laid out from the first slot in the order asked.
        var (placed, bags, placement) = PlaceAll(entries, State.Bags, item => item.Locked, order);
        if (!placement.Unplaced.IsEmpty)
            return "the pack could not be laid out";
        State.SetBags(_owner, bags);
        State.SetInventory(_owner, placed);
        _context.Events.Publish(new PackOrganised(_player, "sort", tick));
        return null;
    }

    public string? Handle(MarkJunkCommand command, long tick)
    {
        if (Carried(command.Actor, command.Item, out var entry, out var definition) is { } refused)
            return refused;
        if (command.Junk && (definition.NoSell || definition.NoDrop))
            return $"{definition.Id} cannot be marked as junk: it cannot be sold or dropped";
        if (command.Junk && Worn(command.Item))
            return $"unequip {definition.Id} first";
        if (entry.Junk == command.Junk)
            return command.Junk ? "it is already marked as junk" : "it is not marked as junk";
        var entries = State.Inventory.ToList();
        entries[entries.IndexOf(entry)] = entry with { Junk = command.Junk };
        Commit(entries);
        _context.Events.Publish(new PackOrganised(_player, "junk", tick));
        return null;
    }

    public string? Handle(LockStackCommand command, long tick)
    {
        if (Carried(command.Actor, command.Item, out var entry, out var definition) is { } refused)
            return refused;
        if (State.Bags.Contains(command.Item))
            return $"{definition.Id} is in a bag slot: a bag is not sorted or sold while it is";
        if (entry.Locked == command.Locked)
            return command.Locked ? "it is already locked" : "it is not locked";
        var entries = State.Inventory.ToList();
        entries[entries.IndexOf(entry)] = entry with { Locked = command.Locked };
        Commit(entries);
        _context.Events.Publish(new PackOrganised(_player, "lock", tick));
        return null;
    }

    // ── the stash ───────────────────────────────────────────────────────────

    /// <summary>Why a stash take cannot be made, or null: each names a stash container, a stack lying in it and no more than it holds.</summary>
    private string? StashProblem(ImmutableArray<StashTake> takes)
    {
        foreach (var group in takes.GroupBy(t => (t.ContainerKey, t.Ref)))
        {
            if (_context.FindContainer(group.Key.ContainerKey) is not { Stash: true } site)
                return $"{group.Key.ContainerKey} is not a stash";
            var contents = ContentsOf(site);
            int index = contents.ToList().FindIndex(i => i.Ref == group.Key.Ref);
            if (index < 0 || group.Any(t => t.Count < 1) || group.Sum(t => t.Count) > contents[index].Count)
                return $"{group.Key.Ref} is not in the stash in that quantity";
        }
        return null;
    }

    private void SpendFromStash(ImmutableArray<StashTake> takes)
    {
        foreach (var byContainer in takes.GroupBy(t => t.ContainerKey))
        {
            var record = Materialize(_context.FindContainer(byContainer.Key)!);
            var items = record.Items.ToList();
            foreach (var take in byContainer)
            {
                int index = items.FindIndex(i => i.ItemId.Value == take.Ref);
                if (take.Count >= items[index].Count)
                {
                    Retire(items[index].ItemId);
                    items.RemoveAt(index);
                }
                else
                {
                    items[index] = items[index] with { Count = items[index].Count - take.Count };
                }
            }
            State.SetContainer(_owner, record with { Items = items.ToImmutableArray() });
        }
    }

    /// <summary>The ItemView stacks a stash site holds now, read without changing anything.</summary>
    public ImmutableArray<ItemView> StashItems(ContainerSite site) => View(site).Items;
}
