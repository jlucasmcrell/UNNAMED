// UNNAMED Domain - Waystone travel and the roads as pure rules (OWNER OD-03, OD-C16; 19 §9.3; NEXT_AREA_TRAVEL_CITY_PLAN.md D.3)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Travel;

/// <summary>
/// The tuning of Waystone travel (<c>config.travel</c>).
/// </summary>
/// <param name="RoadWalkedWithinMm">A stretch of road is walked - and the map draws it - once the body has passed this close to it.</param>
/// <param name="TouchText">What every awake stone says to a hand that touches it.</param>
public sealed record TravelRules(long RoadWalkedWithinMm, string TouchText)
{
    public static readonly TravelRules Default = new(12_000,
        "The stone hums under the hand, as it must for every traveller who stops here. You will know this stone again.");
}

/// <summary>How a character came to know a Waystone (S-38's unlock record): touched while it was awake, or woken by them.</summary>
public enum WaystoneMethod
{
    Touched,
    Woken,
}

public static class WaystoneMethods
{
    public static string Key(WaystoneMethod method) => method switch
    {
        WaystoneMethod.Touched => "touched",
        WaystoneMethod.Woken => "woken",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };

    public static WaystoneMethod? Parse(string key) => key switch
    {
        "touched" => WaystoneMethod.Touched,
        "woken" => WaystoneMethod.Woken,
        _ => null,
    };
}

/// <summary>The road distance and equivalent walking cost; Waystone travel advances no world time (DN8).</summary>
public sealed record TravelCost(long RoadMm, long WalkTicks);

public static class Journeys
{
    /// <summary>
    /// The walking cost of going from one stone to another, for the travel quote only. It never advances the world clock.
    /// </summary>
    public static TravelCost Cost(IReadOnlyList<RoadSite> roads, WaystoneSite from, WaystoneSite to, double walkMmPerSecond, int tickMilliseconds)
    {
        long road = Roads.DistanceMm(roads, (from.ArrivalXMm, from.ArrivalZMm), (to.ArrivalXMm, to.ArrivalZMm));
        long walk = Math.Max(1, (long)Math.Ceiling(road * 1000.0 / (walkMmPerSecond * tickMilliseconds)));
        return new TravelCost(road, walk);
    }
}

/// <summary>The region's roads as a graph: walking distances along them, and which stretch of road a body is on.</summary>
public static class Roads
{
    /// <summary>Two roads meet where a point of one lies this close to a point of the other.</summary>
    public const long JoinMm = 25_000;

    /// <summary>A place this far or further from every road walks straight, across country.</summary>
    public const long ReachMm = 200_000;

    /// <summary>The key a walked stretch of road is saved under: the road and the index of the segment from its point <c>i</c> to <c>i + 1</c>.</summary>
    public static string StretchKey(string roadId, int segment) => $"{roadId}#{segment}";

    /// <summary>The stretches of road a body at this point is within <paramref name="withinMm"/> of, as their keys.</summary>
    public static IEnumerable<string> StretchesNear(IReadOnlyList<RoadSite> roads, long xMm, long zMm, long withinMm)
    {
        foreach (var road in roads)
        {
            for (int i = 0; i + 1 < road.Points.Length; i++)
            {
                var (distance, _) = Project(road.Points[i], road.Points[i + 1], xMm, zMm);
                if (distance <= withinMm)
                    yield return StretchKey(road.Id, i);
            }
        }
    }

    /// <summary>
    /// The walking distance from one place to another by road: across country to the nearest point of a road, along the roads - which
    /// join where they meet - and across country again. Where no road comes within <see cref="ReachMm"/> of both, the straight line.
    /// </summary>
    public static long DistanceMm(IReadOnlyList<RoadSite> roads, (long X, long Z) from, (long X, long Z) to)
    {
        long straight = Length(from.X, from.Z, to.X, to.Z);
        var nodes = new List<(long X, long Z)>();
        var edges = new List<(int A, int B, long Length)>();
        var first = new int[roads.Count];
        for (int r = 0; r < roads.Count; r++)
        {
            first[r] = nodes.Count;
            var points = roads[r].Points;
            for (int i = 0; i < points.Length; i++)
            {
                nodes.Add(points[i]);
                if (i > 0)
                    edges.Add((nodes.Count - 2, nodes.Count - 1, Length(points[i - 1].XMm, points[i - 1].ZMm, points[i].XMm, points[i].ZMm)));
            }
        }
        int roadNodes = nodes.Count;
        for (int a = 0; a < roadNodes; a++)
        {
            for (int b = a + 1; b < roadNodes; b++)
            {
                if (RoadOf(first, a) == RoadOf(first, b))
                    continue;
                long gap = Length(nodes[a].X, nodes[a].Z, nodes[b].X, nodes[b].Z);
                if (gap <= JoinMm)
                    edges.Add((a, b, gap));
            }
        }

        int start = nodes.Count;
        nodes.Add(from);
        int end = nodes.Count;
        nodes.Add(to);
        var entries = new List<(int Road, int Segment, double T, int Node)>();
        foreach (var (place, node) in new[] { (from, start), (to, end) })
        {
            for (int r = 0; r < roads.Count; r++)
            {
                var points = roads[r].Points;
                (long Distance, double T, int Segment) best = (long.MaxValue, 0, -1);
                for (int i = 0; i + 1 < points.Length; i++)
                {
                    var (distance, t) = Project(points[i], points[i + 1], place.X, place.Z);
                    if (distance < best.Distance)
                        best = (distance, t, i);
                }
                if (best.Segment < 0 || best.Distance > ReachMm)
                    continue;
                var (ax, az) = points[best.Segment];
                var (bx, bz) = points[best.Segment + 1];
                long segment = Length(ax, az, bx, bz);
                int on = nodes.Count;
                nodes.Add(((long)Math.Round(ax + (bx - ax) * best.T), (long)Math.Round(az + (bz - az) * best.T)));
                edges.Add((node, on, best.Distance));
                edges.Add((on, first[r] + best.Segment, (long)Math.Round(segment * best.T)));
                edges.Add((on, first[r] + best.Segment + 1, (long)Math.Round(segment * (1 - best.T))));
                // Both ends on the same stretch: along it directly, not round by its ends.
                foreach (var other in entries.Where(e => e.Road == r && e.Segment == best.Segment))
                    edges.Add((on, other.Node, (long)Math.Round(segment * Math.Abs(best.T - other.T))));
                entries.Add((r, best.Segment, best.T, on));
            }
        }
        long byRoad = Shortest(nodes.Count, edges, start, end);
        return byRoad == long.MaxValue ? straight : byRoad;
    }

