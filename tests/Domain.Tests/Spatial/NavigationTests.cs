using System.Collections.Immutable;
using System.Reflection;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Tests.Spatial;

/// <summary>Navigation v1 over synthetic geometry (M7 design §3.20.1): the grid, the planner and the route, pure.</summary>
public class NavigationTests
{
    private static readonly NavAgent Opener = new(0, true);
    private static readonly NavAgent NonOpener = new(0, false);
    private static readonly Func<NavInput, bool> AllShut = _ => false;

    private static NavInput Box(string id, long x0, long z0, long x1, long z1, NavInputKind kind = NavInputKind.Solid, long height = 2_000) =>
        new(kind, new BoxBlocker(id, x0, z0, x1, z1, height), kind == NavInputKind.Solid ? null : id);

    private static NavInput Circle(string id, long x, long z, long radius) => new(NavInputKind.Solid, new CircleBlocker(id, x, z, radius, 2_000), null);

    private static IEnumerable<NavTileKey> TilesOver(long widthMm, long heightMm) =>
        from tz in Enumerable.Range(0, (int)((heightMm - 1) / NavConfig.TileMm + 1))
        from tx in Enumerable.Range(0, (int)((widthMm - 1) / NavConfig.TileMm + 1))
        select new NavTileKey(tx, tz);

    private static NavGrid Field(long widthMm, long heightMm, IEnumerable<NavInput> inputs, NavConfig? config = null) =>
        NavGrid.Build(config ?? NavConfig.Default, new NavRect(0, 0, widthMm, heightMm), TilesOver(widthMm, heightMm), inputs);

    private static NavQuery Query(NavGrid grid, Func<NavInput, bool>? open = null, NavScratch? scratch = null, NavConfig? config = null) =>
        new(grid, open ?? AllShut, config ?? grid.Config, scratch ?? new NavScratch(), null);

    private static NavPoint P(long x, long z) => new(x, z);

    /// <summary>A test-local generator: never System.Random.</summary>
    private sealed class Lcg
    {
        private ulong _state;

        public Lcg(ulong seed) => _state = seed;

        public long Next(long min, long max)
        {
            _state = _state * 6364136223846793005UL + 1442695040888963407UL;
            return min + (long)((_state >> 33) % (ulong)(max - min + 1));
        }
    }

    /// <summary>A 10 x 8 m room with 0.4 m walls, its south wall at z 16-16.4 m, optionally with a 1.6 m doorway at x 19.2-20.8 m.</summary>
    private static List<NavInput> Room(bool doorway)
    {
        var walls = new List<NavInput>
        {
            Box("north", 15_000, 23_600, 25_000, 24_000),
            Box("west", 15_000, 16_000, 15_400, 24_000),
            Box("east", 24_600, 16_000, 25_000, 24_000),
        };
        if (doorway)
        {
            walls.Add(Box("south_west", 15_000, 16_000, 19_200, 16_400));
            walls.Add(Box("south_east", 20_800, 16_000, 25_000, 16_400));
        }
        else
        {
            walls.Add(Box("south", 15_000, 16_000, 25_000, 16_400));
        }
        return walls;
    }

    // N-D1
    [Fact]
    public void AnOpenField_RoutesStraight()
    {
        var grid = Field(40_000, 40_000, Array.Empty<NavInput>());
        var plan = NavSearch.Plan(Query(grid), Opener, P(10_000, 10_000), P(30_000, 25_000));

        Assert.Equal(NavOutcome.Found, plan.Outcome);
        Assert.Equal(new[] { P(30_000, 25_000) }, plan.Corners);
        long di = Math.Abs(grid.NodeOf(30_000) - grid.NodeOf(10_000)), dj = Math.Abs(grid.NodeOf(25_000) - grid.NodeOf(10_000));
        long octile = Math.Max(di, dj) * 1000 + Math.Min(di, dj) * 414;
        Assert.True(plan.Expansions * 1000L <= 2 * octile, $"{plan.Expansions} expansions for an octile distance of {octile / 1000.0} nodes");
    }

    // N-D3
    [Fact]
    public void ADoorway_IsTheWayIn()
    {
        var grid = Field(40_000, 40_000, Room(doorway: true));
        var from = P(8_000, 8_000);
        var plan = NavSearch.Plan(Query(grid), Opener, from, P(22_000, 22_000));

        Assert.Equal(NavOutcome.Found, plan.Outcome);
        var line = new[] { from }.Concat(plan.Corners).ToList();
        const long wallZ = 16_200;
        var crossings = line.Zip(line.Skip(1)).Where(s => (s.First.ZMm - wallZ) * (s.Second.ZMm - wallZ) < 0).ToList();
        var crossing = Assert.Single(crossings);
        var (a, b) = crossing;
        long x = a.XMm + (b.XMm - a.XMm) * (wallZ - a.ZMm) / (b.ZMm - a.ZMm);
        Assert.InRange(x, 19_200 + 350, 20_800 - 350);
    }

