// UNNAMED World - items at run time: carrying, containers, the ground, equipment (SYSTEMS.md S-14, S-15, S-16; M3b)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>The item rules a simulation runs under, built from content at boot.</summary>
public sealed record ItemSetup(
    ItemCatalog Catalog,
    ImmutableSortedDictionary<string, LootTable> LootTables,
    InventoryRules Inventory,
    ImmutableArray<StartingItem> StartingKit,
    Pricing Pricing,
    ImmutableSortedDictionary<string, Merchant> Merchants)
{
    public static ItemSetup Empty { get; } = new(
        ItemCatalog.Empty, ImmutableSortedDictionary.Create<string, LootTable>(StringComparer.Ordinal), new InventoryRules(24, 30_000, 2_000, 1_600),
        ImmutableArray<StartingItem>.Empty, new Pricing(0.4), ImmutableSortedDictionary.Create<string, Merchant>(StringComparer.Ordinal));
}

public enum PlaceKind
{
    Inventory,
    Container,
    Ground,
}

/// <summary>Where an item is: carried, in an authored container, or lying in the world.</summary>
public readonly record struct ItemPlace(PlaceKind Kind, string? ContainerKey = null)
{
    public static ItemPlace Carried => new(PlaceKind.Inventory);
    public static ItemPlace Ground => new(PlaceKind.Ground);
    public static ItemPlace In(string containerKey) => new(PlaceKind.Container, containerKey);

    public override string ToString() => Kind == PlaceKind.Container ? $"container {ContainerKey}" : Kind.ToString().ToLowerInvariant();
}

/// <summary>
/// Move some of a stack (S-14's <c>Transfer</c>, the only sanctioned path for item movement). <see cref="Item"/> is an
/// item ID, or - for a container nobody has touched yet - the entry key its view gave. Carried to carried splits a stack
/// (part of it) or merges it into others of its kind (all of it). To the ground drops it where the body stands.
/// </summary>
public sealed record MoveItemCommand(EntityId Actor, string Item, ItemPlace From, ItemPlace To, int Count) : GameCommand(Actor);

/// <summary>Equip a carried item into its slot; whatever that displaces stays carried (S-15).</summary>
public sealed record EquipCommand(EntityId Actor, EntityId Item) : GameCommand(Actor);

public sealed record UnequipCommand(EntityId Actor, EquipSlot Slot) : GameCommand(Actor);

public sealed record ItemMoved(EntityId Actor, string DefId, int Count, ItemPlace From, ItemPlace To, long Tick);

public sealed record ItemEquipped(EntityId Actor, EquipSlot Slot, EntityId Item, long Tick);

public sealed record ItemUnequipped(EntityId Actor, EquipSlot Slot, EntityId Item, long Tick);

/// <summary>An item as a view shows it: <see cref="Ref"/> is what a <see cref="MoveItemCommand"/> names it by.</summary>
public sealed record ItemView(string Ref, string DefId, int Count);

public sealed record ContainerView(ContainerSite Site, EntityId? Id, ImmutableArray<ItemView> Items);

public sealed record WorldItemView(EntityId Id, string DefId, int Count, long XMm, long ZMm);

