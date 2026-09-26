using System.Collections.Immutable;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Tests.Spatial;

/// <summary>Following a planned route (M7 design §3.7.6, §3.8, §3.20.1): the replan triggers, advancing, arriving, opening a door, and walking it by Kinematics.</summary>
public class NavFollowerTests
{
    private static readonly NavAgent Opener = new(0, true);
    private static readonly Func<NavInput, bool> AllShut = _ => false;
    private const long Reach = 1_600;
    private static readonly MovementRules Rules = new(3_200, 50, 160, 350, Reach);

    private static NavInput Box(string id, long x0, long z0, long x1, long z1, NavInputKind kind = NavInputKind.Solid) =>
        new(kind, new BoxBlocker(id, x0, z0, x1, z1, 2_400), kind == NavInputKind.Solid ? null : id);

    private static NavGrid Field(long side, IEnumerable<NavInput> inputs) =>
        NavGrid.Build(NavConfig.Default, new NavRect(0, 0, side, side),
            from tz in Enumerable.Range(0, (int)((side - 1) / NavConfig.TileMm + 1))
            from tx in Enumerable.Range(0, (int)((side - 1) / NavConfig.TileMm + 1))
            select new NavTileKey(tx, tz), inputs);

    private static NavQuery Query(NavGrid grid, Func<NavInput, bool>? open = null) => new(grid, open ?? AllShut, grid.Config, new NavScratch(), null);

    private static NavPoint P(long x, long z) => new(x, z);

    private static long Length(NavPoint from, IEnumerable<NavPoint> corners)
    {
        double total = 0;
        var at = from;
        foreach (var c in corners)
        {
            total += Math.Sqrt((double)(c.XMm - at.XMm) * (c.XMm - at.XMm) + (double)(c.ZMm - at.ZMm) * (c.ZMm - at.ZMm));
            at = c;
        }
        return (long)total;
    }

    /// <summary>Walk a body by <see cref="Kinematics.Step"/> along what the follower says, until it arrives; the bodies it stood at, tick by tick.</summary>
    private static List<Body> Walk(NavGrid grid, IReadOnlyList<NavInput> inputs, NavPoint from, NavPoint goal, int maxTicks, Func<NavInput, bool>? open = null)
    {
        var q = Query(grid, open);
        var space = new WalkSpace(0, 0, grid.Bounds.MaxXMm, grid.Bounds.MaxZMm, new TerrainGrid(0, 0, grid.Bounds.MaxXMm, 2, 2, new long[] { 1_000, 1_000, 1_000, 1_000 }),
            inputs.Where(i => i.Kind == NavInputKind.Solid).Select(i => i.Shape).ToImmutableArray());
        var body = new Body(from.XMm, 1_000, from.ZMm, 0);
        var route = NavRoute.None;
        var bodies = new List<Body> { body };
        for (int tick = 0; tick < maxTicks; tick++)
        {
            var step = NavFollower.Next(q, Opener, route, P(body.XMm, body.ZMm), goal, 0, tick, Reach);
            route = step.Route;
            if (step.Kind == NavStepKind.Arrived)
                return bodies;
            Assert.Equal(NavStepKind.Walk, step.Kind);
            double dx = step.Target.XMm - body.XMm, dz = step.Target.ZMm - body.ZMm, d = Math.Sqrt(dx * dx + dz * dz);
            var intent = new MoveIntent((int)Math.Round(dx / d * 1000), (int)Math.Round(dz / d * 1000), Gait.Run, body.FacingMdeg);
            body = Kinematics.Step(body, intent, Rules, space, Array.Empty<Blocker>(), 50);
            Assert.True(Kinematics.IsClear(body.XMm, body.ZMm, Rules.BodyRadiusMm, space, Array.Empty<Blocker>()), $"the body stands inside something at {body}");
            bodies.Add(body);
        }
        throw new Xunit.Sdk.XunitException($"never arrived; at {body}");
    }

