// UNNAMED Domain - the placement navigability check (WP12, ported from the M7 design §3.13)
// No Godot references - pure C#, integer only

using System.Collections.Immutable;

namespace UNNAMED.Domain.Spatial;

/// <summary>What a protected point is, in the order the check names them.</summary>
public enum NavPointKind { NewSite, WorkAnchor, Body, NpcSite, Spawn, Reach, DoorApproach }

/// <summary>
/// A point an edit must not cut off: its label for the refusal, its kind, where it is (a point is a degenerate rectangle), the circle no
/// added solid may cover (0: none), and how near a walkable node must lie.
/// </summary>
public sealed record NavProtectedPoint(string Label, NavPointKind Kind, NavRect Target, long RadiusMm, long ReachMm);

/// <summary>The check's answer: allowed, or the rule it fails (<c>V-N1</c>..<c>V-N4</c>) and why; and how many nodes it flooded.</summary>
public sealed record NavEditVerdict(bool Ok, string? Rule, string? Reason, int NodesFlooded);

/// <summary>
/// Whether adding solids and doors keeps the world navigable: one pure rule, on the <c>person</c> class with every gate passable, so the
/// verdict depends only on content, the edit and the points. BEFORE is the grid as it stands; AFTER lowers each node's fit by the added
/// solids, computed on demand. The check looks only inside a window round the edit, and never writes the grid. Rules, in order, the first
/// failure returned: V-N1 no newly sealed pocket; V-N2 no existing protected point covered or cut off; V-N3 a new site standable and
/// reachable; V-N4 a door's approaches walkable. The NPCs' anchors (WP12) are the points building (WP8) must protect.
/// </summary>
public static class NavEditCheck
{
    /// <summary>The window is at most this wide on each axis.</summary>
    public const long MaxWindowMm = 128_000;

    /// <summary>A door's approach points lie this far beyond each face of its leaf, and need a walkable node within the reach below.</summary>
    public const long DoorApproachMm = 700, DoorApproachReachMm = 250;

    public const string SealedReason = "that would close off a space with no way in; rooms need a doorway";
    public const string DoorReason = "the door would open onto a wall";

    public static NavEditVerdict Check(NavGrid before, NavConfig config, NavScratch scratch, ImmutableArray<NavInput> addSolids,
        ImmutableArray<NavInput> addDoors, ImmutableArray<NavProtectedPoint> points)
    {
        if (addSolids.IsEmpty && addDoors.IsEmpty)
            return new NavEditVerdict(true, null, null, 0);
        var run = new Run(before, config, scratch, addSolids, addDoors);
        var inside = points.Where(p => p.Target.Meets(run.Window)).ToImmutableArray();

        // V-N1: every walkable node just outside the added solids either reaches open ground after the edit, or could not before it.
        if (!addSolids.IsEmpty)
        {
            var ring = Union(addSolids.Select(s => s.Bounds)).Inflated(NavConfig.InfluenceMm);
            var (i0, i1) = run.NodesWithin(ring.MinXMm, ring.MaxXMm);
            var (j0, j1) = run.NodesWithin(ring.MinZMm, ring.MaxZMm);
            for (long j = j0 - 1; j <= j1 + 1; j++)
            {
                for (long i = i0 - 1; i <= i1 + 1; i++)
                {
                    bool interior = i >= i0 && i <= i1 && j >= j0 && j <= j1;
                    if (interior || !run.InWindow(i, j) || run.LabelOf(i, j) != 0 || !run.WalkAfter(i, j))
                        continue;
                    int label = run.FloodAfter(i, j);
                    if (run.IsOpen(label) || !run.FloodBeforeIsOpen(i, j))
                        continue;
                    var pocket = run.PocketBox;
                    var named = inside.FirstOrDefault(p => p.Target.Inflated(p.ReachMm).Meets(pocket)
                                                           && run.NearestWalkAfter(p) is { } n && run.LabelOf(n.I, n.J) == label);
                    return run.Refuse("V-N1", named is null ? SealedReason : Phrase(named));
                }
            }
        }

        // V-N2: an existing protected point keeps its ground and its walkable node.
        foreach (var point in inside.Where(p => p.Kind != NavPointKind.NewSite))
        {
            if (addSolids.Any(s => Covers(s.Bounds, point)) || (run.Affects(point) && run.AnyWalkBefore(point) && run.NearestWalkAfter(point) is null))
                return run.Refuse("V-N2", Phrase(point));
        }

        // V-N3: a new site stands clear, and a walkable node near it reaches open ground.
        foreach (var site in inside.Where(p => p.Kind == NavPointKind.NewSite))
        {
            if ((site.RadiusMm != 0 && !run.PointClearAfter(site)) || !run.ReachesOpenGround(site))
                return run.Refuse("V-N3", $"nothing could reach the {site.Label}");
        }

        // V-N4: each door's two approaches are walkable.
        foreach (var door in addDoors)
        {
            if (Approaches(door.Bounds).Any(a => !run.AnyWalkAfterWithin(a.X, a.Z, DoorApproachReachMm)))
                return run.Refuse("V-N4", DoorReason);
        }
        return new NavEditVerdict(true, null, null, run.Flooded);
    }