    // N-D4
    [Fact]
    public void ASealedRoom_IsEnclosed_WithoutAStar()
    {
        var grid = Field(40_000, 40_000, Room(doorway: false));
        var plan = NavSearch.Plan(Query(grid), Opener, P(8_000, 8_000), P(22_000, 22_000));

        Assert.Equal(NavOutcome.Enclosed, plan.Outcome);
        Assert.Equal(0, plan.Expansions);
        Assert.InRange(plan.ProbeNodes, 1, 2_048);
        Assert.Empty(plan.Corners);
    }

    // N-D5 (E6: a placed door's gate, keyed by its piece, too)
    [Theory]
    [InlineData("door.test")]
    [InlineData("pce_01JZZZZZZZZZZZZZZZZZZZZZZZ")]
    public void AClosedDoor_StopsANonOpener_NotAnOpener(string key)
    {
        var door = Box(key, 19_200, 16_000, 20_800, 16_400, NavInputKind.Door);
        var grid = Field(40_000, 40_000, Room(doorway: true).Append(door));
        string digest = grid.Digest();
        bool open = false;
        var query = Query(grid, _ => open);
        var from = P(8_000, 8_000);
        var to = P(22_000, 22_000);

        Assert.Equal(NavOutcome.Enclosed, NavSearch.Plan(query, NonOpener, from, to).Outcome);
        var openerShut = NavSearch.Plan(query, Opener, from, to);
        open = true;
        Assert.Equal(NavOutcome.Found, NavSearch.Plan(query, NonOpener, from, to).Outcome);
        var openerOpen = NavSearch.Plan(query, Opener, from, to);

        Assert.Equal(NavOutcome.Found, openerShut.Outcome);
        Assert.Equal(openerShut.Corners, openerOpen.Corners);
        Assert.Equal(digest, grid.Digest());
    }

    // N-D19
    /// <summary>
    /// The placement navigability check's four rules (M7 design §3.13) on synthetic ground: each refusal with its rule and reason, the
    /// shapes it must allow, and a flood count that does not depend on the scratch's history.
    /// </summary>
    [Fact]
    public void EditCheck_Rules()
    {
        var none = ImmutableArray<NavInput>.Empty;
        var noPoints = ImmutableArray<NavProtectedPoint>.Empty;
        NavEditVerdict Check(IEnumerable<NavInput> existing, IEnumerable<NavInput> solids, IEnumerable<NavInput>? doors = null,
            IEnumerable<NavProtectedPoint>? points = null, NavScratch? scratch = null)
        {
            var grid = Field(40_000, 40_000, existing);
            return NavEditCheck.Check(grid, grid.Config, scratch ?? new NavScratch(), solids.ToImmutableArray(), (doors ?? none).ToImmutableArray(),
                (points ?? noPoints).ToImmutableArray());
        }
        static NavRect At(long x, long z) => new(x, z, x, z);
        void Refused(string rule, string reason, NavEditVerdict verdict) =>
            Assert.Equal((false, rule, reason), (verdict.Ok, verdict.Rule, verdict.Reason));

        // V-N1: the room's doorway walled up seals it - with no protected point inside, the generic reason.
        var seal = Box("seal", 19_200, 16_000, 20_800, 16_400);
        var sealedHut = Check(Room(doorway: true), new[] { seal });
        Refused("V-N1", NavEditCheck.SealedReason, sealedHut);
        Assert.True(sealedHut.NodesFlooded > 0);
        // A doorless one-square hut: three walls standing, and the fourth closes it - a placement is one piece, so the pocket borders it.
        var hut = new[]
        {
            Box("hut_s", 9_800, 9_800, 13_200, 10_200), Box("hut_n", 9_800, 12_800, 13_200, 13_200),
            Box("hut_w", 9_800, 9_800, 10_200, 13_200), Box("hut_e", 12_800, 9_800, 13_200, 13_200),
        };
        Refused("V-N1", NavEditCheck.SealedReason, Check(hut.Take(3), hut.Skip(3)));
        // With someone inside, the refusal names them.
        var you = new NavProtectedPoint("you", NavPointKind.Body, At(11_500, 11_500), 350, 1_600);
        Refused("V-N1", "that would shut you in", Check(hut.Take(3), hut.Skip(3), points: new[] { you }));

        // V-N2: a lone post over an NPC's place.
        var renn = new NavProtectedPoint("Renn Vale", NavPointKind.NpcSite, At(5_000, 5_000), 350, 1_600);
        Refused("V-N2", "that would wall in Renn Vale's place", Check(none, new[] { Box("post", 4_800, 4_800, 5_600, 5_600) }, points: new[] { renn }));

        // V-N3: a bench whose work anchor stands against a wall that is already there.
        var anchor = new NavProtectedPoint("Anvil Bench", NavPointKind.NewSite, At(30_600, 32_000), 350, 250);
        Refused("V-N3", "nothing could reach the Anvil Bench", Check(new[] { Box("wall", 30_000, 30_000, 30_400, 34_000) },
            new[] { Box("bench", 31_000, 31_500, 31_600, 32_500) }, points: new[] { anchor }));

        // V-N4: a door whose far side opens onto a wall.
        var leaf = Box("pce_door", 5_200, 20_000, 6_800, 20_400, NavInputKind.Door);
        Refused("V-N4", NavEditCheck.DoorReason, Check(new[] { Box("behind", 5_000, 20_800, 7_000, 22_000) }, none, new[] { leaf }));

        // Allowed: a U-shaped hut; the room's south wall with a 1.6 m gap; a wall against a room already sealed.
        Assert.True(Check(none, hut.Take(3)).Ok);
        var roomWithoutSouth = Room(doorway: false).Where(w => !w.Shape.Id.StartsWith("south", StringComparison.Ordinal)).ToList();
        Assert.True(Check(roomWithoutSouth, new[] { Box("south_west", 15_000, 16_000, 19_200, 16_400), Box("south_east", 20_800, 16_000, 25_000, 16_400) }).Ok);
        Assert.True(Check(Room(doorway: false), new[] { Box("beside", 25_000, 18_000, 25_400, 22_000) }).Ok);
        Assert.True(Check(none, none, new[] { leaf }).Ok);

        // The same check on a fresh scratch and on one a thousand searches old floods the same nodes.
        var used = new NavScratch();
        for (int k = 0; k < 1_000; k++)
            used.Begin(16);
        Assert.Equal(sealedHut.NodesFlooded, Check(Room(doorway: true), new[] { seal }, scratch: used).NodesFlooded);
    }

