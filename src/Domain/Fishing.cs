// UNNAMED Domain - how fishing is equipped and scheduled: the rod, the baits, the day's bands and the water's table (D20 follow-on)
// No Godot references - this is pure C# domain logic

using System;
using System.Collections.Immutable;

namespace UNNAMED.Domain;


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
