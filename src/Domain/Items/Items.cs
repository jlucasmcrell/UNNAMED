// UNNAMED Domain - item definitions, equipment rules, loot tables, prices (DATA_MODEL.md §4.1-§4.3, §4.14, §4.15; PROGRESSION.md §11.1)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Domain.Items;

/// <summary>Where an equipped item sits. The weapon hands plus <c>DATA_MODEL.md</c> §4.3's armor slots.</summary>
public enum EquipSlot
{
    MainHand,
    OffHand,
    Head,
    Chest,
    Hands,
    Legs,
    Feet,
    Cloak,
    Ring,
    Amulet,
}

public static class EquipSlots
{
    public static string Key(EquipSlot slot) => slot switch
    {
        EquipSlot.MainHand => "main_hand",
        EquipSlot.OffHand => "off_hand",
        _ => slot.ToString().ToLowerInvariant(),
    };

    public static EquipSlot Parse(string key)
    {
        foreach (var slot in Enum.GetValues<EquipSlot>())
        {
            if (Key(slot) == key)
                return slot;
        }
        throw new FormatException($"Unknown equipment slot '{key}'");
    }
}

/// <summary>What equipping an item takes: attribute and skill minima, never a level minimum (PROGRESSION.md §11.1).</summary>
public sealed record Requirements(
    ImmutableSortedDictionary<CharacterAttribute, int> Attributes,
    ImmutableSortedDictionary<string, int> Skills)
{
    public static Requirements None { get; } = new(
        ImmutableSortedDictionary<CharacterAttribute, int>.Empty, ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal));
}

/// <summary>A weapon's numbers (DATA_MODEL.md §4.2), as combat (M3c) reads them.</summary>
public sealed record WeaponStats(int DamageMin, int DamageMax, string DamageType, long ReachMm, bool TwoHanded, string? SkillId, string? AmmoDefId)
{
    /// <summary>One swing, from <c>attack_speed</c> (swings per second); 0 when the weapon gives none.</summary>
    public int AttackMs { get; init; }

    /// <summary>A ranged weapon's draw, from <c>draw_time</c>; 0 for a melee weapon.</summary>
    public int DrawMs { get; init; }

    /// <summary>Stamina per attack, from <c>stamina_cost</c>; null takes the combat default.</summary>
    public int? StaminaCost { get; init; }

    public bool Ranged => DrawMs > 0;
}

/// <summary>
/// One item definition as the simulation uses it. Weight is in grams. <see cref="Slot"/> is null for anything that
/// cannot be equipped; a two-handed weapon's slot is the main hand, and it also keeps the off hand empty.
/// </summary>
public sealed record ItemDefinition(
    string Id,
    string Category,
    int StackMax,
    long WeightGrams,
    long ValueBase,
    string Rarity,
    bool NoDrop,
    bool NoSell,
    EquipSlot? Slot,
    Requirements Requirements,
    WeaponStats? Weapon,
    int ArmorValue)
{
    public bool Stacks => StackMax > 1;
}

/// <summary>The item definitions content defines, by ID.</summary>
public sealed class ItemCatalog
{
    public ItemCatalog(IEnumerable<ItemDefinition> definitions) =>
        Definitions = definitions.ToImmutableSortedDictionary(d => d.Id, d => d, StringComparer.Ordinal);

    public static ItemCatalog Empty { get; } = new(Array.Empty<ItemDefinition>());

    public ImmutableSortedDictionary<string, ItemDefinition> Definitions { get; }

    public ItemDefinition? Find(string id) => Definitions.GetValueOrDefault(id);

    public ItemDefinition Get(string id) =>
        Find(id) ?? throw new KeyNotFoundException($"'{id}' is not an item definition");
}

/// <summary>A starting-kit line from <c>config.inventory</c>: what a new character carries, and whether it is equipped.</summary>
public sealed record StartingItem(string ItemId, int Count, bool Equip);

/// <summary>Inventory limits from <c>config.inventory</c>: stacks carried, and weight carried from Might.</summary>
public sealed record InventoryRules(int StackSlots, long CarryBaseGrams, long CarryGramsPerMight, long ReachMm)
{
    public long CarryLimitGrams(CharacterProgression progression, ProgressionRules rules) =>
        CarryBaseGrams + CarryGramsPerMight * ProgressionEngine.AttributeValue(progression, CharacterAttribute.Might, rules);
}

public static class EquipmentRules
{
    /// <summary>Why this character cannot equip this item, or null when it can.</summary>
    public static string? Refusal(ItemDefinition item, CharacterProgression progression, ProgressionRules rules)
    {
        if (item.Slot is null)
            return $"{item.Id} cannot be equipped";
        foreach (var (attribute, minimum) in item.Requirements.Attributes)
        {
            int value = ProgressionEngine.AttributeValue(progression, attribute, rules);
            if (value < minimum)
                return $"{item.Id} needs {ProgressionKeys.Key(attribute)} {minimum}; you have {value}";
        }
        foreach (var (skill, minimum) in item.Requirements.Skills)
        {
            int level = ProgressionEngine.SkillLevel(progression, skill);
            if (level < minimum)
                return $"{item.Id} needs {skill} {minimum}; you have {level}";
        }
        return null;
    }

