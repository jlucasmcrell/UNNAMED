// UNNAMED Domain - collision shapes on the ground plane (PROTOTYPE.md §6.2 "stub collision oracle")
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Spatial;

/// <summary>
/// Something a body cannot walk through, as a footprint on the XZ plane, standing <see cref="HeightMm"/> tall. Movement is planar -
/// no climbing, nothing stood on - but since the owner's M6 playtest a jump passes over a structure lower than the feet, and a
/// crouched body passes under an overhang (<see cref="ClearanceMm"/>) higher than its head (<see cref="Kinematics.Blocks"/>).
/// </summary>
public abstract record Blocker(string Id, long HeightMm)
{
    /// <summary>How far above the ground the structure begins: 0 for one that stands on it, more for an overhang.</summary>
    public long ClearanceMm { get; init; }

    /// <summary>Rock and ground that a body may step onto, stand on and climb (owner finding 50); fences, carts, walls and doors stay barriers.</summary>
    public bool Climbable { get; init; }

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
        PolygonBlocker polygon => (polygon.CenterXMm, polygon.CenterZMm),
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

/// <summary>
/// A convex footprint, its corners counter-clockwise seen from above (+X right, +Z down the page): a boulder or a crag at the turn it is
/// drawn at, so the body meets the rock and not the empty corners of a box round it (owner finding 50).
/// </summary>
public sealed record PolygonBlocker : Blocker
{
    public ImmutableArray<(long XMm, long ZMm)> Corners { get; }

    /// <summary>The hull of <paramref name="points"/>; at least three that are not all on one line.</summary>
    public PolygonBlocker(string id, IEnumerable<(long XMm, long ZMm)> points, long heightMm) : base(id, heightMm)
    {
        Corners = Hull(points);
        if (Corners.Length < 3)
            throw new ArgumentException($"{id}: a hull needs three corners not on one line", nameof(points));
        (MinX, MinZ, MaxX, MaxZ) = (Corners.Min(c => c.XMm), Corners.Min(c => c.ZMm), Corners.Max(c => c.XMm), Corners.Max(c => c.ZMm));
    }

    // Every body is tested against every structure each sub-step: the hull's box answers most of them without the edges.
    private readonly long MinX, MinZ, MaxX, MaxZ;

    private bool Beyond(double x, double z, double margin) => x < MinX - margin || x > MaxX + margin || z < MinZ - margin || z > MaxZ + margin;

    public long CenterXMm => (long)Math.Round(Corners.Average(c => (double)c.XMm), MidpointRounding.AwayFromZero);
    public long CenterZMm => (long)Math.Round(Corners.Average(c => (double)c.ZMm), MidpointRounding.AwayFromZero);

    /// <summary>Andrew's monotone chain: the convex hull, counter-clockwise, collinear points dropped.</summary>
    public static ImmutableArray<(long XMm, long ZMm)> Hull(IEnumerable<(long XMm, long ZMm)> points)
    {
        var p = points.Distinct().OrderBy(q => q.XMm).ThenBy(q => q.ZMm).ToList();
        if (p.Count < 3)
            return p.ToImmutableArray();
        var hull = new List<(long XMm, long ZMm)>();
        for (int pass = 0; pass < 2; pass++)
        {
            int start = hull.Count;
            foreach (var q in pass == 0 ? p : Enumerable.Reverse(p))
            {
                while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], q) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(q);
            }
            hull.RemoveAt(hull.Count - 1);
        }
        return hull.ToImmutableArray();
    }

    /// <summary>Twice the signed area of (a, b, c): positive when c lies to the left of a -> b.</summary>
    public static Int128 Cross((long XMm, long ZMm) a, (long XMm, long ZMm) b, (long XMm, long ZMm) c) =>
        (Int128)(b.XMm - a.XMm) * (c.ZMm - a.ZMm) - (Int128)(b.ZMm - a.ZMm) * (c.XMm - a.XMm);

    /// <summary>True when the point lies inside or on the footprint.</summary>
    public bool Contains(double x, double z)
    {
        if (Beyond(x, z, 0))
            return false;
        for (int i = 0; i < Corners.Length; i++)
        {
            var (a, b) = (Corners[i], Corners[(i + 1) % Corners.Length]);
            if ((b.XMm - a.XMm) * (z - a.ZMm) - (b.ZMm - a.ZMm) * (x - a.XMm) < 0)
                return false;
        }
        return true;
    }

    /// <summary>The nearest point of the outline, and the edge's outward normal there.</summary>
    private (double X, double Z, double Nx, double Nz) Nearest(double x, double z)
    {
        (double X, double Z, double Nx, double Nz) best = default;
        double bestD = double.MaxValue;
        for (int i = 0; i < Corners.Length; i++)
        {
            var (a, b) = (Corners[i], Corners[(i + 1) % Corners.Length]);
            double ex = b.XMm - a.XMm, ez = b.ZMm - a.ZMm, len2 = ex * ex + ez * ez;
            double t = Math.Clamp(((x - a.XMm) * ex + (z - a.ZMm) * ez) / len2, 0, 1);
            double qx = a.XMm + t * ex, qz = a.ZMm + t * ez;
            double d = (x - qx) * (x - qx) + (z - qz) * (z - qz);
            if (d < bestD)
            {
                double len = Math.Sqrt(len2);
                bestD = d;
                best = (qx, qz, ez / len, -ex / len);
            }
        }
        return best;
    }

    public override (double Dx, double Dz)? Separation(double x, double z, double radius)
    {
        if (Beyond(x, z, radius))
            return null;
        var (qx, qz, nx, nz) = Nearest(x, z);
        double dx = x - qx, dz = z - qz;
        if (Contains(x, z))
        {
            // The centre is inside: leave through the nearest edge.
            double depth = Math.Sqrt(dx * dx + dz * dz);
            return (nx * (depth + radius), nz * (depth + radius));
        }
        double squared = dx * dx + dz * dz;
        if (squared >= radius * radius)
            return null;
        double distance = Math.Sqrt(squared);
        if (distance == 0)
            return (nx * radius, nz * radius);
        double push = radius - distance;
        return (dx / distance * push, dz / distance * push);
    }

    public override double DistanceTo(double x, double z)
    {
        if (Contains(x, z))
            return 0;
        var (qx, qz, _, _) = Nearest(x, z);
        return Math.Sqrt((x - qx) * (x - qx) + (z - qz) * (z - qz));
    }

    public override bool Crosses(double x0, double z0, double x1, double z1)
    {
        if (Contains(x0, z0) || Contains(x1, z1))
            return true;
        for (int i = 0; i < Corners.Length; i++)
        {
            var (a, b) = (Corners[i], Corners[(i + 1) % Corners.Length]);
            double d1 = (b.XMm - a.XMm) * (z0 - a.ZMm) - (b.ZMm - a.ZMm) * (x0 - a.XMm);
            double d2 = (b.XMm - a.XMm) * (z1 - a.ZMm) - (b.ZMm - a.ZMm) * (x1 - a.XMm);
            double d3 = (x1 - x0) * (a.ZMm - z0) - (z1 - z0) * (a.XMm - x0);
            double d4 = (x1 - x0) * (b.ZMm - z0) - (z1 - z0) * (b.XMm - x0);
            if ((d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0))
                return true;
        }
        return false;
    }
}
