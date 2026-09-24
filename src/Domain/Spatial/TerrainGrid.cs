// UNNAMED Domain - the walkable terrain surface (WORLD_ARCHITECTURE.md §3.1, §4)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Spatial;

/// <summary>
/// An authored height grid over the XZ plane, in integer millimetres. Every quad is split into two
/// triangles along its (0,0)-(1,1) diagonal and heights interpolate linearly on each triangle, so a render
/// mesh built from the same grid points with the same split is exactly this surface. Integer arithmetic
/// only: the same query gives the same height on every machine.
/// </summary>
public sealed class TerrainGrid
{
    public TerrainGrid(long originXMm, long originZMm, long spacingMm, int columns, int rows, IEnumerable<long> heightsMm)
    {
        if (spacingMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(spacingMm), spacingMm, "Grid spacing must be positive");
        if (columns < 2 || rows < 2)
            throw new ArgumentException("A terrain grid needs at least 2 x 2 points");
        OriginXMm = originXMm;
        OriginZMm = originZMm;
        SpacingMm = spacingMm;
        Columns = columns;
        Rows = rows;
        HeightsMm = heightsMm.ToImmutableArray();
        if (HeightsMm.Length != columns * rows)
            throw new ArgumentException($"A {columns} x {rows} grid needs {columns * rows} heights, got {HeightsMm.Length}");
    }

    public long OriginXMm { get; }
    public long OriginZMm { get; }
    public long SpacingMm { get; }

    /// <summary>Points along X.</summary>
    public int Columns { get; }

    /// <summary>Points along Z.</summary>
    public int Rows { get; }

    /// <summary>Row-major: the point at column <c>i</c> (X) and row <c>j</c> (Z) is <c>HeightsMm[j * Columns + i]</c>.</summary>
    public ImmutableArray<long> HeightsMm { get; }

    public long MaxXMm => OriginXMm + (Columns - 1) * SpacingMm;
    public long MaxZMm => OriginZMm + (Rows - 1) * SpacingMm;

    public long HeightAt(int column, int row) => HeightsMm[row * Columns + column];

    /// <summary>The surface height at a ground point; points outside the grid take the height of its edge.</summary>
    public long HeightAtMm(long xMm, long zMm)
    {
        long cx = Math.Clamp(xMm - OriginXMm, 0, (Columns - 1) * SpacingMm);
        long cz = Math.Clamp(zMm - OriginZMm, 0, (Rows - 1) * SpacingMm);
        int i = (int)Math.Min(cx / SpacingMm, Columns - 2);
        int j = (int)Math.Min(cz / SpacingMm, Rows - 2);
        long du = cx - i * SpacingMm;
        long dv = cz - j * SpacingMm;
        long h00 = HeightAt(i, j), h10 = HeightAt(i + 1, j), h01 = HeightAt(i, j + 1), h11 = HeightAt(i + 1, j + 1);
        long numerator = du >= dv
            ? (h10 - h00) * du + (h11 - h10) * dv
            : (h11 - h01) * du + (h01 - h00) * dv;
        return h00 + RoundDiv(numerator, SpacingMm);
    }

    private static long RoundDiv(long numerator, long denominator) =>
        numerator >= 0 ? (numerator + denominator / 2) / denominator : -((-numerator + denominator / 2) / denominator);
}
