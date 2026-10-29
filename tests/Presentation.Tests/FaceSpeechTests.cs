// UNNAMED Presentation tests - the lip-sync mapping: a digraph is one shape, and a word ends with the mouth shut (Gate D part 3).

using UNNAMED.Presentation.Art;
using Xunit;

namespace UNNAMED.Presentation.Tests;

public sealed class FaceSpeechTests
{
    [Theory]
    [InlineData('t', 'h', "viseme_TH")]
    [InlineData('s', 'h', "viseme_CH")]
    [InlineData('c', 'h', "viseme_CH")]
    [InlineData('n', 'g', "viseme_nn")]
    [InlineData('a', 'a', "viseme_aa")]
    [InlineData('o', 'o', "viseme_O")]
    [InlineData('m', 'a', "viseme_PP")]
    [InlineData('r', 'o', "viseme_RR")]
    public void ALetter_MapsToItsViseme(char letter, char next, string expected) =>
        Assert.Equal(expected, Speech.Shape(' ', letter, next).Shape);

    [Theory]
    [InlineData(' ')]
    [InlineData('\n')]
    [InlineData('.')]
    [InlineData(',')]
    [InlineData('?')]
    public void AWordOrSentenceEnd_ClosesTheMouth(char letter)
    {
        var (shape, jaw) = Speech.Shape(' ', letter, 'a');
        Assert.Equal("viseme_sil", shape);
        Assert.Equal(0f, jaw);
    }

    [Fact]
    public void TheSecondHalfOfADigraph_IsSilent()
    {
        // "th" is said by the T's viseme; the H must not open the mouth a second time.
        Assert.Equal("viseme_sil", Speech.Shape('t', 'h', 'e').Shape);
    }

    [Fact]
    public void AClosedMouthSound_CarriesNoJaw()
    {
        Assert.Equal(0f, Speech.Shape(' ', 'p', 'a').Jaw);
        Assert.True(Speech.Shape(' ', 'a', ' ').Jaw > 0.4f);
    }
}
