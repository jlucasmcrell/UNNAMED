using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Tests;

public class ScatterPlacementTests
{
    [Theory]
    [InlineData(1.95f, 0, 2.4f, false)] // Cairnwold millpond bed: above sea level, below its own 4.35 m surface.
    [InlineData(4.34f, 0, .01f, false)] // Even shallow water rejects a shrub or dead bush.
    [InlineData(4.35f, 0, 0, true)]
    [InlineData(4.48f, .1f, 0, true)] // Dry bank remains available.
    [InlineData(.1f, 0, 0, false)]
    [InlineData(5, .8f, 0, false)]
    public void AllScatterKindsRequireDryGround(float height, float slope, float depth, bool expected) =>
        Assert.Equal(expected, ScatterRules.None.AllowsGround(height, slope, depth));
}
