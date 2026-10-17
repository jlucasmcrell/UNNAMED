// UNNAMED Domain - planning a route over the navigation lattice (WP12, ported from the M7 design §3.7; D-13)
// No Godot references - pure C#, integer only

using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace UNNAMED.Domain.Spatial;

/// <summary>
/// What a plan runs against: the grid, how a gate's state is read now, the limits (the grid's own configuration gives the geometry),
/// the owner's scratch, and where the work is counted (none for a preview).
/// </summary>
public sealed record NavQuery(NavGrid Grid, Func<NavInput, bool> IsGateOpen, NavConfig Config, NavScratch Scratch, NavCounterSink? Counters);

/// <summary>How a plan ended: always decided by counts, never by time.</summary>
public enum NavOutcome { Found, StartBlocked, GoalBlocked, Enclosed, TooFar, Exhausted, NotInWindow, Budget }

/// <summary>
/// A plan: its outcome, the corners to walk (the start excluded; empty unless found), whether they stop short of the goal at the corner
/// limit, the planning window in millimetres, and the work it took.
/// </summary>
public sealed record NavPlan(NavOutcome Outcome, ImmutableArray<NavPoint> Corners, bool Partial, NavRect Window, int Expansions, int ProbeNodes);

/// <summary>
/// The query pipeline: snap both ends to walkable nodes, bound a window around them, prove an enclosure cheaply with two small floods,
/// search the window with optimal A*, and pull the node path taut into at most <see cref="NavLimits.MaxCorners"/> corners with the exact
/// swept-circle test. Every tie is broken by geometry and index, never by the order work was done in.
/// </summary>
public static class NavSearch
{
    // E, N, W, S, NE, NW, SW, SE; "N" is +Z. A diagonal needs both orthogonals it passes between (no corner cutting).
    private static readonly ImmutableArray<(int Di, int Dj, int Cost)> Neighbours = ImmutableArray.Create(
        (1, 0, 1000), (0, 1, 1000), (-1, 0, 1000), (0, -1, 1000), (1, 1, 1414), (-1, 1, 1414), (-1, -1, 1414), (1, -1, 1414));

    private static readonly ImmutableArray<(int A, int B)> DiagonalNeeds = ImmutableArray.Create((0, 1), (2, 1), (2, 3), (0, 3));

    private enum Probe { Connected, Enclosed, Inconclusive }

    public static string OutcomeKey(NavOutcome outcome) => outcome switch
    {
        NavOutcome.Found => "found",
        NavOutcome.StartBlocked => "start_blocked",
        NavOutcome.GoalBlocked => "goal_blocked",
        NavOutcome.Enclosed => "enclosed",
        NavOutcome.TooFar => "too_far",
        NavOutcome.Exhausted => "exhausted",
        NavOutcome.NotInWindow => "not_in_window",
        NavOutcome.Budget => "budget",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown outcome"),
    };

    /// <summary>Plan a route for <paramref name="agent"/> from one point to another.</summary>
    public static NavPlan Plan(NavQuery q, NavAgent agent, NavPoint from, NavPoint to)
    {
        var plan = new Planner(q, agent).Run(from, to);
        q.Counters?.CountPlan(OutcomeKey(plan.Outcome), plan.Expansions);
        return plan;
    }

    /// <summary>
    /// True when a circle of radius <paramref name="radiusMm"/> swept from <paramref name="a"/> to <paramref name="b"/> overlaps no solid
    /// and no gate the agent cannot pass.
    /// </summary>
    public static bool Clear(NavGrid grid, NavAgent agent, Func<NavInput, bool> isGateOpen, NavPoint a, NavPoint b, long radiusMm)
    {
        var reach = new NavRect(Math.Min(a.XMm, b.XMm), Math.Min(a.ZMm, b.ZMm), Math.Max(a.XMm, b.XMm), Math.Max(a.ZMm, b.ZMm)).Inflated(radiusMm);
        foreach (var tile in grid.Tiles)
        {
            if (!NavGrid.TileRect(tile.Key).Meets(reach))
                continue;
            foreach (var input in tile.Inputs)
            {
                if (!input.Bounds.Meets(reach) || (input.IsGate && NavGrid.Passable(input, agent, isGateOpen)))
                    continue;
                if (!NavGeometry.SegmentClear(a, b, radiusMm, input.Shape))
                    return false;
            }
        }
        return true;
    }

