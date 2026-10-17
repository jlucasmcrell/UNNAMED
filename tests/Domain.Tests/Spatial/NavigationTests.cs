using System.Collections.Immutable;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Tests.Spatial;

/// <summary>Navigation over synthetic geometry (WP12, ported from M7 §3.20.1): the grid, the planner, the follower and the edit check, pure.</summary>
public class NavigationTests
{
    private static readonly NavAgent Opener = new(0, true);
    private static readonly NavAgent NonOpener = new(0, false);
    private static readonly Func<NavInput, bool> AllShut = _ => false;

    private static NavInput Box(string id, long x0, long z0, long x1, long z1, NavInputKind kind = NavInputKind.Solid) =>
        new(kind, new BoxBlocker(id, x0, z0, x1, z1, 2_000), kind == NavInputKind.Solid ? null : id);

    private static IEnumerable<NavTileKey> TilesOver(long widthMm, long heightMm) =>
        from tz in Enumerable.Range(0, (int)((heightMm - 1) / NavConfig.TileMm + 1))
        from tx in Enumerable.Range(0, (int)((widthMm - 1) / NavConfig.TileMm + 1))
        select new NavTileKey(tx, tz);

    private static NavGrid Field(long widthMm, long heightMm, IEnumerable<NavInput> inputs) =>
        NavGrid.Build(NavConfig.Default, new NavRect(0, 0, widthMm, heightMm), TilesOver(widthMm, heightMm), inputs);

    private static NavQuery Query(NavGrid grid, Func<NavInput, bool>? open = null) => new(grid, open ?? AllShut, grid.Config, new NavScratch(), null);

    private static NavPoint P(long x, long z) => new(x, z);

    /// <summary>A 10 x 8 m room with 0.4 m walls, its south wall at z 16-16.4 m, with a 1.6 m doorway at x 19.2-20.8 m - empty, shut by a door, or walled.</summary>
    private static List<NavInput> Room(string doorway)
    {
        var walls = new List<NavInput>
        {
            Box("north", 15_000, 23_600, 25_000, 24_000),
            Box("west", 15_000, 16_000, 15_400, 24_000),
            Box("east", 24_600, 16_000, 25_000, 24_000),
            Box("south_west", 15_000, 16_000, 19_200, 16_400),
            Box("south_east", 20_800, 16_000, 25_000, 16_400),
        };
        if (doorway == "door")
            walls.Add(Box("door.room", 19_200, 16_100, 20_800, 16_300, NavInputKind.Door));
        if (doorway == "wall")
            walls.Add(Box("plug", 19_200, 16_000, 20_800, 16_400));
        return walls;
    }

    [Fact]
    public void TheShippedTuning_BreaksNoRule()
    {
        Assert.Null(NavConfig.Default.Problem());
        Assert.Equal("NAV001", (NavConfig.Default with { NodeMm = 333 }).Problem()!.Value.Code);
        Assert.Equal("NAV002", (NavConfig.Default with { Classes = ImmutableArray.Create(new NavClass("cart", 350)) }).Problem()!.Value.Code);
    }

    [Fact]
    public void ThePersonClass_IsFoundByItsId_WhereverTheClassListPutsIt()
    {
        var config = NavConfig.Default with { Classes = ImmutableArray.Create(new NavClass("small", 300), new NavClass("person", 350)) };
        Assert.Null(config.Problem());
        Assert.Equal(1, config.PersonClass);
        Assert.Equal(0, NavConfig.Default.PersonClass);
    }

    [Fact]
    public void AnOpenField_RoutesStraight()
    {
        var grid = Field(100_000, 100_000, Array.Empty<NavInput>());
        var plan = NavSearch.Plan(Query(grid), Opener, P(10_000, 10_000), P(30_000, 25_000));
        Assert.Equal(NavOutcome.Found, plan.Outcome);
        Assert.Equal(P(30_000, 25_000), Assert.Single(plan.Corners));
    }

    [Fact]
    public void AWall_IsWalkedRound_AndEveryLegIsClear()
    {
        var wall = Box("wall", 20_000, 10_000, 20_400, 30_000);   // inside the 20 m planning margin round both ends
        var grid = Field(100_000, 100_000, new[] { wall });
        var from = P(10_000, 20_000);
        var plan = NavSearch.Plan(Query(grid), Opener, from, P(30_000, 20_000));
        Assert.Equal(NavOutcome.Found, plan.Outcome);
        Assert.True(plan.Corners.Length >= 2);
        var legs = new[] { from }.Concat(plan.Corners).ToList();
        for (int i = 0; i + 1 < legs.Count; i++)
            Assert.True(NavGeometry.SegmentClear(legs[i], legs[i + 1], 350, wall.Shape));
        Assert.True(plan.Corners.All(c => c.ZMm > 30_000 || c.ZMm < 10_000 || c == P(30_000, 20_000)));
    }