    private static int RoadOf(int[] first, int node)
    {
        int road = 0;
        for (int r = 0; r < first.Length; r++)
        {
            if (first[r] <= node)
                road = r;
        }
        return road;
    }

    private static long Shortest(int count, List<(int A, int B, long Length)> edges, int start, int end)
    {
        var best = Enumerable.Repeat(long.MaxValue, count).ToArray();
        var done = new bool[count];
        best[start] = 0;
        for (int step = 0; step < count; step++)
        {
            int at = -1;
            for (int i = 0; i < count; i++)
            {
                if (!done[i] && best[i] != long.MaxValue && (at < 0 || best[i] < best[at]))
                    at = i;
            }
            if (at < 0 || at == end)
                break;
            done[at] = true;
            foreach (var (a, b, length) in edges)
            {
                int next = a == at ? b : b == at ? a : -1;
                if (next >= 0 && !done[next] && best[at] + length < best[next])
                    best[next] = best[at] + length;
            }
        }
        return best[end];
    }

    /// <summary>The distance from a point to a segment, and how far along the segment (0 to 1) its nearest point lies.</summary>
    private static (long Distance, double T) Project((long XMm, long ZMm) a, (long XMm, long ZMm) b, long x, long z)
    {
        double dx = b.XMm - a.XMm, dz = b.ZMm - a.ZMm;
        double squared = dx * dx + dz * dz;
        double t = squared == 0 ? 0 : Math.Clamp(((x - a.XMm) * dx + (z - a.ZMm) * dz) / squared, 0, 1);
        double px = a.XMm + dx * t, pz = a.ZMm + dz * t;
        return ((long)Math.Round(Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz))), t);
    }

    private static long Length(long x0, long z0, long x1, long z1) =>
        (long)Math.Round(Math.Sqrt((double)(x1 - x0) * (x1 - x0) + (double)(z1 - z0) * (z1 - z0)));
}

/// <summary>
/// What the character's map holds (MAPS_CARTOGRAPHY: a map drawn from what the character knows): the places they have discovered with the
/// ground round each, the stretches of road they have walked, and the Waystones they know. Nothing they have not found.
/// </summary>
public sealed record KnownMap(
    ImmutableArray<KnownPlace> Places,
    ImmutableArray<KnownRoad> Roads,
    ImmutableArray<string> Stones);

/// <summary>A discovered place and the ground round it the map shows.</summary>
public sealed record KnownPlace(string LocationId, long XMm, long ZMm, long RevealRadiusMm);

/// <summary>A walked stretch of a road: its two ends.</summary>
public sealed record KnownRoad(string RoadId, string Name, long X0Mm, long Z0Mm, long X1Mm, long Z1Mm);

public static class Cartography
{
    /// <summary>The map of a character who has discovered these places, walked these stretches and knows these stones.</summary>
    public static KnownMap Known(RegionLayout layout, IEnumerable<string> discovered, IEnumerable<string> walked, IEnumerable<string> stones)
    {
        var places = discovered.ToHashSet(StringComparer.Ordinal);
        var stretches = walked.ToHashSet(StringComparer.Ordinal);
        var known = stones.ToHashSet(StringComparer.Ordinal);
        return new KnownMap(
            layout.Locations.Where(l => places.Contains(l.Id)).Select(l => new KnownPlace(l.Id, l.XMm, l.ZMm, l.RevealRadiusMm)).ToImmutableArray(),
            layout.Roads.SelectMany(r => Enumerable.Range(0, Math.Max(0, r.Points.Length - 1))
                    .Where(i => stretches.Contains(Roads.StretchKey(r.Id, i)))
                    .Select(i => new KnownRoad(r.Id, r.Name, r.Points[i].XMm, r.Points[i].ZMm, r.Points[i + 1].XMm, r.Points[i + 1].ZMm)))
                .ToImmutableArray(),
            layout.Waystones.Where(w => known.Contains(w.Key)).Select(w => w.Key).ToImmutableArray());
    }
}