    // N-D2
    [Fact]
    public void AWall_IsRoutedAround_AndWalkedByKinematics()
    {
        var inputs = new List<NavInput> { Box("wall", 30_000, 50_000, 50_000, 50_400) };
        var grid = Field(100_000, inputs);
        var from = P(45_000, 20_000);
        var goal = P(45_000, 80_000);
        var plan = NavSearch.Plan(Query(grid), Opener, from, goal);
        Assert.Equal(NavOutcome.Found, plan.Outcome);
        var at = from;
        foreach (var corner in plan.Corners)
        {
            Assert.True(NavSearch.Clear(grid, Opener, AllShut, at, corner, 350), $"{at} to {corner} is not clear");
            at = corner;
        }

        long length = Length(from, plan.Corners);
        var walked = Walk(grid, inputs, from, goal, 2_000);
        double ticks = length / (Rules.BaseSpeedMmPerSecond / 20.0);
        Assert.True(walked.Count <= 1.3 * ticks, $"{walked.Count} ticks for a {length} mm route, budget {1.3 * ticks:0}");
    }

    /// <summary>A route round the east end of a wall across the field: at least two corners.</summary>
    private static (NavGrid Grid, List<NavInput> Inputs, NavRoute Route, NavPoint From, NavPoint Goal) Detour()
    {
        var inputs = new List<NavInput> { Box("wall", 28_000, 50_000, 50_000, 50_400) };
        var grid = Field(100_000, inputs);
        var from = P(40_000, 30_000);
        var goal = P(40_000, 70_000);
        var step = NavFollower.Next(Query(grid), Opener, NavRoute.None, from, goal, 0, 0, Reach);
        Assert.Equal(NavFollower.None, step.ReplanReason);
        Assert.True(step.Route.Corners.Length >= 2, $"the detour has a corner at the wall's end: {step.Kind} {step.Outcome} {step.Route.Status} [{string.Join(" ", step.Route.Corners)}] partial {step.Route.Partial}");
        return (grid, inputs, step.Route, from, goal);
    }

    // N-D7
    [Fact]
    public void APieceAcrossTheRoute_Invalidates()
    {
        var (grid, inputs, route, from, goal) = Detour();
        var a = route.Corners[0];
        var b = route.Corners[1];
        var mid = P((a.XMm + b.XMm) / 2, (a.ZMm + b.ZMm) / 2);
        var box = Box("piece", mid.XMm - 1_500, mid.ZMm - 1_500, mid.XMm + 1_500, mid.ZMm + 1_500);
        var changed = new NavRect(mid.XMm - 1_500, mid.ZMm - 1_500, mid.XMm + 1_500, mid.ZMm + 1_500);
        var after = grid.With(changed, inputs.Append(box).ToList());

        Assert.NotEqual(route.Stamp, after.WindowStamp(route.Watch));
        var step = NavFollower.Next(Query(after), Opener, route, from, goal, 0, 1, Reach);
        Assert.Equal(NavFollower.Geometry, step.ReplanReason);
        var at = from;
        foreach (var corner in step.Route.Corners)
        {
            Assert.True(NavSearch.Clear(after, Opener, AllShut, at, corner, 350), "a new corner runs through the piece");
            at = corner;
        }

        // Were the stamp not checked (a route stamped against the new grid), the blocked segment is found instead.
        var restamped = NavRoute.Active(goal, route.Corners, route.PlannedTick, after.WindowStamp(route.Watch), route.Watch, route.Partial);
        Assert.Equal(NavFollower.Blocked, NavFollower.ReplanReason(Query(after), Opener, restamped, from, goal, 0, 1, 350));
    }

