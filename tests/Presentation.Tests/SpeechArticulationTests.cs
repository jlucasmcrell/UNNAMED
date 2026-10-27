using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Tests;

public class SpeechArticulationTests
{
    [Fact]
    public void NativeJawAndLipWeightIsReduced_AndBilabialsClose()
    {
        Assert.Equal(.65f * .55f, SpeechArticulation.Jaw("viseme_aa", .65f));
        Assert.Equal(.025f, SpeechArticulation.Jaw("viseme_PP", .65f));
        Assert.Equal(0, SpeechArticulation.Jaw("viseme_aa", 0));
        Assert.Equal(0, SpeechArticulation.Jaw("viseme_aa", float.NaN));
    }
}
