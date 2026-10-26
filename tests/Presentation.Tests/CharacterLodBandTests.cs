using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Tests;

public class CharacterLodBandTests
{
    [Fact]
    public void NamedFaceMaps_StayScopedToTheConversation_AndDoNotChurnAtWalkingDistance()
    {
        Assert.False(CharacterLodBand.CloseTextures(3.5f, false));
        Assert.True(CharacterLodBand.CloseTextures(2.9f, false));
        Assert.True(CharacterLodBand.CloseTextures(3.5f, true));
        Assert.False(CharacterLodBand.CloseTextures(4.1f, true));
        Assert.True(CharacterLodBand.CloseTextures(20f, false, focused: true));
        Assert.False(CharacterLodBand.CloseTextures(20f, true));
    }

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
