using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Tests;

/// <summary>The display-name path's words (doc 09 §8): an ID is read as words, and an instance ID is never shown, not even as words.</summary>
public class NamesTests
{
    [Theory]
    [InlineData("door.forge_shed", "forge shed door")]
    [InlineData("cw_house05_door", "house door")]
    [InlineData("cairnwold.forge_anvil", "forge anvil")]
    public void APlaceKey_ReadsAsWords(string key, string words) => Assert.Equal(words, Names.Place(key));

    /// <summary>The HUD coherence note (08 §2): a placed bench's prompt read "Work at the pce 0000000002vx1t1kpv15h4s90x".</summary>
    [Theory]
    [InlineData("pce_0000000002vx1t1kpv15h4s90x")]
    [InlineData("itm_01J8ZC4K9P4M2Q7X8B3NDTVW6R")]
    [InlineData("crp_01J8ZC4K9P4M2Q7X8B3NDTVW6R")]
    public void AnInstanceId_IsNeverReadAsWords(string key)
    {
        Assert.True(Names.IsInstanceId(key));
        Assert.Equal("thing", Names.Place(key));
        Assert.Equal("thing", Names.Words(key));
    }

    [Theory]
    [InlineData("item.weapon.rusted_sword")]
    [InlineData("cw_house05_door")]
    [InlineData("pce_short")]
    public void AContentOrLayoutKey_IsNotAnInstanceId(string key) => Assert.False(Names.IsInstanceId(key));
}
