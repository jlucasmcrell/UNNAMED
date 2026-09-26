using System.Collections.Immutable;
using System.Diagnostics;
using UNNAMED.Content;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit.Abstractions;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Runs alone, after every parallel test: N-A10's per-call bounds time single calls, and in the parallel run the other test classes'
/// work landed in them (a placement check's worst at 47 ms against a median under 2 ms).
/// </summary>
[CollectionDefinition(nameof(NavigationTimings), DisableParallelization = true)]
public class NavigationTimings
{
}

/// <summary>Navigation over the game's own region (M7 design §3.20.3), through a real session.</summary>
[Collection(nameof(NavigationTimings))]
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

    // N-A10 (E1: the full build, and every authored protected-point pair; E4 and E9 add the mover routes; E8 Kera's workshop routes)
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

        // One piece's rebuild (E5): a wall's 108 nodes restamped round its parts, as RebuildNavigation does it.
        var wall = new BoxBlocker("wall#0", 98_800, 98_800, 102_200, 99_200, 3_000);
        var withWall = inputs.Add(new NavInput(NavInputKind.Solid, wall, null));
        var rebuilds = new List<double>();
        long restamped = 0;
        for (int n = 0; n < 7; n++)
        {
            var clock = Stopwatch.StartNew();
            grid.With(new NavRect(wall.MinXMm, wall.MinZMm, wall.MaxXMm, wall.MaxZMm), withWall, null, out _, out restamped);
            rebuilds.Add(clock.Elapsed.TotalMilliseconds);
        }
        double rebuildMs = rebuilds.Order().ElementAt(rebuilds.Count / 2);
        _output.WriteLine($"one piece's rebuild: {restamped} nodes, median {rebuildMs:F3} ms of {rebuilds.Count} (max {rebuilds.Max():F3})");
        Assert.Equal(108, restamped);
        Assert.True(rebuildMs < 6, $"a one-piece rebuild took {rebuildMs:F3} ms (CI bound 6 ms; ASTRAL target 2 ms)");

        // The placement check (E7, §3.13): after the workshop's first step, the ghost asked with navigability over a wall south of a pad
        // beside the workshop (open ground: a flood to the seal limit, proven) and over the vestibule's wall (a sealed pocket, refused).
        var arena = BuildingTests.Builder(session, (102.0, 102.0));
        BuildingTests.WorkshopStepOne(arena);
        Assert.True(arena.WalkTo(100.5, 100.3) && arena.WalkTo(100.5, 94.5), $"the walk out stopped at {arena.Simulation.Player.Body}");
        foreach (var (def, x, z, r) in new[] { ("piece.pad.timber", 100_500L, 97_500L, 0), ("piece.wall.timber", 99_000L, 97_500L, 1),
                     ("piece.wall.timber", 102_000L, 97_500L, 1), ("piece.pad.timber", 103_500L, 97_500L, 0) })
            Assert.Null(BuildingTests.Place(arena, def, x, z, r));
        arena.Simulation.PreviewPlacement("piece.wall.timber", 103_500, 96_000, 0, checkNavigability: true);   // the scratch's first use, and the JIT
        var checks = new List<double>();
        foreach (var (x, z, verdict) in new[] { (103_500L, 96_000L, NavVerdict.Proven), (100_500L, 96_000L, NavVerdict.Refused) })
        {
            for (int n = 0; n < 9; n++)
            {
                var clock = Stopwatch.StartNew();
                var preview = arena.Simulation.PreviewPlacement("piece.wall.timber", x, z, 0, checkNavigability: true);
                checks.Add(clock.Elapsed.TotalMilliseconds);
                Assert.Equal(verdict, preview.Navigability);
            }
        }
        double checkMedian = checks.Order().ElementAt(checks.Count / 2), checkWorst = checks.Max();
        _output.WriteLine($"placement checks: median {checkMedian:F3} ms, worst {checkWorst:F3} ms of {checks.Count}");
        Assert.True(checkMedian < 6, $"the placement check's median is {checkMedian:F3} ms (CI bound 6 ms; ASTRAL target 2 ms)");
        Assert.True(checkWorst < 30, $"the placement check's worst is {checkWorst:F3} ms (CI bound 30 ms; ASTRAL target 10 ms)");

        // Kera's three workshop routes (E8, §3.18): the actual mover plans once the workshop stands with its door, bench and chest (step
        // 1) - her place to the bench's work anchor, the walk home, and the walk home after step 9's wall on x = 96 m - for a person who
        // opens doors. Each is found within two thirds of the cap; the time is ASTRAL evidence in Release, under the CI bound here.
        var workshop = BuildingTests.Builder(session, (102.0, 102.0));
        BuildingTests.WorkshopStepOne(workshop);
        foreach (var (def, x, z, r) in new[] { ("piece.door.timber", 100_500L, 99_000L, 0), ("piece.station.anvil", 100_500L, 103_500L, 3),
                     ("piece.storage.chest", 103_500L, 103_500L, 0) })
            Assert.Null(BuildingTests.Place(workshop, def, x, z, r));
        NavPoint site = new(61_600, 139_600), anchor = new(100_750, 103_500);
        (NavPlan Plan, double Ms) Timed(NavPoint from, NavPoint to)
        {
            var q = new NavQuery(workshop.Simulation.Navigation.Grid, EveryGatePassable, config, new NavScratch(), null);
            NavSearch.Plan(q, Person, from, to);   // the scratch's first use
            var runs = new List<(NavPlan Plan, double Ms)>();
            for (int n = 0; n < 5; n++)
            {
                var clock = Stopwatch.StartNew();
                var plan = NavSearch.Plan(q, Person, from, to);
                runs.Add((plan, clock.Elapsed.TotalMilliseconds));
            }
            return runs.OrderBy(x => x.Ms).ElementAt(2);
        }
        var routes = new List<(string Label, NavPlan Plan, double Ms)>();
        foreach (var (label, from, to) in new[] { ("Kera's place -> the bench", site, anchor), ("the bench -> Kera's place", anchor, site) })
        {
            var (plan, ms) = Timed(from, to);
            routes.Add((label, plan, ms));
        }
        foreach (var (def, x, z, r) in new[] { ("piece.pad.timber", 97_500L, 100_500L, 0), ("piece.pad.timber", 97_500L, 103_500L, 0),
                     ("piece.wall.timber", 96_000L, 100_500L, 1), ("piece.wall.timber", 96_000L, 103_500L, 1) })
            Assert.Null(BuildingTests.Place(workshop, def, x, z, r));
        var (after, afterMs) = Timed(anchor, site);
        routes.Add(("the bench -> Kera's place, the wall on x = 96", after, afterMs));
        foreach (var (label, plan, ms) in routes)
        {
            _output.WriteLine($"Kera's route {label}: {plan.Outcome}, {plan.Expansions} expansions, {plan.Corners.Length} corners, median {ms:F3} ms");
            Assert.Equal(NavOutcome.Found, plan.Outcome);
            Assert.True(plan.Expansions <= 2 * config.Limits.MaxExpansions / 3, $"{label}: {plan.Expansions} expansions (two thirds of the cap)");
            Assert.True(ms < 45, $"{label}: {ms:F3} ms (CI bound 45 ms; ASTRAL target 15 ms in Release)");
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

    // NoDoorCloses_OnAnyBody: the piece-door half (E6)
    /// <summary>
    /// The same for a placed door, through the same predicate: hung in a doorway at the crossing and saved open, it will not shut on the
    /// character standing in it, Tavar, Renn or a sleeping wolf there; with the doorway clear it shuts.
    /// </summary>
    [Fact]
    public void NoPieceDoorCloses_OnAnyBody()
    {
        const string renn = "npc.ashen_hollow.renn_vale";
        const long inX = 100_500, inZ = 99_000;   // the middle of the doorway
        var none = Array.Empty<(string, double, double, string)>();
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var built = Arena.OpenCreatures(session, session.Setup, (100.5, 97.8), 0, none,
            r => r.WithInventory(r.Inventory.Append(Arena.Stack("item.material.timber", 5))));
        Assert.Null(built.Submit(new PlacePieceCommand(built.Player, "piece.pad.timber", 100_500, 100_500, 0)));
        Assert.Null(built.Submit(new PlacePieceCommand(built.Player, "piece.doorway.timber", inX, inZ, 0)));
        Assert.Null(built.Submit(new PlacePieceCommand(built.Player, "piece.door.timber", inX, inZ, 0)));
        string door = built.Simulation.Pieces.Single(p => p.DefId == "piece.door.timber").Id.Value;
        Assert.Null(built.Submit(new InteractCommand(built.Player, door)));
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("open"), SaveDocuments.Capture(built.Simulation.World, built.Simulation.CaptureRecord(), session.Content,
            built.Simulation.WorldTick, 0));
        // Each world its own load: a loaded world is the one simulation that plays it.
        LoadResult Saved() => store.Load(SaveSlots.Manual("open"), new LoadContext(session.Generator, session.Content, new Registry()));

        (string? Refused, bool Open) Close(Arena arena)
        {
            string? refused = arena.Submit(new InteractCommand(arena.Player, door));
            return (refused, arena.Simulation.Pieces.Single(p => p.Id.Value == door).DoorOpen);
        }
        var refusal = ("the door cannot close: something is in the doorway", true);

        // The character in the doorway.
        var self = Arena.Resume(session.Setup, Saved());
        Assert.True(self.WalkTo(100.5, 99.0));
        Assert.Equal(refusal, Close(self));
        // Tavar waiting in it; Renn standing in it; a wolf asleep in it.
        var tavar = new CompanionRecord(Tavar, CompanionOrder.Wait, CompanionCondition.Up, inX, inZ, 90_000, 100);
        var withTavar = Saved();
        Assert.Equal(refusal, Close(Arena.Resume(session.Setup, withTavar with { Player = withTavar.Player.WithCompanions(new[] { tavar }) })));
        var rennInTheDoorway = session.Setup with
        {
            Layout = session.Setup.Layout with
            {
                Npcs = session.Setup.Layout.Npcs.Select(n => n.NpcId == renn ? n with { XMm = inX, ZMm = inZ } : n).ToImmutableArray(),
            },
        };
        Assert.Equal(refusal, Close(Arena.Resume(rennInTheDoorway, Saved())));
        var wolf = new SpawnSite("spawn.test.sleeper", inX, inZ, 0, ImmutableArray.Create(new SpawnMember(Arena.Wolf, "sleeper")));
        var wolfInTheDoorway = session.Setup with { Combat = session.Setup.Combat with { Spawns = session.Setup.Combat.Spawns.Add(wolf) } };
        Assert.Equal(refusal, Close(Arena.Resume(wolfInTheDoorway, Saved())));

        // The doorway clear: it shuts.
        Assert.Equal(((string?)null, false), Close(Arena.Resume(session.Setup, Saved())));
        Assert.Equal(0, session.SubscriberFailures);
    }

    private const string Tavar = "npc.ashen_hollow.tavar_orr";

    /// <summary>
    /// N-A11's script: Tavar with the character, told to wait inside the lodge at (46.0, 128.0); the character walks out through
    /// <c>door.longhouse</c> to (53.0, 128.0), closes it behind them, walks on to (58.0, 128.0) and calls him. His trail is empty and the
    /// character is out of sight, so the only way to them is a route through the closed door.
    /// </summary>
    private static Arena CalledThroughTheLodgeDoor(GameSession session)
    {
        var arena = Arena.OpenCreatures(session, session.Setup, (48.0, 128.0), 90, Array.Empty<(string, double, double, string)>(),
            r => r.WithCompanions(new[] { new CompanionRecord(Tavar, CompanionOrder.Wait, CompanionCondition.Up, 46_000, 128_000, 90_000, 100) }));
        Assert.True(arena.WalkTo(50.5, 128.0));
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, "door.longhouse")));
        Assert.True(arena.WalkTo(53.0, 128.0));
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, "door.longhouse")));
        Assert.False(arena.Simulation.Doors.Single(d => d.Site.Key == "door.longhouse").Open);
        Assert.True(arena.WalkTo(58.0, 128.0));
        Assert.Null(arena.Submit(new OrderCompanionCommand(arena.Player, Tavar, CompanionOrder.Follow)));
        return arena;
    }

    private static double Apart(Arena arena)
    {
        var him = arena.Simulation.Companions.Single().Body;
        var me = arena.Simulation.Player.Body;
        return Math.Sqrt(Math.Pow(him.XMm - me.XMm, 2) + Math.Pow(him.ZMm - me.ZMm, 2));
    }

    // N-A11
    /// <summary>The entry criterion "companions path reliably": the M6 snag behind a closed door is gone.</summary>
    [Fact]
    public void TheCompanion_OpensTheLodgeDoor_AfterWaitThenFollow()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = CalledThroughTheLodgeDoor(session);
        var planned = arena.Record<RoutePlanned>();
        var toggled = arena.Record<DoorToggled>();
        var caughtUp = arena.Record<CompanionCaughtUp>();

        arena.Tick(400);

        var first = planned.First();
        Assert.Equal((Tavar, "found", NavFollower.None), (first.MoverKey, first.Outcome, first.Reason));
        Assert.Equal(new[] { (arena.Simulation.Companions.Single().InstanceId, "door.longhouse", true) }, toggled.Select(t => (t.Actor, t.DoorKey, t.Open)));
        Assert.InRange(Apart(arena), 0, 4_000);
        Assert.Empty(caughtUp);
        foreach (var p in planned)
            _output.WriteLine($"tick {p.Tick}: {p.Outcome} ({p.Reason}), {p.Corners} corners, {p.Expansions} expansions");
        Assert.Equal(0, session.SubscriberFailures);
    }

    /// <summary>
    /// A line of three pads at the crossing with walls on their south edges and a door hung in the middle one's doorway, shut, built by
    /// <paramref name="builder"/> from (100.5, 102.0); Tavar waiting south of it at (100.5, 96.0), where neither the character nor any
    /// mark is in his sight. The way round the line is twice the way through the door.
    /// </summary>
    private static (Arena Arena, string Door) BehindAShutDoor(GameSession session, TempProfile profile, bool theirs)
    {
        var none = Array.Empty<(string, double, double, string)>();
        var tavar = new CompanionRecord(Tavar, CompanionOrder.Wait, CompanionCondition.Up, 100_500, 96_000, 0, 100);
        var arena = Arena.OpenCreatures(session, session.Setup, (100.5, 102.0), 180, none,
            r => r.WithInventory(r.Inventory.Append(Arena.Stack("item.material.timber", 20))).WithCompanions(new[] { tavar }));
        foreach (var (def, x, z) in new[] { ("piece.pad.timber", 97_500L, 100_500L), ("piece.pad.timber", 100_500L, 100_500L),
                     ("piece.pad.timber", 103_500L, 100_500L), ("piece.wall.timber", 97_500L, 99_000L), ("piece.doorway.timber", 100_500L, 99_000L),
                     ("piece.wall.timber", 103_500L, 99_000L), ("piece.door.timber", 100_500L, 99_000L) })
            Assert.Null(arena.Submit(new PlacePieceCommand(arena.Player, def, x, z, 0)));
        string door = arena.Simulation.Pieces.Single(p => p.DefId == "piece.door.timber").Id.Value;
        if (!theirs)
            return (arena, door);
        // Played by another character with Tavar at their side: the door is the first character's.
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("theirs"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        var loaded = store.Load(SaveSlots.Manual("theirs"), new LoadContext(session.Generator, session.Content, new Registry()));
        var r = loaded.Player;
        var other = loaded with
        {
            Player = new PlayerRecord(EntityId.NewId(EntityKind.Character), r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory, r.Progression,
                r.FacingMdeg, r.Discoveries, r.Equipment, r.Currency, r.Effects, r.Relationships, r.Conversations, r.Quests, r.Companions),
        };
        return (Arena.Resume(session.Setup, other), door);
    }

    /// <summary>
    /// <c>CanOperate</c>'s companion clause (M7 design §4.9): told to follow from behind the shut door, Tavar plans through it, opens it
    /// from within reach - an NPC opens, never shuts - and comes through, with no catch-up.
    /// </summary>
    [Fact]
    public void ACompanion_OpensTheOwnersPieceDoor()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, door) = BehindAShutDoor(session, profile, theirs: false);
        var planned = arena.Record<RoutePlanned>();
        var toggled = arena.Record<DoorToggled>();
        var caughtUp = arena.Record<CompanionCaughtUp>();
        Assert.Null(arena.Submit(new OrderCompanionCommand(arena.Player, Tavar, CompanionOrder.Follow)));

        arena.Tick(400);

        Assert.Equal((Tavar, "found"), (planned.First().MoverKey, planned.First().Outcome));
        Assert.Equal(new[] { (arena.Simulation.Companions.Single().InstanceId, door, true) }, toggled.Select(t => (t.Actor, t.DoorKey, t.Open)));
        Assert.True(arena.Simulation.Pieces.Single(p => p.Id.Value == door).DoorOpen);
        Assert.True(arena.Simulation.Companions.Single().Body.ZMm > 99_200, $"Tavar is at {arena.Simulation.Companions.Single().Body}, still outside");
        Assert.InRange(Apart(arena), 0, 4_000);
        Assert.Empty(caughtUp);
        Assert.Equal(0, session.SubscriberFailures);
    }

    // G26: the companion (E6; E9 adds the errand)
    /// <summary>
    /// A door Tavar may not open - the first character's, in a world a second character plays - stalls him as stuck, never forever: each
    /// tick at it one refused open (no toggle), a stuck replan once he has made no headway for 20 ticks, and his Phase-1 snag catch-up.
    /// </summary>
    [Fact]
    public void ARefusedDoor_CountsAsStuck_ForBothMovers()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, door) = BehindAShutDoor(session, profile, theirs: true);
        var planned = arena.Record<RoutePlanned>();
        var toggled = arena.Record<DoorToggled>();
        var caughtUp = arena.Record<CompanionCaughtUp>();
        Assert.Null(arena.Submit(new OrderCompanionCommand(arena.Player, Tavar, CompanionOrder.Follow)));

        var stuck = new List<int>();
        for (int i = 0; i < 300 && caughtUp.Count == 0; i++)
        {
            arena.Tick();
            stuck.Add(arena.Simulation.CaptureRecord().Companions.Single().StuckTicks);
        }

        Assert.Empty(toggled);
        Assert.False(arena.Simulation.Pieces.Single(p => p.Id.Value == door).DoorOpen);
        // At the door the count climbs at most one a tick - one refused open a tick - to a stuck replan and on to the snag.
        Assert.Contains(1, stuck);
        Assert.All(stuck.Zip(stuck.Skip(1)), p => Assert.True(p.Second <= p.First + 1, $"the stuck count jumped from {p.First} to {p.Second}"));
        Assert.Contains(planned, p => p.MoverKey == Tavar && p.Reason == NavFollower.Stuck);
        var snag = Assert.Single(caughtUp);
        Assert.Equal((Tavar, "snag"), (snag.NpcId, snag.Reason));
        Assert.Equal(0, session.SubscriberFailures);
    }

    // N-A13 (E7: cases (a) and (b); E8 adds (c), the door onto a chest)
    /// <summary>
    /// The ROADMAP's "placement validation that rejects un-navigable configurations", on the game's own content after the workshop's
    /// first step: a one-square hut at (91.5, 91.5) closed from outside is refused with the generic reason; built round the character,
    /// its last wall is refused as shutting them in; and a door that would open onto a chest is refused. Nothing changes any time.
    /// </summary>
    [Fact]
    public void PlacementIsRefused_WhenNavigationWouldBreak()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var walls = new (string Def, long X, long Z, int R)[]
        {
            ("piece.pad.timber", 91_500, 91_500, 0), ("piece.wall.timber", 91_500, 90_000, 0), ("piece.wall.timber", 91_500, 93_000, 0),
            ("piece.wall.timber", 90_000, 91_500, 1), ("piece.wall.timber", 93_000, 91_500, 1),
        };
        foreach (var ((x, z), expected) in new[] { ((95.0, 91.5), "that would close off a space with no way in; rooms need a doorway"),
                     ((91.5, 91.5), "that would shut you in") })
        {
            var arena = BuildingTests.Builder(session, (102.0, 102.0));
            BuildingTests.WorkshopStepOne(arena);
            Assert.True(arena.WalkTo(100.5, 100.3) && arena.WalkTo(100.5, 97.0) && arena.WalkTo(x, z), $"the walk out stopped at {arena.Simulation.Player.Body}");
            foreach (var (def, px, pz, r) in walls.SkipLast(1))
                Assert.Null(BuildingTests.Place(arena, def, px, pz, r));
            string digest = arena.Simulation.StateDigest();
            var last = walls[^1];
            Assert.Equal(expected, BuildingTests.Place(arena, last.Def, last.X, last.Z, last.R));
            Assert.Equal(digest, arena.Simulation.StateDigest());
            Assert.Equal(1, arena.Simulation.Navigation.Counters.EditRefusalsByRule.GetValueOrDefault("V-N1"));
        }

        // (c) (E8): a chest set against a doorway's inner side is accepted; a door hung in that doorway would open onto it.
        var porch = BuildingTests.Builder(session, (102.0, 102.0));
        BuildingTests.WorkshopStepOne(porch);
        Assert.True(porch.WalkTo(100.5, 100.3) && porch.WalkTo(100.5, 97.0) && porch.WalkTo(100.5, 94.5), $"the walk out stopped at {porch.Simulation.Player.Body}");
        Assert.Null(BuildingTests.Place(porch, "piece.pad.timber", 100_500, 94_500, 0));
        Assert.Null(BuildingTests.Place(porch, "piece.doorway.timber", 100_500, 96_000, 0));
        Assert.Null(BuildingTests.Place(porch, "piece.storage.chest", 100_500, 94_500, 0));
        string unchanged = porch.Simulation.StateDigest();
        Assert.Equal("the door would open onto a wall", BuildingTests.Place(porch, "piece.door.timber", 100_500, 96_000, 0));
        Assert.Equal(unchanged, porch.Simulation.StateDigest());
        Assert.Equal(1, porch.Simulation.Navigation.Counters.EditRefusalsByRule.GetValueOrDefault("V-N4"));
        Assert.Equal(0, session.SubscriberFailures);
    }

    // N-A6, part (a): the companion (E4; E9 adds the errand, part (b))
    /// <summary>
    /// Saved mid-route, 10 ticks after Tavar's first plan and before he reaches the door (his route is active for 16 ticks), the loaded
    /// world goes on exactly as the saved one does: the grid, his route by value, the state digest at the load and every 50 ticks for 400
    /// ticks, the door he opens, and the tick he arrives (G15).
    /// </summary>
    [Fact]
    public void MidRoute_SaveLoad_GoesOnTheSame()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = CalledThroughTheLodgeDoor(session);
        var planned = arena.Record<RoutePlanned>();
        for (int i = 0; i < 400 && planned.Count == 0; i++)
            arena.Tick();
        Assert.NotEmpty(planned);
        arena.Tick(10);
        var saved = Assert.Single(arena.Simulation.CaptureRecord().Companions);
        Assert.Equal(NavRouteStatus.Active, saved.Route.Status);

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("mid_route"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        var loaded = Arena.Resume(arena.Simulation.Setup, store.Load(SaveSlots.Manual("mid_route"), new LoadContext(session.Generator, session.Content, new Registry())));

        Assert.Equal(arena.Simulation.Navigation.Grid.Digest(), loaded.Simulation.Navigation.Grid.Digest());
        Assert.Equal(saved.Route, Assert.Single(loaded.Simulation.CaptureRecord().Companions).Route);
        Assert.Equal(arena.Simulation.Navigation.Movers.ToList(), loaded.Simulation.Navigation.Movers.ToList());
        Assert.Equal(arena.Simulation.StateDigest(), loaded.Simulation.StateDigest());

        var opened = arena.Record<DoorToggled>();
        var openedLoaded = loaded.Record<DoorToggled>();
        long? Arrived(Arena world) => Apart(world) <= 3_000 ? world.Simulation.WorldTick : null;
        long? arrivedSaved = null, arrivedLoaded = null;
        for (int n = 0; n < 8; n++)
        {
            for (int i = 0; i < 50; i++)
            {
                arena.Tick();
                loaded.Tick();
                arrivedSaved ??= Arrived(arena);
                arrivedLoaded ??= Arrived(loaded);
            }
            Assert.Equal(arena.Simulation.StateDigest(), loaded.Simulation.StateDigest());
        }
        Assert.Equal("door.longhouse", Assert.Single(opened).DoorKey);
        Assert.Equal(opened, openedLoaded);
        Assert.NotNull(arrivedSaved);
        Assert.Equal(arrivedSaved, arrivedLoaded);
        Assert.Equal(0, session.SubscriberFailures);
    }

    /// <summary>A person's plan on a world's own grid, with a scratch of the test's own and every gate as it stands now.</summary>
    private static NavPlan PlanOn(Arena world, NavConfig config, NavPoint from, NavPoint to)
    {
        var nav = world.Simulation.Navigation;
        var open = nav.Gates.ToDictionary(g => g.Key, g => g.Open, StringComparer.Ordinal);
        return NavSearch.Plan(new NavQuery(nav.Grid, g => g.GateKey is { } key && open.GetValueOrDefault(key), config, new NavScratch(), null), Person, from, to);
    }

    // N-A7
    /// <summary>After workshop step 1, a save and a load keep the grid, every tile's stamp, and every plan through and round the workshop.</summary>
    [Fact]
    public void TheSeam_SurvivesAReload()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = BuildingTests.Builder(session, (102.0, 102.0));
        BuildingTests.WorkshopStepOne(arena);
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("seam"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        var loaded = Arena.Resume(session.Setup, store.Load(SaveSlots.Manual("seam"), new LoadContext(session.Generator, session.Content, new Registry())));

        Assert.Equal(arena.Simulation.Navigation.Grid.Digest(), loaded.Simulation.Navigation.Grid.Digest());
        Assert.Equal(arena.Simulation.Navigation.Grid.Tiles.Select(t => (t.Key, t.Stamp)), loaded.Simulation.Navigation.Grid.Tiles.Select(t => (t.Key, t.Stamp)));
        // Eight fixed points: 3 m outside the middle of each side, and the four inner corners 0.8 m in from the walls; to the bench's anchor.
        var anchor = new NavPoint(100_750, 103_500);
        var from = new[]
        {
            new NavPoint(102_000, 96_000), new NavPoint(108_000, 102_000), new NavPoint(102_000, 108_000), new NavPoint(96_000, 102_000),
            new NavPoint(99_800, 99_800), new NavPoint(104_200, 99_800), new NavPoint(99_800, 104_200), new NavPoint(104_200, 104_200),
        };
        foreach (var point in from)
        {
            var before = PlanOn(arena, session.Setup.Navigation, point, anchor);
            var after = PlanOn(loaded, session.Setup.Navigation, point, anchor);
            Assert.Equal(NavOutcome.Found, before.Outcome);
            Assert.Equal((before.Outcome, before.Partial, before.Expansions), (after.Outcome, after.Partial, after.Expansions));
            Assert.Equal(before.Corners.ToArray(), after.Corners.ToArray());
        }
    }

    // N-A9
    /// <summary>Fifty times a wall placed and taken down again: each state's grid and route are the state's, every time.</summary>
    [Fact]
    public void RepeatedRebuilds_AreIdentical()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = BuildingTests.Builder(session, (100.5, 94.5), timber: new[] { 20, 20, 20 });
        Assert.Null(BuildingTests.Place(arena, "piece.pad.timber", 94_500, 94_500, 0));
        string padOnly = arena.Simulation.Navigation.Grid.Digest();
        var from = new NavPoint(94_500, 92_000);
        var to = new NavPoint(94_500, 100_000);
        var openRoute = PlanOn(arena, session.Setup.Navigation, from, to).Corners.ToArray();
        string? walled = null;
        NavPoint[]? walledRoute = null;
        for (int cycle = 0; cycle < 50; cycle++)
        {
            Assert.Null(BuildingTests.Place(arena, "piece.wall.timber", 94_500, 96_000, 0));
            walled ??= arena.Simulation.Navigation.Grid.Digest();
            walledRoute ??= PlanOn(arena, session.Setup.Navigation, from, to).Corners.ToArray();
            Assert.Equal(walled, arena.Simulation.Navigation.Grid.Digest());
            Assert.Equal(walledRoute, PlanOn(arena, session.Setup.Navigation, from, to).Corners.ToArray());

            var wall = arena.Simulation.Pieces.Single(p => p.DefId == "piece.wall.timber").Id;
            Assert.Null(arena.Submit(new DismantlePieceCommand(arena.Player, wall)));
            Assert.Equal(padOnly, arena.Simulation.Navigation.Grid.Digest());
            Assert.Equal(openRoute, PlanOn(arena, session.Setup.Navigation, from, to).Corners.ToArray());
        }
        Assert.NotEqual(padOnly, walled);
        Assert.NotEqual(openRoute, walledRoute);
        Assert.Equal(101, arena.Simulation.StructureRevision);
    }

    // N-A12
    /// <summary>A wall placed between the character and Tavar, waiting: called, he plans round its end and comes, with no catch-up.</summary>
    [Fact]
    public void TheCompanion_FollowsRoundAPlayerWall()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (100.5, 101.0), 0, Array.Empty<(string, double, double, string)>(),
            r => r.WithInventory(r.Inventory.Append(Arena.Stack("item.material.timber", 20)))
                .WithCompanions(new[] { new CompanionRecord(Tavar, CompanionOrder.Wait, CompanionCondition.Up, 100_500, 108_500, 180_000, 100) }));
        Assert.Null(BuildingTests.Place(arena, "piece.pad.timber", 100_500, 103_500, 0));
        Assert.Null(BuildingTests.Place(arena, "piece.wall.timber", 100_500, 105_000, 0));
        Assert.True(arena.Simulation.Walled(100_500, 101_000, 100_500, 108_500), "Tavar is in clear view");

        var planned = arena.Record<RoutePlanned>();
        var caughtUp = arena.Record<CompanionCaughtUp>();
        Assert.Null(arena.Submit(new OrderCompanionCommand(arena.Player, Tavar, CompanionOrder.Follow)));
        long held = 0, maxHeld = 0;
        var last = arena.Simulation.Companions.Single().Body;
        for (int i = 0; i < 600 && Apart(arena) > 2_500; i++)
        {
            arena.Tick();
            var now = arena.Simulation.Companions.Single().Body;
            held = (now.XMm, now.ZMm) == (last.XMm, last.ZMm) && Apart(arena) > 3_500 ? held + 1 : 0;
            maxHeld = Math.Max(maxHeld, held);
            last = now;
        }

        var first = planned.First();
        Assert.Equal((Tavar, "found"), (first.MoverKey, first.Outcome));
        Assert.True(first.Corners >= 2, $"the first plan has {first.Corners} corners: straight through the wall?");
        Assert.Empty(caughtUp);
        Assert.True(maxHeld < 300, $"he was held {maxHeld} ticks");
        Assert.InRange(Apart(arena), 0, 2_500);
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
