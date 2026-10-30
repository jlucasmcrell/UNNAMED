"""Wire the fishing rules into content loading, on WorldContent's own helpers (goal round 1)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Content\WorldContent.cs')
code = path.read_text(encoding='utf-8')

anchor = '        TryBuild(() => BuildWeather(loader), "config.weather", loader, errors);'
if 'BuildFishing(loader)' not in code:
    code = code.replace(anchor, anchor + '\n        TryBuild(() => BuildFishing(loader), "config.fishing", loader, errors);', 1)

member_anchor = '    public static WeatherRules BuildWeather(ContentLoader loader)'
builder = '''    /// <summary>
    /// Fishing's rules (<c>config.fishing</c>; the D20 follow-on): the rod a hand must carry, the baits and what each is worth, the bands of
    /// the world clock, and the catch table each kind of water rolls on in each band. Every id it names must exist and no two bands may
    /// overlap, or a cast's hour would name two tables.
    /// </summary>
    public static FishingRules BuildFishing(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey("config.fishing"))
            return FishingRules.Default;
        var map = Config(loader, "config.fishing");
        var items = loader.GetByKind("item");
        var loot = loader.GetByKind("loot");

        var baits = ImmutableSortedDictionary.CreateBuilder<string, Bait>(StringComparer.Ordinal);
        foreach (var entry in List(map, "baits"))
        {
            if (entry is not Dictionary<object, object> row)
                throw new FormatException("config.fishing: every bait is a map");
            string item = Text(row, "item");
            Check(items.ContainsKey(item), "CFH001", $"config.fishing: bait '{item}' is not an item");
            double scale = row.TryGetValue("bite_scale", out var rawScale) && rawScale is not null ? Convert.ToDouble(rawScale, CultureInfo.InvariantCulture) : 1;
            int bonus = row.TryGetValue("weight_bonus", out var rawBonus) && rawBonus is not null ? Convert.ToInt32(rawBonus, CultureInfo.InvariantCulture) : 0;
            baits[item] = new Bait(item, scale, bonus);
        }

        var bands = ImmutableArray.CreateBuilder<FishingBand>();
        foreach (var entry in List(map, "bands"))
        {
            if (entry is not Dictionary<object, object> row)
                throw new FormatException("config.fishing: every band is a map");
            bands.Add(new FishingBand(Text(row, "id"), Convert.ToDouble(row["from_h"], CultureInfo.InvariantCulture),
                Convert.ToDouble(row["to_h"], CultureInfo.InvariantCulture)));
        }
        var built = bands.ToImmutable();
        foreach (var band in built)
            foreach (var other in built)
                if (!ReferenceEquals(band, other) && band.Holds(other.FromHour))
                    throw new FormatException($"config.fishing: band '{band.Id}' overlaps '{other.Id}'");

        var tables = ImmutableSortedDictionary.CreateBuilder<string, ImmutableSortedDictionary<string, string>>(StringComparer.Ordinal);
        var tableMap = Map(map, "tables");
        foreach (var (key, value) in tableMap)
        {
            string kind = key as string ?? throw new FormatException("config.fishing: a water kind is named by its key");
            if (value is not Dictionary<object, object> byBand)
                throw new FormatException($"config.fishing: {kind} must be a map of band to catch table");
            var perKind = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (var (bandKey, tableValue) in byBand)
            {
                string band = bandKey as string ?? throw new FormatException($"config.fishing: {kind} names a band by its key");
                string table = tableValue as string ?? throw new FormatException($"config.fishing: {kind}/{band} must name a catch table");
                Check(loot.ContainsKey(table), "CFH002", $"config.fishing: {kind}/{band} names catch table '{table}', which is not defined");
                if (!built.Any(b => b.Id == band))
                    throw new FormatException($"config.fishing: {kind} names band '{band}', which is not defined");
                perKind[band] = table;
            }
            tables[kind] = perKind.ToImmutable();
        }

        string? rod = map.TryGetValue("rod", out var rawRod) ? rawRod as string : null;
        if (rod is not null)
            Check(items.ContainsKey(rod), "CFH003", $"config.fishing: rod '{rod}' is not an item");
        return new FishingRules(rod, baits.ToImmutable(), built, tables.ToImmutable());
    }

''' + member_anchor
assert member_anchor in code
if 'public static FishingRules BuildFishing' not in code:
    code = code.replace(member_anchor, builder, 1)

path.write_text(code, encoding='utf-8')
print('fishing rules wired into content building')
