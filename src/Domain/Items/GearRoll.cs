using System.Collections.Immutable;

namespace UNNAMED.Domain.Items;

/// <summary>
/// D21's loot roll (canon <c>GEAR_GENERATION.md</c> §3 steps 2-4): which maker's work it is, which part fills each slot, and which material it is finished
/// in. Pure and deterministic - the caller supplies the samples - so a drop is the same drop on every load, and the resolver still adds the numbers up.
/// </summary>
public static class GearRoll
{
    /// <summary>
    /// Roll a build for an assembled template. The maker is the template's own maker (canon: an authored item names its maker; a region's table may
    /// narrow it later), parts come from that maker's weighted set for each slot at or under the part tier, and the material from the palette at or
    /// under the tier.
    /// </summary>
    public static GearBuild? Roll(
        ItemDefinition item,
        MakerDefinition maker,
        int partTier,
        IReadOnlyDictionary<string, PartDefinition> parts,
        IReadOnlyDictionary<string, MaterialDefinition> materials,
        Func<uint, double> sample)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(maker);
        if (item.Assembly is not { } assembly || !maker.PartSets.TryGetValue(assembly.Family, out var slots) || slots.IsEmpty)
            return null;

        int tier = Math.Clamp(partTier, maker.TierRange[0], maker.TierRange[^1]);
        var picked = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        uint n = 0;
        foreach (string slot in assembly.Slots)
        {
            if (!slots.TryGetValue(slot, out var offered) || offered.IsEmpty)
                return null;
            var legal = offered.Where(p => parts.TryGetValue(p.PartId, out var part) && part.TierMin <= tier).ToList();
            if (legal.Count == 0)
                legal = offered.ToList();
            picked[slot] = WeightedPick(legal, sample(n++));
        }

        var palette = maker.MaterialPalette
            .Where(p => materials.TryGetValue(p.MaterialId, out var material) && material.TierMin <= tier)
            .ToList();
        if (palette.Count == 0)
            palette = maker.MaterialPalette.ToList();
        if (palette.Count == 0)
            return null;
        string materialId = WeightedPick(palette, sample(n++));

        var finished = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (string slot in picked.Keys)
            if (parts.TryGetValue(picked[slot], out var part))
                foreach (string materialSlot in part.MaterialSlots)
                    finished[materialSlot] = materialId;

        return new GearBuild(assembly.Family, maker.Id, tier, picked.ToImmutable(), finished.ToImmutable(),
            ImmutableArray<string>.Empty, 0);
    }

    /// <summary>Pick one item from a weighted list by a sample in [0, 1): the same sample always gives the same pick.</summary>
    private static string WeightedPick(IReadOnlyList<WeightedPart> offered, double at)
    {
        int total = offered.Sum(p => p.Weight);
        double target = Math.Clamp(at, 0.0, 0.999999) * total;
        int running = 0;
        foreach (var part in offered)
        {
            running += part.Weight;
            if (target < running)
                return part.PartId;
        }
        return offered[^1].PartId;
    }

    private static string WeightedPick(IReadOnlyList<WeightedMaterial> palette, double at)
    {
        int total = palette.Sum(p => p.Weight);
        double target = Math.Clamp(at, 0.0, 0.999999) * total;
        int running = 0;
        foreach (var material in palette)
        {
            running += material.Weight;
            if (target < running)
                return material.MaterialId;
        }
        return palette[^1].MaterialId;
    }
}
