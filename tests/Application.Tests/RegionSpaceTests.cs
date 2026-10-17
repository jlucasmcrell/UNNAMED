using UNNAMED.Application;
using Xunit;

namespace UNNAMED.Application.Tests;

/// <summary>
/// An interior is enclosed (review section 7). Only a layout standing in the exterior has a stream, air and a sky;
/// everything built inside the ring is closed, or its holes would show daylight through the hill.
/// </summary>
public class RegionSpaceTests
{
    [Fact]
    public void TheWorldsRegionIsOutdoors()
    {
        using var profile = new TempProfile();
        Assert.True(Harness.Boot(profile).Setup.Layout.IsOutdoors);
    }

    [Fact]
    public void TheRingIsNot()
    {
        using var profile = new TempProfile();
        var setup = Harness.Boot(profile, barrowworks: true).Setup;
        Assert.NotNull(setup.Interior);
        Assert.False(setup.Interior!.IsOutdoors);
        Assert.NotEqual(setup.Layout.SpaceId, setup.Interior.SpaceId);
    }
}
