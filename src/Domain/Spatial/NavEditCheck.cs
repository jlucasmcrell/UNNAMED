// UNNAMED Domain - the placement navigability check (M7 design §3.13)
// No Godot references - pure C#, integer only

using System.Collections.Immutable;

namespace UNNAMED.Domain.Spatial;

/// <summary>What a protected point is, in the canonical order the check names them (M7 design §3.13).</summary>
public enum NavPointKind { NewSite, WorkAnchor, Body, NpcSite, Spawn, Reach, DoorApproach }

/// <summary>
/// A point an edit must not cut off: its label for the refusal, its kind, where it is (a point is a degenerate rectangle), the circle no
/// added solid may cover (0: none), and how near a walkable node must lie.
/// </summary>
public sealed record NavProtectedPoint(string Label, NavPointKind Kind, NavRect Target, long RadiusMm, long ReachMm);

/// <summary>The check's answer: allowed, or the rule it fails (<c>V-N1</c>..<c>V-N4</c>) and why; and how many nodes it flooded.</summary>
public sealed record NavEditVerdict(bool Ok, string? Rule, string? Reason, int NodesFlooded);

/// <summary>
/// Whether adding solids and doors keeps the world navigable (M7 design §3.13): one pure rule, on the <c>person</c> class with every
/// gate passable, so the verdict depends only on content, the piece set and the points. BEFORE is the grid as it stands; AFTER lowers each
/// node's fit by the added solids, computed on demand. The check looks only inside a window round the edit, and never writes the grid.
/// Rules, in order, the first failure returned: V-N1 no newly sealed pocket; V-N2 no existing protected point covered or cut off;
/// V-N3 a new site standable and reachable; V-N4 a door's approaches walkable.
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
        string? Fail(string rule, string reason, out NavEditVerdict verdict)
        {
            verdict = new NavEditVerdict(false, rule, reason, run.Flooded);
            return reason;
        }

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
                    if (i >= i0 && i <= i1 && j >= j0 && j <= j1)
                        continue;
                    if (!run.InWindow(i, j) || run.Labelled(i, j) || !run.WalkAfter(i, j))
                        continue;
                    int label = run.FloodAfter(i, j);
                    if (run.IsOpen(label) || !run.FloodBeforeIsOpen(i, j))
                        continue;
                    var pocket = run.PocketBox;
                    var named = inside.FirstOrDefault(p => p.Target.Inflated(p.ReachMm).Meets(pocket)
                                                           && run.NearestWalkAfter(p) is { } n && run.LabelOf(n.I, n.J) == label);
                    Fail("V-N1", named is null ? SealedReason : Phrase(named), out var sealedVerdict);
                    return sealedVerdict;
                }
            }
        }

        // V-N2: an existing protected point keeps its ground and its walkable node.
        foreach (var point in inside.Where(p => p.Kind != NavPointKind.NewSite))
        {
            if (addSolids.Any(s => Covers(s.Bounds, point)) || (run.Affects(point) && run.AnyWalkBefore(point) && run.NearestWalkAfter(point) is null))
            {
                Fail("V-N2", Phrase(point), out var coveredVerdict);
                return coveredVerdict;
            }
        }

        // V-N3: a new site stands clear, and a walkable node near it reaches open ground.
        foreach (var site in inside.Where(p => p.Kind == NavPointKind.NewSite))
        {
            bool clear = site.RadiusMm == 0 || run.PointClearAfter(site);
            if (!clear || !run.ReachesOpenGround(site))
            {
                Fail("V-N3", $"nothing could reach the {site.Label}", out var siteVerdict);
                return siteVerdict;
            }
        }

        // V-N4: each door's two approaches are walkable.
        foreach (var door in addDoors)
        {
            foreach (var (x, z) in Approaches(door.Bounds))
            {
                if (!run.AnyWalkAfterWithin(x, z, DoorApproachReachMm))
                {
                    Fail("V-N4", DoorReason, out var doorVerdict);
                    return doorVerdict;
                }
            }
        }
        return new NavEditVerdict(true, null, null, run.Flooded);
    }

    /// <summary>The point's phrase, for a refusal that names it (the table of §3.13).</summary>
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

    /// <summary>
    /// Whether an added solid strictly overlaps a point's circle: nearer than its radius, or - with no radius - over the point itself.
    /// </summary>
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
        private readonly long _wi0, _wi1, _wj0, _wj1, _width;
        private readonly int _gen;
        private readonly List<bool> _open = new() { false };
        private readonly long _tileNodes;
        private readonly long _height;
        private readonly int[] _generation, _labels;
        private readonly byte[] _walk;
        private readonly int _w, _h, _cap;
        private readonly long _node, _half, _affMinX, _affMinZ, _affMaxX, _affMaxZ;

        public Run(NavGrid before, NavConfig config, NavScratch scratch, ImmutableArray<NavInput> addSolids, ImmutableArray<NavInput> addDoors)
        {
            _before = before;
            _config = config;
            _scratch = scratch;
            _addSolids = addSolids;
            var added = Union(addSolids.Concat(addDoors).Select(s => s.Bounds));
            _added = addSolids.IsEmpty ? added : Union(addSolids.Select(s => s.Bounds));
            _reach = config.RpMm(config.Classes.Length - 1);
            _tileNodes = config.TileNodes;
            var w = added.Inflated(config.Limits.EditWindowMarginMm);
            w = Capped(w);
            var b = before.Bounds;
            Window = new NavRect(Math.Max(w.MinXMm, b.MinXMm), Math.Max(w.MinZMm, b.MinZMm), Math.Min(w.MaxXMm, b.MaxXMm), Math.Min(w.MaxZMm, b.MaxZMm));
            (_wi0, _wi1) = NodesWithin(Window.MinXMm, Window.MaxXMm);
            (_wj0, _wj1) = NodesWithin(Window.MinZMm, Window.MaxZMm);
            _width = Math.Max(0, _wi1 - _wi0 + 1);
            _height = Math.Max(0, _wj1 - _wj0 + 1);
            _gen = scratch.Begin(_width * _height);
            (_generation, _labels, _walk) = (scratch.Generation, scratch.G, scratch.Dir);
            (_w, _h, _cap) = ((int)_width, (int)_height, config.Limits.SealLimitNodes);
            (_node, _half) = (config.NodeMm, config.NodeMm / 2);
            // Where the added solids can lower a node's fit: their bounds grown by the largest planning radius.
            (_affMinX, _affMinZ, _affMaxX, _affMaxZ) = (_added.MinXMm - _reach, _added.MinZMm - _reach, _added.MaxXMm + _reach, _added.MaxZMm + _reach);
        }

        public NavRect Window { get; }

        /// <summary>The node centres the last flood reached, as a box: a point far from it cannot have a node in its component.</summary>
        public NavRect PocketBox { get; private set; }

        public int Flooded { get; private set; }

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

        private int Index(long i, long j) => (int)((j - _wj0) * _width + (i - _wi0));

        public bool Labelled(long i, long j) => LabelOf(i, j) != 0;

        public int LabelOf(long i, long j)
        {
            if (!InWindow(i, j))
                return 0;
            int idx = Index(i, j);
            return _generation[idx] == _gen ? _labels[idx] : 0;
        }

        public bool IsOpen(int label) => _open[label];

        private ImmutableArray<byte> _tileFit;
        private long _tileI0 = long.MinValue, _tileJ0 = long.MinValue;

        /// <summary>A node's walkability before the edit, the last tile's bytes kept at hand (a flood stays mostly inside one).</summary>
        public bool WalkBefore(long i, long j)
        {
            if (_tileFit.IsDefault || i < _tileI0 || j < _tileJ0 || i >= _tileI0 + _tileNodes || j >= _tileJ0 + _tileNodes)
            {
                if (!_before.TryLocate(i, j, out var tile, out _))
                    return false;
                (_tileFit, _tileI0, _tileJ0) = (tile.SolidFit, tile.I0, tile.J0);
            }
            return _tileFit[(int)((j - _tileJ0) * _tileNodes + (i - _tileI0))] > Person;
        }

        /// <summary>Whether the edit can change walkability within a point's reach: only nodes near the added solids are lowered.</summary>
        public bool Affects(NavProtectedPoint point) =>
            _addSolids.Length > 0 && point.Target.Inflated(point.ReachMm).Meets(_added.Inflated(_reach));

        /// <summary>A node's walkability after the edit, cached in the scratch for this check.</summary>
        public bool WalkAfter(long i, long j) =>
            InWindow(i, j) ? WalkAfterAt(Index(i, j), i, j) : WalkBefore(i, j) && AddedFit(i, j) > Person;

        private bool WalkAfterAt(int idx, long i, long j)
        {
            if (_generation[idx] != _gen)
            {
                _generation[idx] = _gen;
                _labels[idx] = 0;
                _walk[idx] = 0;
            }
            byte bits = _walk[idx];
            if ((bits & NavScratch.WalkKnown) == 0)
            {
                bool ok = WalkBefore(i, j) && AddedFit(i, j) > Person;
                bits = (byte)(NavScratch.WalkKnown | (ok ? NavScratch.WalkOk : 0));
                _walk[idx] = bits;
            }
            return (bits & NavScratch.WalkOk) != 0;
        }

        /// <summary>The classes that fit at a node against the added solids alone.</summary>
        private int AddedFit(long i, long j)
        {
            long cx = i * _node + _half, cz = j * _node + _half;
            if (cx < _affMinX || cx > _affMaxX || cz < _affMinZ || cz > _affMaxZ || DistanceSquared(_added, cx, cz) >= _reach * _reach)
                return _config.Classes.Length;
            int fit = _config.Classes.Length;
            foreach (var solid in _addSolids)
                fit = Math.Min(fit, NavGeometry.FitAt(solid.Shape, cx, cz, _config));
            return fit;
        }

        /// <summary>
        /// Flood the walkable-after component of a seed, 4-connected, inside the window: open once it reaches the window's border, the
        /// seal limit, or a component already found open. Returns its label.
        /// </summary>
        public int FloodAfter(long si, long sj)
        {
            int label = _open.Count;
            _open.Add(false);
            int w = _w, h = _h, cap = _cap, g = _gen;
            int[] queue = _scratch.QueueOf(cap + 1), generation = _generation, labels = _labels;
            byte[] walk = _walk;
            int seed = Index(si, sj);
            WalkAfterAt(seed, si, sj);
            labels[seed] = label;
            queue[0] = seed;
            int head = 0, tail = 1;
            bool open = OnBorder(si, sj);
            while (head < tail && !open)
            {
                int idx = queue[head++];
                int li = idx % w, lj = idx / w;
                // The four neighbours, south, west, east, north, in that order.
                for (int d = 0; d < 4; d++)
                {
                    int ni = d == 1 ? li - 1 : d == 2 ? li + 1 : li;
                    int nj = d == 0 ? lj - 1 : d == 3 ? lj + 1 : lj;
                    if (ni < 0 || nj < 0 || ni >= w || nj >= h)
                        continue;
                    int n = nj * w + ni;
                    if (generation[n] != g)
                    {
                        generation[n] = g;
                        labels[n] = 0;
                        walk[n] = 0;
                    }
                    byte bits = walk[n];
                    if ((bits & NavScratch.WalkKnown) == 0)
                    {
                        long gi = _wi0 + ni, gj = _wj0 + nj;
                        var fits = _tileFit;
                        bool ok = !fits.IsDefault && gi >= _tileI0 && gj >= _tileJ0 && gi < _tileI0 + _tileNodes && gj < _tileJ0 + _tileNodes
                            ? fits[(int)((gj - _tileJ0) * _tileNodes + (gi - _tileI0))] > Person
                            : WalkBefore(gi, gj);
                        if (ok)
                        {
                            long cx = gi * _node + _half, cz = gj * _node + _half;
                            if (cx >= _affMinX && cx <= _affMaxX && cz >= _affMinZ && cz <= _affMaxZ)
                                ok = AddedFit(gi, gj) > Person;
                        }
                        bits = (byte)(NavScratch.WalkKnown | (ok ? NavScratch.WalkOk : 0));
                        walk[n] = bits;
                    }
                    if ((bits & NavScratch.WalkOk) == 0)
                        continue;
                    int other = labels[n];
                    if (other != 0)
                    {
                        if (other != label && _open[other])
                            open = true;
                        continue;
                    }
                    labels[n] = label;
                    if (ni == 0 || nj == 0 || ni == w - 1 || nj == h - 1 || tail >= cap)
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
                int i0 = int.MaxValue, j0 = int.MaxValue, i1 = int.MinValue, j1 = int.MinValue;
                for (int k = 0; k < tail; k++)
                {
                    int li = queue[k] % w, lj = queue[k] / w;
                    (i0, i1, j0, j1) = (Math.Min(i0, li), Math.Max(i1, li), Math.Min(j0, lj), Math.Max(j1, lj));
                }
                PocketBox = new NavRect((_wi0 + i0) * _node + _half, (_wj0 + j0) * _node + _half, (_wi0 + i1) * _node + _half, (_wj0 + j1) * _node + _half);
            }
            return label;
        }

        /// <summary>Whether a seed's walkable component reached open ground before the edit: a flood of its own, stamped apart.</summary>
        public bool FloodBeforeIsOpen(long si, long sj)
        {
            int w = _w, h = _h, cap = _cap;
            var queue = _scratch.QueueOf(cap + 1);
            int stamp = _scratch.BeginBefore(_width * _height);
            var seen = _scratch.Before;
            int seed = Index(si, sj);
            seen[seed] = stamp;
            queue[0] = seed;
            int head = 0, tail = 1;
            if (OnBorder(si, sj))
                return true;
            while (head < tail)
            {
                int idx = queue[head++];
                int li = idx % w, lj = idx / w;
                for (int d = 0; d < 4; d++)
                {
                    int ni = d == 1 ? li - 1 : d == 2 ? li + 1 : li;
                    int nj = d == 0 ? lj - 1 : d == 3 ? lj + 1 : lj;
                    if (ni < 0 || nj < 0 || ni >= w || nj >= h)
                        continue;
                    int n = nj * w + ni;
                    if (seen[n] == stamp)
                        continue;
                    // A node of a component found open after the edit was walkable, and joined to it, before the edit as well: open.
                    if (_generation[n] == _gen && _labels[n] != 0 && _open[_labels[n]])
                    {
                        Flooded += tail;
                        return true;
                    }
                    long gi = _wi0 + ni, gj = _wj0 + nj;
                    var fits = _tileFit;
                    bool walkable = !fits.IsDefault && gi >= _tileI0 && gj >= _tileJ0 && gi < _tileI0 + _tileNodes && gj < _tileJ0 + _tileNodes
                        ? fits[(int)((gj - _tileJ0) * _tileNodes + (gi - _tileI0))] > Person
                        : WalkBefore(gi, gj);
                    if (!walkable)
                        continue;
                    seen[n] = stamp;
                    if (ni == 0 || nj == 0 || ni == w - 1 || nj == h - 1 || tail >= cap)
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
            return found.OrderBy(f => f.D2).ThenBy(f => f.J).ThenBy(f => f.I).Select(f => (f.I, f.J));
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

        public bool AnyWalkAfterWithin(long x, long z, long reachMm) =>
            WithinReach(new NavRect(x, z, x, z), reachMm).Any(n => WalkAfter(n.I, n.J));

        /// <summary>A body of the site's radius stands at it, clear of every solid there will be.</summary>
        public bool PointClearAfter(NavProtectedPoint site)
        {
            long x = (site.Target.MinXMm + site.Target.MaxXMm) / 2, z = (site.Target.MinZMm + site.Target.MaxZMm) / 2;
            return _before.Inputs.Where(input => !input.IsGate).Concat(_addSolids)
                .All(solid => NavGeometry.PointClear(x, z, site.RadiusMm, solid.Shape));
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
