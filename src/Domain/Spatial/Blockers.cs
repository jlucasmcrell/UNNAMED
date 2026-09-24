// UNNAMED Domain - collision shapes on the ground plane (PROTOTYPE.md §6.2 "stub collision oracle")
// No Godot references - pure C#

namespace UNNAMED.Domain.Spatial;

/// <summary>
/// Something a body cannot walk through, as a footprint on the XZ plane. Heights exist for rendering and
/// the camera; movement is planar in Phase 1 (PROTOTYPE.md §3: no climb, jump never load-bearing).
/// </summary>
public abstract record Blocker(string Id, long HeightMm)
{
    /// <summary>
    /// If a circle at <paramref name="x"/>,<paramref name="z"/> overlaps this footprint, the smallest move
    /// that separates them; otherwise null.
    /// </summary>
    public abstract (double Dx, double Dz)? Separation(double x, double z, double radius);

    /// <summary>The distance from a point to the footprint's edge, 0 inside it.</summary>
    public abstract double DistanceTo(double x, double z);

    /// <summary>True when the straight line between two points passes through the footprint: a blow or an arrow stops here.</summary>
    public abstract bool Crosses(double x0, double z0, double x1, double z1);
}

public static class Footprints
{
    /// <summary>Where a footprint stands: a box's middle, a circle's centre.</summary>
    public static (long XMm, long ZMm) Center(Blocker blocker) => blocker switch
    {
        BoxBlocker box => (box.CenterXMm, box.CenterZMm),
        CircleBlocker circle => (circle.CenterXMm, circle.CenterZMm),
        _ => throw new ArgumentOutOfRangeException(nameof(blocker), blocker.GetType().Name, "Unknown blocker shape"),
    };
}

/// <summary>An axis-aligned box footprint. Structures in the greybox are axis-aligned, which keeps the math exact.</summary>
public sealed record BoxBlocker(string Id, long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, long HeightMm) : Blocker(Id, HeightMm)
{
    public long CenterXMm => (MinXMm + MaxXMm) / 2;
    public long CenterZMm => (MinZMm + MaxZMm) / 2;

    public override (double Dx, double Dz)? Separation(double x, double z, double radius)
    {
        double qx = Math.Clamp(x, MinXMm, MaxXMm);
        double qz = Math.Clamp(z, MinZMm, MaxZMm);
        double dx = x - qx, dz = z - qz;
        double squared = dx * dx + dz * dz;
        if (squared >= radius * radius)
            return null;
        if (squared > 0)
        {
            double distance = Math.Sqrt(squared);
            double push = radius - distance;
            return (dx / distance * push, dz / distance * push);
        }
        // The centre is inside the box: leave by the nearest face.
        double left = x - MinXMm, right = MaxXMm - x, back = z - MinZMm, front = MaxZMm - z;
        double nearest = Math.Min(Math.Min(left, right), Math.Min(back, front));
        if (nearest == left) return (-(left + radius), 0);
        if (nearest == right) return (right + radius, 0);
        if (nearest == back) return (0, -(back + radius));
        return (0, front + radius);
    }

    public override double DistanceTo(double x, double z)
    {
        double dx = x - Math.Clamp(x, MinXMm, MaxXMm);
        double dz = z - Math.Clamp(z, MinZMm, MaxZMm);
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>The slab test: clip the segment's parameter range against each axis's pair of faces.</summary>
    public override bool Crosses(double x0, double z0, double x1, double z1)
    {
        double low = 0, high = 1;
        return Clip(x0, x1 - x0, MinXMm, MaxXMm, ref low, ref high) && Clip(z0, z1 - z0, MinZMm, MaxZMm, ref low, ref high);

        static bool Clip(double start, double delta, double min, double max, ref double low, ref double high)
        {
            if (delta == 0)
                return start >= min && start <= max;
            double a = (min - start) / delta, b = (max - start) / delta;
            low = Math.Max(low, Math.Min(a, b));
            high = Math.Min(high, Math.Max(a, b));
            return low <= high;
        }
    }
}

/// <summary>A round footprint: a tree trunk, a boulder, a post.</summary>
public sealed record CircleBlocker(string Id, long CenterXMm, long CenterZMm, long RadiusMm, long HeightMm) : Blocker(Id, HeightMm)
{
    public override (double Dx, double Dz)? Separation(double x, double z, double radius)
    {
        double dx = x - CenterXMm, dz = z - CenterZMm;
        double reach = radius + RadiusMm;
        double squared = dx * dx + dz * dz;
        if (squared >= reach * reach)
            return null;
        if (squared == 0)
            return (reach, 0);
        double distance = Math.Sqrt(squared);
        double push = reach - distance;
        return (dx / distance * push, dz / distance * push);
    }

    public override double DistanceTo(double x, double z)
    {
        double dx = x - CenterXMm, dz = z - CenterZMm;
        return Math.Max(0, Math.Sqrt(dx * dx + dz * dz) - RadiusMm);
    }

    public override bool Crosses(double x0, double z0, double x1, double z1)
    {
        double dx = x1 - x0, dz = z1 - z0;
        double length = dx * dx + dz * dz;
        double t = length == 0 ? 0 : Math.Clamp(((CenterXMm - x0) * dx + (CenterZMm - z0) * dz) / length, 0, 1);
        double px = x0 + t * dx - CenterXMm, pz = z0 + t * dz - CenterZMm;
        return px * px + pz * pz <= (double)RadiusMm * RadiusMm;
    }
}