    [Fact]
    public void ADoorway_IsTheWayIn_AndASealedRoomIsEnclosed()
    {
        var open = Field(100_000, 100_000, Room("open"));
        Assert.Equal(NavOutcome.Found, NavSearch.Plan(Query(open), Opener, P(20_000, 10_000), P(20_000, 20_000)).Outcome);

        var sealedRoom = Field(100_000, 100_000, Room("wall"));
        var plan = NavSearch.Plan(Query(sealedRoom), Opener, P(20_000, 10_000), P(20_000, 20_000));
        Assert.Equal(NavOutcome.Enclosed, plan.Outcome);
        Assert.Equal(0, plan.Expansions);
    }

    [Fact]
    public void AClosedDoor_StopsANonOpener_NotAnOpener()
    {
        var grid = Field(100_000, 100_000, Room("door"));
        Assert.Equal(NavOutcome.Found, NavSearch.Plan(Query(grid), Opener, P(20_000, 10_000), P(20_000, 20_000)).Outcome);
        Assert.NotEqual(NavOutcome.Found, NavSearch.Plan(Query(grid), NonOpener, P(20_000, 10_000), P(20_000, 20_000)).Outcome);
        Assert.Equal(NavOutcome.Found, NavSearch.Plan(Query(grid, _ => true), NonOpener, P(20_000, 10_000), P(20_000, 20_000)).Outcome);
    }

    [Fact]
    public void TheFollower_OpensTheDoorInReach_ThenWalksIn()
    {
        var grid = Field(100_000, 100_000, Room("door"));
        var q = Query(grid);
        var goal = P(20_000, 20_000);
        var far = NavFollower.Next(q, Opener, NavRoute.None, P(20_000, 10_000), goal, 0, 1, 1_600);
        Assert.Equal(NavStepKind.Walk, far.Kind);
        Assert.Equal(NavFollower.None, far.ReplanReason);
        var near = NavFollower.Next(q, Opener, far.Route, P(20_000, 15_200), goal, 0, 2, 1_600);
        Assert.Equal(NavStepKind.OpenGate, near.Kind);
        Assert.Equal("door.room", near.GateKey);
        var opened = NavFollower.Next(Query(grid, _ => true), Opener, near.Route, P(20_000, 15_200), goal, 0, 3, 1_600);
        Assert.Equal(NavStepKind.Walk, opened.Kind);
        var there = NavFollower.Next(Query(grid, _ => true), Opener, opened.Route, P(20_000, 19_900), goal, 0, 4, 1_600);
        Assert.Equal(NavStepKind.Arrived, there.Kind);
    }

    [Fact]
    public void AnUnreachableGoal_IsHeld_UntilItMayRetry()
    {
        var grid = Field(100_000, 100_000, Room("wall"));
        var q = Query(grid);
        var first = NavFollower.Next(q, Opener, NavRoute.None, P(20_000, 10_000), P(20_000, 20_000), 0, 100, 1_600);
        Assert.Equal(NavStepKind.Unreachable, first.Kind);
        var held = NavFollower.Next(q, Opener, first.Route, P(20_000, 10_000), P(20_000, 20_000), 0, 101, 1_600);
        Assert.Null(held.ReplanReason);
        var retried = NavFollower.Next(q, Opener, first.Route, P(20_000, 10_000), P(20_000, 20_000), 0, 100 + q.Config.Limits.RetryTicks, 1_600);
        Assert.Equal(NavFollower.Retry, retried.ReplanReason);
    }

    [Fact]
    public void ARestamp_EqualsAFullBuild_ByteForByte()
    {
        var before = Room("open");
        var after = Room("wall");
        var grid = Field(200_000, 100_000, before);
        var rebuilt = grid.With(new NavRect(19_200, 16_000, 20_800, 16_400), after, null, out var touched, out long nodes);
        Assert.Equal(Field(200_000, 100_000, after).Digest(), rebuilt.Digest());
        Assert.Single(touched);
        Assert.True(nodes > 0);
        Assert.Equal(grid.Digest(), rebuilt.With(new NavRect(19_200, 16_000, 20_800, 16_400), before, null, out _, out _).Digest());
    }

