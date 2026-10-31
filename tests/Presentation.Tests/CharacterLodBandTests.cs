using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Tests;

public class CharacterLodBandTests
{
    [Theory]
    [InlineData("Pirate_Shirt_LOD0", "Pirate_Shirt", 0)]
    [InlineData("Pirate_Shirt_LOD2_lining", "Pirate_Shirt_lining", 2)]
    [InlineData("Pirate_Hat_LOD3_lining", "Pirate_Hat_lining", 3)]
    [InlineData("Native_Body_LOD1", "Native_Body", 1)]
    public void GarmentLinings_KeepTheSameBandAsTheirOuterSurface(string name, string family, int level)
    {
        Assert.Equal((family, level), CharacterLodBand.Part(name));
    }

    [Theory]
    [InlineData("UnbandedWing")]
    [InlineData("Shirt_LOD_lining")]
    [InlineData("Shirt_LOD1_unrecognised")]
    public void UnbandedOrMalformedNames_AreNotSilentlyAssignedABand(string name) =>
        Assert.Null(CharacterLodBand.Part(name));

    [Fact]
    public void WholeCharacterBands_HoldAcrossBoundaryJitter_AndCloseUpsStayDetailed()
    {
        Assert.Equal(0, CharacterLodBand.Select(2, 0, true));
        Assert.Equal(0, CharacterLodBand.Select(10, 0, true));
        Assert.Equal(2, CharacterLodBand.Select(40, 0, true));
        Assert.Equal(0, CharacterLodBand.Select(19, 0, true));
        Assert.Equal(1, CharacterLodBand.Select(21, 0, true));
        Assert.Equal(1, CharacterLodBand.Select(17, 1, true));
        Assert.Equal(0, CharacterLodBand.Select(15, 1, true));
        Assert.Equal(3, CharacterLodBand.Select(100, 0, true));
        Assert.Equal(0, CharacterLodBand.Select(100, 3, true, closeUp: true));
        Assert.Equal(1, CharacterLodBand.Select(16, 0, false));
    }
}
