using System.Collections.Immutable;
using UNNAMED.Domain.Character;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

public static class WardrobeContent
{
    public static WardrobeSetup Build(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey("config.wardrobe")) return WardrobeSetup.Empty;
        var map = Config(loader, "config.wardrobe");
        ImmutableArray<WardrobeChoice> Choices(string key, string prefix) => Rows(map, key, "config.wardrobe").Select(row =>
        {
            string id = Text(row, "id"), bodies = Text(row, "bodies");
            if (!UNNAMED.Domain.DefinitionId.IsValid(id) || !id.StartsWith(prefix + ".", StringComparison.Ordinal)
                || bodies is not ("m" or "f" or "mf")) throw new FormatException($"invalid wardrobe choice {id}");
            int regions = row.ContainsKey("dye_regions") ? Int(row, "dye_regions") : 0;
            if (regions is < 0 or > 3) throw new FormatException($"invalid cloth regions {id}");
            return new WardrobeChoice(id, Text(row, "name"), bodies, regions);
        }).ToImmutableArray();
        var dyes = Rows(map, "dyes", "config.wardrobe").Select(row =>
        {
            string id = Text(row, "item_ref");
            if (!loader.Definitions.TryGetValue(id, out var definition) || definition.Kind != "item") throw new FormatException($"undefined cloth dye {id}");
            int shirt = Int(row, "shirt"), trousers = Int(row, "trousers");
            if (shirt is < 0 or > 4 || trousers is < 0 or > 5) throw new FormatException($"invalid dye palette {id}");
            return new ClothDye(id, shirt, trousers);
        }).ToImmutableArray();
        var statures = Map(map, "default_statures").ToImmutableSortedDictionary(k => (string)k.Key,
            k => int.Parse(k.Value.ToString()!, System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal);
        if (statures.Values.Any(n => n is < CharacterAppearance.MinStatureMm or > CharacterAppearance.MaxStatureMm)) throw new FormatException("invalid wardrobe stature");
        var outfits = Choices("outfits", "outfit"); var feet = Choices("footwear", "footwear");
        if (outfits.Select(c => c.Id).Distinct().Count() != outfits.Length || feet.Select(c => c.Id).Distinct().Count() != feet.Length
            || dyes.Select(c => c.ItemId).Distinct().Count() != dyes.Length) throw new FormatException("duplicate wardrobe choice");
        return new(outfits, feet, dyes, statures);
    }
}
