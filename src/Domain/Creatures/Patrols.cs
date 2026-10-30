// UNNAMED Domain - a patrol on its round (STEALTH.md 8.2)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Creatures;

/// <summary>One place a patrol stops: a post, a door, a corner of the yard.</summary>
public readonly record struct PatrolStop(long XMm, long ZMm);

/// <summary>
/// A patrol loop as content authors it (STEALTH §8.2): a guard, the places she stops, how long the whole round takes and how much of that round
/// is her blind spot. The round is the period; each stop owns an equal share of it, and inside that share she walks to the stop and then stands
/// at it. Nothing here decides anything about sight or hearing - it only says where she is and whether she is walking.
/// </summary>
public sealed record PatrolSite(string Key, string NpcId, ImmutableArray<PatrolStop> Stops, long PeriodTicks)
{
    /// <summary>The stretch of the round in which a body behind cover cannot be seen: content's own claim about the loop.</summary>
    public long BlindSpotTicks { get; init; }
}

/// <summary>
/// Where a patrol is on its round, as a function of the tick - so the round needs no state, and two ticks of the same world agree.
/// </summary>
public static class PatrolRules
{
    /// <summary>
    /// The leg the patrol is on at <paramref name="tick"/>: which stop it is heading for, whether it is still walking there, and where that puts
    /// her. A stop's share of the round is <c>period / count</c> ticks; the walk to it takes the time the ground needs at
    /// <paramref name="stepMmPerTick"/>, and the rest of the share is standing at it.
    /// </summary>
    public static PatrolLeg At(PatrolSite patrol, long tick, long stepMmPerTick)
    {
        if (patrol.Stops.IsDefaultOrEmpty)
            return new PatrolLeg(0, false, 0, 0, 0);
        int count = patrol.Stops.Length;
        long period = Math.Max(patrol.PeriodTicks, count);
        long share = Math.Max(1, period / count);
        long phase = ((tick % period) + period) % period;
        int index = (int)Math.Min(count - 1, phase / share);
        long into = phase - index * share;

        // The share she is in is the walk from this stop to the next: at the start of the period she is standing on the first.
        var previous = patrol.Stops[index];
        var stop = patrol.Stops[(index + 1) % count];
        long dx = stop.XMm - previous.XMm, dz = stop.ZMm - previous.ZMm;
        double distance = Math.Sqrt((double)dx * dx + (double)dz * dz);
        long travel = stepMmPerTick <= 0 ? 0 : (long)Math.Ceiling(distance / stepMmPerTick);
        travel = Math.Min(travel, share);

        if (into >= travel)
            return new PatrolLeg(index, false, stop.XMm, stop.ZMm, Facing(previous, stop));

        double fraction = travel == 0 ? 1 : (double)into / travel;
        long x = previous.XMm + (long)Math.Round(dx * fraction);
        long z = previous.ZMm + (long)Math.Round(dz * fraction);
        return new PatrolLeg(index, true, x, z, Facing(previous, stop));
    }

    /// <summary>Which way a body faces looking along a direction, in millidegrees clockwise from north (the world's convention).</summary>
    public static long FacingToward(long dx, long dz)
    {
        if (dx == 0 && dz == 0)
            return 0;
        double degrees = Math.Atan2(dx, dz) * 180 / Math.PI;
        return (long)Math.Round(((degrees + 360) % 360) * 1000);
    }

    /// <summary>Which way a body faces walking from one stop to the next, in millidegrees clockwise from north (the world's convention).</summary>
    private static long Facing(PatrolStop from, PatrolStop to)
    {
        double dx = to.XMm - from.XMm, dz = to.ZMm - from.ZMm;
        if (dx == 0 && dz == 0)
            return 0;
        double degrees = Math.Atan2(dx, dz) * 180 / Math.PI;
        return (long)Math.Round(((degrees + 360) % 360) * 1000);
    }
}

/// <summary>One tick of a round: the stop being walked to, whether she is walking, where that is, and which way she faces.</summary>
public readonly record struct PatrolLeg(int Index, bool Walking, long XMm, long ZMm, long FacingMdeg);