    /// <summary>True when a body of radius <paramref name="radiusMm"/> can stand at the point: in bounds, and clear of every solid and impassable gate.</summary>
    public static bool Standable(NavGrid grid, NavAgent agent, Func<NavInput, bool> isGateOpen, NavPoint p, long radiusMm)
    {
        var b = grid.Bounds;
        return p.XMm - radiusMm >= b.MinXMm && p.XMm + radiusMm <= b.MaxXMm && p.ZMm - radiusMm >= b.MinZMm && p.ZMm + radiusMm <= b.MaxZMm
            && Clear(grid, agent, isGateOpen, p, p, radiusMm);
    }

    /// <summary>
    /// The walkable node nearest the point within a Chebyshev radius of its own node, by (squared distance to its centre, j, i), that
    /// the point can reach (or be reached from) in a clear straight line at <paramref name="clearanceMm"/>; null when none can.
    /// </summary>
    internal static (long I, long J)? SnapNode(NavGrid grid, NavAgent agent, Func<NavInput, bool> isGateOpen, NavPoint p, int radius,
        long clearanceMm, bool fromPoint)
    {
        long ci = grid.NodeOf(p.XMm), cj = grid.NodeOf(p.ZMm);
        var nodes = new List<(long D2, long J, long I)>((2 * radius + 1) * (2 * radius + 1));
        for (long j = cj - radius; j <= cj + radius; j++)
        {
            for (long i = ci - radius; i <= ci + radius; i++)
            {
                long dx = grid.CentreOf(i) - p.XMm, dz = grid.CentreOf(j) - p.ZMm;
                nodes.Add((dx * dx + dz * dz, j, i));
            }
        }
        nodes.Sort();
        foreach (var (_, j, i) in nodes)
        {
            if (!grid.Walkable(i, j, agent, isGateOpen))
                continue;
            var centre = new NavPoint(grid.CentreOf(i), grid.CentreOf(j));
            if (fromPoint ? Clear(grid, agent, isGateOpen, p, centre, clearanceMm) : Clear(grid, agent, isGateOpen, centre, p, clearanceMm))
                return (i, j);
        }
        return null;
    }

    /// <summary>One plan's working state.</summary>
    private sealed class Planner
    {
        private readonly NavQuery _q;
        private readonly NavAgent _agent;
        private readonly NavGrid _grid;
        private readonly NavLimits _limits;
        private readonly NavScratch _scratch;
        private readonly long _node;
        private readonly long _radius;
        private long _wi0, _wj0, _wi1, _wj1, _ww;
        private int _gen;
        private NavTile? _tile;

        public Planner(NavQuery q, NavAgent agent)
        {
            _q = q;
            _agent = agent;
            _grid = q.Grid;
            _limits = q.Config.Limits;
            _scratch = q.Scratch;
            _node = _grid.Config.NodeMm;
            _radius = _grid.Config.Classes[agent.ClassIndex].RadiusMm;
        }

