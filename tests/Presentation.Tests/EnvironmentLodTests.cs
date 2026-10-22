using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Tests;

public class EnvironmentLodTests
{
    [Fact]
    public void EveryTierKeepsOneFullyOpaqueLevelThroughoutEachHandoff()
    {
        foreach (string tier in new[] { "low", "medium", "high", "ultra" })
        foreach (bool tree in new[] { false, true })
        {
            var starts = EnvironmentLod.ModelStarts(tier, tree, 14, 1);
            for (int i = 1; i < starts.Length; i++)
            {
                var near = EnvironmentLod.Band(starts[i - 1], starts[i], true);
                var far = EnvironmentLod.Band(starts[i], i + 1 < starts.Length ? starts[i + 1] : 0, true);
                Assert.True(near.End - near.EndMargin >= far.Begin + far.BeginMargin - .001f);
                Assert.True(near.End + near.EndMargin >= far.Begin);
            }
        }
        Assert.Equal(400, EnvironmentLod.ModelStarts("ultra", true, 14, 1)[1]);
        Assert.Equal(250, EnvironmentLod.ModelStarts("high", true, 14, 1)[1]);
        Assert.Equal(35, EnvironmentLod.ModelStarts("medium", true, 14, 1)[1]);
    }

    [Fact]
    public void FullDensityExtendsToTheExistingFurthestGrassReachBeforeOuterFade()
    {
        Assert.Equal(400, EnvironmentLod.GroundFullReach("ultra", 65, 1, tree: true));
        Assert.Equal(250, EnvironmentLod.GroundFullReach("high", 65, 1, tree: true));
        foreach (float authored in new[] { 30f, 45f, 70f, 120f })
        {
            float full = EnvironmentLod.GroundFullReach("ultra", authored, 1);
            Assert.True(full >= authored * 1.8f);
            Assert.True(EnvironmentLod.GroundEnd("ultra", full) > full);
            Assert.Equal(authored, EnvironmentLod.GroundFullReach("medium", authored, 1));
        }
    }
}
