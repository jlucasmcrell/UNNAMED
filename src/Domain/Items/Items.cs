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
/// The record a notable instance carries on its stack (WP3a; schema 19): its name, where it came from - how, from what (a recipe, a
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

/// <summary>
/// The wear a stack with durability has taken (WP3b, D5): points of wear against its definition's <see cref="ItemDefinition.DurabilityMax"/>,
/// and the quality it was made at - what a repair is capped by (C13:226). Worn out, the stack's own quality reads crude, so everything
/// that reads quality treats it as crude; its made quality waits here for the repair. A stack that is unworn and at its made quality
/// carries none.
/// </summary>
public sealed record ItemWear(int Points, int MadeQuality)
{
    public bool IsValid => Points >= 0 && Crafting.Quality.IsValid(MadeQuality);
}

/// <summary>Where and how a piece is repaired at a station (WP3b): the station kind, the skill and its least level, and the one material spent.</summary>
public sealed record RepairSpec(string StationKind, string SkillId, int SkillMin, string MaterialId);

/// <summary>
/// The tuning of wear and repair (<c>config.inventory durability</c>; D5): the wear one use takes - a blow landed, a blow taken, a harvest -
/// and a trader's repair: its fee, as a share of the piece's value at full wear, and the best quality a trader's hand gives back.
/// </summary>
public sealed record DurabilityRules(int WearPerHit, int WearPerHitTaken, int WearPerGather, int MerchantFeePercent, int MerchantQualityCap)
{
    public static readonly DurabilityRules Default = new(1, 1, 1, 50, Crafting.Quality.Standard);

    /// <summary>
    /// How many ticks a lit light in the off hand burns for one point of its durability (<c>durability light_wear_ticks</c>, note 13). 20 ticks
    /// is the one-second world tick, so a torch authored at 300 lasts five minutes and a lantern at 3600 an hour. Not above 0: a light that
    /// never burns down would make the fuel meaningless.
    /// </summary>
    public int LightWearTicks { get; init; } = 20;
}

/// <summary>
/// The laden band (<c>config.inventory laden</c>; D4): above this share of the carry limit the character is laden - a sprint drains this
/// share of its usual stamina and a dodge costs this share of its usual. The limit itself still refuses anything heavier (the 100% refusal).
/// </summary>
public sealed record LadenRules(int BandPercent, int SprintDrainPercent, int DodgeCostPercent)
{
    public static readonly LadenRules Default = new(80, 150, 125);

    /// <summary>Above the band: what is carried exceeds <see cref="BandPercent"/> of the limit.</summary>
    public bool IsLaden(long carriedGrams, long limitGrams) => limitGrams > 0 && carriedGrams * 100 > limitGrams * BandPercent;
}

/// <summary>Wear and repair (WP3b, D5): pure functions of a stack's quality and wear. Nothing here ever destroys an item.</summary>
public static class Durability
{
    /// <summary>The quality the stack was made at: its wear's, or its own when it carries none.</summary>
    public static int MadeQuality(int quality, ItemWear? wear) => wear?.MadeQuality ?? quality;

    public static bool IsWornOut(ItemWear? wear, int durabilityMax) => durabilityMax > 0 && wear is not null && wear.Points >= durabilityMax;

    /// <summary>What is left of its durability, 0..max; 0 for an item with none.</summary>
    public static int Remaining(ItemWear? wear, int durabilityMax) => durabilityMax <= 0 ? 0 : Math.Max(0, durabilityMax - (wear?.Points ?? 0));

    /// <summary>
    /// How far a light in the hand reaches now (note 13): its authored <c>light_radius</c> while it has fuel left, and nothing once it has
    /// burned out - a spent torch or lantern gives off no light, though it is still carried, and can be made again or filled again.
    /// </summary>
    public static long LitRadius(long radiusMm, int durabilityMax, ItemWear? wear) =>
        radiusMm <= 0 ? 0 : IsWornOut(wear, durabilityMax) ? 0 : radiusMm;