        public NavPlan Run(NavPoint from, NavPoint to)
        {
            long rawGi = _grid.NodeOf(to.XMm), rawGj = _grid.NodeOf(to.ZMm);
            int startRadius = (int)(_limits.StartSnapMm / _node), goalRadius = (int)(_limits.GoalSnapMm / _node);

            var start = SnapNode(_grid, _agent, _q.IsGateOpen, from, startRadius, _radius - _limits.OffLineToleranceMm, fromPoint: true);
            if (start is null)
                return Fail(NavOutcome.StartBlocked, _grid.NodeOf(from.XMm), _grid.NodeOf(from.ZMm), rawGi, rawGj);
            var goal = SnapNode(_grid, _agent, _q.IsGateOpen, to, goalRadius, _radius, fromPoint: false);
            if (goal is null)
                return Fail(NavOutcome.GoalBlocked, start.Value.I, start.Value.J, rawGi, rawGj);
            var (si, sj) = start.Value;
            var (gi, gj) = goal.Value;

            long margin = _limits.WindowMarginMm / _node, maxNodes = _limits.WindowMaxMm / _node;
            if (Math.Abs(gi - si) + 1 > maxNodes - 2 * margin || Math.Abs(gj - sj) + 1 > maxNodes - 2 * margin)
                return Fail(NavOutcome.TooFar, si, sj, gi, gj);
            _wi0 = Math.Max(Math.Min(si, gi) - margin, _grid.NodeMinI);
            _wj0 = Math.Max(Math.Min(sj, gj) - margin, _grid.NodeMinJ);
            _wi1 = Math.Min(Math.Max(si, gi) + margin, _grid.NodeMaxI);
            _wj1 = Math.Min(Math.Max(sj, gj) + margin, _grid.NodeMaxJ);
            _ww = _wi1 - _wi0 + 1;
            var window = WatchOf(_wi0, _wj0, _wi1, _wj1);
            long cells = _ww * (_wj1 - _wj0 + 1);
            int startIdx = Index(si, sj), goalIdx = Index(gi, gj);

            int probeNodes = 0;
            if (startIdx != goalIdx)
            {
                var fromGoal = Flood(goalIdx, startIdx, cells, ref probeNodes);
                if (fromGoal == Probe.Enclosed
                    || (fromGoal == Probe.Inconclusive && Flood(startIdx, goalIdx, cells, ref probeNodes) == Probe.Enclosed))
                    return new NavPlan(NavOutcome.Enclosed, ImmutableArray<NavPoint>.Empty, false, window, 0, probeNodes);
            }

            var (outcome, expansions) = AStar(startIdx, goalIdx, (int)(gi - _wi0), (int)(gj - _wj0), cells);
            if (outcome != NavOutcome.Found)
                return new NavPlan(outcome, ImmutableArray<NavPoint>.Empty, false, window, expansions, probeNodes);

            var candidates = new List<NavPoint> { from };
            foreach (int idx in PathTo(goalIdx, startIdx))
                candidates.Add(new NavPoint(_grid.CentreOf(_wi0 + idx % _ww), _grid.CentreOf(_wj0 + idx / _ww)));
            if (Standable(_grid, _agent, _q.IsGateOpen, to, _radius))
                candidates.Add(to);
            var corners = Pull(candidates);
            bool partial = corners.Count > _limits.MaxCorners;
            var kept = partial ? corners.Take(_limits.MaxCorners).ToImmutableArray() : corners.ToImmutableArray();
            return new NavPlan(NavOutcome.Found, kept, partial, window, expansions, probeNodes);
        }

        private NavPlan Fail(NavOutcome outcome, long i0, long j0, long i1, long j1)
        {
            long margin = _limits.WindowMarginMm / _node;
            long ai = Math.Min(i0, i1) - margin, aj = Math.Min(j0, j1) - margin, bi = Math.Max(i0, i1) + margin, bj = Math.Max(j0, j1) + margin;
            long ci0 = Math.Max(ai, _grid.NodeMinI), cj0 = Math.Max(aj, _grid.NodeMinJ), ci1 = Math.Min(bi, _grid.NodeMaxI), cj1 = Math.Min(bj, _grid.NodeMaxJ);
            // Clipped to the grid when anything of it is left; otherwise the unclipped rectangle, so the watch is always ordered.
            var window = ci0 <= ci1 && cj0 <= cj1 && !_grid.Tiles.IsEmpty ? WatchOf(ci0, cj0, ci1, cj1) : WatchOf(ai, aj, bi, bj);
            return new NavPlan(outcome, ImmutableArray<NavPoint>.Empty, false, window, 0, 0);
        }

        private NavRect WatchOf(long i0, long j0, long i1, long j1) =>
            new(i0 * _node, j0 * _node, i1 * _node + _node - 1, j1 * _node + _node - 1);

        private int Index(long i, long j) => (int)((j - _wj0) * _ww + (i - _wi0));

        private bool OnBorder(long i, long j) => i == _wi0 || i == _wi1 || j == _wj0 || j == _wj1;

        /// <summary>First use of a window node in this generation: clear its entries.</summary>
        private void Touch(int idx)
        {
            if (_scratch.Generation[idx] == _gen)
                return;
            _scratch.Generation[idx] = _gen;
            _scratch.G[idx] = int.MaxValue;
            _scratch.Dir[idx] = 0;
        }

