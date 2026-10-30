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
/// Which way the air moves over a region, day by day (STEALTH §4.6). Derived from the world seed and the day, so it is the same for every player in
/// the same world and costs no saved state - and it turns slowly enough that a route planned at dawn still means something at dusk.
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
}
