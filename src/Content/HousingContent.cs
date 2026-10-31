// UNNAMED Content - homes from content: the plots a character may claim and build on (HSG-; GAPS_WORK_QUEUE #6)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Housing;
using YamlDotNet.Serialization;

namespace UNNAMED.Content;

/// <summary>
/// The homes the pack defines, by plot ID. A home is a plot: where it is, what the claim costs, what building it takes and how much floor it has.
/// Everything else about a home is the player's - the furniture they carry and set down, and the chest whose goods are the shared stash.
/// </summary>
public static class HousingContent
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder().Build();

    /// <summary>Every home the pack defines, by ID, in a fixed order.</summary>
    public static ImmutableSortedDictionary<string, HomeDefinition> Build(ContentLoader loader)
    {
        var homes = ImmutableSortedDictionary.CreateBuilder<string, HomeDefinition>(StringComparer.Ordinal);
        foreach (var (id, definition) in loader.Definitions.Where(d => d.Value.Kind == "home").OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            var map = Read(definition.YamlSource);
            string region = map.GetValueOrDefault("region") as string
                ?? throw new FormatException($"{id} names no region");
            if (!loader.Definitions.ContainsKey($"region.{region}"))
                throw new FormatException($"{id} stands in region '{region}', which is not a region");

            var at = map.GetValueOrDefault("at_m") as List<object>
                ?? throw new FormatException($"{id} names no at_m");
            if (at.Count != 2)
                throw new FormatException($"{id} names at_m as {at.Count} numbers, not a place");

            long price = Number(map, "price_coin");
            if (price < 0)
                throw new FormatException($"{id} costs {price} coin");

            var materials = ImmutableArray.CreateBuilder<HomeMaterial>();
            if (map.GetValueOrDefault("build_materials") is List<object> rows)
            {
                foreach (var row in rows)
                {
                    if (row is not Dictionary<object, object> line)
                        throw new FormatException($"{id}: a build material is a map");
                    string item = line.GetValueOrDefault("item_ref") as string
                        ?? throw new FormatException($"{id}: a build material names no item_ref");
                    if (!loader.Definitions.TryGetValue(item, out var itemDefinition) || itemDefinition.Kind != "item")
                        throw new FormatException($"{id} is built from '{item}', which is not an item");
                    int count = Number(line, "count");
                    if (count <= 0)
                        throw new FormatException($"{id}: '{item}' is built from a count of {count}");
                    materials.Add(new HomeMaterial(item, count));
                }
            }

            long radius = map.ContainsKey("radius_m") ? Metres(map, "radius_m") : 8_000;
            if (radius <= 0)
                throw new FormatException($"{id} has a plot {radius} mm across");

            homes[id] = new HomeDefinition(id, region, Metres(at[0], id, "at_m x"), Metres(at[1], id, "at_m z"), price,
                map.GetValueOrDefault("name") as string ?? id)
            {
                Materials = materials.ToImmutable(),
                RadiusMm = radius,
            };
        }
        return homes.ToImmutable();
    }

    /// <summary>The lint's own pass: every home stands in a region that has a layout, and no two plots stand on one patch of ground.</summary>
    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        ImmutableSortedDictionary<string, HomeDefinition> homes;
        try
        {
            homes = Build(loader);
        }
        catch (FormatException e)
        {
            errors.Add(NavigationContent.Error("HSG001", e.Message, null));
            return errors;
        }
        foreach (var (id, home) in homes)
        {
            bool laid = loader.Definitions.ContainsKey($"region.{home.RegionId}") || loader.Definitions.ContainsKey("region.starting_frontier");
            if (!laid)
                errors.Add(NavigationContent.Error("HSG001", $"{id} stands in '{home.RegionId}', which the pack cannot lay out", null));
            foreach (var other in homes.Values.Where(o => o.Id != id && o.RegionId == home.RegionId))
            {
                long dx = other.XMm - home.XMm, dz = other.ZMm - home.ZMm;
                long apart = dx * dx + dz * dz;
                if (apart < (home.RadiusMm + other.RadiusMm) * (home.RadiusMm + other.RadiusMm))
                    errors.Add(NavigationContent.Error("HSG002", $"{id} and {other.Id} stand on the same ground in {home.RegionId}", null));
            }
        }
        return errors;
    }

    private static Dictionary<object, object> Read(string? yaml) =>
        string.IsNullOrEmpty(yaml) ? new Dictionary<object, object>() : Yaml.Deserialize<Dictionary<object, object>>(yaml);

    private static long Metres(object value, string id, string what) => value switch
    {
        double d => (long)Math.Round(d * 1000),
        int i => i * 1000L,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => (long)Math.Round(parsed * 1000),
        _ => throw new FormatException($"{id} names {what} as {value}, which is not a distance"),
    };

    private static long Metres(Dictionary<object, object> map, string key) => Metres(map[key], key, key);

    private static int Number(Dictionary<object, object> map, string key) => map.GetValueOrDefault(key) switch
    {
        int i => i,
        long l => (int)l,
        double d => (int)d,
        string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) => parsed,
        var other => throw new FormatException($"'{key}' is {other ?? "missing"}, which is not a number"),
    };
}
