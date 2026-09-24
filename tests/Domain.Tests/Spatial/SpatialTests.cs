using System.Collections.Immutable;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Tests.Spatial;

public class TerrainGridTests
{
    // 3 x 2 points, 10 m apart: heights 0, 1, 2 m on the first row and 3, 4, 5 m on the second.
    private static readonly TerrainGrid Grid = new(0, 0, 10_000, 3, 2, new long[] { 0, 1_000, 2_000, 3_000, 4_000, 5_000 });

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(10_000, 0, 1_000)]
    [InlineData(20_000, 10_000, 5_000)]
    [InlineData(0, 10_000, 3_000)]
    public void GridPoints_AreExact(long x, long z, long height) => Assert.Equal(height, Grid.HeightAtMm(x, z));

    [Theory]
    [InlineData(5_000, 0, 500)]         // along an edge: linear
    [InlineData(5_000, 5_000, 2_000)]   // on the diagonal: both triangles agree
    [InlineData(7_500, 2_500, 1_500)]   // lower-right triangle: h00 + (h10-h00)u + (h11-h10)v
    [InlineData(2_500, 7_500, 2_500)]   // upper-left triangle: h00 + (h11-h01)u + (h01-h00)v
    public void BetweenPoints_TheSurfaceIsTheTwoTriangles(long x, long z, long height) => Assert.Equal(height, Grid.HeightAtMm(x, z));

    [Fact]
    public void OutsideTheGrid_TheEdgeHeightHolds()
    {
        Assert.Equal(0, Grid.HeightAtMm(-5_000, -5_000));
        Assert.Equal(5_000, Grid.HeightAtMm(99_000, 99_000));
    }

    [Fact]
    public void AMismatchedHeightCount_IsRefused() =>
        Assert.Throws<ArgumentException>(() => new TerrainGrid(0, 0, 1_000, 3, 3, new long[] { 0, 0, 0 }));
}

public class KinematicsTests
{
    private static readonly MovementRules Rules = new(3_200, 50, 160, 350, 1_600);
    private static readonly TerrainGrid Flat = new(0, 0, 100_000, 2, 2, new long[] { 1_000, 1_000, 1_000, 1_000 });
    private static readonly BoxBlocker Wall = new("wall", 10_000, 0, 10_200, 100_000, 3_000);   // a 20 cm wall at x = 10 m
    private static readonly WalkSpace Space = new(0, 0, 100_000, 100_000, Flat, ImmutableArray.Create<Blocker>(Wall));
    private static readonly IReadOnlyList<Blocker> None = Array.Empty<Blocker>();

    private static Body Run(Body body, MoveIntent intent, int ticks, IReadOnlyList<Blocker>? dynamic = null)
    {
        for (int i = 0; i < ticks; i++)
            body = Kinematics.Step(body, intent, Rules, Space, dynamic ?? None, 50);
        return body;
    }

    [Fact]
    public void FullDeflection_CoversTheBaseSpeed() =>
        Assert.Equal(new Body(5_000, 1_000, 5_160, 0), Kinematics.Step(new Body(5_000, 1_000, 5_000, 0), new MoveIntent(0, 1000, Gait.Run, 0), Rules, Space, None, 50));

    [Fact]
    public void ADiagonal_IsNoFasterThanAStraightLine()
    {
        var body = Kinematics.Step(new Body(5_000, 1_000, 5_000, 0), new MoveIntent(1000, 1000, Gait.Run, 45_000), Rules, Space, None, 50);
        double travelled = Math.Sqrt(Math.Pow(body.XMm - 5_000, 2) + Math.Pow(body.ZMm - 5_000, 2));
        Assert.InRange(travelled, 159, 161);
    }

    [Fact]
    public void HalfDeflection_IsHalfSpeed() =>
        Assert.Equal(5_080, Kinematics.Step(new Body(5_000, 1_000, 5_000, 0), new MoveIntent(0, 500, Gait.Run, 0), Rules, Space, None, 50).ZMm);

    [Fact]
    public void AWall_StopsTheBodyAtItsSurface()
    {
        var body = Run(new Body(5_000, 1_000, 50_000, 90_000), new MoveIntent(1000, 0, Gait.Sprint, 90_000), 100);
        Assert.Equal(10_000 - Rules.BodyRadiusMm, body.XMm);
        Assert.Equal(50_000, body.ZMm);
    }