    // N-D20
    /// <summary>A chest's site lies inside its own solid part: V-N3 takes it as a reach point, and finds open ground within 1.6 m.</summary>
    [Fact]
    public void EditCheck_AChestSiteInsideItsOwnBox_IsAReachPoint()
    {
        var grid = Field(40_000, 40_000, Array.Empty<NavInput>());
        var chest = Box("chest", 10_000, 10_000, 11_000, 10_600);
        var site = new NavProtectedPoint("Storage Chest", NavPointKind.NewSite, new NavRect(10_500, 10_300, 10_500, 10_300), 0, 1_600);
        var verdict = NavEditCheck.Check(grid, grid.Config, new NavScratch(), ImmutableArray.Create(chest), ImmutableArray<NavInput>.Empty,
            ImmutableArray.Create(site));
        Assert.True(verdict.Ok, verdict.Reason);
    }

    // N-D6
    [Fact]
    public void BodyClasses_ThroughDoors()
    {
        var limits = NavConfig.Default.Limits;
        var config = NavConfig.Default with
        {
            Classes = ImmutableArray.Create(new NavClass("person", 350), new NavClass("medium", 450), new NavClass("large", 550)),
        };
        Assert.Null(config.Problem());
        foreach (var (top, lanes) in new[] { (11_600L, new[] { 3, 2, 2 }), (11_200L, new[] { 1, 1, 0 }) })
        {
            var inputs = new[] { Box("south", 20_000, 0, 20_400, 10_000), Box("north", 20_000, top, 20_400, 30_000) };
            var grid = Field(40_000, 30_000, inputs, config);
            long column = grid.NodeOf(20_200);
            for (int k = 0; k < 3; k++)
            {
                var agent = new NavAgent(k, true);
                int count = Enumerable.Range(0, 120).Count(j => grid.Walkable(column, j, agent, AllShut));
                Assert.True(lanes[k] == count, $"class {config.Classes[k].Id} through {top - 10_000} mm: {count} lanes, expected {lanes[k]}");
            }
            if (top == 11_200)
            {
                var query = Query(grid, config: config);
                Assert.NotEqual(NavOutcome.Found, NavSearch.Plan(query, new NavAgent(2, true), P(10_000, 10_600), P(30_000, 10_600)).Outcome);
                Assert.Equal(NavOutcome.Found, NavSearch.Plan(query, new NavAgent(0, true), P(10_000, 10_600), P(30_000, 10_600)).Outcome);
            }
        }
        Assert.Equal(limits, config.Limits);
    }

    /// <summary>A 6 x 6 m hut centred on the four-tile corner (100, 100) m, doorways west and east, its east door on the z = 100 m seam.</summary>
    private static List<NavInput> SeamHut() => new()
    {
        Box("hut_south", 97_000, 97_000, 103_000, 97_400),
        Box("hut_north", 97_000, 102_600, 103_000, 103_000),
        Box("hut_west_south", 97_000, 97_000, 97_400, 99_200),
        Box("hut_west_north", 97_000, 100_800, 97_400, 103_000),
        Box("hut_east_south", 102_600, 97_000, 103_000, 99_200),
        Box("hut_east_north", 102_600, 100_800, 103_000, 103_000),
        Box("door.hut_east", 102_600, 99_200, 103_000, 100_800, NavInputKind.Door),
        Circle("rock_seam", 100_000, 110_000, 1_500),
        Box("wall_seam_x", 99_000, 80_000, 101_000, 90_000),
    };

