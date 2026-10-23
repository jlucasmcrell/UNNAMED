// UNNAMED Domain - navigation's exact integer geometry (WP12, ported from the M7 design §3.7.2; D-13)
// No Godot references - pure C#, integer only

namespace UNNAMED.Domain.Spatial;

/// <summary>
/// The predicates navigation is built on, all exact in integers: how many classes fit at a node, whether a circle is clear of a
/// footprint, and whether a circle swept along a segment is. Touching is clear throughout, as it is for <see cref="Blocker.Separation"/>.
/// </summary>
public static class NavGeometry
{
    /// <summary>Integer division rounding towards negative infinity.</summary>
    public static long FloorDiv(long a, long b)
    {
        long q = a / b;
        return a % b != 0 && (a < 0) != (b < 0) ? q - 1 : q;
    }

    /// <summary>Integer division rounding towards positive infinity.</summary>
    public static long CeilDiv(long a, long b) => -FloorDiv(-a, b);

    /// <summary>A footprint's bounding rectangle.</summary>
    public static NavRect Aabb(Blocker shape) => shape switch
    {
        BoxBlocker b => new NavRect(b.MinXMm, b.MinZMm, b.MaxXMm, b.MaxZMm),
        CircleBlocker c => new NavRect(c.CenterXMm - c.RadiusMm, c.CenterZMm - c.RadiusMm, c.CenterXMm + c.RadiusMm, c.CenterZMm + c.RadiusMm),
        PolygonBlocker p => new NavRect(p.Corners.Min(q => q.XMm), p.Corners.Min(q => q.ZMm), p.Corners.Max(q => q.XMm), p.Corners.Max(q => q.ZMm)),
        _ => throw Unknown(shape),
    };

    /// <summary>
    /// How many of the configured classes fit at a point beside this footprint: counted in ascending order, stopping at the first whose
    /// planning-radius circle there would overlap it.
    /// </summary>
    public static int FitAt(Blocker shape, long px, long pz, NavConfig config)
    {
        int k = 0;
        switch (shape)
        {
            case BoxBlocker b:
            {
                long d2 = BoxDistanceSquared(b, px, pz);
                while (k < config.Classes.Length && d2 >= Square(config.RpMm(k)))
                    k++;
                return k;
            }
            case CircleBlocker c:
            {
                long e2 = Square(px - c.CenterXMm) + Square(pz - c.CenterZMm);
                while (k < config.Classes.Length && e2 >= Square(config.RpMm(k) + c.RadiusMm))
                    k++;
                return k;
            }
            case PolygonBlocker p:
            {
                while (k < config.Classes.Length && PolygonClear(p, px, pz, config.RpMm(k)))
                    k++;
                return k;
            }
            default:
                throw Unknown(shape);
        }
    }

    /// <summary>True when a circle of this radius at the point overlaps nothing of the footprint.</summary>
    public static bool PointClear(long px, long pz, long radiusMm, Blocker shape) => shape switch
    {
        BoxBlocker b => BoxDistanceSquared(b, px, pz) >= radiusMm * radiusMm,
        CircleBlocker c => Square(px - c.CenterXMm) + Square(pz - c.CenterZMm) >= Square(radiusMm + c.RadiusMm),
        PolygonBlocker p => PolygonClear(p, px, pz, radiusMm),
        _ => throw Unknown(shape),
    };

    /// <summary>True when a circle of this radius swept from one point to the other overlaps nothing of the footprint.</summary>
    public static bool SegmentClear(NavPoint a, NavPoint b, long radiusMm, Blocker shape) =>
        SegmentClear(a.XMm, a.ZMm, b.XMm, b.ZMm, radiusMm, shape);

    /// <summary>True when a circle of this radius swept from (ax, az) to (bx, bz) overlaps nothing of the footprint.</summary>
    public static bool SegmentClear(long ax, long az, long bx, long bz, long radiusMm, Blocker shape)
    {
        if (ax == bx && az == bz)
            return PointClear(ax, az, radiusMm, shape);
        switch (shape)
        {
            case CircleBlocker c:
                return ClearOfDisc(ax, az, bx, bz, c.CenterXMm, c.CenterZMm, radiusMm + c.RadiusMm);
            case BoxBlocker box:
            {
                long r = radiusMm;
                // The box swept by the circle is two open slabs and a disc at each corner.
                if (MeetsOpenRect(ax, az, bx, bz, box.MinXMm - r, box.MinZMm, box.MaxXMm + r, box.MaxZMm)
                    || MeetsOpenRect(ax, az, bx, bz, box.MinXMm, box.MinZMm - r, box.MaxXMm, box.MaxZMm + r))
                    return false;
                return ClearOfDisc(ax, az, bx, bz, box.MinXMm, box.MinZMm, r) && ClearOfDisc(ax, az, bx, bz, box.MaxXMm, box.MinZMm, r)
                    && ClearOfDisc(ax, az, bx, bz, box.MinXMm, box.MaxZMm, r) && ClearOfDisc(ax, az, bx, bz, box.MaxXMm, box.MaxZMm, r);
            }
            case PolygonBlocker p:
            {
                // The swept circle misses the hull when the path does not cross it and every edge keeps the radius from the path.
                if (!PolygonClear(p, ax, az, 0) || !PolygonClear(p, bx, bz, 0))
                    return false;
                for (int i = 0; i < p.Corners.Length; i++)
                {
                    var (e0, e1) = (p.Corners[i], p.Corners[(i + 1) % p.Corners.Length]);
                    if (SegmentsMeet((ax, az), (bx, bz), e0, e1)
                        || !ClearOfDisc(ax, az, bx, bz, e0.XMm, e0.ZMm, radiusMm) || !ClearOfDisc(e0.XMm, e0.ZMm, e1.XMm, e1.ZMm, ax, az, radiusMm)
                        || !ClearOfDisc(e0.XMm, e0.ZMm, e1.XMm, e1.ZMm, bx, bz, radiusMm))
                        return false;
                }
                return true;
            }
            default:
                throw Unknown(shape);
        }
    }