/// <summary>
/// Owns: <see cref="StateSlice.PlayerInventory"/> and <see cref="StateSlice.WorldItems"/>. Every movement of an item goes
/// through here, and every check happens before anything changes: a refused move leaves every stack as it was.
/// An authored container holds its loot table's result until the first change, when it and everything in it get
/// identities (the slot-promotion rule, M2) and its record is kept from then on.
/// </summary>
internal sealed class InventorySystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;

    public InventorySystem(SystemContext context, SliceOwner owner, EntityId player)
    {
        _context = context;
        _owner = owner;
        _player = player;
    }

    private ItemSetup Items => _context.Setup.Items;
    private RuntimeState State => _context.State;

    public string? Handle(MoveItemCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (command.Count < 1)
            return "move at least one";
        if (command.From == command.To && command.From.Kind != PlaceKind.Inventory)
            return "the item is already there";
        if ((Check(command.From) ?? Check(command.To)) is { } placeProblem)
            return placeProblem;
        if (Find(command.From, command.Item) is not { } source)
            return $"there is no '{command.Item}' in the {command.From}";
        if (command.Count > source.Count)
            return $"there are only {source.Count} of {source.DefId}";
        if (Items.Catalog.Find(source.DefId) is not { } definition)
            return $"{source.DefId} is not an item this build knows";
        if (command.From.Kind == PlaceKind.Ground && Distance(source.XMm, source.ZMm) > Items.Inventory.ReachMm)
            return "that is out of reach";
        if (command.From.Kind == PlaceKind.Inventory && command.To.Kind != PlaceKind.Inventory && source.Id is { } carried
            && State.Equipment.ContainsValue(carried))
            return $"unequip {definition.Id} first";
        if (command.To.Kind == PlaceKind.Ground && definition.NoDrop)
            return $"{definition.Id} cannot be dropped";

        bool whole = command.Count == source.Count;
        if (command.From.Kind == PlaceKind.Inventory && command.To.Kind == PlaceKind.Inventory)
            return MoveWithinInventory(command, source, definition, whole, tick);

        // Room at the destination, judged as it will be once the items leave their source.
        if (command.To.Kind == PlaceKind.Inventory)
        {
            var stacks = State.Inventory.Select(e => (e.ItemId, e.DefId, e.Count)).ToList();
            if (Room(stacks, definition, command.Count, Items.Inventory.StackSlots) is { } full)
                return full;
            long weight = CarriedGrams() + definition.WeightGrams * command.Count;
            long limit = Items.Inventory.CarryLimitGrams(State.Progression, _context.Setup.Progression);
            if (weight > limit)
                return $"too heavy: {weight / 1000.0:0.##} kg of {limit / 1000.0:0.##} kg";
        }
        else if (command.To.Kind == PlaceKind.Container)
        {
            var site = _context.Setup.Layout.FindContainer(command.To.ContainerKey!)!;
            var stacks = ContentsOf(site).Select(i => (i.Id ?? default!, i.DefId, i.Count)).ToList();
            if (Room(stacks, definition, command.Count, site.StackSlots) is { } full)
                return full;
        }

        // Everything checked: take from the source, then put at the destination.
        var moving = Take(command.From, source, command.Count, whole);
        Put(command.To, definition, command.Count, moving);
        _context.Events.Publish(new ItemMoved(_player, definition.Id, command.Count, command.From, command.To, tick));
        return null;
    }

    /// <summary>Split part of a stack off, or merge a whole stack into others of its kind.</summary>
    private string? MoveWithinInventory(MoveItemCommand command, Located source, ItemDefinition definition, bool whole, long tick)
    {
        var entries = State.Inventory.ToList();
        var entry = entries.Single(e => e.ItemId == source.Id);
        if (!whole)
        {
            if (entries.Count >= Items.Inventory.StackSlots)
                return "no free stack slot to split into";
            entries[entries.IndexOf(entry)] = entry with { Count = entry.Count - command.Count };
            entries.Add(new InventoryEntry(NewItem(definition.Id), definition.Id, command.Count));
        }
        else
        {
            var others = entries.Where(e => e.DefId == entry.DefId && e.ItemId != entry.ItemId).OrderBy(e => e.ItemId.Value, StringComparer.Ordinal).ToList();
            int room = others.Sum(e => definition.StackMax - e.Count);
            if (others.Count == 0 || room < entry.Count)
                return others.Count == 0 ? "there is no other stack to merge into" : "the other stacks have no room for all of it";
            if (State.Equipment.ContainsValue(entry.ItemId))
                return $"unequip {definition.Id} first";
            entries.Remove(entry);
            int left = entry.Count;
            foreach (var other in others)
            {
                int add = Math.Min(left, definition.StackMax - other.Count);
                entries[entries.IndexOf(other)] = other with { Count = other.Count + add };
                left -= add;
            }
            Retire(entry.ItemId);
        }
        State.SetInventory(_owner, entries);
        _context.Events.Publish(new ItemMoved(_player, definition.Id, command.Count, ItemPlace.Carried, ItemPlace.Carried, tick));
        return null;
    }

    // ── views ───────────────────────────────────────────────────────────────

    public ContainerView View(ContainerSite site)
    {
        var record = State.World.Container(site.Key);
        return new ContainerView(site, record?.InstanceId, ContentsOf(site).Select(i => new ItemView(i.Ref, i.DefId, i.Count)).ToImmutableArray());
    }

    public ImmutableArray<WorldItemView> WorldItems() =>
        _context.Setup.Layout.CellKeys.Select(CellKey.Parse)
            .SelectMany(cell => State.World.CreatedIn(cell).Select(c =>
            {
                var (x, z) = WorldPosition(cell, c.XCm, c.ZCm);
                return new WorldItemView(c.InstanceId, c.DefId, c.Count, x, z);
            }))
            .ToImmutableArray();

    public long CarriedGrams() =>
        State.Inventory.Sum(e => (Items.Catalog.Find(e.DefId)?.WeightGrams ?? 0) * e.Count);

    // ── the moving parts ────────────────────────────────────────────────────

    /// <summary>One stack as it is found: its reference, what and how many, its identity if it has one, where it lies.</summary>
    private sealed record Located(string Ref, string DefId, int Count, EntityId? Id, int Index, long XMm, long ZMm);

    private string? Check(ItemPlace place)
    {
        if (place.Kind != PlaceKind.Container)
            return null;
        if (_context.Setup.Layout.FindContainer(place.ContainerKey ?? string.Empty) is not { } site)
            return $"there is no container '{place.ContainerKey}'";
        return Distance(site.XMm, site.ZMm) > Items.Inventory.ReachMm ? $"{site.Key} is out of reach" : null;
    }

    private Located? Find(ItemPlace place, string itemRef)
    {
        switch (place.Kind)
        {
            case PlaceKind.Inventory:
                return State.Inventory.Where(e => e.ItemId.Value == itemRef)
                    .Select(e => new Located(itemRef, e.DefId, e.Count, e.ItemId, -1, 0, 0)).FirstOrDefault();
            case PlaceKind.Ground:
            {
                if (!EntityId.TryParse(itemRef, out var id) || State.World.FindCreated(id) is not { } record || !CellKey.TryParse(record.HostCell, out var cell))
                    return null;
                var (x, z) = WorldPosition(cell, record.XCm, record.ZCm);
                return new Located(itemRef, record.DefId, record.Count, id, -1, x, z);
            }
            default:
            {
                var site = _context.Setup.Layout.FindContainer(place.ContainerKey!)!;
                return ContentsOf(site).Select((item, index) => new Located(item.Ref, item.DefId, item.Count, item.Id, index, site.XMm, site.ZMm))
                    .FirstOrDefault(l => l.Ref == itemRef);
            }
        }
    }

    /// <summary>A container's contents: its record once it has changed, otherwise its loot table rolled for this world.</summary>
    private IReadOnlyList<(string Ref, string DefId, int Count, EntityId? Id)> ContentsOf(ContainerSite site)
    {
        if (State.World.Container(site.Key) is { } record)
            return record.Items.Select(i => (i.ItemId.Value, i.DefId, i.Count, (EntityId?)i.ItemId)).ToList();
        return Baseline(site).Select((drop, i) => ($"{site.Key}#{i:00}", drop.ItemId, drop.Count, (EntityId?)null)).ToList();
    }

    /// <summary>The loot table's result for this world, split into stacks: the same on every load (SYSTEMS.md S-16).</summary>
    private List<LootDrop> Baseline(ContainerSite site)
    {
        var cell = CellOf(site.XMm, site.ZMm);
        var channel = RngChannel.Open(State.World.WorldSeed, cell, "loot", site.Key);
        var drops = LootRoller.Roll(Items.LootTables[site.LootTableId], Items.LootTables, sample => channel.UInt64(sample) / 18446744073709551616.0);
        var stacks = new List<LootDrop>();
        foreach (var drop in drops)
        {
            int stackMax = Items.Catalog.Find(drop.ItemId)?.StackMax ?? 1;
            for (int left = drop.Count; left > 0; left -= stackMax)
                stacks.Add(new LootDrop(drop.ItemId, Math.Min(left, stackMax)));
        }
        return stacks;
    }

    /// <summary>The first change to a container: it and each stack in it get an identity, and its record starts.</summary>
    private ContainerRecord Materialize(ContainerSite site)
    {
        if (State.World.Container(site.Key) is { } existing)
            return existing;
        var registry = State.World.Registry;
        var chest = registry.CreateEntity(DefinitionId.Parse(site.Key), EntityKind.Container).InstanceId;
        var items = Baseline(site).Select(drop => new ContainerItem(NewItem(drop.ItemId), drop.ItemId, drop.Count)).ToImmutableArray();
        var record = new ContainerRecord(site.Key, chest, CellOf(site.XMm, site.ZMm).ToString(), items);
        State.SetContainer(_owner, record);
        return record;
    }

    /// <summary>Remove <paramref name="count"/> from the source. Returns the identity that travels, if the whole stack moves.</summary>
    private EntityId? Take(ItemPlace place, Located source, int count, bool whole)
    {
        switch (place.Kind)
        {
            case PlaceKind.Inventory:
            {
                var entries = State.Inventory.ToList();
                var entry = entries.Single(e => e.ItemId == source.Id);
                if (whole)
                    entries.Remove(entry);
                else
                    entries[entries.IndexOf(entry)] = entry with { Count = entry.Count - count };
                State.SetInventory(_owner, entries);
                return whole ? entry.ItemId : null;
            }
            case PlaceKind.Ground:
            {
                var record = State.TakeItem(_owner, source.Id!);
                if (whole)
                    return record.InstanceId;
                var cell = CellKey.Parse(record.HostCell);
                State.PlaceItem(_owner, cell, record.InstanceId, record.DefId, record.Count - count, record.XCm, record.ZCm);
                return null;
            }
            default:
            {
                var site = _context.Setup.Layout.FindContainer(place.ContainerKey!)!;
                var record = Materialize(site);
                var item = record.Items[source.Id is null ? source.Index : record.Items.IndexOf(record.Items.Single(i => i.ItemId == source.Id))];
                var items = whole ? record.Items.Remove(item) : record.Items.Replace(item, item with { Count = item.Count - count });
                State.SetContainer(_owner, record with { Items = items });
                return whole ? item.ItemId : null;
            }
        }
    }

    /// <summary>Add <paramref name="count"/> at the destination, merging into stacks of the same kind first.</summary>
    private void Put(ItemPlace place, ItemDefinition definition, int count, EntityId? moving)
    {
        switch (place.Kind)
        {
            case PlaceKind.Inventory:
            {
                var entries = State.Inventory.ToList();
                int left = MergeInto(entries.Where(e => e.DefId == definition.Id).OrderBy(e => e.ItemId.Value, StringComparer.Ordinal).ToList(),
                    definition, count, (e, add) => entries[entries.IndexOf(e)] = e with { Count = e.Count + add });
                foreach (int stack in Stacks(left, definition.StackMax))
                    entries.Add(new InventoryEntry(Identity(ref moving, definition.Id), definition.Id, stack));
                Retire(moving);
                State.SetInventory(_owner, entries);
                break;
            }
            case PlaceKind.Ground:
            {
                var body = State.Body;
                var cell = CellOf(body.XMm, body.ZMm);
                var (minX, minZ) = CellOrigin(cell);
                // One placed stack per drop, where the body stands; a drop larger than a stack lies as several.
                foreach (int stack in Stacks(count, definition.StackMax))
                    State.PlaceItem(_owner, cell, Identity(ref moving, definition.Id), definition.Id, stack,
                        (int)((body.XMm - minX) / 10), (int)((body.ZMm - minZ) / 10));
                break;
            }
            default:
            {
                var site = _context.Setup.Layout.FindContainer(place.ContainerKey!)!;
                var record = Materialize(site);
                var items = record.Items.ToList();
                int left = MergeInto(items.Where(i => i.DefId == definition.Id).OrderBy(i => i.ItemId.Value, StringComparer.Ordinal).ToList(),
                    definition, count, (i, add) => items[items.IndexOf(i)] = i with { Count = i.Count + add });
                foreach (int stack in Stacks(left, definition.StackMax))
                    items.Add(new ContainerItem(Identity(ref moving, definition.Id), definition.Id, stack));
                Retire(moving);
                State.SetContainer(_owner, record with { Items = items.ToImmutableArray() });
                break;
            }
        }
    }

    private static int MergeInto<T>(List<T> sameKind, ItemDefinition definition, int count, Action<T, int> add) where T : notnull
    {
        int left = count;
        if (!definition.Stacks)
            return left;
        foreach (var stack in sameKind)
        {
            int current = stack switch { InventoryEntry e => e.Count, ContainerItem i => i.Count, _ => definition.StackMax };
            int take = Math.Min(left, definition.StackMax - current);
            if (take <= 0)
                continue;
            add(stack, take);
            left -= take;
            if (left == 0)
                break;
        }
        return left;
    }

    /// <summary>Why <paramref name="count"/> more cannot fit in these stacks, or null when it can.</summary>
    private string? Room(List<(EntityId Id, string DefId, int Count)> stacks, ItemDefinition definition, int count, int slots)
    {
        int free = definition.Stacks ? stacks.Where(s => s.DefId == definition.Id).Sum(s => Math.Max(0, definition.StackMax - s.Count)) : 0;
        int needed = Stacks(Math.Max(0, count - free), definition.StackMax).Count();
        return stacks.Count + needed > slots ? $"no room: {needed} more stack(s) would not fit in {slots}" : null;
    }

    private static IEnumerable<int> Stacks(int count, int stackMax)
    {
        for (int left = count; left > 0; left -= stackMax)
            yield return Math.Min(left, stackMax);
    }

    /// <summary>The travelling identity for the first new stack, a fresh one for any after it.</summary>
    private EntityId Identity(ref EntityId? moving, string defId)
    {
        if (moving is { } id)
        {
            moving = null;
            return id;
        }
        return NewItem(defId);
    }

    private EntityId NewItem(string defId) => State.World.Registry.CreateEntity(DefinitionId.Parse(defId)).InstanceId;

    /// <summary>An identity that merged away into other stacks retires (D-10).</summary>
    private void Retire(EntityId? id)
    {
        if (id is { } gone && State.World.Registry.Exists(gone))
            State.World.Registry.DestroyEntity(gone);
    }

    private double Distance(long xMm, long zMm)
    {
        double dx = State.Body.XMm - xMm, dz = State.Body.ZMm - zMm;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    private static CellKey CellOf(long xMm, long zMm) => CellKey.OfWorld(xMm / 1000.0, zMm / 1000.0);

    private static (long X, long Z) CellOrigin(CellKey cell) =>
        (((long)cell.Region.Rx * WorldMath.RegionSizeMeters + (long)cell.Cx * WorldMath.CellSizeMeters) * 1000,
         ((long)cell.Region.Rz * WorldMath.RegionSizeMeters + (long)cell.Cz * WorldMath.CellSizeMeters) * 1000);

    private static (long X, long Z) WorldPosition(CellKey cell, int xCm, int zCm)
    {
        var (minX, minZ) = CellOrigin(cell);
        return (minX + xCm * 10L, minZ + zCm * 10L);
    }
}