    // N-D9
    [Fact]
    public void TheSeam_IsNotARepresentationBoundary()
    {
        var inputs = SeamHut();
        var bounds = new NavRect(0, 0, 200_000, 200_000);
        var keys = TilesOver(200_000, 200_000).ToList();
        var full = NavGrid.Build(NavConfig.Default, bounds, keys, inputs);

        // (a) Any order of the four tiles gives the same grid.
        foreach (var order in Permutations(keys))
            Assert.Equal(full.Digest(), NavGrid.Build(NavConfig.Default, bounds, order, inputs).Digest());

        // (b) A tile built alone, or evicted and rebuilt, equals the same tile of the full build.
        foreach (var key in keys)
        {
            var alone = NavGrid.Build(NavConfig.Default, bounds, new[] { key }, inputs).Tiles.Single();
            var inFull = full.Tiles.Single(t => t.Key == key);
            Assert.True(alone.SolidFit.SequenceEqual(inFull.SolidFit));
            Assert.True(alone.ClosedFit.SequenceEqual(inFull.ClosedFit));
            Assert.Equal(inFull.Stamp, alone.Stamp);
        }

        // (c) A monolithic raster by the same rule equals the union of the tiles.
        var (solid, closed) = Monolithic(inputs, bounds, 800);
        for (long j = 0; j < 800; j++)
        {
            for (long i = 0; i < 800; i++)
            {
                Assert.True(solid[j * 800 + i] == full.SolidFitAt(i, j), $"solid ({i}, {j})");
                Assert.True(closed[j * 800 + i] == full.ClosedFitAt(i, j), $"closed ({i}, {j})");
            }
        }

        // (d) x = 100,000 mm is node 400, in tile 1.
        Assert.Equal(400, full.NodeOf(100_000));
        Assert.True(full.TryLocate(400, 10, out var tile, out _));
        Assert.Equal(new NavTileKey(1, 0), tile.Key);
        Assert.Equal(new NavTileKey(0, 1), (full.TryLocate(10, full.NodeOf(100_000), out var tileZ, out _) ? tileZ : null)!.Key);

        // (e) Routes across both seams equal a reference search over the monolithic raster.
        var lcg = new Lcg(0x9E3779B97F4A7C15);
        int pairs = 0;
        while (pairs < 50)
        {
            var a = RandomWalkableCentre(lcg, solid);
            var b = RandomWalkableCentre(lcg, solid);
            if (!((a.XMm < 100_000) != (b.XMm < 100_000) || (a.ZMm < 100_000) != (b.ZMm < 100_000)))
                continue;
            pairs++;
            var plan = NavSearch.Plan(Query(full), Opener, a, b);
            var (expansions, path) = ReferenceSearch(solid, full, a, b);
            Assert.Equal(NavOutcome.Found, plan.Outcome);
            Assert.Equal(expansions, plan.Expansions);
            var candidates = new List<NavPoint> { a };
            candidates.AddRange(path);
            candidates.Add(b);
            Assert.Equal(ReferencePull(full, candidates), plan.Corners);
        }
    }

    private static NavPoint RandomWalkableCentre(Lcg lcg, byte[] solid)
    {
        while (true)
        {
            long i = lcg.Next(260, 540), j = lcg.Next(260, 540);
            if (solid[j * 800 + i] > 0)
                return new NavPoint(i * 250 + 125, j * 250 + 125);
        }
    }

    /// <summary>Every node computed directly from all the inputs: the minimum of the bounds fit and each input's fit.</summary>
    private static (byte[] Solid, byte[] Closed) Monolithic(IReadOnlyList<NavInput> inputs, NavRect bounds, int side)
    {
        var config = NavConfig.Default;
        var solid = new byte[side * side];
        var closed = new byte[side * side];
        for (int j = 0; j < side; j++)
        {
            for (int i = 0; i < side; i++)
            {
                long x = i * 250L + 125, z = j * 250L + 125;
                int s = NavGeometry.BoundsFit(x, z, bounds, config), c = s;
                foreach (var input in inputs)
                {
                    int f = NavGeometry.FitAt(input.Shape, x, z, config);
                    c = Math.Min(c, f);
                    if (!input.IsGate)
                        s = Math.Min(s, f);
                }
                solid[j * side + i] = (byte)s;
                closed[j * side + i] = (byte)c;
            }
        }
        return (solid, closed);
    }

