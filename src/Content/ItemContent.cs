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
        if (loader.Definitions.ContainsKey("config.inventory"))
            Try(() => BuildStartingKit(loader), "config.inventory", errors);
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
        if (rules.StackSlots < 1 || rules.CarryBaseGrams < 0 || rules.CarryGramsPerMight < 0)
            throw new FormatException("config.inventory needs at least one stack slot and non-negative carry weights");
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
        var pricing = new Pricing(Number(Config(loader, "config.economy"), "sell_ratio"));
        if (pricing.SellRatio is < 0 or > 1)
            throw new FormatException("config.economy sell_ratio must be in [0, 1]");
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
            if (slot is EquipSlot.MainHand or EquipSlot.OffHand)
                throw new FormatException($"{id}: equip_slot {equip} is for weapons; a plain item takes a worn slot");
        }
        return new ItemDefinition(id, Text(map, "category"), Int(map, "stack_max"), Grams(map, "weight"), Long(map, "value_base"),
            Text(map, "rarity"), Flag(map, "no_drop"), Flag(map, "no_sell"), slot, Requirements(id, map), weapon,
            map.ContainsKey("armor_value") ? Int(map, "armor_value") : 0);
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
