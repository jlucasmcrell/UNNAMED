// UNNAMED Content - items, loot tables, merchants and carrying rules from content
// (DATA_MODEL.md §4.1-§4.3, §4.14, §4.15, §4.19; PROTOTYPE.md §4.2; M3b)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using YamlDotNet.Serialization;

namespace UNNAMED.Content;

/// <summary>
/// Builds the domain's item catalog, loot tables, merchants, <see cref="InventoryRules"/>, <see cref="Pricing"/> and the
/// starting kit, and lints what the reference and semantic passes cannot see (ITM codes): loot-table shape and
/// recursion, stock lines, the starting kit, and requirement keys.
/// </summary>
public static class ItemContent
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder().Build();
    private static readonly string[] ItemKinds = { "item", "item.weapon", "item.armor" };

    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        Try(() => BuildCatalog(loader), "items", errors);
        Try(() => CheckLootRecursion(BuildLootTables(loader)), "loot", errors);
        Try(() => BuildMerchants(loader), "merchants", errors);
        Try(() => CheckCraftOnly(BuildCatalog(loader), BuildLootTables(loader), BuildMerchants(loader)), "masterwork", errors);
        Try(() => CheckRepairStations(BuildCatalog(loader), loader), "repair", errors);
        if (loader.Definitions.ContainsKey("config.inventory"))
        {
            Try(() => BuildStartingKit(loader), "config.inventory", errors);
            Try(() => BuildInventoryRules(loader, 0), "config.inventory", errors);
        }
        if (loader.Definitions.ContainsKey("config.economy"))
            Try(() => BuildPricing(loader), "config.economy", errors);
        return errors;
    }

    public static ItemCatalog BuildCatalog(ContentLoader loader) =>
        new(ItemKinds.SelectMany(kind => loader.GetByKind(kind).Values).OrderBy(d => d.Id, StringComparer.Ordinal).Select(Item));

    public static ImmutableSortedDictionary<string, LootTable> BuildLootTables(ContentLoader loader) =>
        loader.GetByKind("loot").Values.Select(Loot).ToImmutableSortedDictionary(t => t.Id, t => t, StringComparer.Ordinal);

    public static ImmutableSortedDictionary<string, Merchant> BuildMerchants(ContentLoader loader) =>
        loader.GetByKind("merchant").Values.Select(MerchantOf).ToImmutableSortedDictionary(m => m.Id, m => m, StringComparer.Ordinal);

    public static InventoryRules BuildInventoryRules(ContentLoader loader, long reachMm)
    {
        var map = Config(loader, "config.inventory");
        var rules = new InventoryRules(Int(map, "stack_slots"), Grams(map, "carry_base_kg"), Grams(map, "carry_kg_per_might"), reachMm);
        if (map.ContainsKey("familiarity_uses_per_step"))
            rules = rules with { FamiliarityUsesPerStep = List(map, "familiarity_uses_per_step").Select((v, i) => ParseInt(v, $"familiarity_uses_per_step[{i}]")).ToImmutableArray() };
        if (map.GetValueOrDefault("durability") is Dictionary<object, object> durability)
        {
            rules = rules with
            {
                Durability = new DurabilityRules(Int(durability, "wear_per_hit"), Int(durability, "wear_per_hit_taken"), Int(durability, "wear_per_gather"),
                    Int(durability, "merchant_fee_percent"), Domain.Crafting.Quality.Parse(Text(durability, "merchant_quality_cap")))
                {
                    // How long a lit light burns for one point of its fuel (note 13); the records before this key keep the default.
                    LightWearTicks = durability.ContainsKey("light_wear_ticks") ? Int(durability, "light_wear_ticks") : new DurabilityRules(0, 0, 0, 0, 0).LightWearTicks,
                },
            };
            var d = rules.Durability;
            if (d.WearPerHit < 0 || d.WearPerHitTaken < 0 || d.WearPerGather < 0 || d.MerchantFeePercent < 0 || d.MerchantQualityCap == Domain.Crafting.Quality.Masterwork)
                throw new FormatException("config.inventory durability: wear and the fee are not below 0, and no trader's repair gives masterwork");
            if (d.LightWearTicks < 1)
                throw new FormatException("config.inventory durability: light_wear_ticks is at least 1, or a lit light would never burn down");
        }
        if (map.GetValueOrDefault("laden") is Dictionary<object, object> laden)
        {
            rules = rules with { Laden = new LadenRules(Int(laden, "band_percent"), Int(laden, "sprint_drain_percent"), Int(laden, "dodge_cost_percent")) };
            if (rules.Laden.BandPercent is < 1 or > 99 || rules.Laden.SprintDrainPercent < 100 || rules.Laden.DodgeCostPercent < 100)
                throw new FormatException("config.inventory laden: band_percent in [1, 99], and the sprint drain and dodge cost at least 100 (percent of usual)");
        }
        if (map.GetValueOrDefault("bags") is Dictionary<object, object> bags)
        {
            rules = rules with { Bags = new BagRules(Int(bags, "slots_per_quality_step"), Grams(bags, "stash_pull_radius_m")) };
            if (rules.Bags.SlotsPerQualityStep is < 0 or > 4 || rules.Bags.StashPullRadiusMm < 0)
                throw new FormatException("config.inventory bags: slots_per_quality_step in [0, 4] and a stash_pull_radius_m of at least 0");
        }
        if (rules.StackSlots < 1 || rules.CarryBaseGrams < 0 || rules.CarryGramsPerMight < 0)
            throw new FormatException("config.inventory needs at least one stack slot and non-negative carry weights");
        if (rules.FamiliarityUsesPerStep.Length != NotableRecord.MaxFamiliarity || rules.FamiliarityUsesPerStep.Any(n => n < 1))
            throw new FormatException($"config.inventory familiarity_uses_per_step lists {NotableRecord.MaxFamiliarity} counts of at least 1, one per step");
        return rules;
    }

    public static ImmutableArray<StartingItem> BuildStartingKit(ContentLoader loader)
    {
        var map = Config(loader, "config.inventory");
        var catalog = BuildCatalog(loader);
        var kit = List(map, "starting_items").Select((row, i) =>
        {
            var line = row as Dictionary<object, object> ?? throw new FormatException($"starting_items[{i}] must be a map");
            var item = new StartingItem(Text(line, "item_ref"), Int(line, "count"), line.GetValueOrDefault("equip") as string == "true");
            var definition = catalog.Find(item.ItemId) ?? throw new FormatException($"starting_items[{i}] names {item.ItemId}, which is not an item");
            if (item.Count < 1 || item.Count > definition.StackMax)
                throw new FormatException($"starting_items[{i}]: {item.Count} of {item.ItemId} is not one stack (1..{definition.StackMax})");
            if (item.Equip && definition.Slot is null)
                throw new FormatException($"starting_items[{i}]: {item.ItemId} cannot be equipped");
            return item;
        }).ToImmutableArray();
        return kit;
    }

    public static Pricing BuildPricing(ContentLoader loader)
    {
        var map = Config(loader, "config.economy");
        double Setting(string key, double fallback) => map.ContainsKey(key) ? Number(map, key) : fallback;
        var pricing = new Pricing(Number(map, "sell_ratio"), Setting("quality_value_step", 0.25),
            Setting("maker_mark_premium", 0.1), Setting("recipe_vendor_margin", 0.9));
        if (pricing.SellRatio is < 0 or > 1 || pricing.QualityStep is < 0 or >= 1
            || pricing.MakerPremium is < 0 or > 1 || pricing.RecipeMargin is <= 0 or >= 1)
            throw new FormatException("config.economy needs sell_ratio [0,1], quality_value_step [0,1), maker_mark_premium [0,1], recipe_vendor_margin (0,1)");
        return pricing;
    }

    private static ItemDefinition Item(ContentEnvelope definition)
    {
        var map = Read(definition.YamlSource);
        string id = definition.Id;
        WeaponStats? weapon = null;
        EquipSlot? slot = null;
        if (definition.Kind == "item.weapon")
        {
            var damage = List(map, "damage");
            string hands = Text(map, "hands");
            weapon = new WeaponStats(IntOf(damage, 0, "damage"), IntOf(damage, 1, "damage"), Text(map, "damage_type"),
                map.ContainsKey("reach") ? Grams(map, "reach") : 0, hands == "two",
                map.GetValueOrDefault("skill_ref") as string, map.GetValueOrDefault("ammo_item_ref") as string)
            {
                AttackMs = map.ContainsKey("attack_speed") ? (int)Math.Round(1000 / Number(map, "attack_speed"), MidpointRounding.AwayFromZero) : 0,
                DrawMs = map.ContainsKey("draw_time") ? (int)Grams(map, "draw_time") : 0,
                StaminaCost = map.ContainsKey("stamina_cost") ? Int(map, "stamina_cost") : null,
                SneakMultiplier = map.ContainsKey("sneak_multiplier") ? Number(map, "sneak_multiplier") : 1.0,
                SwingNoiseMm = map.ContainsKey("swing_noise_m") ? (long)Math.Round(Number(map, "swing_noise_m") * 1000, MidpointRounding.AwayFromZero) : 0,
                ShotNoiseMm = map.ContainsKey("shot_noise_m") ? (long)Math.Round(Number(map, "shot_noise_m") * 1000, MidpointRounding.AwayFromZero) : 0,
            };
            if (weapon.AttackMs <= 0 && weapon.DrawMs <= 0)
                throw new FormatException($"{id}: a weapon gives attack_speed (melee) or draw_time (ranged)");
            if (weapon.DrawMs <= 0 && weapon.ReachMm <= 0)
                throw new FormatException($"{id}: a melee weapon gives its reach");
            slot = hands == "offhand" ? EquipSlot.OffHand : EquipSlot.MainHand;
        }
        else if (definition.Kind == "item.armor")
        {
            slot = EquipSlots.Parse(Text(map, "slot"));
        }
        else if (map.GetValueOrDefault("equip_slot") is string equip)
        {
            slot = EquipSlots.Parse(equip);
            // The off hand takes a carried light (the torch) as well as a weapon; the main hand is a weapon's alone.
            if (slot is EquipSlot.MainHand)
                throw new FormatException($"{id}: equip_slot {equip} is for weapons; a plain item takes a worn slot or the off hand");
        }

        // The class is authored; rarity is derived from it, and a rarity written beside it must be that one (DCP:903).
        var itemClass = map.GetValueOrDefault("item_class") is string written ? ItemClasses.Parse(written) : ItemClass.Ordinary;
        string rarity = ItemClasses.Rarity(itemClass);
        if (map.GetValueOrDefault("rarity") is string authored && authored != rarity)
            throw new FormatException($"{id}: rarity {authored} does not match item_class {ItemClasses.Key(itemClass)}, whose rarity is {rarity} (rarity is derived from the class)");
        int stackMax = Int(map, "stack_max");
        if (itemClass != ItemClass.Ordinary && stackMax != 1)
            throw new FormatException($"{id}: a {ItemClasses.Key(itemClass)} item is one of a kind (stack_max 1): its record is the instance's");
        long light = map.ContainsKey("light_radius") ? Grams(map, "light_radius") : 0;
        double stealth = map.ContainsKey("stealth_penalty") ? Number(map, "stealth_penalty") : 0;
        if (light < 0 || stealth is < 0 or > 1 || ((light > 0 || stealth > 0) && slot is null))
            throw new FormatException($"{id}: light_radius (m, not below 0) and stealth_penalty ([0, 1]) belong to something equipped");
        // Durability (WP3b, D5): weapons, armour and tools only, one to a stack, since wear is the instance's.
        string category = Text(map, "category");
        int durability = map.ContainsKey("durability_max") ? Int(map, "durability_max") : 0;
        if (durability < 0 || (durability > 0 && (category is not ("weapon" or "armor" or "tool") || stackMax != 1)))
            throw new FormatException($"{id}: durability_max belongs to a weapon, armour or a tool that does not stack");
        RepairSpec? repair = null;
        if (map.GetValueOrDefault("repair") is Dictionary<object, object> repairMap)
        {
            repair = new RepairSpec(Text(repairMap, "station"), Text(repairMap, "skill_ref"), repairMap.ContainsKey("skill_min") ? Int(repairMap, "skill_min") : 0,
                Text(repairMap, "item_ref"));
            if (durability == 0 || repair.SkillMin < 0)
                throw new FormatException($"{id}: repair belongs to something with durability_max, and its skill_min is not below 0");
        }
        else if (durability > 0)
        {
            throw new FormatException($"{id}: something that wears gives its repair (station, skill_ref, item_ref): a worn-out piece is never lost for want of one");
        }
        // Bags and the kinds a specialised bag takes (owner request, 2026-10-18).
        BagSpec? bag = null;
        if (map.GetValueOrDefault("bag") is Dictionary<object, object> bagMap)
        {
            var accepts = bagMap.ContainsKey("accepts") ? List(bagMap, "accepts").Select(v => v.ToString()!).ToImmutableArray() : ImmutableArray<string>.Empty;
            bag = new BagSpec(Int(bagMap, "slots"), accepts);
            if (bag.Slots is < 1 or > 40 || category != "bag" || stackMax != 1 || accepts.Any(k => !PackKinds.All.Contains(k)) || accepts.Distinct().Count() != accepts.Length)
                throw new FormatException($"{id}: a bag is category bag, stack_max 1, with 1 to 40 slots and, if specialised, only {string.Join(", ", PackKinds.All)}");
        }
        else if (category == "bag")
        {
            throw new FormatException($"{id}: a bag item gives its bag (slots, and accepts for a specialised bag)");
        }
        string? packKind = map.GetValueOrDefault("pack_kind") as string;
        if (packKind is not null && !PackKinds.All.Contains(packKind))
            throw new FormatException($"{id}: pack_kind {packKind} is not one of {string.Join(", ", PackKinds.All)}");
        return new ItemDefinition(id, category, stackMax, Grams(map, "weight"), Long(map, "value_base"),
            rarity, Flag(map, "no_drop"), Flag(map, "no_sell"), slot, Requirements(id, map), weapon,
            map.ContainsKey("armor_value") ? Int(map, "armor_value") : 0)
        {
            Name = map.GetValueOrDefault("name") as string,
            Assembly = AssemblyOf(id, map),
            Class = itemClass,
            LightRadiusMm = light,
            StealthPenalty = stealth,
            DurabilityMax = durability,
            Repair = repair,
            Bag = bag,
            PackKind = packKind,
            PetCreature = map.GetValueOrDefault("pet_creature") as string,
            Heritage = map.GetValueOrDefault("heritage") as string,
            ThrowNoiseMm = map.ContainsKey("throw_noise_m") ? (long)Math.Round(Number(map, "throw_noise_m") * 1000, MidpointRounding.AwayFromZero) : 0,
            ThrowRangeMm = map.ContainsKey("throw_range_m") ? (long)Math.Round(Number(map, "throw_range_m") * 1000, MidpointRounding.AwayFromZero) : 0,
        };
    }

    /// <summary>
    /// A masterwork is craft-only (PROGRESSION.md §11.2: no drop may reproduce a masterwork-quality crafted base): no loot table holds a
    /// masterwork-class item and no trader stocks one.
    /// </summary>
    private static bool CheckCraftOnly(ItemCatalog catalog, ImmutableSortedDictionary<string, LootTable> tables, ImmutableSortedDictionary<string, Merchant> merchants)
    {
        bool Masterwork(string? item) => item is not null && catalog.Find(item)?.Class == ItemClass.Masterwork;
        foreach (var table in tables.Values)
        {
            if (table.Weighted.Concat(table.Independent).Concat(table.Guaranteed).FirstOrDefault(e => Masterwork(e.ItemId)) is { } entry)
                throw new FormatException($"{table.Id} drops {entry.ItemId}, a masterwork: masterwork is craft-only");
        }
        foreach (var merchant in merchants.Values)
        {
            if (merchant.Stock.FirstOrDefault(s => Masterwork(s.ItemId)) is { } line)
                throw new FormatException($"{merchant.Id} stocks {line.ItemId}, a masterwork: masterwork is craft-only");
        }
        return true;
    }

    /// <summary>A piece is repaired at a kind of station something is worked at (WP3b): a station kind no recipe names is one no world builds.</summary>
    private static bool CheckRepairStations(ItemCatalog catalog, ContentLoader loader)
    {
        var kinds = loader.GetByKind("recipe").Values.Select(r => Read(r.YamlSource).GetValueOrDefault("station") as string).OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        foreach (var item in catalog.Definitions.Values)
        {
            if (item.Repair is { } repair && !kinds.Contains(repair.StationKind))
                throw new FormatException($"{item.Id}: repair station {repair.StationKind} is no kind of station any recipe is worked at ({string.Join(", ", kinds.Order(StringComparer.Ordinal))})");
        }
        return true;
    }

    /// <summary><c>requirements: { attribute.might: 9, skill.one_hand_blade: 5 }</c> - never a level (PROGRESSION.md §11.1).</summary>
    private static Requirements Requirements(string id, Dictionary<object, object> map)
    {
        if (map.GetValueOrDefault("requirements") is not Dictionary<object, object> requirements)
            return Domain.Items.Requirements.None;
        var attributes = ImmutableSortedDictionary.CreateBuilder<CharacterAttribute, int>();
        var skills = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        foreach (var (key, value) in requirements)
        {
            string name = key as string ?? string.Empty;
            int minimum = ParseInt(value, $"{id} requirements.{name}");
            if (name.StartsWith("attribute.", StringComparison.Ordinal))
                attributes[ProgressionKeys.ParseAttribute(name["attribute.".Length..])] = minimum;
            else if (name.StartsWith("skill.", StringComparison.Ordinal))
                skills[name] = minimum;
            else
                throw new FormatException($"{id} requirements.{name}: only attribute.* and skill.* minima exist - never a level (PROGRESSION.md §11.1)");
        }
        return new Requirements(attributes.ToImmutable(), skills.ToImmutable());
    }

    private static LootTable Loot(ContentEnvelope definition)
    {
        var map = Read(definition.YamlSource);
        LootEntry Entry(object row, string where)
        {
            var e = row as Dictionary<object, object> ?? throw new FormatException($"{definition.Id} {where} must be a map");
            string? item = e.GetValueOrDefault("item_ref") as string, table = e.GetValueOrDefault("loot_ref") as string;
            if ((item is null) == (table is null))
                throw new FormatException($"{definition.Id} {where} names exactly one of item_ref or loot_ref");
            var range = e.ContainsKey("count_range") ? List(e, "count_range") : new List<object> { "1", "1" };
            var entry = new LootEntry(item, table, e.ContainsKey("weight") ? Int(e, "weight") : 0,
                e.ContainsKey("chance") ? Number(e, "chance") : 0, IntOf(range, 0, "count_range"), IntOf(range, 1, "count_range"));
            if (entry.CountMin < 0 || entry.CountMin > entry.CountMax)
                throw new FormatException($"{definition.Id} {where}: count_range must be [min, max] with 0 <= min <= max");
            return entry;
        }
        var entries = (map.ContainsKey("entries") ? List(map, "entries") : new List<object>()).Select((r, i) => Entry(r, $"entries[{i}]")).ToList();
        foreach (var (entry, i) in entries.Select((e, i) => (e, i)))
        {
            bool weighted = entry.Weight > 0, chanced = entry.Chance > 0;
            if (weighted == chanced || entry.Chance > 1)
                throw new FormatException($"{definition.Id} entries[{i}] has either a positive weight or a chance in (0, 1], never both");
        }
        var table = new LootTable(definition.Id, map.ContainsKey("rolls") ? Int(map, "rolls") : 1,
            entries.Where(e => e.Weight > 0).ToImmutableArray(), entries.Where(e => e.Chance > 0).ToImmutableArray(),
            (map.ContainsKey("guaranteed") ? List(map, "guaranteed") : new List<object>()).Select((r, i) => Entry(r, $"guaranteed[{i}]")).ToImmutableArray());
        if (table.Rolls < 0 || (table.Rolls > 0 && table.Weighted.IsEmpty))
            throw new FormatException($"{definition.Id}: rolls draw from weighted entries, so a table that rolls needs some");
        return table;
    }

    /// <summary>DATA_MODEL.md §4.14: nesting is legal, recursion (direct or transitive) is not.</summary>
    private static bool CheckLootRecursion(ImmutableSortedDictionary<string, LootTable> tables)
    {
        void Visit(string id, ImmutableStack<string> path)
        {
            if (path.Contains(id))
                throw new FormatException($"loot tables recurse: {string.Join(" -> ", path.Reverse().Append(id))}");
            if (!tables.TryGetValue(id, out var table))
                return;   // a dangling loot_ref is XREF003's to report
            foreach (var entry in table.Weighted.Concat(table.Independent).Concat(table.Guaranteed))
            {
                if (entry.TableId is { } nested)
                    Visit(nested, path.Push(id));
            }
        }
        foreach (string id in tables.Keys)
            Visit(id, ImmutableStack<string>.Empty);
        return true;
    }

    private static Merchant MerchantOf(ContentEnvelope definition)
    {
        var map = Read(definition.YamlSource);
        var stock = List(map, "stock").Select((row, i) =>
        {
            var line = row as Dictionary<object, object> ?? throw new FormatException($"{definition.Id} stock[{i}] must be a map");
            var entry = new MerchantStock(Text(line, "item_ref"), Int(line, "count"), line.ContainsKey("price_bias") ? Number(line, "price_bias") : 1.0);
            if (entry.Count < 1 || entry.PriceBias <= 0)
                throw new FormatException($"{definition.Id} stock[{i}] needs a positive count and price_bias");
            return entry;
        }).ToImmutableArray();
        var buys = (map.ContainsKey("buys_tags") ? List(map, "buys_tags") : new List<object>()).Select(t => t as string ?? "").ToImmutableArray();
        return new Merchant(definition.Id, stock, buys);
    }

    private static void Try(Func<object> build, string what, List<ValidationError> errors)
    {
        try
        {
            build();
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(new ValidationError { SeverityLevel = ValidationError.Severity.Error, Code = "ITM001", Message = $"{what}: {e.Message}" });
        }
    }

    private static Dictionary<object, object> Config(ContentLoader loader, string id) =>
        loader.Definitions.TryGetValue(id, out var definition)
            ? Read(definition.YamlSource)
            : throw new KeyNotFoundException($"{id} is missing (DATA_MODEL.md §4.19)");

    private static Dictionary<object, object> Read(string? yaml) =>
        string.IsNullOrEmpty(yaml) ? new Dictionary<object, object>() : Yaml.Deserialize<Dictionary<object, object>>(yaml) ?? new Dictionary<object, object>();

    /// <summary>
    /// The assembly block an item may carry (D21, GEAR_GENERATION.md §2.5): its family, the slots that family has (defaulting to all of them), and the
    /// maker allowed to build it. Absent means a plain authored item.
    /// </summary>
    private static AssemblySpec? AssemblyOf(string id, Dictionary<object, object> map)
    {
        if (map.GetValueOrDefault("assembly") is not Dictionary<object, object> assembly)
            return null;
        string family = Text(assembly, "family");
        if (!GearContent.FamilySlots.TryGetValue(family, out var known))
            throw new FormatException($"{id} assembly.family '{family}' is not one of {string.Join(", ", GearContent.FamilySlots.Keys)}");
        var slots = assembly.GetValueOrDefault("slots") is List<object> listed
            ? listed.Select((v, i) => v as string ?? throw new FormatException($"{id} assembly.slots[{i}] is a name")).ToImmutableArray()
            : known;
        string? maker = assembly.GetValueOrDefault("maker_ref") as string;
        int tier = assembly.ContainsKey("part_tier") ? Int(assembly, "part_tier") : 1;
        if (tier < 1)
            throw new FormatException($"{id} assembly.part_tier is 1 or more");
        return new AssemblySpec(family, slots, maker, tier);
    }

    private static List<object> List(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value) && value is List<object> list ? list : throw new KeyNotFoundException($"'{key}' must be a list");

    private static string Text(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value) && value is string text && text.Length > 0 ? text : throw new KeyNotFoundException($"'{key}' is missing");

    private static bool Flag(Dictionary<object, object> map, string key) => map.GetValueOrDefault(key) as string == "true";

    private static int Int(Dictionary<object, object> map, string key) => ParseInt(map.GetValueOrDefault(key), key);

    private static long Long(Dictionary<object, object> map, string key) =>
        long.TryParse(map.GetValueOrDefault(key) as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : throw new FormatException($"'{key}' must be a whole number");

    private static double Number(Dictionary<object, object> map, string key) =>
        double.TryParse(map.GetValueOrDefault(key) as string, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : throw new FormatException($"'{key}' must be a number");

    /// <summary>Kilograms (or metres) in content, grams (or millimetres) in the domain.</summary>
    private static long Grams(Dictionary<object, object> map, string key) => (long)Math.Round(Number(map, key) * 1000, MidpointRounding.AwayFromZero);

    private static int IntOf(List<object> cells, int i, string what) =>
        i < cells.Count ? ParseInt(cells[i], what) : throw new FormatException($"'{what}' needs {i + 1} values");

    private static int ParseInt(object? value, string what) =>
        int.TryParse(value as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : throw new FormatException($"'{what}' must be a whole number");
}
