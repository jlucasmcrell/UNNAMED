using System.Collections.Immutable;
using System.Diagnostics;
using UNNAMED.Content;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit.Abstractions;

namespace UNNAMED.Application.Tests;

/// <summary>Navigation over the game's own region (M7 design §3.20.3), through a real session.</summary>
public class NavigationTests
{
    private static readonly NavAgent Person = new(0, true);
    private static readonly Func<NavInput, bool> EveryGatePassable = _ => true;
    private readonly ITestOutputHelper _output;

    public NavigationTests(ITestOutputHelper output) => _output = output;
    // N-A1
    [Fact]
    public void TheHollow_EveryProtectedPointIsReachable()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Tester", 42);
        var grid = simulation.Navigation.Grid;

        // NAV006's points, on the grid the running world built.
        Assert.Empty(NavigationContent.Unreachable(session.Setup.Layout, session.Setup.Navigation, grid));

        // The doorway lanes of §3.3, by centre: the lodge's and the smithy's 1.6 m doors, the gap by the smithy fence, and the beam
        // line at the Woundmoss, which a standing body cannot pass.
        Assert.Equal(new long[] { 127_625, 127_875, 128_125, 128_375 }, LanesAlongZ(grid, 51_800, 126_000, 130_000));
        Assert.Equal(new long[] { 141_625, 141_875, 142_125, 142_375 }, LanesAlongZ(grid, 53_200, 140_000, 144_000));
        Assert.Equal(new long[] { 51_875, 52_125, 52_375 }, LanesAlongX(grid, 146_200, 51_400, 53_000));
        foreach (long z in new long[] { 131_625, 131_875, 132_125, 132_375 })
            Assert.Empty(LanesAlongX(grid, z, 144_600, 153_400));

