using UNNAMED.Presentation.Audio;

namespace UNNAMED.Presentation.Tests;

/// <summary>The score's rule (AUDIO_DESIGN.md, "Where music stops"): title, fights, settlements by day and night, and silence elsewhere.</summary>
public class MusicChoiceTests
{
    [Theory]
    [InlineData(true, true, "mus.settlement", false, "mus.title")]
    [InlineData(false, true, "mus.settlement", false, "mus.combat")]
    [InlineData(false, true, null, true, "mus.combat")]
    [InlineData(false, false, "mus.settlement", false, "mus.settlement.day")]
    [InlineData(false, false, "mus.settlement", true, "mus.settlement.night")]
    [InlineData(false, false, null, false, null)]
    [InlineData(false, false, null, true, null)]
    public void TheMoment_PicksItsBed(bool title, bool fighting, string? theme, bool night, string? bed) =>
        Assert.Equal(bed, MusicChoice.Bed(title, fighting, theme, night));
}
