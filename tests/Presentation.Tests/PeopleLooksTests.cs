using UNNAMED.Presentation.Art;
namespace UNNAMED.Presentation.Tests;

public class PeopleLooksTests
{
    [Theory]
    [InlineData(18, .5f)]
    [InlineData(20, .5f)]
    [InlineData(48, .668f)]
    [InlineData(70, .8f)]
    [InlineData(120, .8f)]
    public void StoneSpreadsWithAgeWithinItsBounds(int age, float coverage) => Assert.Equal(coverage, PeopleLooks.StoneCoverage(age), 3);

    [Fact]
    public void OlderKalCarryMoreStone() => Assert.True(PeopleLooks.StoneCoverage(60) > PeopleLooks.StoneCoverage(30));

    [Theory]
    [InlineData(WingState.Folded, 0f, 0f)]
    [InlineData(WingState.Leap, .6f, 0f)]
    [InlineData(WingState.Glide, 1f, 0f)]
    [InlineData(WingState.Flare, 1f, -.35f)]
    public void EachWingStateHasItsSpanAndPitch(WingState state, float openness, float pitch)
    {
        Assert.Equal(openness, PeopleLooks.WingOpenness(state));
        Assert.Equal(pitch, PeopleLooks.WingPitch(state));
    }

    [Fact]
    public void WingStateOrderIsTheAbilitiesContract() =>
        // The race-abilities view passes these as ints.
        Assert.Equal(new[] { 0, 1, 2, 3 }, new[] { WingState.Folded, WingState.Glide, WingState.Flare, WingState.Leap }.Select(s => (int)s));

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1.4f, 0f)]
    [InlineData(2.6f, 0f)]
    [InlineData(3.8f, .09f)]
    [InlineData(5f, .18f)]
    [InlineData(9f, .18f)]
    public void AfterimageOnlyAtARunAndStaysFaint(float speed, float strength) => Assert.Equal(strength, PeopleLooks.AfterimageStrength(speed), 3);
}
