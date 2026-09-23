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
}
