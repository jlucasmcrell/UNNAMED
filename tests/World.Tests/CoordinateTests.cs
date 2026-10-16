using System.Collections.Immutable;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

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

    // ── space qualification (M8 D10) ────────────────────────────────────────────────────────────────
    // The design's requirement is that two equal local coordinates in different spaces must never
    // interact or share a random stream. The Barrowworks sits at interior-local metres near the origin,
    // which is real exterior ground, so without a space in the identity its deltas and its RNG would
    // land on the same cell as the waystation's.

    private static readonly SpaceId Barrowworks = SpaceId.Parse("space.barrowworks");

    [Fact]
    public void SameLocalCoordinates_InDifferentSpaces_AreDifferentCells()
    {
        var exterior = CellKey.OfWorld(0.0, 0.0);
        var interior = CellKey.OfInterior(Barrowworks, 0.0, 0.0);

        Assert.NotEqual(exterior, interior);
        Assert.NotEqual(exterior.ToString(), interior.ToString());
        Assert.NotEqual(exterior.GetHashCode(), interior.GetHashCode());
        Assert.Equal("r_0_0:c_00_00", exterior.ToString());
        Assert.Equal("space.barrowworks/r_0_0:c_00_00", interior.ToString());
    }

    [Fact]
    public void TwoInteriorSpaces_DoNotCollide()
    {
        var barrowworks = CellKey.OfInterior(Barrowworks, 0.0, 0.0);
        var undermeasure = CellKey.OfInterior(SpaceId.Parse("space.undermeasure"), 0.0, 0.0);

        Assert.NotEqual(barrowworks, undermeasure);
        Assert.NotEqual(barrowworks.ToString(), undermeasure.ToString());
    }

    [Fact]
    public void InteriorCells_RoundTripThroughTheirCanonicalKey()
    {
        string key = CellKey.OfInterior(Barrowworks, 0.0, 26.0).ToString();
        Assert.Equal("space.barrowworks/r_0_0:c_00_00", key);
        Assert.Equal(key, CellKey.Parse(key).ToString());
        Assert.Equal(Barrowworks, CellKey.Parse(key).Space);
    }

    [Fact]
    public void ExteriorKeys_AreUnchangedBySpaceQualification()
    {
        // Every pre-M8 key must parse and print exactly as it did, or existing saves break.
        Assert.Equal("r_0_0:c_07_11", CellKey.OfWorld(750.0, 1150.0).ToString());
        Assert.Equal(SpaceId.Exterior, CellKey.OfWorld(750.0, 1150.0).Space);
        Assert.Equal(SpaceId.Exterior, CellKey.Parse("r_0_0:c_07_11").Space);
        Assert.True(SpaceId.Exterior.IsExterior);
        Assert.Equal("", SpaceId.Exterior.ToString());
    }

    [Fact]
    public void OfInterior_WithTheExteriorSpace_MatchesOfWorld() =>
        Assert.Equal(CellKey.OfWorld(250.0, 350.0), CellKey.OfInterior(SpaceId.Exterior, 250.0, 350.0));

    [Theory]
    [InlineData("space/name")]     // '/' is the space separator
    [InlineData("space:name")]     // ':' separates region from cell
    [InlineData("space name")]     // whitespace
    public void SpaceId_RejectsNamesThatCannotRoundTrip(string name) =>
        Assert.Throws<FormatException>(() => SpaceId.Parse(name));

    [Fact]
    public void CellKey_RejectsAnInteriorKeyWhoseSpaceIsMalformed() =>
        Assert.False(CellKey.TryParse("bad/space/r_0_0:c_00_00", out _));

    [Fact]
    public void DefaultCellKey_IsTheExteriorSpace()
    {
        // TryParse starts from `default`, and a struct's default runs no constructor.
        Assert.True(default(SpaceId).IsExterior);
        Assert.Equal("", default(SpaceId).ToString());
        Assert.True(default(CellKey).Space.IsExterior);
    }

    // ── a site's cell follows the space it stands in (M8 D10) ───────────────────────────────────────
    // Simulation.CellOf is how a door's, a switch's and a barrier's flag cell is found, and a flag cell
    // is where the flag, its delta and its baseline hash live. Two sites at the same authored coordinates
    // in different spaces must therefore resolve to different cells.

    private static DoorSite DoorAt(SpaceId space, long xMm, long zMm) =>
        new("door.barrowworks.grille", "world.barrowworks.gate_g1_open",
            new BoxBlocker("door.barrowworks.grille", xMm - 150, zMm - 150, xMm + 150, zMm + 150, 2_200))
        { Space = space };

    [Fact]
    public void AnInteriorSite_AndAnExteriorSiteAtTheSamePlace_ResolveToDifferentCells()
    {
        var exterior = Simulation.CellOf(DoorAt(SpaceId.Exterior, 40_000, 260_000));
        var interior = Simulation.CellOf(DoorAt(Barrowworks, 40_000, 260_000));

        Assert.Equal("r_0_0:c_00_02", exterior.ToString());
        Assert.Equal("space.barrowworks/r_0_0:c_00_02", interior.ToString());
        Assert.NotEqual(exterior, interior);
    }

    [Fact]
    public void AnExteriorSite_KeepsTheCellItHasAlwaysHad()
    {
        // The default Space is the exterior, so no authored site changes behaviour.
        var implicitSpace = Simulation.CellOf(DoorAt(SpaceId.Exterior, 40_000, 260_000));
        var explicitExterior = Simulation.CellOf(new DoorSite("k", "f",
            new BoxBlocker("k", 39_850, 259_850, 40_150, 260_150, 2_200)));

        Assert.Equal(explicitExterior, implicitSpace);
        Assert.Equal("r_0_0:c_00_02", explicitExterior.ToString());
    }

    [Fact]
    public void TwoInteriorSites_InDifferentSpaces_DoNotShareAFlagCell()
    {
        var barrowworks = Simulation.CellOf(DoorAt(Barrowworks, 0, 0));
        var undermeasure = Simulation.CellOf(DoorAt(SpaceId.Parse("space.undermeasure"), 0, 0));

        Assert.NotEqual(barrowworks, undermeasure);
    }

    // ── the random stream follows the cell, so it follows the space (M8 D10) ────────────────────────
    // RngChannel.Open hashes the cell's canonical string, and that string carries the space. This is the
    // design's requirement made structural: "two equal local coordinates in different spaces must never
    // interact or share a random stream accidentally". It holds because nothing had to be added - a channel
    // is keyed by the cell, and the cell now knows its space.

    private const ulong WorldSeed = 0x5C1A9E7B4D2F0083;

    [Fact]
    public void TheSameLocalCoordinates_InDifferentSpaces_DrawDifferentValues()
    {
        var exterior = RngChannel.Open(WorldSeed, CellKey.OfWorld(0.0, 260.0), "combat", "a>b@1");
        var interior = RngChannel.Open(WorldSeed, CellKey.OfInterior(Barrowworks, 0.0, 260.0), "combat", "a>b@1");

        Assert.NotEqual(exterior.UInt64(0), interior.UInt64(0));
    }

    [Fact]
    public void AChannelIsStable_AcrossOpensInTheSameSpace()
    {
        // The separation must not have cost determinism: the same key still gives the same stream, which is
        // what makes a replay and a reload agree.
        var first = RngChannel.Open(WorldSeed, CellKey.OfInterior(Barrowworks, 12.0, 34.0), "gather", "k@7");
        var second = RngChannel.Open(WorldSeed, CellKey.OfInterior(Barrowworks, 12.0, 34.0), "gather", "k@7");

        for (uint sample = 0; sample < 8; sample++)
            Assert.Equal(first.UInt64(sample), second.UInt64(sample));
    }

    [Fact]
    public void TwoInteriorSpaces_DoNotShareAStream()
    {
        var barrowworks = RngChannel.Open(WorldSeed, CellKey.OfInterior(Barrowworks, 0.0, 0.0), "loot", "crate");
        var undermeasure = RngChannel.Open(WorldSeed, CellKey.OfInterior(SpaceId.Parse("space.undermeasure"), 0.0, 0.0), "loot", "crate");

        Assert.NotEqual(barrowworks.UInt64(3), undermeasure.UInt64(3));
    }

    // ── a layout is one space, so its sites share it (M8 D10) ───────────────────────────────────────
    // Nodes, spawns, containers, stations, NPCs and named places are authored together in one coordinate
    // frame, so the space belongs on the layout rather than on six site records that could disagree with
    // each other and with the layout holding them.

    [Fact]
    public void ALayoutIsInTheExteriorUnlessItSaysOtherwise()
    {
        var layout = new RegionLayout("region.test", ImmutableArray<string>.Empty, default!, [], [],
            new Body(0, 0, 0, 0), new RegionGeneration(0, 0, 0));

        Assert.True(layout.SpaceId.IsExterior);
        Assert.True(layout.Portals.Portals.IsEmpty);
    }

    [Fact]
    public void AnInteriorLayoutsSiteCells_AreQualifiedWithItsOwnSpace()
    {
        // A node authored at the interior's local metres must not resolve to the exterior ground those
        // metres would otherwise name.
        var layout = new RegionLayout("region.barrowworks", ImmutableArray<string>.Empty, default!, [], [],
            new Body(0, 0, 0, 0), new RegionGeneration(0, 0, 0)) { SpaceId = Barrowworks };
        var node = new NodeSite("barrowworks.ore", "resource.ore.iron_vein", 4_000, 26_000);

        var inLayoutSpace = CellKey.OfInterior(layout.SpaceId, node.XMm / 1000.0, node.ZMm / 1000.0);
        var inExterior = CellKey.OfWorld(node.XMm / 1000.0, node.ZMm / 1000.0);

        Assert.Equal("space.barrowworks/r_0_0:c_00_00", inLayoutSpace.ToString());
        Assert.Equal("r_0_0:c_00_00", inExterior.ToString());
        Assert.NotEqual(inLayoutSpace, inExterior);
    }
}
