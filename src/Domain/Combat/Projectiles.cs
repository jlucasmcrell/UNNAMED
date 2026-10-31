// UNNAMED Domain - an arrow in the air (STEALTH.md §7.3, §7.4): it takes time, it falls, and the wind pushes it
// No Godot references - pure C#

namespace UNNAMED.Domain.Combat;

/// <summary>
/// What a loosed projectile does (STEALTH §7.3): how fast it goes, how hard the world pulls it down, and how far it will fly. Speed is per second in
/// millimetres so a bow and a crossbow differ by a factor rather than by a table, and gravity is per second squared for the same reason.
/// </summary>
public sealed record ProjectileProfile(long SpeedMmPerSecond, double GravityMmPerSecondSquared = 9_800, long RangeMm = 60_000)
{
    /// <summary>An arrow from a hunting bow: quick enough that its drop over a room is under an inch, slow enough that forty metres is a second.</summary>
    public static ProjectileProfile Arrow { get; } = new(65_000);

    /// <summary>A crossbow bolt: flatter and faster, and that is the whole of what the extra draw buys.</summary>
    public static ProjectileProfile Bolt { get; } = new(90_000);

    /// <summary>A thrown knife: much slower, so it drops visibly and the wind owns it.</summary>
    public static ProjectileProfile Thrown { get; } = new(24_000, RangeMm: 20_000);
}

/// <summary>Where a shot ends up, in the units the rest of the fight uses: ticks to arrive, how far it fell, and how far the wind moved it.</summary>
public readonly record struct Flight(long Ticks, long DropMm, long DriftXMm, long DriftZMm, long ReachMm);

public static class ProjectileRules
{
    /// <summary>
    /// The flight of a shot over a distance (STEALTH §7.3). The fall is half the gravity over the square of the time, the drift is the wind's own push
    /// over the same time, and the reach is how far the arrow would have flown if nothing had stopped it - which is what makes a bow at the end of its
    /// range a threat to nothing.
    /// </summary>
    /// <param name="windBearingMdeg">The bearing the wind blows TOWARD, in millidegrees (§4.6).</param>
    /// <param name="windPush">The wind's push as a fraction of gravity: 0 for calm, a few hundredths for a working breeze.</param>
    public static Flight Of(long distanceMm, ProjectileProfile profile, long windBearingMdeg = 0, double windPush = 0, int tickMs = 50)
    {
        if (profile.SpeedMmPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(profile), "a projectile has to go somewhere");
        long distance = Math.Clamp(distanceMm, 0, profile.RangeMm);
        double seconds = distance / (double)profile.SpeedMmPerSecond;
        long ticks = Math.Max(1, (long)Math.Round(seconds * 1000 / tickMs, MidpointRounding.AwayFromZero));
        long drop = (long)Math.Round(0.5 * profile.GravityMmPerSecondSquared * seconds * seconds, MidpointRounding.AwayFromZero);
        long drift = (long)Math.Round(0.5 * profile.GravityMmPerSecondSquared * windPush * seconds * seconds, MidpointRounding.AwayFromZero);
        double wind = windBearingMdeg / 1000.0 * Math.PI / 180;
        return new Flight(ticks, drop,
            (long)Math.Round(Math.Sin(wind) * drift, MidpointRounding.AwayFromZero),
            (long)Math.Round(Math.Cos(wind) * drift, MidpointRounding.AwayFromZero),
            Math.Max(distance, profile.RangeMm - distance));
    }

    /// <summary>
    /// Whether a shot that fell <paramref name="dropMm"/> short of its aim still lands on a body of this size: a shot aimed at the centre of a deer is
    /// still on the deer if it fell no further than half its height.
    /// </summary>
    public static bool Lands(long dropMm, long targetHeightMm) => dropMm <= Math.Max(1, targetHeightMm) / 2;

    /// <summary>
    /// How far above the mark a shooter must hold to hit it at this distance (STEALTH §7.4's holdover): the fall itself, because a bow does not
    /// consult the shooter about gravity.
    /// </summary>
    /// <summary>
    /// How far the air carries a shot in its flight (STEALTH §7.5): the wind's own strength times the seconds the arrow is in the air, toward the bearing
    /// it blows. It moves where a shot that hits nothing comes down - the line a shot takes through the air at release is the line it is judged on.
    /// </summary>
    public static (long XMm, long ZMm) CarriedMm(long bearingMdeg, long strengthMmPerSecond, long distanceMm, ProjectileProfile profile, int tickMs = 50)
    {
        double seconds = Of(distanceMm, profile, tickMs: tickMs).Ticks * tickMs / 1000.0;
        double rad = bearingMdeg / 1000.0 * Math.PI / 180;
        double carry = strengthMmPerSecond * seconds;
        return ((long)Math.Round(Math.Sin(rad) * carry), (long)Math.Round(Math.Cos(rad) * carry));
    }

    public static long HoldoverMm(long distanceMm, ProjectileProfile profile, int tickMs = 50) =>
        Of(distanceMm, profile, tickMs: tickMs).DropMm;
}