        Assert.Equal(0, session.SubscriberFailures);
    }

    /// <summary>The centres of the person-walkable nodes, every gate passable, in the node column holding x, with z in [z0, z1].</summary>
    private static long[] LanesAlongZ(NavGrid grid, long x, long z0, long z1) =>
        Range(grid, z0, z1).Where(j => grid.Walkable(grid.NodeOf(x), j, Person, EveryGatePassable)).Select(grid.CentreOf).ToArray();

    private static long[] LanesAlongX(NavGrid grid, long z, long x0, long x1) =>
        Range(grid, x0, x1).Where(i => grid.Walkable(i, grid.NodeOf(z), Person, EveryGatePassable)).Select(grid.CentreOf).ToArray();

    private static IEnumerable<long> Range(NavGrid grid, long min, long max)
    {
        for (long n = grid.NodeOf(min); n <= grid.NodeOf(max); n++)
        {
            if (grid.CentreOf(n) >= min && grid.CentreOf(n) <= max)
                yield return n;
        }
    }

    // N-A10 (E1: the full build, and every authored protected-point pair; E4 and E9 add the mover routes)
    [Fact]
    public void Navigation_StaysWithinBudget()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var layout = session.Setup.Layout;
        var config = session.Setup.Navigation;
        var bounds = NavigationLayout.Bounds(layout);
        var keys = NavigationLayout.TileKeys(layout);
        var inputs = NavigationLayout.AuthoredInputs(layout, config);

        // The full build of the four tiles.
        NavGrid grid = NavGrid.Build(config, bounds, keys, inputs);
        var builds = new List<double>();
        for (int n = 0; n < 7; n++)
        {
            var clock = Stopwatch.StartNew();
            grid = NavGrid.Build(config, bounds, keys, inputs);
            builds.Add(clock.Elapsed.TotalMilliseconds);
        }
        double buildMs = builds.Order().ElementAt(builds.Count / 2);
        Assert.True(buildMs < 60, $"a full build took {buildMs:F2} ms (CI bound 60 ms; ASTRAL target 20 ms)");

        // Every ordered pair of authored protected points no more than 88 m apart on either axis, planned for a person with every gate
        // passable, as the placement check's graph is (§3.13). Diagnostic coverage (the owner's ruling of 2026-09-25): every pair is found
        // within the cap; the mean is ASTRAL evidence in Release, reported, not asserted here.
        var points = ProtectedPoints(layout, grid);
        var scratch = new NavScratch();
        var query = new NavQuery(grid, EveryGatePassable, config, scratch, null);
        NavSearch.Plan(query, Person, points[0].At, points[1].At);   // warm the scratch and the JIT
        var times = new List<double>();
        int maxExpansions = 0;
        long totalExpansions = 0;
        string worst = "";
        var failures = new List<string>();
        var all = new List<(int Expansions, double Ms, string Pair)>();
        foreach (var a in points)
        {
            foreach (var b in points)
            {
                if (a == b || Math.Abs(a.At.XMm - b.At.XMm) > 88_000 || Math.Abs(a.At.ZMm - b.At.ZMm) > 88_000)
                    continue;
                var clock = Stopwatch.StartNew();
                var plan = NavSearch.Plan(query, Person, a.At, b.At);
                times.Add(clock.Elapsed.TotalMilliseconds);
                if (plan.Outcome != NavOutcome.Found)
                    failures.Add($"{a.Label} -> {b.Label}: {plan.Outcome}");
                totalExpansions += plan.Expansions;
                all.Add((plan.Expansions, clock.Elapsed.TotalMilliseconds, $"{a.Label} -> {b.Label}"));
                if (plan.Expansions > maxExpansions)
                    (maxExpansions, worst) = (plan.Expansions, $"{a.Label} -> {b.Label}");
            }
        }
        double meanMs = times.Average();
        _output.WriteLine($"full build: median {buildMs:F2} ms of {builds.Count} (min {builds.Min():F2}, max {builds.Max():F2})");
        _output.WriteLine($"authored pairs: {times.Count} over {points.Count} points; mean {meanMs:F3} ms, max {times.Max():F3} ms; " +
            $"max {maxExpansions} expansions ({worst}); {1_000_000.0 * times.Sum() / Math.Max(1, totalExpansions):F0} ns per expansion overall");
        foreach (var p in points)
            _output.WriteLine($"  point {p.Label} at ({p.At.XMm}, {p.At.ZMm})");
        // The costliest pairs, recorded as evidence: the sweep is diagnostic, so none is held to the two-thirds bound of a mover route.
        foreach (var (e, ms, pair) in all.OrderByDescending(x => x.Expansions).Take(5))
            _output.WriteLine($"  {e,6} expansions {ms,8:F3} ms  {pair}");
        _output.WriteLine($"  pairs over two thirds of the cap: {all.Count(x => x.Expansions > 2 * config.Limits.MaxExpansions / 3)}; median {all.Select(x => x.Ms).Order().ElementAt(all.Count / 2):F3} ms");

        // The routes the design's model measured, logged beside it (§3.7.7: 9,709 and 1,179 expansions).
        foreach (var (label, from, to) in new[] { ("west of the lodge -> Renn", new NavPoint(30_000, 128_000), new NavPoint(46_500, 126_200)),
                     ("Kera -> (100.75, 96.0)", new NavPoint(61_600, 139_600), new NavPoint(100_750, 96_000)) })
        {
            var p = NavSearch.Plan(new NavQuery(grid, _ => false, config, new NavScratch(), null), Person, from, to);
            _output.WriteLine($"  model route {label}: {p.Outcome}, {p.Expansions} expansions, {p.Corners.Length} corners");
        }

        Assert.Empty(failures);
        Assert.All(all, x => Assert.True(x.Expansions <= config.Limits.MaxExpansions, x.Pair));
        Assert.True(times.Count > 100, $"only {times.Count} pairs");
        Assert.Equal(0, session.SubscriberFailures);
    }

    // NoDoorCloses_OnAnyBody: the authored-door half (E4; E6 adds the piece doors)
    /// <summary>
    /// The character cannot close an authored door on anyone standing in it - themselves, a companion, any other NPC or a living
    /// creature (the Phase-1 technical audit, L-16, now <c>SystemContext.BodyIn</c>) - and closes it as before once the doorway is clear.
    /// </summary>
    [Fact]
    public void NoDoorCloses_OnAnyBody()
    {
        const string door = "door.longhouse", tavar = "npc.ashen_hollow.tavar_orr", renn = "npc.ashen_hollow.renn_vale";
        const long inX = 51_800, inZ = 128_000;   // the middle of the lodge doorway
        (double X, double Z) doorway = (51.8, 128), outside = (53.5, 128);
        var none = Array.Empty<(string, double, double, string)>();
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var rennInTheDoorway = session.Setup with
        {
            Layout = session.Setup.Layout with
            {
                Npcs = session.Setup.Layout.Npcs.Select(n => n.NpcId == renn ? n with { XMm = inX, ZMm = inZ } : n).ToImmutableArray(),
            },
        };
        var tavarWaiting = new CompanionRecord(tavar, CompanionOrder.Wait, CompanionCondition.Up, inX, inZ, 90_000, 100);

        (string? Refused, bool Open) OpenThenClose(Arena arena)
        {
            Assert.Null(arena.Submit(new InteractCommand(arena.Player, door)));
            arena.Tick();
            string? refused = arena.Submit(new InteractCommand(arena.Player, door));
            return (refused, arena.Simulation.Doors.Single(d => d.Site.Key == door).Open);
        }

        var refusal = ("door.longhouse cannot close: something is in the doorway", true);
        Assert.Equal(refusal, OpenThenClose(Arena.OpenCreatures(session, session.Setup, doorway, 270, none)));
        Assert.Equal(refusal, OpenThenClose(Arena.OpenCreatures(session, session.Setup, outside, 270, none, r => r.WithCompanions(new[] { tavarWaiting }))));
        Assert.Equal(refusal, OpenThenClose(Arena.OpenCreatures(session, rennInTheDoorway, outside, 270, none)));
        Assert.Equal(refusal, OpenThenClose(Arena.OpenCreatures(session, session.Setup, outside, 270, new[] { (Arena.Wolf, 51.8, 128.0, "sleeper") })));

        // The doorway clear: it closes, as it always has.
        Assert.Equal(((string?)null, false), OpenThenClose(Arena.OpenCreatures(session, session.Setup, outside, 270, none)));
        Assert.Equal(0, session.SubscriberFailures);
    }

    /// <summary>
    /// The authored protected points of §3.13 at a new game - the spawn, each NPC's place, the containers, stations, resource nodes and
    /// switches within reach, and both approach points of each door - each at its own position where a body can stand there, and
    /// otherwise at the nearest walkable node within its reach (a switch's rock, the iron seam's rock face).
    /// </summary>
    private static List<(string Label, NavPoint At)> ProtectedPoints(RegionLayout layout, NavGrid grid)
    {
        var points = new List<(string, NavPoint)>
        {
            Resolve(grid, "spawn", new NavPoint(layout.Spawn.XMm, layout.Spawn.ZMm), null, 1_600),
        };
        points.AddRange(layout.Npcs.Select(n => Resolve(grid, n.NpcId, new NavPoint(n.XMm, n.ZMm), null, 1_600)));
        points.AddRange(layout.Containers.Select(c => Resolve(grid, c.Key, new NavPoint(c.XMm, c.ZMm), null, 1_600)));
        points.AddRange(layout.Stations.Select(s => Resolve(grid, s.Key, new NavPoint(s.XMm, s.ZMm), null, 1_600)));
        points.AddRange(layout.Nodes.Select(n => Resolve(grid, "node " + n.Name, new NavPoint(n.XMm, n.ZMm), null, 1_600)));
        points.AddRange(layout.Switches.Select(s =>
        {
            var (x, z) = Footprints.Center(s.Body);
            return Resolve(grid, s.Key, new NavPoint(x, z), s.Body, 1_600);
        }));
        foreach (var door in layout.Doors)
        {
            var approach = NavigationContent.ApproachPoints(door.ClosedFootprint);
            points.Add(Resolve(grid, door.Key + " a", approach[0], null, 250));
            points.Add(Resolve(grid, door.Key + " b", approach[1], null, 250));
        }
        return points;
    }

    private static (string, NavPoint) Resolve(NavGrid grid, string label, NavPoint at, Blocker? shape, long reachMm)
    {
        if (shape is null && NavSearch.Standable(grid, Person, EveryGatePassable, at, 350))
            return (label, at);
        var near = new List<(long D2, long J, long I)>();
        for (long j = grid.NodeOf(at.ZMm - reachMm - 3_000); j <= grid.NodeOf(at.ZMm + reachMm + 3_000); j++)
        {
            for (long i = grid.NodeOf(at.XMm - reachMm - 3_000); i <= grid.NodeOf(at.XMm + reachMm + 3_000); i++)
            {
                long cx = grid.CentreOf(i), cz = grid.CentreOf(j);
                bool inReach = shape is null
                    ? (cx - at.XMm) * (cx - at.XMm) + (cz - at.ZMm) * (cz - at.ZMm) <= reachMm * reachMm
                    : NavGeometry.Within(shape, cx, cz, reachMm);
                if (inReach && grid.Walkable(i, j, Person, EveryGatePassable))
                    near.Add(((cx - at.XMm) * (cx - at.XMm) + (cz - at.ZMm) * (cz - at.ZMm), j, i));
            }
        }
        Assert.True(near.Count > 0, $"nothing walkable within reach of {label}");
        var (_, nj, ni) = near.Min();
        return (label, new NavPoint(grid.CentreOf(ni), grid.CentreOf(nj)));
    }
}