        /// <summary>Whether a window node is walkable, worked out once per generation; the tile last looked in is tried first.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private bool Walk(int idx)
        {
            Touch(idx);
            byte d = _scratch.Dir[idx];
            if ((d & NavScratch.WalkKnown) != 0)
                return (d & NavScratch.WalkOk) != 0;
            long i = _wi0 + idx % _ww, j = _wj0 + idx / _ww, n = _grid.Config.TileNodes;
            bool ok;
            if (_tile is not null && i >= _tile.I0 && i < _tile.I0 + n && j >= _tile.J0 && j < _tile.J0 + n)
                ok = _grid.WalkableAt(_tile, (int)((j - _tile.J0) * n + (i - _tile.I0)), i, j, _agent, _q.IsGateOpen);
            else if (_grid.TryLocate(i, j, out var tile, out int local))
            {
                _tile = tile;
                ok = _grid.WalkableAt(tile, local, i, j, _agent, _q.IsGateOpen);
            }
            else
                ok = false;
            _scratch.Dir[idx] = (byte)(_scratch.Dir[idx] | NavScratch.WalkKnown | (ok ? NavScratch.WalkOk : 0));
            return ok;
        }

        /// <summary>
        /// A 4-connected flood from <paramref name="seed"/> inside the window, of at most <see cref="NavLimits.ProbeNodes"/> nodes: connected
        /// when it reaches <paramref name="target"/>; an enclosure proved when it runs dry below its limit without touching the window's
        /// border; anything else proves nothing.
        /// </summary>
        private Probe Flood(int seed, int target, long cells, ref int probeNodes)
        {
            _gen = _scratch.Begin(cells);
            int limit = _limits.ProbeNodes;
            int[] queue = _scratch.QueueOf(limit);
            int head = 0, tail = 0, visited = 1;
            Touch(seed);
            _scratch.Dir[seed] |= NavScratch.Closed;
            queue[tail++] = seed;
            probeNodes++;
            if (visited >= limit || OnBorder(_wi0 + seed % _ww, _wj0 + seed / _ww))
                return Probe.Inconclusive;
            while (head < tail)
            {
                int idx = queue[head++];
                long i = _wi0 + idx % _ww, j = _wj0 + idx / _ww;
                for (int d = 0; d < 4; d++)
                {
                    long ni = i + Neighbours[d].Di, nj = j + Neighbours[d].Dj;
                    if (ni < _wi0 || ni > _wi1 || nj < _wj0 || nj > _wj1)
                        continue;
                    int n = Index(ni, nj);
                    Touch(n);
                    if ((_scratch.Dir[n] & NavScratch.Closed) != 0 || !Walk(n))
                        continue;
                    _scratch.Dir[n] |= NavScratch.Closed;
                    visited++;
                    probeNodes++;
                    if (n == target)
                        return Probe.Connected;
                    if (visited >= limit || OnBorder(ni, nj))
                        return Probe.Inconclusive;
                    queue[tail++] = n;
                }
            }
            return Probe.Enclosed;
        }

