using UNNAMED.Presentation.Audio;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// The score's rule (AUDIO_DESIGN.md, "Where music stops", and the owner's ruling of 2026-10-24): title, fights, the tension before and after
/// them, settlements by day and night, the road in under the title theme, a known place now and then, and silence elsewhere.
/// </summary>
public class MusicChoiceTests
{
    [Theory]
    [InlineData(true, true, true, "mus.settlement", false, false, false, "mus.title")]
    [InlineData(false, true, true, "mus.settlement", false, true, true, "mus.combat")]
    [InlineData(false, true, false, null, true, false, false, "mus.combat")]
    [InlineData(false, false, true, "mus.settlement", false, true, true, "mus.tension")]
    [InlineData(false, false, true, null, false, false, false, "mus.tension")]
    [InlineData(false, false, false, "mus.settlement", false, true, false, "mus.settlement.day")]
    [InlineData(false, false, false, "mus.settlement", true, false, true, "mus.settlement.night")]
    [InlineData(false, false, false, null, false, true, true, "mus.title")]
    [InlineData(false, false, false, null, false, false, true, "mus.explore")]
    [InlineData(false, false, false, null, false, false, false, null)]
    [InlineData(false, false, false, null, true, false, false, null)]
    public void TheMoment_PicksItsBed(bool title, bool fighting, bool tense, string? theme, bool night, bool road, bool exploring, string? bed) =>
        Assert.Equal(bed, MusicChoice.Bed(title, fighting, tense, theme, night, road, exploring));
}
