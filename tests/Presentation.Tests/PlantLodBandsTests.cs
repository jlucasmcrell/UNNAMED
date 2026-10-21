using System.Numerics;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Tests;

public class PlantLodBandsTests
{
    [Theory]
    [InlineData(0, 6, 5.8f, false)]
    [InlineData(6, 14, 5.8f, false)]
    [InlineData(14, 30, 5.8f, false)]
    [InlineData(30, 52, 5.8f, true)]
    [InlineData(8, 20, 11.4f, false)]
    [InlineData(20, 40, 11.4f, true)]
    public void EveryContributingPlantHasItsCoarseBatchAvailable(float begin, float end, float radius, bool last)
    {
        var range = PlantLodBands.Guard(begin, end, radius, last);
        var random = new Random(4019);
        for (int i = 0; i < 20000; i++)
        {
            var plant = Vector3.Normalize(new Vector3((float)random.NextDouble()-.5f,
                (float)random.NextDouble()-.5f, (float)random.NextDouble()-.5f)) * (float)random.NextDouble() * radius;
            var camera = new Vector3((float)random.NextDouble() * 90, 1.5f, 0);
            float distance = Vector3.Distance(camera, plant), variation = (float)random.NextDouble()*2-1;
            // Independent shader predicate, including the final density fade reaching zero at end.
            bool contributes = (begin == 0 || distance >= begin + variation*Math.Min(1,begin*.1f)) &&
                (last ? distance < end : distance < end + variation*Math.Min(1,end*.1f));
            if (contributes)
                Assert.True((range.Begin == 0 || camera.Length() >= range.Begin) && camera.Length() <= range.End,
                    $"Contributing plant {distance} m away lost with batch centre {camera.Length()} m away");
        }
    }

    [Fact]
    public void OldCentreCutoffCanLoseNearPlantsInsideTheirOwnLod()
    {
        const float centre = 8, plantOffset = 5.8f, boundary = 6;
        Assert.True(centre > boundary); // Existing batch has already disappeared.
        Assert.True(centre - plantOffset < boundary*.9f); // Near plant still belongs to LOD0 for every hash.
        Assert.True(centre < PlantLodBands.Guard(0,boundary,plantOffset,false).End);
    }
}
