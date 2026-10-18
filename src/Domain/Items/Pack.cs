// UNNAMED Domain - the pack: bags as items, specialised bags, where a stack lies, sorting and the category tabs (owner request, 2026-10-18)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Items;

/// <summary>
/// What a bag is, as an item definition gives it: its slots at standard make, and - for a specialised bag - the kinds of thing it holds and
/// nothing else (<see cref="PackKinds"/>). A general bag has no kinds. A bag adds slots, never carrying capacity: its weight counts like
/// any item's and the laden band is unchanged.
/// </summary>
public sealed record BagSpec(int Slots, ImmutableArray<string> Accepts)
{
    public bool IsSpecialised => !Accepts.IsDefaultOrEmpty;
}

/// <summary>The kinds a specialised bag can be for. An item's kind is its authored <c>pack_kind</c>, else <c>material</c> for a material.</summary>
public static class PackKinds
{
    public const string Material = "material";
    public const string Herb = "herb";
    public const string Ammo = "ammo";

    public static readonly ImmutableArray<string> All = ImmutableArray.Create(Material, Herb, Ammo);

    /// <summary>The kind an item routes by; null for everything only a general bag holds.</summary>
    public static string? Of(ItemDefinition definition) =>
        definition.PackKind ?? (definition.Category == "material" ? Material : null);
}

/// <summary>Where a stack lies in the pack: bag 0 is the backpack, 1..N the equipped bags in their bag slots.</summary>
public readonly record struct PackSlot(int Bag, int Slot);

/// <summary>One container of the pack: the backpack or an equipped bag. An empty <see cref="Accepts"/> takes anything.</summary>
public sealed record PackContainer(int Bag, int Capacity, ImmutableArray<string> Accepts)
{
    public bool Takes(string? kind) => Accepts.IsDefaultOrEmpty || (kind is not null && Accepts.Contains(kind));
}

/// <summary>A stack as placement sees it: its identity, its kind, where it lies now (null: nowhere yet), and whether it holds that place.</summary>
public sealed record PackItem(string Id, string? Kind, PackSlot? At, bool Locked);

/// <summary>The result of placing stacks: where each one lies, and the ones that found no room.</summary>
public sealed record PackPlacement(ImmutableSortedDictionary<string, PackSlot> Placed, ImmutableArray<string> Unplaced);

/// <summary>The fixed shape of the pack: the number of bag slots beside the backpack (owner request: a backpack plus four).</summary>
public static class PackLimits
{
    public const int BagSlots = 4;
}

/// <summary>The tuning of bags (<c>config.inventory bags</c>).</summary>
public sealed record BagRules(int SlotsPerQualityStep, long StashPullRadiusMm)
{
    public static readonly BagRules Default = new(1, 60_000);

    /// <summary>A bag's slots at a quality: its definition's, a step more or fewer for each step of quality, never fewer than 1.</summary>
    public int Capacity(BagSpec spec, int quality) => Math.Max(1, spec.Slots + quality * SlotsPerQualityStep);
}

/// <summary>
/// Placement, pure and deterministic: a stack keeps the slot it holds when that slot is still its own to hold; every other stack goes to
/// the most specific bag that takes its kind, then the general containers, backpack first, in the first free slot, in identity order.
/// </summary>
public static class PackRules
{
    /// <summary>The containers of the pack: the backpack, then each equipped bag in its bag slot (1-based; an empty bag slot is not a container).</summary>
    public static ImmutableArray<PackContainer> Containers(int backpackSlots, IEnumerable<(int Bag, int Capacity, ImmutableArray<string> Accepts)> bags)
    {
        var all = ImmutableArray.CreateBuilder<PackContainer>();
        all.Add(new PackContainer(0, backpackSlots, ImmutableArray<string>.Empty));
        foreach (var (bag, capacity, accepts) in bags.OrderBy(b => b.Bag))
            all.Add(new PackContainer(bag, capacity, accepts.IsDefault ? ImmutableArray<string>.Empty : accepts));
        return all.ToImmutable();
    }

    /// <summary>The containers a kind goes to, in the order they are tried: bags specialised to it (narrowest first), then the general ones.</summary>
    public static IEnumerable<PackContainer> Route(string? kind, IReadOnlyList<PackContainer> containers) =>
        containers.Where(c => !c.Accepts.IsDefaultOrEmpty && c.Takes(kind)).OrderBy(c => c.Accepts.Length).ThenBy(c => c.Bag)
            .Concat(containers.Where(c => c.Accepts.IsDefaultOrEmpty).OrderBy(c => c.Bag));