/// <summary>
/// Owns: <see cref="StateSlice.PlayerEquipment"/>. Equipping binds a carried item to its slot; what it displaces stays
/// carried. Requirements are attribute and skill minima, never a level (PROGRESSION.md §11.1).
/// </summary>
internal sealed class EquipmentSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;

    public EquipmentSystem(SystemContext context, SliceOwner owner, EntityId player)
    {
        _context = context;
        _owner = owner;
        _player = player;
    }

    public string? Handle(EquipCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        var state = _context.State;
        if (state.Inventory.FirstOrDefault(e => e.ItemId == command.Item) is not { } entry)
            return $"{command.Item} is not carried";
        if (_context.Setup.Items.Catalog.Find(entry.DefId) is not { } definition)
            return $"{entry.DefId} is not an item this build knows";
        if (EquipmentRules.Refusal(definition, state.Progression, _context.Setup.Progression) is { } refused)
            return refused;
        var slot = definition.Slot!.Value;
        if (state.Equipment.TryGetValue(slot, out var current) && current == command.Item)
            return $"{definition.Id} is already equipped";

        // What this displaces: its own slots, and a two-hander in the main hand when it takes the off hand.
        var freed = EquipmentRules.Occupies(definition).ToHashSet();
        if (slot == EquipSlot.OffHand && state.Equipment.TryGetValue(EquipSlot.MainHand, out var main)
            && state.Inventory.FirstOrDefault(e => e.ItemId == main) is { } mainEntry
            && _context.Setup.Items.Catalog.Find(mainEntry.DefId)?.Weapon is { TwoHanded: true })
            freed.Add(EquipSlot.MainHand);
        var equipment = state.Equipment;
        foreach (var (taken, item) in state.Equipment.Where(kv => freed.Contains(kv.Key) || kv.Value == command.Item).ToList())
        {
            equipment = equipment.Remove(taken);
            _context.Events.Publish(new ItemUnequipped(_player, taken, item, tick));
        }
        state.SetEquipment(_owner, equipment.SetItem(slot, command.Item));
        _context.Events.Publish(new ItemEquipped(_player, slot, command.Item, tick));
        return null;
    }

    public string? Handle(UnequipCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        var state = _context.State;
        if (!state.Equipment.TryGetValue(command.Slot, out var item))
            return $"nothing is equipped in {EquipSlots.Key(command.Slot)}";
        state.SetEquipment(_owner, state.Equipment.Remove(command.Slot));
        _context.Events.Publish(new ItemUnequipped(_player, command.Slot, item, tick));
        return null;
    }

    /// <summary>The armour worn, summed (combat reads it in M3c).</summary>
    public int Armor() =>
        _context.State.Equipment.Values
            .Select(id => _context.State.Inventory.FirstOrDefault(e => e.ItemId == id)?.DefId)
            .Sum(def => def is null ? 0 : _context.Setup.Items.Catalog.Find(def)?.ArmorValue ?? 0);
}
