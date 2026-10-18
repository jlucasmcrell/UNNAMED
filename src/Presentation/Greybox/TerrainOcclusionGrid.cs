namespace UNNAMED.Presentation.Greybox;

/// <summary>A lower envelope of the height image, including the support of the coarsest geomorphed triangles.</summary>
public sealed record TerrainOcclusionGrid(int Step, int Inset, int Columns, int Rows, float[] Heights)
{
    public static TerrainOcclusionGrid? Build(ReadOnlySpan<float> heights, int width, int height, int meshLods, int step = 16)
    {
        if (width < 1 || height < 1 || heights.Length != width * height || step < 1 || meshLods is < 1 or > 10)
            throw new ArgumentException("Invalid terrain occlusion grid");
        // Terrain3D's last clipmap has spacing 2^(lods-1). A morph and a triangle
        // together reach at most twice that, plus texel rounding. Every occluder
        // corner covers that full support AND its adjacent occluder cells.
        int radius = (1 << meshLods) + 2 + step;
        int inset = (radius + step - 1) / step * step;
        int columns = (width - 1 - 2 * inset) / step + 1, rows = (height - 1 - 2 * inset) / step + 1;
        if (width <= 2 * inset || height <= 2 * inset || columns < 2 || rows < 2) return null;
        int tileColumns = (width + step - 1) / step, tileRows = (height + step - 1) / step;
        var minima = new float[tileColumns * tileRows];
        Array.Fill(minima, float.PositiveInfinity);
        for (int z = 0; z < height; z++)
        for (int x = 0; x < width; x++)
        {
            float h = heights[z * width + x];
            if (!float.IsFinite(h)) return null; // Holes/unknown samples cannot prove solid occlusion.
            int tile = z / step * tileColumns + x / step;
            minima[tile] = Math.Min(minima[tile], h);
        }
        var result = new float[columns * rows];
        for (int z = 0; z < rows; z++)
        for (int x = 0; x < columns; x++)
        {
            int px = inset + x * step, pz = inset + z * step;
            float low = float.PositiveInfinity;
            for (int tz = (pz - radius) / step; tz <= (pz + radius) / step; tz++)
            for (int tx = (px - radius) / step; tx <= (px + radius) / step; tx++)
                low = Math.Min(low, minima[tz * tileColumns + tx]);
            result[z * columns + x] = low - .05f;
        }
        return new TerrainOcclusionGrid(step, inset, columns, rows, result);
    }
}