    /// <summary>
    /// The same A* written separately, over the monolithic raster: an opener walks every node its class fits against the solids.
    /// Both ends are node centres, so both snap to themselves.
    /// </summary>
    private static (int Expansions, List<NavPoint> Path) ReferenceSearch(byte[] solid, NavGrid grid, NavPoint a, NavPoint b)
    {
        long si = grid.NodeOf(a.XMm), sj = grid.NodeOf(a.ZMm), gi = grid.NodeOf(b.XMm), gj = grid.NodeOf(b.ZMm);
        long wi0 = Math.Max(Math.Min(si, gi) - 80, 0), wj0 = Math.Max(Math.Min(sj, gj) - 80, 0);
        long wi1 = Math.Min(Math.Max(si, gi) + 80, 799), wj1 = Math.Min(Math.Max(sj, gj) + 80, 799);
        long ww = wi1 - wi0 + 1;
        bool Walk(long i, long j) => i >= wi0 && i <= wi1 && j >= wj0 && j <= wj1 && solid[j * 800 + i] > 0;
        long Idx(long i, long j) => (j - wj0) * ww + (i - wi0);
        int H(long i, long j) => (int)(1000 * Math.Max(Math.Abs(gi - i), Math.Abs(gj - j)) + 414 * Math.Min(Math.Abs(gi - i), Math.Abs(gj - j)));
        var steps = new (int Di, int Dj, int Cost)[] { (1, 0, 1000), (0, 1, 1000), (-1, 0, 1000), (0, -1, 1000), (1, 1, 1414), (-1, 1, 1414), (-1, -1, 1414), (1, -1, 1414) };
        var g = new Dictionary<long, int>();
        var parent = new Dictionary<long, long>();
        var closedSet = new HashSet<long>();
        var open = new SortedSet<(int F, int H, long Idx)>();
        g[Idx(si, sj)] = 0;
        open.Add((H(si, sj), H(si, sj), Idx(si, sj)));
        int expansions = 0;
        while (open.Count > 0)
        {
            var top = open.Min;
            open.Remove(top);
            if (closedSet.Contains(top.Idx))
                continue;
            expansions++;
            if (top.Idx == Idx(gi, gj))
                break;
            closedSet.Add(top.Idx);
            long i = wi0 + top.Idx % ww, j = wj0 + top.Idx / ww;
            foreach (var (di, dj, cost) in steps)
            {
                long ni = i + di, nj = j + dj;
                if (!Walk(ni, nj) || (di != 0 && dj != 0 && (!Walk(ni, j) || !Walk(i, nj))))
                    continue;
                long n = Idx(ni, nj);
                if (closedSet.Contains(n))
                    continue;
                int ng = g[top.Idx] + cost;
                if (g.TryGetValue(n, out int old) && ng >= old)
                    continue;
                g[n] = ng;
                parent[n] = top.Idx;
                open.Add((ng + H(ni, nj), H(ni, nj), n));
            }
        }
        var path = new List<NavPoint>();
        for (long at = Idx(gi, gj); ; at = parent[at])
        {
            path.Add(new NavPoint((wi0 + at % ww) * 250 + 125, (wj0 + at / ww) * 250 + 125));
            if (at == Idx(si, sj))
                break;
        }
        path.Reverse();
        return (expansions, path);
    }

    private static List<NavPoint> ReferencePull(NavGrid grid, List<NavPoint> candidates)
    {
        var corners = new List<NavPoint>();
        for (int a = 0; a < candidates.Count - 1;)
        {
            int k = a + 1;
            while (k + 1 < candidates.Count && NavSearch.Clear(grid, Opener, AllShut, candidates[a], candidates[k + 1], 350))
                k++;
            corners.Add(candidates[k]);
            a = k;
        }
        return corners;
    }