    /// <summary>The squared distance from a point to a box, 0 inside it.</summary>
    public static long DistanceSquaredTo(BoxBlocker box, long px, long pz) => BoxDistanceSquared(box, px, pz);

    /// <summary>True when the point lies within this distance of the footprint (inside it counts).</summary>
    public static bool Within(Blocker shape, long px, long pz, long distanceMm) => shape switch
    {
        BoxBlocker b => BoxDistanceSquared(b, px, pz) <= distanceMm * distanceMm,
        CircleBlocker c => Square(px - c.CenterXMm) + Square(pz - c.CenterZMm) <= Square(distanceMm + c.RadiusMm),
        PolygonBlocker p => !PolygonClear(p, px, pz, distanceMm + 1),
        _ => throw Unknown(shape),
    };

    /// <summary>True when a circle of this radius at the point is outside the hull and at least the radius from every edge, exactly.</summary>
    public static bool PolygonClear(PolygonBlocker p, long px, long pz, long radiusMm)
    {
        bool inside = true;
        for (int i = 0; i < p.Corners.Length; i++)
        {
            var (a, b) = (p.Corners[i], p.Corners[(i + 1) % p.Corners.Length]);
            if (PolygonBlocker.Cross(a, b, (px, pz)) < 0)
                inside = false;
            if (radiusMm > 0 && !ClearOfDisc(a.XMm, a.ZMm, b.XMm, b.ZMm, px, pz, radiusMm))
                return false;
        }
        return !inside;
    }

    /// <summary>True when two closed segments share a point (exact orientation tests).</summary>
    private static bool SegmentsMeet((long XMm, long ZMm) a, (long XMm, long ZMm) b, (long XMm, long ZMm) c, (long XMm, long ZMm) d)
    {
        Int128 d1 = PolygonBlocker.Cross(c, d, a), d2 = PolygonBlocker.Cross(c, d, b), d3 = PolygonBlocker.Cross(a, b, c), d4 = PolygonBlocker.Cross(a, b, d);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            return true;
        return d1 == 0 && OnSegment(c, d, a) || d2 == 0 && OnSegment(c, d, b) || d3 == 0 && OnSegment(a, b, c) || d4 == 0 && OnSegment(a, b, d);

        static bool OnSegment((long XMm, long ZMm) p, (long XMm, long ZMm) q, (long XMm, long ZMm) r) =>
            Math.Min(p.XMm, q.XMm) <= r.XMm && r.XMm <= Math.Max(p.XMm, q.XMm) && Math.Min(p.ZMm, q.ZMm) <= r.ZMm && r.ZMm <= Math.Max(p.ZMm, q.ZMm);
    }

    private static ArgumentOutOfRangeException Unknown(Blocker shape) =>
        new(nameof(shape), shape.GetType().Name, "Unknown blocker shape");

    private static long BoxDistanceSquared(BoxBlocker b, long px, long pz)
    {
        long dx = Math.Max(Math.Max(b.MinXMm - px, 0), px - b.MaxXMm);
        long dz = Math.Max(Math.Max(b.MinZMm - pz, 0), pz - b.MaxZMm);
        return dx * dx + dz * dz;
    }

    private static long Square(long v) => v * v;

    /// <summary>True when every point of the segment lies at least <paramref name="q"/> from (cx, cz).</summary>
    private static bool ClearOfDisc(long ax, long az, long bx, long bz, long cx, long cz, long q)
    {
        Int128 dx = bx - ax, dz = bz - az, wx = cx - ax, wz = cz - az;
        Int128 q2 = (Int128)q * q;
        Int128 w2 = wx * wx + wz * wz;
        Int128 len2 = dx * dx + dz * dz;
        Int128 dot = wx * dx + wz * dz;
        if (len2 == 0 || dot <= 0)
            return w2 >= q2;
        if (dot >= len2)
        {
            Int128 ex = cx - bx, ez = cz - bz;
            return ex * ex + ez * ez >= q2;
        }
        // The squared distance to the line, times len2: |w|^2 len2 - dot^2.
        return w2 * len2 - dot * dot >= q2 * len2;
    }

    /// <summary>
    /// True when the closed segment meets the open rectangle (x0, x1) x (z0, z1): its bounds overlap the rectangle strictly on both axes,
    /// and its line has corners of the rectangle strictly on both sides.
    /// </summary>
    private static bool MeetsOpenRect(long ax, long az, long bx, long bz, long x0, long z0, long x1, long z1)
    {
        if (x0 >= x1 || z0 >= z1)
            return false;
        if (Math.Max(ax, bx) <= x0 || Math.Min(ax, bx) >= x1 || Math.Max(az, bz) <= z0 || Math.Min(az, bz) >= z1)
            return false;
        int above = 0, below = 0;
        foreach (var (cx, cz) in new[] { (x0, z0), (x1, z0), (x0, z1), (x1, z1) })
        {
            Int128 s = (Int128)(az - bz) * (cx - ax) + (Int128)(bx - ax) * (cz - az);
            if (s > 0)
                above++;
            else if (s < 0)
                below++;
        }
        return above > 0 && below > 0;
    }
}
