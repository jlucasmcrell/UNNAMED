// UNNAMED Domain - scent on the wind (STEALTH.md §4.6): the one sense that ignores light, cover and posture
// No Godot references - pure C#

namespace UNNAMED.Domain.Creatures;

/// <summary>
/// What the wind carries (STEALTH §4.6). A nose does not care about darkness, a hedge or a crouch: it cares about which way the air is moving, and
/// it gives the animal a place to go rather than a sighting. This is the counter to the stealth player's best tricks - the dark and the grass - and
/// it is why the wind is worth reading before crossing open ground.
/// </summary>
public static class ScentRules
{
    /// <summary>How wide the cone of carried scent is, either side of the wind's own line: an animal's nose is not a compass needle.</summary>
    public const double ConeDegrees = 55;

    /// <summary>
    /// Whether the air at <paramref name="nose"/> carries scent from <paramref name="scent"/>: the wind blows toward
    /// <paramref name="windBearingMdeg"/>, so the thing being smelled has to lie upwind of the nose, inside the cone.
    /// </summary>
    public static bool Carries(long windBearingMdeg, long noseX, long noseZ, long scentX, long scentZ)
    {
        double dx = scentX - noseX, dz = scentZ - noseZ;
        double length = Math.Sqrt(dx * dx + dz * dz);
        if (length < 1)
            return true;   // stood on top of it: no wind needed
        double wind = windBearingMdeg / 1000.0 * Math.PI / 180;
        // The unit vector from the nose to the scent, against the direction the wind is blowing FROM (the reverse of its bearing).
        double along = (dx * -Math.Sin(wind) + dz * -Math.Cos(wind)) / length;
        return along >= Math.Cos(ConeDegrees * Math.PI / 180);
    }
}

/// <summary>
/// A patch of ground where the air moves its own way (STEALTH §4.6, §8.2): a gorge that draws, a scar that funnels, a road gap. Authored as a
/// circle with a bearing and a strength, and the nearest one that holds a point wins, so a wind inside a valley is one entry and everywhere
/// unwritten is the day's prevailing wind.
/// </summary>
public sealed record WindSite(string Key, long XMm, long ZMm, long VectorMdeg, double StrengthMps)
{
    /// <summary>How far the patch reaches: the authored claim about the ground under it.</summary>
    public long RadiusMm { get; init; }
}

/// <summary>
/// Which way the air moves over a region, day by day (STEALTH §4.6). Derived from the world seed and the day, so it is the same for every player in
/// the same world and costs no saved state - and it turns slowly enough that a route planned at dawn still means something at dusk. Where content
/// authors a wind, that wind wins for the ground it covers: the day's air is what an unwritten place gets.
/// </summary>
public static class WindRules
{
    /// <summary>The bearing the wind blows toward, in millidegrees, for a given world seed and day number.</summary>
    public static long Bearing(ulong worldSeed, long dayNumber)
    {
        ulong mixed = worldSeed ^ (ulong)dayNumber * 0x9E3779B97F4A7C15UL;
        mixed ^= mixed >> 33;
        mixed *= 0xFF51AFD7ED558CCDL;
        mixed ^= mixed >> 33;
        return (long)(mixed % 360_000UL);
    }

    /// <summary>
    /// The bearing at a place: the nearest authored wind that covers the point, else the day's prevailing wind. Content only ever adds a wind
    /// where the ground has its own - a gorge, a gap - and everything else keeps the day's slow turn.
    /// </summary>
    public static long BearingAt(long prevailingMdeg, IReadOnlyList<WindSite> winds, long xMm, long zMm)
    {
        if (winds.Count == 0)
            return prevailingMdeg;
        WindSite? nearest = null;
        long best = long.MaxValue;
        foreach (var wind in winds)
        {
            long dx = wind.XMm - xMm, dz = wind.ZMm - zMm;
            long distance = dx * dx + dz * dz;
            if (distance > wind.RadiusMm * wind.RadiusMm || distance >= best)
                continue;
            best = distance;
            nearest = wind;
        }
        return nearest?.VectorMdeg ?? prevailingMdeg;
    }

    /// <summary>The strength at a place, in metres a second: the authored patch's, else the day's own (which content does not set).</summary>
    public static double StrengthAt(double prevailingMps, IReadOnlyList<WindSite> winds, long xMm, long zMm)
    {
        WindSite? nearest = null;
        long best = long.MaxValue;
        foreach (var wind in winds)
        {
            long dx = wind.XMm - xMm, dz = wind.ZMm - zMm;
            long distance = dx * dx + dz * dz;
            if (distance > wind.RadiusMm * wind.RadiusMm || distance >= best)
                continue;
            best = distance;
            nearest = wind;
        }
        return nearest?.StrengthMps ?? prevailingMps;
    }
}