    // N-D8
    [Fact]
    public void ARemovedPiece_GivesTheShorterRoute()
    {
        var (grid, inputs, route, from, goal) = Detour();
        long before = Length(from, route.Corners);
        var after = grid.With(new NavRect(28_000, 50_000, 50_000, 50_400), Array.Empty<NavInput>());

        var step = NavFollower.Next(Query(after), Opener, route, from, goal, 0, 1, Reach);
        Assert.Equal(NavFollower.Geometry, step.ReplanReason);
        Assert.True(Length(from, step.Route.Corners) < before, $"{Length(from, step.Route.Corners)} is not below {before}");
    }

    // N-D10
    [Fact]
    public void AStraddlingHut_IsEnteredOnce_AndCrossedTwice()
    {
        // A 6 x 6 m hut centred on (100, 100) m - on the corner of four tiles - with 1.6 m doorways west and east.
        var inputs = new List<NavInput>
        {
            Box("north", 97_000, 102_600, 103_000, 103_000),
            Box("south", 97_000, 97_000, 103_000, 97_400),
            Box("west_n", 97_000, 100_800, 97_400, 103_000),
            Box("west_s", 97_000, 97_000, 97_400, 99_200),
            Box("east_n", 102_600, 100_800, 103_000, 103_000),
            Box("east_s", 102_600, 97_000, 103_000, 99_200),
        };
        var grid = Field(200_000, inputs);
        int Crossings(List<Body> path) =>
            path.Zip(path.Skip(1)).Count(p => Inside(p.First) != Inside(p.Second));
        static bool Inside(Body b) => b.XMm >= 97_000 && b.XMm <= 103_000 && b.ZMm >= 97_000 && b.ZMm <= 103_000;

        Assert.Equal(1, Crossings(Walk(grid, inputs, P(85_000, 106_000), P(100_000, 100_000), 1_500)));
        Assert.Equal(2, Crossings(Walk(grid, inputs, P(85_000, 100_000), P(115_000, 100_000), 1_500)));
    }

    // N-D17
    [Fact]
    public void TheFollower_OpensTheNearestDoorInReach()
    {
        // A corridor north: walls at x 38.8 and 41.2 m, door leaves across it at z 51 and z 54 m; the route runs through both.
        List<NavInput> Corridor(string near, string far) => new()
        {
            Box("west", 38_400, 40_000, 38_800, 70_000),
            Box("east", 41_200, 40_000, 41_600, 70_000),
            Box(near, 38_800, 51_000, 41_200, 51_200, NavInputKind.Door),
            Box(far, 38_800, 54_000, 41_200, 54_200, NavInputKind.Door),
        };
        foreach (var (near, far) in new[] { ("door.a", "door.b"), ("door.b", "door.a") })
        {
            var grid = Field(100_000, Corridor(near, far));
            var goal = P(40_000, 65_000);
            var farOff = NavFollower.Next(Query(grid), Opener, NavRoute.None, P(40_000, 48_000), goal, 0, 0, Reach);
            Assert.Equal(NavStepKind.Walk, farOff.Kind);   // 3 m from the near leaf: out of reach
            var close = NavFollower.Next(Query(grid), Opener, NavRoute.None, P(40_000, 49_800), goal, 0, 0, Reach);
            Assert.Equal((NavStepKind.OpenGate, near), (close.Kind, close.GateKey));
        }

        // Two leaves side by side, equally far: the lower MinX, whatever the keys.
        foreach (var (left, right) in new[] { ("door.a", "door.b"), ("door.b", "door.a") })
        {
            var grid = Field(100_000, new List<NavInput>
            {
                Box("west", 37_600, 40_000, 38_000, 70_000),
                Box("east", 42_000, 40_000, 42_400, 70_000),
                Box(left, 38_000, 51_000, 40_000, 51_200, NavInputKind.Door),
                Box(right, 40_000, 51_000, 42_000, 51_200, NavInputKind.Door),
            });
            var step = NavFollower.Next(Query(grid), Opener, NavRoute.None, P(40_000, 49_800), P(40_000, 65_000), 0, 0, Reach);
            Assert.Equal((NavStepKind.OpenGate, left), (step.Kind, step.GateKey));
        }
    }