    [Fact]
    public void TheSameQuery_GivesTheSameRoute_WithAnyScratch()
    {
        var grid = Field(100_000, 100_000, Room("open"));
        var shared = new NavScratch();
        var a = NavSearch.Plan(new NavQuery(grid, AllShut, grid.Config, shared, null), Opener, P(5_000, 5_000), P(20_000, 20_000));
        NavSearch.Plan(new NavQuery(grid, AllShut, grid.Config, shared, null), Opener, P(70_000, 70_000), P(60_000, 10_000));
        var b = NavSearch.Plan(new NavQuery(grid, AllShut, grid.Config, shared, null), Opener, P(5_000, 5_000), P(20_000, 20_000));
        var c = NavSearch.Plan(Query(grid), Opener, P(5_000, 5_000), P(20_000, 20_000));
        Assert.Equal(a.Corners, b.Corners);
        Assert.Equal(a.Corners, c.Corners);
        Assert.Equal(a.Expansions, c.Expansions);
    }

    [Fact]
    public void AFarGoal_IsTooFar_NotSearched()
    {
        var grid = Field(300_000, 100_000, Array.Empty<NavInput>());
        var plan = NavSearch.Plan(Query(grid), Opener, P(1_000, 1_000), P(290_000, 1_000));
        Assert.Equal(NavOutcome.TooFar, plan.Outcome);
        Assert.Equal(0, plan.Expansions);
    }

    [Fact]
    public void TheEditCheck_RefusesASealedPocket_AndADoorOntoAWall()
    {
        var grid = Field(100_000, 100_000, Room("open"));
        var points = ImmutableArray.Create(new NavProtectedPoint("Kera", NavPointKind.WorkAnchor, new NavRect(20_000, 20_000, 20_000, 20_000), 0, 400));
        var plug = ImmutableArray.Create(Box("plug", 19_200, 16_000, 20_800, 16_400));
        var sealedVerdict = NavEditCheck.Check(grid, grid.Config, new NavScratch(), plug, ImmutableArray<NavInput>.Empty, points);
        Assert.False(sealedVerdict.Ok);
        Assert.Equal("V-N1", sealedVerdict.Rule);
        Assert.Equal("that would cut Kera's work place off", sealedVerdict.Reason);

        var fine = NavEditCheck.Check(grid, grid.Config, new NavScratch(), ImmutableArray.Create(Box("post", 40_000, 40_000, 40_400, 40_400)),
            ImmutableArray<NavInput>.Empty, points);
        Assert.True(fine.Ok);

        var doorInWall = ImmutableArray.Create(Box("door.bad", 15_000, 18_000, 15_400, 19_600, NavInputKind.Door));
        var blind = Field(100_000, 100_000, Room("open").Append(Box("block", 13_000, 17_000, 15_000, 20_600)));
        var verdict = NavEditCheck.Check(blind, blind.Config, new NavScratch(), ImmutableArray<NavInput>.Empty, doorInWall, ImmutableArray<NavProtectedPoint>.Empty);
        Assert.Equal("V-N4", verdict.Rule);
    }

    [Fact]
    public void ARoute_IsOnlyBuiltValid_AndComparesByValue()
    {
        Assert.Throws<ArgumentException>(() => NavRoute.Active(P(1, 1), ImmutableArray<NavPoint>.Empty, 0, 0, default, false));
        Assert.Null(NavRoute.TryCreate(NavRouteStatus.Unreachable, 1, 1, ImmutableArray.Create(P(1, 1)), 0, 0, default, false, out var problem));
        Assert.NotNull(problem);
        var a = NavRoute.Active(P(5, 5), ImmutableArray.Create(P(1, 1), P(5, 5)), 3, 7, new NavRect(0, 0, 9, 9), false);
        var b = NavRoute.TryCreate(NavRouteStatus.Active, 5, 5, ImmutableArray.Create(P(1, 1), P(5, 5)), 3, 7, new NavRect(0, 0, 9, 9), false, out _);
        Assert.Equal(a, b);
        Assert.Equal(P(5, 5), Assert.Single(a.Advance(5).Corners));
    }

    [Fact]
    public void IntegerGeometry_AgreesWithSeparation()
    {
        var box = new BoxBlocker("b", 1_000, 1_000, 2_000, 3_000, 2_000);
        for (long x = 0; x <= 3_000; x += 137)
        {
            for (long z = 0; z <= 4_000; z += 131)
                Assert.Equal(box.Separation(x, z, 350) is null, NavGeometry.PointClear(x, z, 350, box));
        }
        Assert.False(NavGeometry.SegmentClear(0, 2_000, 3_000, 2_000, 350, box));
        Assert.True(NavGeometry.SegmentClear(0, 3_400, 3_000, 3_400, 350, box));
    }
}