    /// <summary>
    /// A stack after <paramref name="points"/> more wear: wear counts up to the item's durability and no further; at it the stack is worn
    /// out and its quality reads crude (D5), never lower and never gone. An item with no durability takes none.
    /// </summary>
    public static (int Quality, ItemWear? Wear) Worn(int quality, ItemWear? wear, int durabilityMax, int points)
    {
        if (durabilityMax <= 0 || points <= 0)
            return (quality, wear);
        int made = MadeQuality(quality, wear);
        int worn = Math.Min(durabilityMax, (wear?.Points ?? 0) + points);
        return Normal(worn >= durabilityMax ? Crafting.Quality.Crude : quality, new ItemWear(worn, made));
    }

    /// <summary>Whether a repair would change anything: some wear taken, or its quality below what it was made at.</summary>
    public static bool NeedsRepair(int quality, ItemWear? wear) => wear is not null && (wear.Points > 0 || quality < wear.MadeQuality);

    /// <summary>
    /// A stack repaired: no wear, and the quality the repair gave - re-rolled, capped at the quality it was made at (C13:226), so a repair
    /// never makes a piece better than its maker did.
    /// </summary>
    public static (int Quality, ItemWear? Wear) Repaired(int quality, ItemWear? wear, int rolled)
    {
        int made = MadeQuality(quality, wear);
        return Normal(Math.Clamp(rolled, Crafting.Quality.Crude, made), new ItemWear(0, made));
    }

    /// <summary>
    /// A trader's fee for a repair: the share of the piece's value <see cref="DurabilityRules.MerchantFeePercent"/> gives at full wear,
    /// scaled by the wear taken, and at least 1 coin.
    /// </summary>
    public static long MerchantFee(ItemDefinition item, ItemWear? wear, DurabilityRules rules)
    {
        int points = wear?.Points ?? 0;
        if (item.DurabilityMax <= 0)
            return 0;
        long fee = (item.ValueBase * rules.MerchantFeePercent * Math.Max(points, 1) + 100L * item.DurabilityMax - 1) / (100L * item.DurabilityMax);
        return Math.Max(1, fee);
    }

    /// <summary>
    /// What a trader pays for a worn piece: their offer less the share of it that a trader's repair would charge for the wear, so a worn-out
    /// piece sells for what <see cref="DurabilityRules.MerchantFeePercent"/> leaves of it. Repairing to resell can never gain: the fee is that
    /// share of the piece's value, and the offer is never above it. A piece that sells for anything sells for at least 1.
    /// </summary>
    public static long SalePrice(long offer, ItemDefinition item, ItemWear? wear, DurabilityRules rules)
    {
        if (offer <= 0 || item.DurabilityMax <= 0 || wear is null)
            return offer;
        long points = Math.Min(wear.Points, item.DurabilityMax);
        return Math.Max(1, offer * (100L * item.DurabilityMax - rules.MerchantFeePercent * points) / (100L * item.DurabilityMax));
    }

    /// <summary>
    /// What a trader asks for a worn piece on their shelf: the same share-worn drop <see cref="SalePrice"/> makes, but never by more than the
    /// fee a trader's repair would charge for that wear, so buying a worn piece and mending it never costs less than buying it whole.
    /// </summary>
    public static long ShelfPrice(long ask, ItemDefinition item, ItemWear? wear, DurabilityRules rules)
    {
        if (ask <= 0 || item.DurabilityMax <= 0 || wear is null || wear.Points <= 0)
            return ask;
        return Math.Max(Math.Max(1, ask - MerchantFee(item, wear, rules)), SalePrice(ask, item, wear, rules));
    }

    private static (int, ItemWear?) Normal(int quality, ItemWear wear) => (quality, wear.Points == 0 && quality == wear.MadeQuality ? null : wear);
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

    /// <summary>
    /// What a blow from this weapon multiplies by on a target that has not noticed the attacker, from <c>sneak_multiplier</c>: the dagger
    /// class's own number (STEALTH §12). 1.0 is every other weapon, so a sneak attack with a sword is just a sword blow.
    /// </summary>
    public double SneakMultiplier { get; init; } = 1.0;

    /// <summary>
    /// How far this weapon's own swing carries, from <c>swing_noise_m</c> (STEALTH §3.3, §5.1): a knife's arm moves quietly, a great
    /// sword does not. Zero takes <c>config.creature_behaviour.noise_m.swing</c>.
    /// </summary>
    public long SwingNoiseMm { get; init; }