    /// <summary>The slots an item occupies: a two-handed weapon takes both hands.</summary>
    public static IEnumerable<EquipSlot> Occupies(ItemDefinition item)
    {
        if (item.Slot is not { } slot)
            yield break;
        yield return slot;
        if (item.Weapon is { TwoHanded: true })
            yield return EquipSlot.OffHand;
    }
}

/// <summary>One entry of a loot table: an item or a nested table, with a weight for weighted rolls or a flat chance.</summary>
public sealed record LootEntry(string? ItemId, string? TableId, int Weight, double Chance, int CountMin, int CountMax);

/// <summary>
/// A loot table (DATA_MODEL.md §4.14). <see cref="Guaranteed"/> always resolves; each of <see cref="Rolls"/> draws one
/// weighted entry; <see cref="Independent"/> entries each roll their own chance. Nesting is legal, recursion is not.
/// </summary>
public sealed record LootTable(string Id, int Rolls, ImmutableArray<LootEntry> Weighted, ImmutableArray<LootEntry> Independent, ImmutableArray<LootEntry> Guaranteed);

/// <summary>A resolved drop: this many of this item.</summary>
public sealed record LootDrop(string ItemId, int Count);

public static class LootRoller
{
    /// <summary>
    /// Resolve a table. <paramref name="random"/> maps a sample index to a uniform value in [0, 1): the caller keys it
    /// semantically (seed, cell, source), so the same source always rolls the same result (SYSTEMS.md S-16).
    /// Drops of one item are merged; the order is by item ID.
    /// </summary>
    public static ImmutableArray<LootDrop> Roll(LootTable table, IReadOnlyDictionary<string, LootTable> tables, Func<uint, double> random)
    {
        var drops = new SortedDictionary<string, int>(StringComparer.Ordinal);
        uint sample = 0;
        Resolve(table, tables, random, drops, ref sample, depth: 0);
        return drops.Select(kv => new LootDrop(kv.Key, kv.Value)).ToImmutableArray();
    }

    private static void Resolve(LootTable table, IReadOnlyDictionary<string, LootTable> tables, Func<uint, double> random,
        SortedDictionary<string, int> drops, ref uint sample, int depth)
    {
        if (depth > 8)
            throw new InvalidOperationException($"Loot table {table.Id} nests too deeply (a cycle the content lint should have refused)");
        foreach (var entry in table.Guaranteed)
            Grant(entry, tables, random, drops, ref sample, depth);
        foreach (var entry in table.Independent)
        {
            if (random(sample++) < entry.Chance)
                Grant(entry, tables, random, drops, ref sample, depth);
        }
        int total = table.Weighted.Sum(e => e.Weight);
        for (int roll = 0; roll < table.Rolls && total > 0; roll++)
        {
            double pick = random(sample++) * total;
            foreach (var entry in table.Weighted)
            {
                pick -= entry.Weight;
                if (pick < 0)
                {
                    Grant(entry, tables, random, drops, ref sample, depth);
                    break;
                }
            }
        }
    }

    private static void Grant(LootEntry entry, IReadOnlyDictionary<string, LootTable> tables, Func<uint, double> random,
        SortedDictionary<string, int> drops, ref uint sample, int depth)
    {
        if (entry.TableId is { } nested)
        {
            Resolve(tables[nested], tables, random, drops, ref sample, depth + 1);
            return;
        }
        int count = entry.CountMin == entry.CountMax
            ? entry.CountMin
            : entry.CountMin + (int)Math.Floor(random(sample++) * (entry.CountMax - entry.CountMin + 1));
        if (count > 0)
            drops[entry.ItemId!] = drops.GetValueOrDefault(entry.ItemId!) + count;
    }
}

/// <summary>A merchant's stock and pricing (DATA_MODEL.md §4.15), Phase 1's stub: fixed stock, no economy.</summary>
public sealed record MerchantStock(string ItemId, int Count, double PriceBias)
{
    /// <summary>A gated row (M7): the ware is listed and sold only while the character stands high enough with a faction.</summary>
    public UNNAMED.Domain.Factions.StandingRequirement? Requires { get; init; }
}

public sealed record Merchant(string Id, ImmutableArray<MerchantStock> Stock, ImmutableArray<string> BuysCategories);

/// <summary>Prices from <c>config.economy</c>. A merchant buys at a fraction of value, so buying to resell loses money.</summary>
public sealed record Pricing(double SellRatio)
{
    public long BuyPrice(ItemDefinition item, MerchantStock stock) => (long)Math.Ceiling(item.ValueBase * stock.PriceBias);

    /// <summary>What a merchant pays, or null when it will not buy this item.</summary>
    public long? SellPrice(ItemDefinition item, Merchant merchant) =>
        item.NoSell || !merchant.BuysCategories.Contains(item.Category) ? null : (long)Math.Floor(item.ValueBase * SellRatio);
}
