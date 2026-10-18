using UNNAMED.Presentation.Greybox;

namespace UNNAMED.Presentation.Tests;

public sealed class TerrainOcclusionGridTests
{
    [Fact]
    public void CoarsePlanesStayBelowFineHeightsAndGeomorphedLodSupport()
    {
        const int size = 256, lods = 4;
        var heights = new float[size * size];
        for (int z = 0; z < size; z++)
        for (int x = 0; x < size; x++)
            heights[z * size + x] = (float)(6 * Math.Sin(x * .13) + 4 * Math.Cos(z * .17) + .03 * x);
        heights[127 * size + 113] = -37; // A one-texel pit, easily missed by a sparse bake.
        heights[128 * size + 129] = 45;
        var grid = TerrainOcclusionGrid.Build(heights, size, size, lods)!;
        for (int cz = 0; cz < grid.Rows - 1; cz++)
        for (int cx = 0; cx < grid.Columns - 1; cx++)
        {
            float highest = new[] { grid.Heights[cz * grid.Columns + cx], grid.Heights[cz * grid.Columns + cx + 1],
                grid.Heights[(cz + 1) * grid.Columns + cx], grid.Heights[(cz + 1) * grid.Columns + cx + 1] }.Max();
            int x0 = grid.Inset + cx * grid.Step, z0 = grid.Inset + cz * grid.Step;
            // Every possible contributing source height is above every corner of the
            // coarse cell, so both coarse triangles and every convex geomorph are safe.
            for (int z = z0 - (1 << lods); z <= z0 + grid.Step + (1 << lods); z++)
            for (int x = x0 - (1 << lods); x <= x0 + grid.Step + (1 << lods); x++)
                Assert.True(highest < heights[z * size + x], $"Occluder above terrain at {x},{z}");
        }
        heights[100] = float.NaN;
        Assert.Null(TerrainOcclusionGrid.Build(heights, size, size, lods));
        Assert.Null(TerrainOcclusionGrid.Build(new float[64 * 64], 64, 64, 7));
    }
}