    // N-D18
    [Fact]
    public void EachReplanTrigger_FiresOnItsCondition()
    {
        var (grid, inputs, route, from, goal) = Detour();
        var q = Query(grid);
        string? Reason(NavRoute r, NavPoint body, NavPoint g, int stuck, long tick) => NavFollower.ReplanReason(q, Opener, r, body, g, stuck, tick, 350);
        long t0 = route.PlannedTick;

        // 1. none
        Assert.Equal(NavFollower.None, Reason(NavRoute.None, from, goal, 0, 5));
        // 2. retry: an unreachable route holds until 40 ticks have passed, even while the stuck count crosses 20 and 40.
        var unreachable = NavRoute.Unreachable(goal, t0, grid.WindowStamp(route.Watch), route.Watch);
        Assert.Null(Reason(unreachable, from, goal, 20, t0 + 39));
        Assert.Null(Reason(unreachable, from, goal, 40, t0 + 39));
        Assert.Equal(NavFollower.Retry, Reason(unreachable, from, goal, 0, t0 + 40));
        var held = NavFollower.Next(q, Opener, unreachable, from, goal, 40, t0 + 39, Reach);
        Assert.Equal((NavStepKind.Unreachable, (string?)null), (held.Kind, held.ReplanReason));
        // 3. goal_moved: more than 2 m, and not within 10 ticks of the plan.
        var moved = P(goal.XMm + 2_001, goal.ZMm);
        Assert.Null(Reason(route, from, moved, 0, t0 + 9));
        Assert.Equal(NavFollower.GoalMoved, Reason(route, from, moved, 0, t0 + 10));
        Assert.Null(Reason(route, from, P(goal.XMm + 2_000, goal.ZMm), 0, t0 + 10));
        // 4. geometry: at once.
        var stale = NavRoute.Active(goal, route.Corners, t0, route.Stamp ^ 1, route.Watch, route.Partial);
        Assert.Equal(NavFollower.Geometry, Reason(stale, from, goal, 0, t0));
        // 5. blocked: a corner segment through a wall, the stamp current.
        var through = NavRoute.Active(goal, ImmutableArray.Create(P(40_000, 45_000), P(40_000, 55_000)), t0, route.Stamp, route.Watch, false);
        Assert.Equal(NavFollower.Blocked, Reason(through, from, goal, 0, t0));
        // 6. stuck: every 20 ticks without headway.
        Assert.Null(Reason(route, from, goal, 19, t0));
        Assert.Equal(NavFollower.Stuck, Reason(route, from, goal, 20, t0));
        Assert.Equal(NavFollower.Stuck, Reason(route, from, goal, 40, t0));
        // 7. partial: at the last corner of a route cut short.
        var corner = route.Corners[0];
        var partial = NavRoute.Active(goal, ImmutableArray.Create(corner), t0, route.Stamp, route.Watch, partial: true);
        Assert.Equal(NavFollower.PartialEnd, Reason(partial, P(corner.XMm + 400, corner.ZMm), goal, 0, t0));
        Assert.Null(Reason(partial, P(corner.XMm + 401, corner.ZMm), goal, 0, t0));
        // 8. off_line: shouldered behind the wall's end, where the first corner is out of sight - not within 10 ticks of the plan.
        var behind = P(corner.XMm - 3_000, 51_500);
        Assert.Null(Reason(route, behind, goal, 0, t0 + 9));
        Assert.Equal(NavFollower.OffLine, Reason(route, behind, goal, 0, t0 + 10));
        // The order: the first that holds wins.
        Assert.Equal(NavFollower.GoalMoved, Reason(route, from, moved, 20, t0 + 10));
        Assert.Equal(NavFollower.Geometry, Reason(stale, from, goal, 20, t0));
    }
}