    private static IEnumerable<List<T>> Permutations<T>(List<T> items)
    {
        if (items.Count <= 1)
        {
            yield return items.ToList();
            yield break;
        }
        for (int i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, k) => k != i).ToList();
            foreach (var tail in Permutations(rest))
                yield return tail.Prepend(items[i]).ToList();
        }
    }

    // N-D11
    [Fact]
    public void RectRebuild_EqualsFullBuild_AlwaysAndBack()
    {
        var bounds = new NavRect(0, 0, 200_000, 200_000);
        var keys = TilesOver(200_000, 200_000).ToList();
        var original = SeamHut();
        var grid = NavGrid.Build(NavConfig.Default, bounds, keys, original);
        var reversed = NavGrid.Build(NavConfig.Default, bounds, keys, Enumerable.Reverse(original));
        string originalDigest = grid.Digest();
        Assert.Equal(originalDigest, reversed.Digest());

        var lcg = new Lcg(0x2545F4914F6CDD1D);
        var placed = new List<NavInput>();
        var current = new List<NavInput>(original);
        for (int edit = 0; edit < 200; edit++)
        {
            NavInput changed;
            if (placed.Count > 0 && lcg.Next(0, 2) == 0)
            {
                changed = placed[(int)lcg.Next(0, placed.Count - 1)];
                placed.Remove(changed);
                current.Remove(changed);
            }
            else
            {
                // A third of the edits lie within 600 mm of a seam.
                bool nearSeam = edit % 3 == 0;
                long x = nearSeam ? 100_000 + lcg.Next(-1_200, 600) : lcg.Next(60_000, 140_000);
                long z = nearSeam ? lcg.Next(60_000, 140_000) : lcg.Next(60_000, 140_000);
                if (edit % 6 == 3)
                    (x, z) = (z, x);
                changed = lcg.Next(0, 1) == 0
                    ? Box($"edit_{edit}", x, z, x + lcg.Next(100, 3_400), z + lcg.Next(100, 3_400))
                    : Circle($"edit_{edit}", x, z, lcg.Next(100, 1_500));
                placed.Add(changed);
                current.Add(changed);
            }
            grid = grid.With(changed.Bounds, current);
            reversed = reversed.With(changed.Bounds, Enumerable.Reverse(current));
            string expected = NavGrid.Build(NavConfig.Default, bounds, keys, current).Digest();
            Assert.True(expected == grid.Digest(), $"edit {edit}: the rectangle rebuild differs from a full build");
            Assert.Equal(expected, reversed.Digest());
        }
        foreach (var input in placed.ToList())
        {
            current.Remove(input);
            grid = grid.With(input.Bounds, current);
        }
        Assert.Equal(originalDigest, grid.Digest());
    }

    // N-D12
    [Fact]
    public void TheSameQuery_GivesTheSameRoute()
    {
        // Symmetric about z = 20,125 mm, a line of node centres; so are the start and the goal.
        var inputs = new List<NavInput> { Box("obstacle", 19_000, 15_125, 21_000, 25_125), Circle("pebble", 5_000, 35_000, 300), Box("post", 35_000, 2_000, 35_500, 2_500) };
        var orders = new[] { inputs, Enumerable.Reverse(inputs).ToList(), new List<NavInput> { inputs[1], inputs[0], inputs[2] } };
        var from = P(10_125, 20_125);
        var to = P(30_125, 20_125);
        NavPlan? first = null;
        var reused = new NavScratch();
        foreach (var order in orders)
        {
            var grid = Field(40_000, 40_000, order);
            for (int repeat = 0; repeat < 100; repeat++)
            {
                var plan = NavSearch.Plan(Query(grid, scratch: repeat % 2 == 0 ? reused : new NavScratch()), Opener, from, to);
                first ??= plan;
                Assert.Equal(first.Outcome, plan.Outcome);
                Assert.Equal(first.Corners, plan.Corners);
                Assert.Equal(first.Expansions, plan.Expansions);
                Assert.Equal(first.ProbeNodes, plan.ProbeNodes);
            }
        }
        Assert.Equal(NavOutcome.Found, first!.Outcome);
        // The tie between the mirror-image routes resolves to the lower-(j, i) side: south of the obstacle.
        Assert.All(first.Corners, c => Assert.True(c.ZMm <= 20_125, $"corner {c} lies north of the line"));
        Assert.Contains(first.Corners, c => c.ZMm < 15_125);
    }

    // N-D13
    [Fact]
    public void TheBudget_EndsASearch_Deterministically()
    {
        var config = NavConfig.Default with { Limits = NavConfig.Default.Limits with { MaxExpansions = 16_000 } };
        var ring = new[]
        {
            Box("ring_south", 50_000, 20_000, 110_000, 20_400), Box("ring_north", 50_000, 79_600, 110_000, 80_000),
            Box("ring_west", 50_000, 20_000, 50_400, 80_000), Box("ring_east", 109_600, 20_000, 110_000, 80_000),
        };
        var grid = Field(200_000, 100_000, ring, config);
        for (int run = 0; run < 3; run++)
        {
            var plan = NavSearch.Plan(Query(grid, config: config), Opener, P(40_000, 50_000), P(80_000, 50_000));
            Assert.Equal(NavOutcome.Budget, plan.Outcome);
            Assert.Equal(16_000, plan.Expansions);
            Assert.Empty(plan.Corners);
        }
    }

    // N-D14
    [Fact]
    public void TheEnds_Snap_ByDistanceThenIndex()
    {
        var grid = Field(40_000, 40_000, new[] { Box("wall", 20_000, 0, 20_400, 40_000), Circle("rock", 30_000, 30_000, 2_500) });

        // 360 mm from the wall: node 82's centre (20,625) is within 400 mm of it, so the nearest walkable nodes are in column 83,
        // (20,875, 19,875) and (20,875, 20,125) at equal distance; the lower j wins.
        var start = P(20_760, 20_000);
        Assert.Equal((83L, 79L), NavSearch.SnapNode(grid, Opener, AllShut, start, 4, 300, fromPoint: true));
        var plan = NavSearch.Plan(Query(grid), Opener, start, P(25_000, 20_000));
        Assert.Equal(NavOutcome.Found, plan.Outcome);
        Assert.Equal(new[] { P(25_000, 20_000) }, plan.Corners);

        // A goal among four equally near centres snaps to the lowest (j, i).
        Assert.Equal((99L, 99L), NavSearch.SnapNode(grid, Opener, AllShut, P(25_000, 25_000), 8, 350, fromPoint: false));

        // Inside a rock, nothing stands within 2 m that a straight line reaches.
        Assert.Equal(NavOutcome.GoalBlocked, NavSearch.Plan(Query(grid), Opener, P(25_000, 20_000), P(30_000, 30_000)).Outcome);
        // Off the grid, nothing stands within 2 m at all.
        Assert.Equal(NavOutcome.GoalBlocked, NavSearch.Plan(Query(grid), Opener, P(25_000, 20_000), P(-5_000, 20_000)).Outcome);
        // And a start with nothing within 1 m is blocked before the goal is looked at.
        Assert.Equal(NavOutcome.StartBlocked, NavSearch.Plan(Query(grid), Opener, P(30_000, 30_000), P(-5_000, 20_000)).Outcome);
    }

    // N-D15
    [Fact]
    public void IntegerGeometry_EqualsSeparation()
    {
        var lcg = new Lcg(0xD1B54A32D192ED03);
        Blocker RandomShape()
        {
            long x = lcg.Next(-20_000, 20_000), z = lcg.Next(-20_000, 20_000);
            return lcg.Next(0, 1) == 0
                ? new BoxBlocker("b", x, z, x + lcg.Next(1, 5_000), z + lcg.Next(1, 5_000), 2_000)
                : new CircleBlocker("c", x, z, lcg.Next(1, 3_000), 2_000);
        }

        for (int n = 0; n < 20_000; n++)
        {
            var shape = RandomShape();
            var centre = Footprints.Center(shape);
            long px = centre.XMm + lcg.Next(-6_000, 6_000), pz = centre.ZMm + lcg.Next(-6_000, 6_000);
            long r = lcg.Next(0, 2_000);
            Assert.True(NavGeometry.PointClear(px, pz, r, shape) == (shape.Separation(px, pz, r) is null), $"point {px},{pz} r {r} against {shape}");
        }

        for (int n = 0; n < 5_000; n++)
        {
            var shape = RandomShape();
            var centre = Footprints.Center(shape);
            long ax = centre.XMm + lcg.Next(-5_000, 5_000), az = centre.ZMm + lcg.Next(-5_000, 5_000);
            long bx = ax + lcg.Next(-3_000, 3_000), bz = az + lcg.Next(-3_000, 3_000);
            long r = lcg.Next(1, 1_500);
            bool clear = NavGeometry.SegmentClear(ax, az, bx, bz, r, shape);
            long length = (long)Math.Ceiling(Math.Sqrt((double)(bx - ax) * (bx - ax) + (double)(bz - az) * (bz - az)));
            if (clear)
            {
                long samples = Math.Max(1, length / 10);
                for (long s = 0; s <= samples; s++)
                {
                    double t = (double)s / samples;
                    Assert.True(shape.Separation(ax + (bx - ax) * t, az + (bz - az) * t, r - 0.001) is null, $"segment {ax},{az} -> {bx},{bz} r {r} hits {shape} at t {t}");
                }
            }
            else
            {
                bool hit = false;
                long samples = Math.Max(1, length);
                for (long s = 0; s <= samples && !hit; s++)
                {
                    double t = (double)s / samples;
                    hit = shape.Separation(ax + (bx - ax) * t, az + (bz - az) * t, r + 1) is not null;
                }
                Assert.True(hit, $"segment {ax},{az} -> {bx},{bz} r {r} was refused, but passes {shape} everywhere");
            }
        }
    }

    // N-D16
    [Fact]
    public void EveryLatticeEdge_IsWalkableByTheBody()
    {
        var config = NavConfig.Default with
        {
            Classes = ImmutableArray.Create(new NavClass("person", 350), new NavClass("medium", 450), new NavClass("large", 550)),
        };
        var lcg = new Lcg(0x94D049BB133111EB);
        var inputs = new List<NavInput>();
        for (int n = 0; n < 16; n++)
        {
            long x = lcg.Next(500, 11_500), z = lcg.Next(500, 11_500);
            inputs.Add(n % 2 == 0 ? Box($"b{n}", x, z, x + lcg.Next(50, 2_000), z + lcg.Next(50, 2_000)) : Circle($"c{n}", x, z, lcg.Next(50, 900)));
        }
        var grid = Field(12_000, 12_000, inputs, config);
        var steps = new (int Di, int Dj)[] { (1, 0), (0, 1), (1, 1), (-1, 1) };
        int edges = 0;
        for (int k = 0; k < 3; k++)
        {
            var agent = new NavAgent(k, true);
            long r = config.Classes[k].RadiusMm;
            for (long j = 0; j < 48; j++)
            {
                for (long i = 0; i < 48; i++)
                {
                    if (!grid.Walkable(i, j, agent, AllShut))
                        continue;
                    foreach (var (di, dj) in steps)
                    {
                        if (!grid.Walkable(i + di, j + dj, agent, AllShut))
                            continue;
                        edges++;
                        double ax = i * 250 + 125, az = j * 250 + 125, bx = ax + di * 250, bz = az + dj * 250;
                        var near = inputs.Where(input => input.Bounds.Inflated(r + 400).Meets(new NavRect((long)Math.Min(ax, bx), (long)Math.Min(az, bz), (long)Math.Max(ax, bx), (long)Math.Max(az, bz)))).ToList();
                        int samples = di != 0 && dj != 0 ? 36 : 25;
                        for (int s = 0; s <= samples; s++)
                        {
                            double t = (double)s / samples;
                            foreach (var input in near)
                                Assert.True(input.Shape.Separation(ax + (bx - ax) * t, az + (bz - az) * t, r) is null, $"class {k}: edge ({i},{j})+({di},{dj}) meets {input.Shape.Id}");
                        }
                    }
                }
            }
        }
        Assert.True(edges > 1_000, $"only {edges} edges were checked");
    }

    // N-D21
    [Fact]
    public void NavRoute_IsBuiltOnlyThroughValidatingFactories()
    {
        var watch = new NavRect(0, 0, 1_000, 1_000);
        var goal = P(500, 500);
        var ok = NavRoute.Active(goal, ImmutableArray.Create(P(100, 100), goal), 3, 7, watch, false);

        void Refused(Func<NavRoute> build, string expected)
        {
            var e = Assert.Throws<ArgumentException>(() => build());
            Assert.Equal(expected, e.Message);
        }

        Refused(() => NavRoute.Active(goal, ImmutableArray<NavPoint>.Empty, 3, 7, watch, false),
            NavRoute.ProblemOf(NavRouteStatus.Active, 500, 500, ImmutableArray<NavPoint>.Empty, 3, 7, watch, false)!);
        var tooMany = Enumerable.Range(0, 33).Select(n => P(n, n)).ToImmutableArray();
        Refused(() => NavRoute.Active(goal, tooMany, 3, 7, watch, false), NavRoute.ProblemOf(NavRouteStatus.Active, 500, 500, tooMany, 3, 7, watch, false)!);
        var backwards = new NavRect(1_000, 0, 0, 1_000);
        Refused(() => NavRoute.Active(goal, ImmutableArray.Create(goal), 3, 7, backwards, false),
            NavRoute.ProblemOf(NavRouteStatus.Active, 500, 500, ImmutableArray.Create(goal), 3, 7, backwards, false)!);
        Refused(() => NavRoute.Unreachable(goal, 3, 7, backwards), NavRoute.ProblemOf(NavRouteStatus.Unreachable, 500, 500, ImmutableArray<NavPoint>.Empty, 3, 7, backwards, false)!);
        Refused(() => NavRoute.Active(goal, ImmutableArray.Create(goal), -1, 7, watch, false),
            NavRoute.ProblemOf(NavRouteStatus.Active, 500, 500, ImmutableArray.Create(goal), -1, 7, watch, false)!);
        Refused(() => ok.Advance(2), NavRoute.ProblemOf(NavRouteStatus.Active, 500, 500, ImmutableArray<NavPoint>.Empty, 3, 7, watch, false)!);
        Assert.Throws<InvalidOperationException>(() => NavRoute.None.Advance(0));

        Assert.NotNull(NavRoute.ProblemOf(NavRouteStatus.Unreachable, 500, 500, ImmutableArray.Create(goal), 3, 7, watch, false));
        Assert.NotNull(NavRoute.ProblemOf(NavRouteStatus.Unreachable, 500, 500, ImmutableArray<NavPoint>.Empty, 3, 7, watch, true));
        Assert.NotNull(NavRoute.ProblemOf(NavRouteStatus.None, 1, 0, ImmutableArray<NavPoint>.Empty, 0, 0, default, false));
        Assert.NotNull(NavRoute.ProblemOf(NavRouteStatus.None, 0, 0, ImmutableArray<NavPoint>.Empty, 0, 1, default, false));
        Assert.NotNull(NavRoute.ProblemOf((NavRouteStatus)9, 0, 0, ImmutableArray<NavPoint>.Empty, 0, 0, default, false));
        Assert.Null(NavRoute.None.Problem());
        Assert.Null(ok.Problem());

        Assert.Equal(ImmutableArray.Create(goal), ok.Advance(1).Corners);
        Assert.Equal(ok, NavRoute.Active(goal, ImmutableArray.Create(P(100, 100), goal), 3, 7, watch, false));
        Assert.Equal(ok.GetHashCode(), NavRoute.Active(goal, ImmutableArray.Create(P(100, 100), goal), 3, 7, watch, false).GetHashCode());
        Assert.NotEqual(ok, ok.Advance(1));

        Assert.Empty(typeof(NavRoute).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.All(typeof(NavRoute).GetProperties(BindingFlags.Public | BindingFlags.Instance), p => Assert.Null(p.SetMethod));
    }

    // N-D22
    [Fact]
    public void TheScratchGenerationWrap_ClearsAndAnswersTheSame()
    {
        var grid = Field(40_000, 40_000, Room(doorway: true).Concat(new[] { Box("closet_a", 2_000, 30_000, 8_000, 30_400) }));
        var queries = new (NavPoint From, NavPoint To)[]
        {
            (P(8_000, 8_000), P(22_000, 22_000)),
            (P(30_000, 5_000), P(5_000, 35_000)),
            (P(22_000, 22_000), P(10_000, 10_000)),
        };
        var fresh = queries.Select(q => NavSearch.Plan(Query(grid), Opener, q.From, q.To)).ToList();
        var wrapping = new NavScratch(int.MaxValue - 1);
        var sealedRoom = Field(40_000, 40_000, Room(doorway: false));
        var freshSealed = NavSearch.Plan(Query(sealedRoom), Opener, P(8_000, 8_000), P(22_000, 22_000));
        for (int round = 0; round < 2; round++)
        {
            Assert.Equal(freshSealed, NavSearch.Plan(Query(sealedRoom, scratch: wrapping), Opener, P(8_000, 8_000), P(22_000, 22_000)) with { Corners = freshSealed.Corners });
            for (int n = 0; n < queries.Length; n++)
            {
                var plan = NavSearch.Plan(Query(grid, scratch: wrapping), Opener, queries[n].From, queries[n].To);
                Assert.Equal(fresh[n].Outcome, plan.Outcome);
                Assert.Equal(fresh[n].Corners, plan.Corners);
                Assert.Equal(fresh[n].Expansions, plan.Expansions);
                Assert.Equal(fresh[n].ProbeNodes, plan.ProbeNodes);
            }
        }
        Assert.InRange(wrapping.Current, 1, 100);
    }
}