    /// <summary>
    /// How far this weapon is heard <em>at the shooter</em>, from <c>shot_noise_m</c> (STEALTH §7.8): a bow's twang, a crossbow's thunk,
    /// a thrown blade's almost nothing. Zero takes the build's shot radius. It is never the melee swing radius - a shot is a shot.
    /// </summary>
    public long ShotNoiseMm { get; init; }

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

    /// <summary>
    /// It can be thrown and what it makes where it lands (STEALTH §3.2): zero for anything that cannot be thrown as a lure. The throw is a
    /// noise, not a weapon - what an observer gets is a place to look, never the thrower.
    /// </summary>
    public long ThrowNoiseMm { get; init; }

    /// <summary>How far it is thrown, from <c>throw_range_m</c>; zero uses the build's own arm.</summary>
    public long ThrowRangeMm { get; init; }

    /// <summary>How much wear it takes before it is worn out (WP3b, D5): weapons, armour and tools only; 0 for none.</summary>
    public int DurabilityMax { get; init; }

    /// <summary>How it is repaired at a station (WP3b); null when no station repairs it.</summary>
    public RepairSpec? Repair { get; init; }

    /// <summary>What a bag is, for an item that is one (stack_max 1); null for everything else. A bag is equipped into a bag slot.</summary>
    public BagSpec? Bag { get; init; }

    /// <summary>The kind of thing it is for a specialised bag to take (<see cref="PackKinds"/>); null takes the default, material for a material.</summary>
    public string? PackKind { get; init; }

    /// <summary>
    /// A live animal carried on a lead (spec 43's buy route): the creature definition it becomes when the character takes it in. Null for
    /// everything else a pack can hold.
    /// </summary>
    public string? PetCreature { get; init; }

    /// <summary>
    /// The people whose heritage it is (<c>heritage</c>, owner 2026-10-24): only that people may use, buy, sell, trade or be given it.
    /// Null for everything else.
    /// </summary>
    public string? Heritage { get; init; }
}

/// <summary>Heritage items (owner, 2026-10-24): used, bought, sold, traded and given only between characters of the same people.</summary>
public static class HeritageRules
{
    /// <summary>Whether someone of this people (null: none known) may use or trade the item.</summary>
    public static bool Allows(ItemDefinition item, string? peopleId) => item.Heritage is null || item.Heritage == peopleId;

    /// <summary>Why someone of another people cannot have the item; null when they can.</summary>
    public static string? Refusal(ItemDefinition item, string? peopleId) =>
        Allows(item, peopleId) ? null : $"that is {PeopleWord(item.Heritage!)} heritage: only their own people use or trade it";

    /// <summary>A people's ID as a word: <c>people.veth</c> is "Veth".</summary>
    public static string PeopleWord(string peopleId)
    {
        string word = peopleId.StartsWith("people.", StringComparison.Ordinal) ? peopleId["people.".Length..] : peopleId;
        return word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];
    }
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

    /// <summary>Wear and a trader's repair (WP3b, D5).</summary>
    public DurabilityRules Durability { get; init; } = DurabilityRules.Default;

    /// <summary>The laden band (WP3b, D4).</summary>
    public LadenRules Laden { get; init; } = LadenRules.Default;

    /// <summary>Bags and the stash (owner request, 2026-10-18).</summary>
    public BagRules Bags { get; init; } = BagRules.Default;

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
public sealed record Pricing(double SellRatio, double QualityStep = 0.25, double MakerPremium = 0.1, double RecipeMargin = 0.9)
{
    /// <summary>Instance value before market and wear: quality and an authenticated maker mark, never the holder's level.</summary>
    public long Value(ItemDefinition item, int quality = 0, bool makerMarked = false) =>
        (long)Math.Floor(item.ValueBase * (1 + QualityStep * Math.Clamp(quality, -1, 2)) * (makerMarked ? 1 + MakerPremium : 1));

    public long BuyPrice(ItemDefinition item, MerchantStock stock) => (long)Math.Ceiling(item.ValueBase * stock.PriceBias);

    /// <summary>What a merchant pays, or null when it will not buy this item.</summary>
    public long? SellPrice(ItemDefinition item, Merchant merchant) =>
        item.NoSell || !merchant.BuysCategories.Contains(item.Category) ? null : (long)Math.Floor(item.ValueBase * SellRatio);
}
