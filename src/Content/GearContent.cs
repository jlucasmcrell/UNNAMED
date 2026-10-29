using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Items;
using YamlDotNet.Serialization;

namespace UNNAMED.Content;

/// <summary>
/// D21's content: part, maker and material definitions (canon <c>GEAR_GENERATION.md</c> §2.1-§2.4), loaded and validated so a typo is a load error
/// rather than a gear piece nobody can build. A part's slot must belong to its family, a maker's parts must exist and fit the family they are listed
/// under, and a material's reference item must be a real item.
/// </summary>
public static class GearContent
{
    /// <summary>Every family's slots, as canon §4 gives them. A part names one of these, and an item's assembly block names them all.</summary>
    public static readonly ImmutableSortedDictionary<string, ImmutableArray<string>> FamilySlots = new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal)
    {
        ["sword"] = ImmutableArray.Create("blade", "guard", "grip", "pommel"),
        ["greatsword"] = ImmutableArray.Create("blade", "guard", "grip", "pommel"),
        ["axe"] = ImmutableArray.Create("head", "haft", "binding"),
        ["mace"] = ImmutableArray.Create("head", "haft", "binding"),
        ["spear"] = ImmutableArray.Create("head", "haft", "binding"),
        ["dagger"] = ImmutableArray.Create("blade", "guard", "grip"),
        ["bow"] = ImmutableArray.Create("limbs", "grip", "string"),
        ["crossbow"] = ImmutableArray.Create("limbs", "stock", "mechanism"),
        ["staff"] = ImmutableArray.Create("shaft", "cap", "binding"),
        ["shield"] = ImmutableArray.Create("face", "rim", "boss"),
        ["armour.chest"] = ImmutableArray.Create("shell", "straps", "lining"),
        ["armour.head"] = ImmutableArray.Create("shell", "lining", "visor"),
        // High tech (GEAR_GENERATION 4.1.1): one slot shape for a projector, eight families of emitter in it.
        ["projector.fire"] = ImmutableArray.Create("emitter", "cell", "frame", "optic"),
        ["projector.ice"] = ImmutableArray.Create("emitter", "cell", "frame", "optic"),
        ["projector.air"] = ImmutableArray.Create("emitter", "cell", "frame", "optic"),
        ["projector.water"] = ImmutableArray.Create("emitter", "cell", "frame", "optic"),
        ["projector.laser"] = ImmutableArray.Create("emitter", "cell", "frame", "optic"),
        ["projector.tractor"] = ImmutableArray.Create("emitter", "cell", "frame", "optic"),
        ["projector.cosmic"] = ImmutableArray.Create("emitter", "cell", "frame", "optic"),
        ["projector.reality"] = ImmutableArray.Create("emitter", "cell", "frame", "optic"),
        // Precision (4.2): the four slots plus the stabiliser, and the optic is mandatory.
        ["marksman"] = ImmutableArray.Create("emitter", "cell", "frame", "optic", "stabiliser"),
        // Non-lethal stealth devices (4.3.1).
        ["dart_launcher"] = ImmutableArray.Create("emitter", "cell", "frame"),
        ["stunner"] = ImmutableArray.Create("emitter", "cell", "frame"),
        ["blowpipe"] = ImmutableArray.Create("tube", "mouthpiece"),
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    /// <summary>The eras canon allows a maker to belong to (GEAR_GENERATION §2.3, WORLD_ATLAS §4 flavours).</summary>
    public static readonly ImmutableArray<string> Eras =
        ImmutableArray.Create("medieval", "old_works", "industrial", "advanced", "other", "cosmic");

    public static ImmutableSortedDictionary<string, PartDefinition> BuildParts(ContentLoader loader)
    {
        var parts = loader.GetByKind("part").Values.Select(definition => Part(loader, definition))
            .ToImmutableSortedDictionary(p => p.Id, p => p, StringComparer.Ordinal);
        return parts;
    }

    public static ImmutableSortedDictionary<string, MakerDefinition> BuildMakers(ContentLoader loader)
    {
        var parts = BuildParts(loader);
        return loader.GetByKind("maker").Values.Select(definition => Maker(loader, definition, parts))
            .ToImmutableSortedDictionary(m => m.Id, m => m, StringComparer.Ordinal);
    }

    public static ImmutableSortedDictionary<string, MaterialDefinition> BuildMaterials(ContentLoader loader) =>
        loader.GetByKind("material").Values.Select(definition => Material(loader, definition))
            .ToImmutableSortedDictionary(m => m.Id, m => m, StringComparer.Ordinal);

    private static PartDefinition Part(ContentLoader loader, UNNAMED.Content.ContentEnvelope definition)
    {
        string id = definition.Id;
        var map = Read(definition.YamlSource);
        string family = Text(map, "family");
        if (!FamilySlots.TryGetValue(family, out var slots))
            throw new FormatException($"{id}: family '{family}' is not one of {string.Join(", ", FamilySlots.Keys)}");
        string slot = Text(map, "slot");
        if (!slots.Contains(slot, StringComparer.Ordinal))
            throw new FormatException($"{id}: '{slot}' is not a {family} slot ({string.Join(", ", slots)})");

        var materialSlots = (map.ContainsKey("material_slots") ? List(map, "material_slots") : new List<object>())
            .Select((s, i) => s as string ?? throw new FormatException($"{id} material_slots[{i}] is a name")).ToImmutableArray();
        if (materialSlots.IsEmpty)
            throw new FormatException($"{id}: a part exposes at least one material slot");

        var stats = Map(map, "stat_contrib");
        var part = new PartDefinition(
            id, family, slot, Text(map, "mesh"), materialSlots, Int(map, "tier_min"),
            new PartStats(
                stats.ContainsKey("damage_mult") ? Number(stats, "damage_mult") : 1.0,
                stats.ContainsKey("reach_add") ? (long)Math.Round(Number(stats, "reach_add") * 1000, MidpointRounding.AwayFromZero) : 0,
                stats.ContainsKey("weight_add") ? (long)Math.Round(Number(stats, "weight_add") * 1000, MidpointRounding.AwayFromZero) : 0,
                stats.ContainsKey("speed_mult") ? Number(stats, "speed_mult") : 1.0,
                stats.ContainsKey("durability_add") ? Int(stats, "durability_add") : 0),
            Texts(map, "tags"));

        if (map.GetValueOrDefault("noun") is string noun)
            part = part with { Noun = noun };

        if (part.TierMin < 1)
            throw new FormatException($"{id}: tier_min is 1 or more");
        if (part.Stats.DamageMult <= 0 || part.Stats.SpeedMult <= 0)
            throw new FormatException($"{id}: damage_mult and speed_mult are positive");

        if (map.GetValueOrDefault("compat") is not Dictionary<object, object> compat)
            return part;
        if (compat.GetValueOrDefault("requires_tags_on") is not Dictionary<object, object> requires)
            return part;
        return part with
        {
            RequiresTagsOn = requires.ToImmutableSortedDictionary(
                kv => kv.Key as string ?? throw new FormatException($"{id} compat names a slot"),
                kv => kv.Value is List<object> tags
                    ? tags.Select((t, i) => t as string ?? throw new FormatException($"{id} compat.{kv.Key}[{i}] is a tag")).ToImmutableArray()
                    : throw new FormatException($"{id} compat.{kv.Key} is a list of tags"),
                StringComparer.Ordinal),
        };
    }

    private static MakerDefinition Maker(ContentLoader loader, UNNAMED.Content.ContentEnvelope definition,
        ImmutableSortedDictionary<string, PartDefinition> parts)
    {
        string id = definition.Id;
        var map = Read(definition.YamlSource);
        string era = Text(map, "era");
        if (!Eras.Contains(era, StringComparer.Ordinal))
            throw new FormatException($"{id}: era '{era}' is not one of {string.Join(", ", Eras)}");

        var sets = Map(map, "part_sets").ToImmutableSortedDictionary(
            kv => kv.Key as string ?? throw new FormatException($"{id} part_sets names a family"),
            kv =>
            {
                string family = kv.Key as string ?? "?";
                if (!FamilySlots.ContainsKey(family))
                    throw new FormatException($"{id}: part_sets names family '{family}', which has no slots");
                var slots = kv.Value as Dictionary<object, object> ?? throw new FormatException($"{id} part_sets.{family} is a map");
                return slots.ToImmutableSortedDictionary(
                    s => s.Key as string ?? throw new FormatException($"{id} part_sets.{family} names a slot"),
                    s =>
                    {
                        var rows = s.Value as List<object> ?? throw new FormatException($"{id} part_sets.{family}.{s.Key} is a list");
                        return rows.Select((row, i) =>
                        {
                            var cells = row as Dictionary<object, object> ?? throw new FormatException($"{id} {family}.{s.Key}[{i}] is a map");
                            string partId = Text(cells, "part_ref");
                            if (!parts.TryGetValue(partId, out var part))
                                throw new FormatException($"{id}: {family}.{s.Key} names {partId}, which is not a part");
                            if (part.Family != family || part.Slot != (s.Key as string ?? ""))
                                throw new FormatException($"{id}: {partId} is a {part.Family} {part.Slot}, listed under {family}.{s.Key}");
                            return new WeightedPart(partId, Int(cells, "weight"));
                        }).ToImmutableArray();
                    }, StringComparer.Ordinal);
            }, StringComparer.Ordinal);

        var palette = List(map, "material_palette").Select((row, i) =>
        {
            var cells = row as Dictionary<object, object> ?? throw new FormatException($"{id} material_palette[{i}] is a map");
            return new WeightedMaterial(Text(cells, "material_ref"), Int(cells, "weight"));
        }).ToImmutableArray();

        var signature = Map(map, "signature");
        string tendency = Text(signature, "tendency");
        if (!MakerSignature.Tendencies.Contains(tendency, StringComparer.Ordinal))
            throw new FormatException($"{id}: tendency '{tendency}' is not one of {string.Join(", ", MakerSignature.Tendencies)}");
        var bias = signature.GetValueOrDefault("stat_bias") as Dictionary<object, object> ?? new Dictionary<object, object>();
        var tier = List(map, "tier_range").Select(v => ParseInt(v, $"{id} tier_range")).ToImmutableArray();
        if (tier.Length != 2 || tier[0] < 1 || tier[1] < tier[0])
            throw new FormatException($"{id}: tier_range is [low, high] with 1 <= low <= high");

        return new MakerDefinition(
            id, map.GetValueOrDefault("people_ref") is string people ? Defined(loader, people, "people", id) : null, era, Text(map, "fit_family"), sets, palette,
            new MakerSignature(tendency, new MakerStats(
                bias.ContainsKey("weight_mult") ? Number(bias, "weight_mult") : 1.0,
                bias.ContainsKey("durability_mult") ? Number(bias, "durability_mult") : 1.0,
                bias.ContainsKey("speed_mult") ? Number(bias, "speed_mult") : 1.0,
                bias.ContainsKey("damage_mult") ? Number(bias, "damage_mult") : 1.0)),
            tier, Text(map, "name_grammar"), Text(map, "tooltip_line_key"))
        {
            SourceNotes = Texts(map, "source_notes"),
            AffixPoolTags = Texts(map, "affix_pool_tags"),
            Words = Texts(map, "maker_words"),
        };
    }

    private static MaterialDefinition Material(ContentLoader loader, UNNAMED.Content.ContentEnvelope definition)
    {
        string id = definition.Id;
        var map = Read(definition.YamlSource);
        string materialClass = Text(map, "class");
        if (!MaterialDefinition.Classes.Contains(materialClass, StringComparer.Ordinal))
            throw new FormatException($"{id}: class '{materialClass}' is not one of {string.Join(", ", MaterialDefinition.Classes)}");
        var mult = Map(map, "mult");
        var material = new MaterialDefinition(
            id, materialClass,
            Defined(loader, Text(map, "material_ref_item"), "item", id),
            new MaterialMultipliers(
                mult.ContainsKey("damage") ? Number(mult, "damage") : 1.0,
                mult.ContainsKey("armor") ? Number(mult, "armor") : 1.0,
                mult.ContainsKey("weight") ? Number(mult, "weight") : 1.0,
                mult.ContainsKey("durability") ? Number(mult, "durability") : 1.0,
                mult.ContainsKey("value") ? Number(mult, "value") : 1.0),
            Int(map, "tier_min"));
        if (map.GetValueOrDefault("word") is string word)
            material = material with { Word = word };
        if (material.TierMin < 1)
            throw new FormatException($"{id}: tier_min is 1 or more");
        return material;
    }

    private static Dictionary<object, object> Map(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value) && value is Dictionary<object, object> nested
            ? nested
            : throw new FormatException($"'{key}' is a map");

    private static List<object> List(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value) && value is List<object> list ? list : throw new FormatException($"'{key}' is a list");

    private static ImmutableArray<string> Texts(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value) && value is List<object> list
            ? list.Select((v, i) => v as string ?? throw new FormatException($"'{key}'[{i}] is a string")).ToImmutableArray()
            : ImmutableArray<string>.Empty;

    private static string Text(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value) && value is string text && text.Length > 0
            ? text
            : throw new FormatException($"'{key}' is missing");

    private static int Int(Dictionary<object, object> map, string key) => ParseInt(map.GetValueOrDefault(key), key);

    private static double Number(Dictionary<object, object> map, string key) =>
        double.TryParse(map.GetValueOrDefault(key) as string, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : throw new FormatException($"'{key}' must be a number");

    private static int ParseInt(object? value, string what) =>
        int.TryParse(value as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : throw new FormatException($"'{what}' must be a whole number");

    private static string Defined(ContentLoader loader, string id, string kind, string at) =>
        loader.GetByKind(kind).ContainsKey(id) ? id : throw new FormatException($"{at}: '{id}' is not a {kind}");

    private static Dictionary<object, object> Read(string yaml) =>
        new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(yaml)
        ?? throw new FormatException("a definition must be a map");

    /// <summary>The three tables the craft path needs, from one load; a load with no gear content is empty, not an error.</summary>
    public static GearSetup Build(ContentLoader loader) => new(BuildParts(loader), BuildMakers(loader), BuildMaterials(loader));
}
