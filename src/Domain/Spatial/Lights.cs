// UNNAMED Domain - lights and cover as places in a space (STEALTH.md §2.2, §2.3): the geometry of being seen
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Spatial;

/// <summary>
/// A light standing in a space that a body can be seen by (STEALTH §2.2): a torch on a post, a brazier, a lantern. Its lit state belongs to
/// the world, not to the record - the same authored light is the burning torch and the same torch doused - so what this holds is what the
/// light <em>would</em> give if it were burning.
/// </summary>
public sealed record LightSource(string Key, long XMm, long ZMm, long RadiusMm, int IntensityPermille)
{
    /// <summary>True when a hand, water or an arrow can put it out (§2.2); a window or a hearth that cannot is authored so.</summary>
    public bool Dousable { get; init; } = true;

    /// <summary>What it adds at a point: full at its middle, nothing at its radius, linear between - cheap and deterministic (§2.1).</summary>
    public int At(long xMm, long zMm)
    {
        if (RadiusMm <= 0 || IntensityPermille <= 0)
            return 0;
        double dx = xMm - XMm, dz = zMm - ZMm;
        double distance = Math.Sqrt(dx * dx + dz * dz);
        if (distance >= RadiusMm)
            return 0;
        return (int)Math.Round(IntensityPermille * (1 - distance / RadiusMm), MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// A cover volume (STEALTH §2.3): tall grass, a hedge, a crate, a pillar, a curtain, a shadow alcove, a rooftop lip. It hides a body at or
/// below its own height - a 1.2 m hedge hides a crouched body and not a standing one - and it hides by a stated fraction, because a body
/// half behind a crate is half seen. That fraction is what makes cover readable instead of a switch.
/// </summary>
public sealed record CoverVolume(string Key, string Kind, long XMm, long ZMm, long RadiusMm, long HeightMm, int BlocksSightPercent)
{
    /// <summary>The kinds the level kit offers (§2.3, §8.1); an authored cover must be one of them.</summary>
    public static ImmutableArray<string> Kinds { get; } = ImmutableArray.Create(
        "tall_grass", "hedge", "crate", "pillar", "shadow_alcove", "curtain", "rooftop_lip");
}