    /// <summary>
    /// Place stacks. <paramref name="keeps"/> says which keep the slot they hold (a stack that holds one that is out of range, taken by another
    /// stack, or of a bag that does not take it, loses it). Stacks that keep nothing, and the rest in <paramref name="order"/> (identity order
    /// when null), are placed by routing.
    /// </summary>
    public static PackPlacement Place(IEnumerable<PackItem> items, IReadOnlyList<PackContainer> containers, Func<PackItem, bool>? keeps = null,
        IReadOnlyList<string>? order = null)
    {
        keeps ??= _ => true;
        var list = items.OrderBy(i => i.Id, StringComparer.Ordinal).ToList();
        var taken = new HashSet<PackSlot>();
        var placed = new Dictionary<string, PackSlot>(StringComparer.Ordinal);
        foreach (var item in list)
        {
            if (item.At is not { } at || !keeps(item) || containers.FirstOrDefault(c => c.Bag == at.Bag) is not { } container
                || at.Slot < 0 || at.Slot >= container.Capacity || !container.Takes(item.Kind) || !taken.Add(at))
                continue;
            placed[item.Id] = at;
        }
        var unplaced = ImmutableArray.CreateBuilder<string>();
        var sequence = order is null ? list : order.Select(id => list.FirstOrDefault(i => i.Id == id)).OfType<PackItem>()
            .Concat(list.Where(i => !order.Contains(i.Id))).ToList();
        foreach (var item in sequence.Where(i => !placed.ContainsKey(i.Id)))
        {
            var slot = FirstFree(item.Kind, containers, taken);
            if (slot is { } found)
            {
                taken.Add(found);
                placed[item.Id] = found;
            }
            else
            {
                unplaced.Add(item.Id);
            }
        }
        return new PackPlacement(placed.ToImmutableSortedDictionary(StringComparer.Ordinal), unplaced.ToImmutable());
    }

    /// <summary>The first free slot a kind would be put in, or null when every container that takes it is full.</summary>
    public static PackSlot? FirstFree(string? kind, IReadOnlyList<PackContainer> containers, ISet<PackSlot> taken)
    {
        foreach (var container in Route(kind, containers))
        {
            for (int slot = 0; slot < container.Capacity; slot++)
            {
                var candidate = new PackSlot(container.Bag, slot);
                if (!taken.Contains(candidate))
                    return candidate;
            }
        }
        return null;
    }

    /// <summary>Whether these stacks, plus <paramref name="more"/> new ones of these kinds, all find a slot.</summary>
    public static bool Fits(IEnumerable<PackItem> items, IReadOnlyList<string?> more, IReadOnlyList<PackContainer> containers)
    {
        var all = items.Concat(more.Select((kind, i) => new PackItem($"￿{i:D6}", kind, null, false)));
        return Place(all, containers).Unplaced.IsEmpty;
    }
}

/// <summary>How the pack is sorted (a command): by type, name, value or weight.</summary>
public enum PackSort
{
    Type,
    Name,
    Value,
    Weight,
}

public static class PackSorts
{
    public static string Key(PackSort sort) => sort.ToString().ToLowerInvariant();

    public static PackSort? Parse(string key) => Enum.GetValues<PackSort>().Cast<PackSort?>().FirstOrDefault(s => Key(s!.Value) == key);
}

/// <summary>A stack as sorting sees it. <see cref="Value"/> and <see cref="Weight"/> are the whole stack's.</summary>
public sealed record SortRow(string Id, string Category, string Name, long Value, long Weight, int Quality);

public static class PackSorting
{
    private static readonly string[] TypeOrder = { "weapon", "armor", "bag", "tool", "consumable", "book", "material", "trinket", "misc", "quest_item" };

    /// <summary>The order a sort puts stacks in. Ties fall to the name, the finer quality, then the identity: the same pack always sorts the same way.</summary>
    public static ImmutableArray<string> Order(IEnumerable<SortRow> rows, PackSort sort)
    {
        int Rank(string category) => Array.IndexOf(TypeOrder, category) is var i and >= 0 ? i : TypeOrder.Length;
        IOrderedEnumerable<SortRow> ordered = sort switch
        {
            PackSort.Name => rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            PackSort.Value => rows.OrderByDescending(r => r.Value).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            PackSort.Weight => rows.OrderByDescending(r => r.Weight).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderBy(r => Rank(r.Category)).ThenBy(r => r.Category, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
        };
        return ordered.ThenByDescending(r => r.Quality).ThenBy(r => r.Id, StringComparer.Ordinal).Select(r => r.Id).ToImmutableArray();
    }
}

/// <summary>The category tabs of the inventory: all, gear, consumables, materials, quest.</summary>
public enum PackTab
{
    All,
    Gear,
    Consumables,
    Materials,
    Quest,
}

public static class PackTabs
{
    public static string Label(PackTab tab) => tab switch
    {
        PackTab.All => "All",
        PackTab.Gear => "Gear",
        PackTab.Consumables => "Consumables",
        PackTab.Materials => "Materials",
        _ => "Quest",
    };

    /// <summary>Which tab an item belongs to besides All: what is eaten or read is a consumable, crafting makings are materials, the rest is gear.</summary>
    public static PackTab Of(ItemDefinition definition) => definition.Category switch
    {
        "consumable" or "book" => PackTab.Consumables,
        "quest_item" => PackTab.Quest,
        _ when PackKinds.Of(definition) is PackKinds.Material or PackKinds.Herb => PackTab.Materials,
        _ => PackTab.Gear,
    };

    public static bool Shows(PackTab tab, ItemDefinition definition) => tab == PackTab.All || Of(definition) == tab;

    /// <summary>Whether a search text finds an item: every word of it is part of the name, the category or the kind, ignoring case.</summary>
    public static bool Matches(string search, string name, ItemDefinition definition)
    {
        foreach (string word in search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!name.Contains(word, StringComparison.OrdinalIgnoreCase) && !definition.Category.Contains(word, StringComparison.OrdinalIgnoreCase)
                && !(PackKinds.Of(definition)?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false))
                return false;
        }
        return true;
    }
}