    /// <summary>The point's phrase, for a refusal that names it.</summary>
    public static string Phrase(NavProtectedPoint point) => point.Kind switch
    {
        NavPointKind.NewSite => $"that would shut in the {point.Label}",
        NavPointKind.WorkAnchor => $"that would cut {point.Label}'s work place off",
        NavPointKind.Body => point.Label == "you" ? "that would shut you in" : $"that would shut {point.Label} in",
        NavPointKind.NpcSite => $"that would wall in {point.Label}'s place",
        NavPointKind.Spawn => "that would wall in the waystone",
        NavPointKind.Reach => $"that would shut {point.Label} away",
        _ => $"that would close off {point.Label}",
    };

    /// <summary>A door leaf's two approach points: its centre, out past each face along its thin axis.</summary>
    public static IEnumerable<(long X, long Z)> Approaches(NavRect leaf)
    {
        long cx = (leaf.MinXMm + leaf.MaxXMm) / 2, cz = (leaf.MinZMm + leaf.MaxZMm) / 2;
        long wx = leaf.MaxXMm - leaf.MinXMm, wz = leaf.MaxZMm - leaf.MinZMm;
        if (wz <= wx)
        {
            long d = wz / 2 + DoorApproachMm;
            yield return (cx, cz - d);
            yield return (cx, cz + d);
        }
        else
        {
            long d = wx / 2 + DoorApproachMm;
            yield return (cx - d, cz);
            yield return (cx + d, cz);
        }
    }

    /// <summary>Whether an added solid strictly overlaps a point's circle: nearer than its radius, or - with no radius - over the point itself.</summary>
    private static bool Covers(NavRect solid, NavProtectedPoint point)
    {
        var t = point.Target;
        if (point.RadiusMm == 0)
            return t.MinXMm > solid.MinXMm && t.MaxXMm < solid.MaxXMm && t.MinZMm > solid.MinZMm && t.MaxZMm < solid.MaxZMm;
        long dx = Math.Max(0, Math.Max(solid.MinXMm - t.MaxXMm, t.MinXMm - solid.MaxXMm));
        long dz = Math.Max(0, Math.Max(solid.MinZMm - t.MaxZMm, t.MinZMm - solid.MaxZMm));
        return dx * dx + dz * dz < point.RadiusMm * point.RadiusMm;
    }

    private static NavRect Union(IEnumerable<NavRect> rects)
    {
        var all = rects.ToList();
        return new NavRect(all.Min(r => r.MinXMm), all.Min(r => r.MinZMm), all.Max(r => r.MaxXMm), all.Max(r => r.MaxZMm));
    }

    private static long DistanceSquared(NavRect rect, long x, long z)
    {
        long dx = Math.Max(0, Math.Max(rect.MinXMm - x, x - rect.MaxXMm));
        long dz = Math.Max(0, Math.Max(rect.MinZMm - z, z - rect.MaxZMm));
        return dx * dx + dz * dz;
    }

