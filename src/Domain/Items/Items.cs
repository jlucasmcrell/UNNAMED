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

/// <summary>
/// What kind of thing an item is, beyond its category (DCP E5, ruled A): an ordinary make, a masterwork, a notable find, an artefact.
/// Rarity is derived from it and is never authored apart from it (DCP:903).
/// </summary>
public enum ItemClass
{
    Ordinary,
    Masterwork,
    Notable,
    Artefact,
}

public static class ItemClasses
{
    public static string Key(ItemClass value) => value switch
    {
        ItemClass.Ordinary => "ordinary",
        ItemClass.Masterwork => "masterwork",
        ItemClass.Notable => "notable",
        ItemClass.Artefact => "artefact",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static ItemClass Parse(string key)
    {
        foreach (var value in Enum.GetValues<ItemClass>())
        {
            if (Key(value) == key)
                return value;
        }
        throw new FormatException($"Unknown item class '{key}' (ordinary, masterwork, notable, artefact)");
    }

    /// <summary>The storage rarity a class maps to (DCP:903): the one rarity content may write beside it.</summary>
    public static string Rarity(ItemClass value) => value switch
    {
        ItemClass.Ordinary => "common",
        ItemClass.Masterwork => "uncommon",
        ItemClass.Notable => "rare",
        ItemClass.Artefact => "artifact",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>
    /// What a stack is, as a tooltip names it: its definition's class, or masterwork when an ordinary make came off the bench at masterwork
    /// quality.
    /// </summary>
    public static ItemClass Of(ItemDefinition definition, int quality) =>
        definition.Class == ItemClass.Ordinary && quality >= Crafting.Quality.Masterwork ? ItemClass.Masterwork : definition.Class;

    /// <summary>Whether a stack of this definition at this quality carries a notable-instance record.</summary>
    public static bool KeepsRecord(ItemDefinition definition, int quality) => Of(definition, quality) != ItemClass.Ordinary;
}

/// <summary>How a notable instance came to its holder: the provenance line a tooltip reads (DCP:673-676).</summary>
public enum ProvenanceKind
{
    /// <summary>Made at a station: the maker's mark (PROGRESSION.md §11.2, C12).</summary>
    Crafted,

    /// <summary>Found in a container, a body or on the ground.</summary>
    Found,

    /// <summary>Bought from a trader.</summary>
    Bought,

    /// <summary>Given: a quest's reward, a gift.</summary>
    Given,

    /// <summary>Carried from the start.</summary>
    Kit,
}

public static class ProvenanceKinds
{
    public static string Key(ProvenanceKind value) => value.ToString().ToLowerInvariant();

    public static ProvenanceKind Parse(string key)
    {
        foreach (var value in Enum.GetValues<ProvenanceKind>())
        {
            if (Key(value) == key)
                return value;
        }
        throw new FormatException($"Unknown provenance '{key}'");
    }
}

/// <summary>
/// The record a notable instance carries on its stack (WP3a; schema 16): its name, where it came from - how, from what (a recipe, a
/// container, a quest, a trader), by whose hand when it was made, and when - and how familiar its holder has grown with it, 0..5.
/// Familiarity is never generic damage (OD-C07): it is counted from use and shown, and what it unlocks is a later milestone's.
/// </summary>
public sealed record NotableRecord(string Name, ProvenanceKind Provenance, string SourceRef, long Tick)
{
    public const int MaxFamiliarity = 5;

    /// <summary>The maker's mark: the name of the hand that made it, for a crafted piece; null otherwise.</summary>
    public string? Maker { get; init; }

    /// <summary>0..<see cref="MaxFamiliarity"/>.</summary>
    public int Familiarity { get; init; }

    /// <summary>Uses counted towards the next step of familiarity; 0 at the last step.</summary>
    public int Uses { get; init; }

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Name) && Enum.IsDefined(Provenance) && !string.IsNullOrWhiteSpace(SourceRef) && Tick >= 0
        && Familiarity is >= 0 and <= MaxFamiliarity && Uses >= 0 && (Familiarity < MaxFamiliarity || Uses == 0)
        && (Provenance == ProvenanceKind.Crafted) == (Maker is not null) && (Maker is null || !string.IsNullOrWhiteSpace(Maker));

    /// <summary>
    /// One more use: at the threshold for the next step (<paramref name="usesPerStep"/>[familiarity]) familiarity rises and the count
    /// starts again. At the last step nothing more is counted.
    /// </summary>
    public NotableRecord Used(IReadOnlyList<int> usesPerStep)
    {
        if (Familiarity >= MaxFamiliarity || Familiarity >= usesPerStep.Count)
            return this;
        int uses = Uses + 1;
        return uses >= usesPerStep[Familiarity] ? this with { Familiarity = Familiarity + 1, Uses = 0 } : this with { Uses = uses };
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

    /// <summary>Its authored <c>name</c>: what a notable instance is called when its record is made (WP3a). Null when content gives none.</summary>
    public string? Name { get; init; }

    /// <summary>The item's class (WP3a); <see cref="Rarity"/> is derived from it.</summary>
    public ItemClass Class { get; init; } = ItemClass.Ordinary;

    /// <summary>A light carried in the hand (the off-hand torch): how far it lights, in millimetres; 0 for none.</summary>
    public long LightRadiusMm { get; init; }

    /// <summary>
    /// The stealth cost of having it equipped, [0, 1]: creatures see the character this share further (DATA_MODEL.md §4.3
    /// <c>stealth_penalty</c>; a lit torch, ORR:1333).
    /// </summary>
    public double StealthPenalty { get; init; }
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
    /// <summary>How many uses raise a notable instance's familiarity from each step to the next (<c>familiarity_uses_per_step</c>).</summary>
    public ImmutableArray<int> FamiliarityUsesPerStep { get; init; } = ImmutableArray.Create(3, 6, 10, 15, 25);

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

    /// <summary>
    /// The armour one worn piece gives: its definition's, moved a step per step of its quality (WP3a: ±1 a step, as a weapon's damage
    /// moves), never below 0. Something that gives no armour gains none from its quality.
    /// </summary>
    public static int ArmorOf(ItemDefinition item, int quality, int armorPerStep) =>
        item.ArmorValue <= 0 ? 0 : Math.Max(0, item.ArmorValue + quality * armorPerStep);

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
public sealed record MerchantStock(string ItemId, int Count, double PriceBias);

public sealed record Merchant(string Id, ImmutableArray<MerchantStock> Stock, ImmutableArray<string> BuysCategories);

/// <summary>Prices from <c>config.economy</c>. A merchant buys at a fraction of value, so buying to resell loses money.</summary>
public sealed record Pricing(double SellRatio)
{
    public long BuyPrice(ItemDefinition item, MerchantStock stock) => (long)Math.Ceiling(item.ValueBase * stock.PriceBias);

    /// <summary>What a merchant pays, or null when it will not buy this item.</summary>
    public long? SellPrice(ItemDefinition item, Merchant merchant) =>
        item.NoSell || !merchant.BuysCategories.Contains(item.Category) ? null : (long)Math.Floor(item.ValueBase * SellRatio);
}
