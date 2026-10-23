using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Tests.Spatial;

/// <summary>Owner finding 50: a turned boulder's footprint is its hull, not the box round it.</summary>
public class PolygonBlockerTests
{
    // A long rock turned 30 degrees: its box would add a metre of empty corner.
    private static readonly PolygonBlocker Rock = new("rock", new (long, long)[]
    {
        (0, 0), (2600, 1500), (2100, 2400), (-500, 900), (1300, 1200),
    }, 900);

    [Fact]
    public void TheHull_DropsInnerPoints_AndRunsCounterClockwise()
    {
        Assert.Equal(4, Rock.Corners.Length);
        for (int i = 0; i < 4; i++)
            Assert.True(PolygonBlocker.Cross(Rock.Corners[i], Rock.Corners[(i + 1) % 4], Rock.Corners[(i + 2) % 4]) > 0);
    }

    [Fact]
    public void ABodyMeetsTheRock_NotTheEmptyCornerOfItsBox()
    {
        // (2400, 300) is inside the box (−500..2600 x 0..2400) but well clear of the rock.
        Assert.Null(Rock.Separation(2400, 300, 350));
        Assert.True(Rock.DistanceTo(2400, 300) > 350);
        var push = Rock.Separation(1300, 1200, 350);
        Assert.NotNull(push);
        var (dx, dz) = push!.Value;
        Assert.Null(Rock.Separation(1300 + dx * 1.001, 1200 + dz * 1.001, 350));
    }

    [Fact]
    public void Navigation_AgreesWithCollision()
    {
        for (long x = -1500; x <= 3600; x += 137)
        for (long z = -1000; z <= 3400; z += 131)
        {
            bool clear = Rock.Separation(x, z, 350) is null;
            Assert.Equal(clear, NavGeometry.PointClear(x, z, 350, Rock));
        }
    }

    [Fact]
    public void ASweptPath_IsBlockedOnlyWhereItMeetsTheRock()
    {
        Assert.False(NavGeometry.SegmentClear(-1500, 1200, 3600, 1200, 350, Rock));
        Assert.True(NavGeometry.SegmentClear(-1500, 3400, 3600, 3400, 350, Rock));
        Assert.True(Rock.Crosses(-1500, 1200, 3600, 1200));
        Assert.False(Rock.Crosses(-1500, 3400, 3600, 3400));
    }
}