    /// <summary>One check's working state: the window, the scratch it floods in, the components found and how many nodes it flooded.</summary>
    private sealed class Run
    {
        private const int Person = 0;
        private readonly NavGrid _before;
        private readonly NavConfig _config;
        private readonly NavScratch _scratch;
        private readonly ImmutableArray<NavInput> _addSolids;
        private readonly NavRect _added;
        private readonly long _reach;
        private readonly long _wi0, _wi1, _wj0, _wj1, _w, _h;
        private readonly int _gen;
        private readonly List<bool> _open = new() { false };

        public Run(NavGrid before, NavConfig config, NavScratch scratch, ImmutableArray<NavInput> addSolids, ImmutableArray<NavInput> addDoors)
        {
            _before = before;
            _config = config;
            _scratch = scratch;
            _addSolids = addSolids;
            var everything = Union(addSolids.Concat(addDoors).Select(s => s.Bounds));
            _added = addSolids.IsEmpty ? everything : Union(addSolids.Select(s => s.Bounds));
            _reach = config.RpMm(config.Classes.Length - 1);
            var w = Capped(everything.Inflated(config.Limits.EditWindowMarginMm));
            var b = before.Bounds;
            Window = new NavRect(Math.Max(w.MinXMm, b.MinXMm), Math.Max(w.MinZMm, b.MinZMm), Math.Min(w.MaxXMm, b.MaxXMm), Math.Min(w.MaxZMm, b.MaxZMm));
            (_wi0, _wi1) = NodesWithin(Window.MinXMm, Window.MaxXMm);
            (_wj0, _wj1) = NodesWithin(Window.MinZMm, Window.MaxZMm);
            _w = Math.Max(0, _wi1 - _wi0 + 1);
            _h = Math.Max(0, _wj1 - _wj0 + 1);
            _gen = scratch.Begin(_w * _h);
        }

        public NavRect Window { get; }

        /// <summary>The node centres the last sealed flood reached, as a box.</summary>
        public NavRect PocketBox { get; private set; }

        public int Flooded { get; private set; }

        public NavEditVerdict Refuse(string rule, string reason) => new(false, rule, reason, Flooded);

        public (long First, long Last) NodesWithin(long min, long max)
        {
            long s = _config.NodeMm, half = s / 2;
            return (NavGeometry.CeilDiv(min - half, s), NavGeometry.FloorDiv(max - half, s));
        }

        private static NavRect Capped(NavRect w)
        {
            static (long, long) Axis(long min, long max)
            {
                if (max - min <= MaxWindowMm)
                    return (min, max);
                long centre = min + (max - min) / 2;
                return (centre - MaxWindowMm / 2, centre + MaxWindowMm / 2);
            }
            var (x0, x1) = Axis(w.MinXMm, w.MaxXMm);
            var (z0, z1) = Axis(w.MinZMm, w.MaxZMm);
            return new NavRect(x0, z0, x1, z1);
        }

        public bool InWindow(long i, long j) => i >= _wi0 && i <= _wi1 && j >= _wj0 && j <= _wj1;

        private bool OnBorder(long i, long j) => i == _wi0 || i == _wi1 || j == _wj0 || j == _wj1;

        private int Index(long i, long j) => (int)((j - _wj0) * _w + (i - _wi0));

        /// <summary>A window node's entries, cleared on first use in this check.</summary>
        private int Touch(long i, long j)
        {
            int idx = Index(i, j);
            if (_scratch.Generation[idx] != _gen)
            {
                _scratch.Generation[idx] = _gen;
                _scratch.G[idx] = 0;
                _scratch.Dir[idx] = 0;
            }
            return idx;
        }

        public int LabelOf(long i, long j) => InWindow(i, j) && _scratch.Generation[Index(i, j)] == _gen ? _scratch.G[Index(i, j)] : 0;

        public bool IsOpen(int label) => _open[label];