        /// <summary>
        /// Optimal A* over the window, 8-connected, no corner cutting. The heap orders by (f, h, index), a total order, so the nodes pop in
        /// one sequence whatever the heap's layout. A node has one entry: a shorter way to it moves its entry up rather than adding another.
        /// The heuristic is the octile distance.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private (NavOutcome Outcome, int Expansions) AStar(int startIdx, int goalIdx, int goalI, int goalJ, long cells)
        {
            _gen = _scratch.Begin(cells);
            _scratch.EnsureHeap((int)cells);
            long[] hk = _scratch.HeapKey;
            int[] hx = _scratch.HeapIdx, at = _scratch.HeapAt, g = _scratch.G;
            byte[] dir = _scratch.Dir;
            int w = (int)_ww, lastI = w - 1, lastJ = (int)(_wj1 - _wj0), count = 0, expansions = 0;
            bool touchedBorder = false;
            Span<bool> open = stackalloc bool[4];

            Touch(startIdx);
            g[startIdx] = 0;
            int h0 = Octile(startIdx % w, startIdx / w);
            Place(Key(h0, h0), startIdx, count++);
            while (count > 0)
            {
                int idx = hx[0];
                Pop();
                if (expansions == _limits.MaxExpansions)
                    return (NavOutcome.Budget, expansions);
                expansions++;
                if (idx == goalIdx)
                    return (NavOutcome.Found, expansions);
                dir[idx] |= NavScratch.Closed;
                int li = idx % w, lj = idx / w;
                if (li == 0 || li == lastI || lj == 0 || lj == lastJ)
                    touchedBorder = true;
                for (int d = 0; d < 8; d++)
                {
                    var (di, dj, cost) = Neighbours[d];
                    int ni = li + di, nj = lj + dj;
                    if (d < 4)
                        open[d] = false;
                    if (ni < 0 || ni > lastI || nj < 0 || nj > lastJ)
                        continue;
                    if (d >= 4 && !(open[DiagonalNeeds[d - 4].A] && open[DiagonalNeeds[d - 4].B]))
                        continue;
                    int n = nj * w + ni;
                    if (!Walk(n))
                        continue;
                    if (d < 4)
                        open[d] = true;
                    int ng = g[idx] + cost;
                    if ((dir[n] & NavScratch.Closed) != 0 || ng >= g[n])
                        continue;
                    bool entered = g[n] != int.MaxValue;
                    g[n] = ng;
                    dir[n] = (byte)((dir[n] & ~0x0F) | NavScratch.ParentSet | d);
                    int h = Octile(ni, nj);
                    Place(Key(ng + h, h), n, entered ? at[n] : count++);
                }
            }
            return (touchedBorder ? NavOutcome.NotInWindow : NavOutcome.Exhausted, expansions);

            int Octile(int i, int j)
            {
                int a = Math.Abs(goalI - i), b = Math.Abs(goalJ - j);
                return a > b ? 1000 * a + 414 * b : 1000 * b + 414 * a;
            }

            // (f, h) as one number, f above h: h is below 2^20 in any window, so the pair orders as (f, h) does.
            static long Key(int f, int h) => ((long)f << 20) | (uint)h;

            bool Less(long k, int x, long k2, int x2) => k < k2 || (k == k2 && x < x2);

            // An entry with a smaller key than before, or a new one at the end, sifted up from its slot.
            void Place(long k, int x, int c)
            {
                while (c > 0)
                {
                    int p = (c - 1) >> 1;
                    if (!Less(k, x, hk[p], hx[p]))
                        break;
                    hk[c] = hk[p];
                    hx[c] = hx[p];
                    at[hx[c]] = c;
                    c = p;
                }
                hk[c] = k;
                hx[c] = x;
                at[x] = c;
            }

            // The least entry leaves; the last one sifts down from the root.
            void Pop()
            {
                count--;
                if (count == 0)
                    return;
                long k = hk[count];
                int x = hx[count], c = 0;
                while (true)
                {
                    int l = 2 * c + 1;
                    if (l >= count)
                        break;
                    int m = l + 1 < count && Less(hk[l + 1], hx[l + 1], hk[l], hx[l]) ? l + 1 : l;
                    if (!Less(hk[m], hx[m], k, x))
                        break;
                    hk[c] = hk[m];
                    hx[c] = hx[m];
                    at[hx[c]] = c;
                    c = m;
                }
                hk[c] = k;
                hx[c] = x;
                at[x] = c;
            }
        }

        /// <summary>The node path from the start to the goal, by the parent directions A* left.</summary>
        private List<int> PathTo(int goalIdx, int startIdx)
        {
            var path = new List<int> { goalIdx };
            int idx = goalIdx;
            while (idx != startIdx)
            {
                var (di, dj, _) = Neighbours[_scratch.Dir[idx] & 0x07];
                idx = Index(_wi0 + idx % _ww - di, _wj0 + idx / _ww - dj);
                path.Add(idx);
            }
            path.Reverse();
            return path;
        }

        /// <summary>
        /// Greedy, exact string pulling: from each anchor, reach as far along the candidates as a clear straight line allows, and emit that
        /// point as the next corner. Lattice edges are always clear, so every emission advances.
        /// </summary>
        private List<NavPoint> Pull(List<NavPoint> candidates)
        {
            var corners = new List<NavPoint>();
            int a = 0;
            while (a < candidates.Count - 1)
            {
                int k = a + 1;
                while (k + 1 < candidates.Count && Clear(_grid, _agent, _q.IsGateOpen, candidates[a], candidates[k + 1], _radius))
                    k++;
                corners.Add(candidates[k]);
                a = k;
            }
            return corners;
        }
    }
}
