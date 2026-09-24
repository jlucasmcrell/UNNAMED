using UNNAMED.World;

namespace UNNAMED.World.Tests;

/// <summary>WORLD_ARCHITECTURE.md §3: keys and conversions, including the mandated edge values.</summary>
public class CoordinateTests
{
    // §3.3 mandates these six values, "with these expected cell indices stated, so the test can fail".
    // Three of them fail under C#'s % operator.
    [Theory]
    [InlineData(-200.0, 18, -1)]
    [InlineData(-100.0, 19, -1)]
    [InlineData(-0.001, 19, -1)]
    [InlineData(0.0, 0, 0)]
    [InlineData(0.001, 0, 0)]
    [InlineData(100.0, 1, 0)]
    public void CellOf_And_RegionOf_MatchTheSpecTable(double coordinate, int expectedCell, int expectedRegion)
    {
        var onX = CellKey.OfWorld(coordinate, 0.0);
        var onZ = CellKey.OfWorld(0.0, coordinate);

        Assert.Equal(expectedCell, onX.Cx);
        Assert.Equal(expectedRegion, onX.Region.Rx);
        Assert.Equal(expectedCell, onZ.Cz);
        Assert.Equal(expectedRegion, onZ.Region.Rz);
    }

    [Fact]
    public void FloorMod_IsEuclidean_WhereCSharpRemainderIsNot()
    {
        Assert.Equal(-2, -2 % 20);                     // the trap
        Assert.Equal(18, WorldMath.FloorMod(-2, 20));  // the rule
    }

    [Fact]
    public void Keys_UseTheSpecSpelling()
    {
        Assert.Equal("r_0_0:c_07_11", new CellKey(new RegionKey(0, 0), 7, 11).ToString());
        Assert.Equal("r_neg1_2", new RegionKey(-1, 2).ToString());
        Assert.Equal("r_neg1_0:c_19_03", CellKey.OfWorld(-50.0, 350.0).ToString());
    }

    [Theory]
    [InlineData("r_0_0:c_07_11")]
    [InlineData("r_neg1_2:c_00_19")]
    [InlineData("r_12_neg30:c_19_00")]
    public void CellKey_RoundTrips(string key) => Assert.Equal(key, CellKey.Parse(key).ToString());

    [Theory]
    [InlineData("r_0_0:c_20_00")]    // index out of [0, 19]
    [InlineData("r_-1_0:c_00_00")]   // '-' instead of neg<abs>
    [InlineData("r_00_0:c_00_00")]   // non-canonical spelling
    [InlineData("r_0_0:c_7_11")]     // missing zero padding
    [InlineData("r_0_0")]            // a region, not a cell
    public void CellKey_RejectsNonCanonicalKeys(string key) => Assert.False(CellKey.TryParse(key, out _));

    [Fact]
    public void CellKey_RejectsOutOfRangeIndices() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CellKey(new RegionKey(0, 0), 20, 0));

    [Fact]
    public void Region_Has400Cells_InCanonicalOrder()
    {
        var cells = CellKey.AllIn(new RegionKey(0, 0)).ToList();
        Assert.Equal(400, cells.Count);
        Assert.Equal(cells.OrderBy(c => c.ToString(), StringComparer.Ordinal), cells);
    }
}
