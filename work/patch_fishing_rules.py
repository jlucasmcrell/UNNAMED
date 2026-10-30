"""Fishing rules: what a rod and bait are worth, and which table a water's fish come from by the hour (D20 follow-on).

The merged fishing loop casts, waits for a bite and reels against one catch table per water. This adds the three things the canon and the
owner ask for on top of it: a rod is needed to cast at all, a bait shortens the wait and sweetens the table, and the table a water uses
depends on what kind of water it is and what hour of the day it is - so a pond at night gives eels and a creek at dawn gives trout.
"""
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')

# ── rules type and loader ───────────────────────────────────────────────────────────────────────────────────────────────────────
content = ROOT / 'src' / 'Content' / 'FishingContent.cs'
content.write_text('''// UNNAMED Content - how fishing is equipped and scheduled (config.fishing; the D20 fishing follow-on)
// No Godot references - content only

using System;
using System.Collections.Immutable;
using UNNAMED.Domain;

namespace UNNAMED.Content;

/// <summary>When a band of the day starts and ends, in world-clock hours.</summary>
public sealed record FishingBand(string Id, double FromHour, double ToHour)
{
    /// <summary>True when <paramref name="hour"/> falls in this band, wrapping midnight when the band does.</summary>
    public bool Holds(double hour) => FromHour <= ToHour
        ? hour >= FromHour && hour < ToHour
        : hour >= FromHour || hour < ToHour;
}

/// <summary>A bait: which item it is, how far it shortens the wait, and what it does to the table.</summary>
public sealed record Bait(string ItemId, double BiteScale, int WeightBonus);

/// <summary>
/// The rules a cast obeys: the rod a hand must carry, the baits a pack may offer, the day's bands, and the catch table each kind of water
/// uses in each band. Everything is authored; a pack without the config keeps the merged behaviour (one table per water, no rod, no bait).
/// </summary>
public sealed record FishingRules(
    string? RodItemId,
    ImmutableSortedDictionary<string, Bait> Baits,
    ImmutableArray<FishingBand> Bands,
    ImmutableSortedDictionary<string, ImmutableSortedDictionary<string, string>> Tables)
{
    /// <summary>A pack that says nothing about fishing: no rod, no bait, no schedule - the water's own table is the only one.</summary>
    public static FishingRules Default { get; } = new(
        null,
        ImmutableSortedDictionary.Create<string, Bait>(StringComparer.Ordinal),
        ImmutableArray<FishingBand>.Empty,
        ImmutableSortedDictionary.Create<string, ImmutableSortedDictionary<string, string>>(StringComparer.Ordinal));

    /// <summary>The band <paramref name="hour"/> falls in, or null when the schedule does not name one.</summary>
    public FishingBand? BandAt(double hour)
    {
        foreach (var band in Bands)
            if (band.Holds(hour))
                return band;
        return null;
    }

    /// <summary>The table a cast into <paramref name="waterKind"/> at <paramref name="hour"/> rolls on, or null to use the water's own.</summary>
    public string? TableFor(string waterKind, double hour)
    {
        if (!Tables.TryGetValue(waterKind, out var byBand))
            return null;
        if (BandAt(hour) is not { } band)
            return null;
        return byBand.TryGetValue(band.Id, out string? table) ? table : null;
    }
}
''', encoding='utf-8')

# ── the builder, beside the weather's own ──────────────────────────────────────────────────────────────────────────────────────────
world = ROOT / 'src' / 'Content' / 'WorldContent.cs'
code = world.read_text(encoding='utf-8')
anchor = '        TryBuild(() => BuildWeather(loader), "config.weather", loader, errors);'
assert anchor in code
code = code.replace(anchor, anchor + '\n        TryBuild(() => BuildFishing(loader), "config.fishing", loader, errors);', 1)

member_anchor = '    public static WeatherRules BuildWeather(ContentLoader loader)'
addition = '''    /// <summary>
    /// Fishing's rules (<c>config.fishing</c>; the D20 follow-on): the rod, the baits, the day's bands and the table each kind of water
    /// uses in each band. Every id it names must exist, and a band must not overlap another, or a cast's hour is ambiguous.
    /// </summary>
    public static FishingRules BuildFishing(ContentLoader loader)
    {
        if (!loader.Definitions.TryGetValue("config.fishing", out var definition))
            return FishingRules.Default;
        var map = definition;
        var baits = ImmutableSortedDictionary.CreateBuilder<string, Bait>(StringComparer.Ordinal);
        if (Rows(map, "baits") is { } baitRows)
            foreach (var row in baitRows)
            {
                string item = Text(row, "item");
                Require(items.ContainsKey(item), $"config.fishing: bait '{item}' is not an item");
                baits[item] = new Bait(item, Num(row, "bite_scale", 1), (int)Num(row, "weight_bonus", 0));
            }
        var bands = ImmutableArray.CreateBuilder<FishingBand>();
        if (Rows(map, "bands") is { } bandRows)
            foreach (var row in bandRows)
                bands.Add(new FishingBand(Text(row, "id"), Num(row, "from_h", 0), Num(row, "to_h", 24)));
        foreach (var band in bands)
            foreach (var other in bands)
                if (!ReferenceEquals(band, other) && band.Holds(other.FromHour))
                    throw new FormatException($"config.fishing: band '{band.Id}' overlaps '{other.Id}'");
        var tables = ImmutableSortedDictionary.CreateBuilder<string, ImmutableSortedDictionary<string, string>>(StringComparer.Ordinal);
        if (Rows(map, "tables") is { } tableRows)
            foreach (var row in tableRows)
            {
                string kind = Text(row, "water");
                var byBand = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
                foreach (var (band, value) in row)
                {
                    if (band is "water" || value is not string table)
                        continue;
                    Require(loader.GetByKind("loot").ContainsKey(table), $"config.fishing: {kind}/{band} names catch table '{table}', which is not defined");
                    Require(bands.Any(b => b.Id == band), $"config.fishing: {kind} names band '{band}', which is not defined");
                    byBand[band] = table;
                }
                tables[kind] = byBand.ToImmutable();
            }
        return new FishingRules(
            map.GetValueOrDefault("rod") as string,
            baits.ToImmutable(),
            bands.ToImmutable(),
            tables.ToImmutable());
    }

''' + member_anchor
assert member_anchor in code
code = code.replace(member_anchor, addition, 1)
world.write_text(code, encoding='utf-8')
print('fishing rules loader written')