        public bool WalkBefore(long i, long j) => _before.SolidFitAt(i, j) > Person;

        /// <summary>Whether the edit can change walkability within a point's reach: only nodes near the added solids are lowered.</summary>
        public bool Affects(NavProtectedPoint point) =>
            _addSolids.Length > 0 && point.Target.Inflated(point.ReachMm).Meets(_added.Inflated(_reach));

        /// <summary>A node's walkability after the edit, cached in the scratch inside the window.</summary>
        public bool WalkAfter(long i, long j)
        {
            if (!InWindow(i, j))
                return WalkBefore(i, j) && AddedFit(i, j) > Person;
            int idx = Touch(i, j);
            byte bits = _scratch.Dir[idx];
            if ((bits & NavScratch.WalkKnown) == 0)
            {
                bool ok = WalkBefore(i, j) && AddedFit(i, j) > Person;
                bits = (byte)(NavScratch.WalkKnown | (ok ? NavScratch.WalkOk : 0));
                _scratch.Dir[idx] = bits;
            }
            return (bits & NavScratch.WalkOk) != 0;
        }

        /// <summary>The classes that fit at a node against the added solids alone.</summary>
        private int AddedFit(long i, long j)
        {
            long cx = _before.CentreOf(i), cz = _before.CentreOf(j);
            if (DistanceSquared(_added, cx, cz) >= _reach * _reach)
                return _config.Classes.Length;
            int fit = _config.Classes.Length;
            foreach (var solid in _addSolids)
                fit = Math.Min(fit, NavGeometry.FitAt(solid.Shape, cx, cz, _config));
            return fit;
        }

        private static readonly ImmutableArray<(int Di, int Dj)> Four = ImmutableArray.Create((0, -1), (-1, 0), (1, 0), (0, 1));

        /// <summary>
        /// Flood the walkable-after component of a seed, 4-connected, inside the window: open once it reaches the window's border, the seal
        /// limit, or a component already found open. Returns its label.
        /// </summary>
        public int FloodAfter(long si, long sj)
        {
            int label = _open.Count;
            _open.Add(false);
            int cap = _config.Limits.SealLimitNodes;
            var queue = _scratch.QueueOf(cap + 1);
            int seed = Touch(si, sj);
            _scratch.G[seed] = label;
            queue[0] = seed;
            int head = 0, tail = 1;
            bool open = OnBorder(si, sj);
            while (head < tail && !open)
            {
                int idx = queue[head++];
                long li = idx % _w, lj = idx / _w;
                foreach (var (di, dj) in Four)
                {
                    long ni = _wi0 + li + di, nj = _wj0 + lj + dj;
                    if (!InWindow(ni, nj) || !WalkAfter(ni, nj))
                        continue;
                    int n = Index(ni, nj);
                    int other = _scratch.G[n];
                    if (other != 0)
                    {
                        if (other != label && _open[other])
                            open = true;
                        continue;
                    }
                    _scratch.G[n] = label;
                    if (OnBorder(ni, nj) || tail >= cap)
                    {
                        open = true;
                        break;
                    }
                    queue[tail++] = n;
                }
            }
            Flooded += tail;
            _open[label] = open;
            if (!open)
            {
                long i0 = long.MaxValue, j0 = long.MaxValue, i1 = long.MinValue, j1 = long.MinValue;
                for (int k = 0; k < tail; k++)
                {
                    long li = queue[k] % _w, lj = queue[k] / _w;
                    (i0, i1, j0, j1) = (Math.Min(i0, li), Math.Max(i1, li), Math.Min(j0, lj), Math.Max(j1, lj));
                }
                PocketBox = new NavRect(_before.CentreOf(_wi0 + i0), _before.CentreOf(_wj0 + j0), _before.CentreOf(_wi0 + i1), _before.CentreOf(_wj0 + j1));
            }
            return label;
        }