    [Fact]
    public void AGlancingMove_SlidesAlongTheWall_InsteadOfSticking()
    {
        var body = Run(new Body(9_000, 1_000, 50_000, 0), new MoveIntent(700, 700, Gait.Run, 45_000), 40);
        Assert.Equal(10_000 - Rules.BodyRadiusMm, body.XMm);
        Assert.True(body.ZMm > 54_000, $"the body should have slid along the wall; z = {body.ZMm}");
    }

    [Fact]
    public void EvenASprint_CannotTunnelThroughAThinWall()
    {
        var body = Run(new Body(9_500, 1_000, 50_000, 90_000), new MoveIntent(1000, 0, Gait.Sprint, 90_000), 20);
        Assert.True(body.XMm < 10_000, $"the body tunnelled to x = {body.XMm}");
    }

    [Fact]
    public void TheBounds_StopTheBody()
    {
        var body = Run(new Body(50_000, 1_000, 50_000, 0), new MoveIntent(0, 1000, Gait.Sprint, 0), 2_000);
        Assert.Equal(100_000 - Rules.BodyRadiusMm, body.ZMm);
    }

    [Fact]
    public void ADynamicBlocker_BlocksOnlyWhileItIsPassedIn()
    {
        var door = new BoxBlocker("door", 20_000, 49_000, 20_200, 51_000, 2_400);
        var start = new Body(15_000, 1_000, 50_000, 90_000);
        var intent = new MoveIntent(1000, 0, Gait.Run, 90_000);

        Assert.Equal(20_000 - Rules.BodyRadiusMm, Run(start, intent, 100, new[] { door }).XMm);
        Assert.True(Run(start, intent, 100).XMm > 20_200);
    }

    [Fact]
    public void TheHeight_ComesFromTheTerrain_AndTheFacingFromTheIntent()
    {
        var sloped = new WalkSpace(0, 0, 100_000, 100_000, new TerrainGrid(0, 0, 100_000, 2, 2, new long[] { 0, 10_000, 0, 10_000 }), ImmutableArray<Blocker>.Empty);
        var body = Kinematics.Step(new Body(50_000, 0, 50_000, 0), new MoveIntent(1000, 0, Gait.Run, 90_000), Rules, sloped, None, 50);
        Assert.Equal((50_160L, 5_016L, 90_000), (body.XMm, body.YMm, body.FacingMdeg));
    }

    [Fact]
    public void TheSameStep_GivesTheSameBody_EveryTime()
    {
        var intent = new MoveIntent(313, -977, Gait.Sprint, 162_000);
        var first = Run(new Body(12_345, 1_000, 67_890, 0), intent, 500);
        var second = Run(new Body(12_345, 1_000, 67_890, 0), intent, 500);
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(1001, 0, 0)]
    [InlineData(0, -1001, 0)]
    [InlineData(0, 0, -1)]
    [InlineData(0, 0, 360_000)]
    public void AnOutOfRangeIntent_HasAProblem(int x, int z, int facing) =>
        Assert.NotNull(new MoveIntent(x, z, Gait.Run, facing).Problem());

    [Fact]
    public void AnInRangeIntent_HasNone() => Assert.Null(new MoveIntent(-1000, 1000, Gait.Walk, 359_999).Problem());
}

public class TierRulesTests
{
    // A within 100 m, B within 300 m, C within 1000 m, D beyond; 10 m of hysteresis.
    private static readonly TierRules Rules = new(100_000, 300_000, 1_000_000, 10_000);

    [Fact]
    public void Promotion_IsOneStepAtATime()
    {
        Assert.Equal(SimulationTier.C, Rules.Next(SimulationTier.D, 0));
        Assert.Equal(SimulationTier.B, Rules.Next(SimulationTier.C, 0));
        Assert.Equal(SimulationTier.A, Rules.Next(SimulationTier.B, 0));
    }

    [Fact]
    public void Demotion_IsOneStepAtATime_NeverAToD() =>
        Assert.Equal(SimulationTier.B, Rules.Next(SimulationTier.A, 5_000_000));

    [Theory]
    [InlineData(SimulationTier.A, 105_000, SimulationTier.A)]   // inside the hysteresis band: stays
    [InlineData(SimulationTier.B, 95_000, SimulationTier.B)]    // inside the band from the other side: stays
    [InlineData(SimulationTier.A, 111_000, SimulationTier.B)]   // clear of the band: demotes
    [InlineData(SimulationTier.B, 89_000, SimulationTier.A)]    // clear of the band: promotes
    public void Hysteresis_KeepsABoundaryCellFromThrashing(SimulationTier current, long distance, SimulationTier next) =>
        Assert.Equal(next, Rules.Next(current, distance));
}