        /// <summary>Whether a seed's walkable component reached open ground before the edit: a flood of its own, stamped apart.</summary>
        public bool FloodBeforeIsOpen(long si, long sj)
        {
            if (OnBorder(si, sj))
                return true;
            int cap = _config.Limits.SealLimitNodes;
            var queue = _scratch.QueueOf(cap + 1);
            int stamp = _scratch.BeginBefore(_w * _h);
            var seen = _scratch.Before;
            int seed = Index(si, sj);
            seen[seed] = stamp;
            queue[0] = seed;
            int head = 0, tail = 1;
            while (head < tail)
            {
                int idx = queue[head++];
                long li = idx % _w, lj = idx / _w;
                foreach (var (di, dj) in Four)
                {
                    long ni = _wi0 + li + di, nj = _wj0 + lj + dj;
                    if (!InWindow(ni, nj))
                        continue;
                    int n = Index(ni, nj);
                    if (seen[n] == stamp)
                        continue;
                    // A node of a component found open after the edit was walkable, and joined to it, before the edit as well: open.
                    if (LabelOf(ni, nj) is var l && l != 0 && _open[l])
                    {
                        Flooded += tail;
                        return true;
                    }
                    if (!WalkBefore(ni, nj))
                        continue;
                    seen[n] = stamp;
                    if (OnBorder(ni, nj) || tail >= cap)
                    {
                        Flooded += tail + 1;
                        return true;
                    }
                    queue[tail++] = n;
                }
            }
            Flooded += tail;
            return false;
        }

        /// <summary>The nodes whose centres lie within a point's reach of its target, nearest first, then by (j, i).</summary>
        private IEnumerable<(long I, long J)> WithinReach(NavRect target, long reachMm)
        {
            var box = target.Inflated(reachMm);
            var (i0, i1) = NodesWithin(box.MinXMm, box.MaxXMm);
            var (j0, j1) = NodesWithin(box.MinZMm, box.MaxZMm);
            var found = new List<(long D2, long J, long I)>();
            for (long j = j0; j <= j1; j++)
            {
                for (long i = i0; i <= i1; i++)
                {
                    long d2 = DistanceSquared(target, _before.CentreOf(i), _before.CentreOf(j));
                    if (d2 <= reachMm * reachMm)
                        found.Add((d2, j, i));
                }
            }
            found.Sort();
            return found.Select(f => (f.I, f.J));
        }

        /// <summary>The point's nearest node walkable after the edit, within its reach; null when there is none.</summary>
        public (long I, long J)? NearestWalkAfter(NavProtectedPoint point)
        {
            foreach (var n in WithinReach(point.Target, point.ReachMm))
            {
                if (WalkAfter(n.I, n.J))
                    return n;
            }
            return null;
        }

        public bool AnyWalkBefore(NavProtectedPoint point) => WithinReach(point.Target, point.ReachMm).Any(n => WalkBefore(n.I, n.J));

        public bool AnyWalkAfterWithin(long x, long z, long reachMm) => WithinReach(new NavRect(x, z, x, z), reachMm).Any(n => WalkAfter(n.I, n.J));

        /// <summary>A body of the site's radius stands at it, clear of every solid there will be.</summary>
        public bool PointClearAfter(NavProtectedPoint site)
        {
            long x = (site.Target.MinXMm + site.Target.MaxXMm) / 2, z = (site.Target.MinZMm + site.Target.MaxZMm) / 2;
            return _before.Inputs.Where(input => !input.IsGate).Concat(_addSolids).All(solid => NavGeometry.PointClear(x, z, site.RadiusMm, solid.Shape));
        }

        /// <summary>Some node walkable after the edit, within the site's reach, lies in a component that reaches open ground.</summary>
        public bool ReachesOpenGround(NavProtectedPoint site)
        {
            foreach (var n in WithinReach(site.Target, site.ReachMm))
            {
                if (!InWindow(n.I, n.J) || !WalkAfter(n.I, n.J))
                    continue;
                int label = LabelOf(n.I, n.J);
                if (label == 0)
                    label = FloodAfter(n.I, n.J);
                if (IsOpen(label))
                    return true;
            }
            return false;
        }
    }
}
