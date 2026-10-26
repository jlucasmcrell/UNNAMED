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
    [InlineData(2.4f, 0f)]
    [InlineData(3.4f, .19f)]
    [InlineData(4.4f, .38f)]
    [InlineData(9f, .38f)]
    public void NearestGhostOnlyFromAJogAndFullAtASprint(float speed, float strength) => Assert.Equal(strength, PeopleLooks.AfterimageStrength(speed, 0), 3);

    [Fact]
    public void LaterGhostsTrailFurtherAndFainter()
    {
        Assert.Equal(new[] { .08, .16, .24 }, PeopleLooks.AfterimageLags);
        var strengths = Enumerable.Range(0, PeopleLooks.AfterimageLags.Length).Select(g => PeopleLooks.AfterimageStrength(5f, g)).ToArray();
        Assert.True(strengths.Zip(strengths.Skip(1)).All(p => p.First > p.Second && p.Second > 0));
        Assert.Equal(new[] { .38f, .251f, .122f }, strengths.Select(v => MathF.Round(v, 3)));
    }

    [Theory]
    [InlineData(0.0, .25f)]
    [InlineData(.6, .25f)]
    [InlineData(.85, .1125f)]
    [InlineData(1.0, 0f)]
    [InlineData(2.0, 0f)]
    public void DecoyHoldsThenFadesOverItsLastThird(double age, float strength) => Assert.Equal(strength, PeopleLooks.DecoyStrength(age, 1.0), 3);

    [Theory]
    [InlineData(0.0, 0f)]
    [InlineData(.1, .225f)]
    [InlineData(.75, .45f)]
    [InlineData(1.4, .225f)]
    [InlineData(1.5, 0f)]
    [InlineData(2.0, 0f)]
    public void PhaseVeilEasesInAndOut(double age, float strength) => Assert.Equal(strength, PeopleLooks.PhaseVeil(age, 1.5), 3);

    [Fact]
    public void PhasedTrailShowsAtAWalkAndStaysFainterThanASprint()
    {
        Assert.True(PeopleLooks.PhaseTrailFloor(0) > 0 && PeopleLooks.PhaseTrailFloor(0) < PeopleLooks.AfterimageStrength(5f, 0));
        Assert.True(PeopleLooks.PhaseTrailFloor(2) < PeopleLooks.PhaseTrailFloor(0));
    }

    [Fact]
    public void SiannPupilIsDark()
    {
        var pupil = PeopleLooks.SiannIris(0f, .5f);
        Assert.True(pupil.R + pupil.G + pupil.B < .1f && pupil.Blend > .5f);
    }

    [Fact]
    public void SiannScleraIsACoolPearl()
    {
        var sclera = PeopleLooks.SiannIris(.2f, .8f);
        Assert.True(sclera.B > sclera.R && sclera.R >= sclera.G, "cool: blue over red over green, never the warm HumGen white");
        Assert.InRange(sclera.Blend, .5f, 1f);
    }

    [Fact]
    public void SiannIrisIsLavenderWithABrighterInnerRing()
    {
        var inner = PeopleLooks.SiannIris(.06f, .4f);
        var outer = PeopleLooks.SiannIris(.09f, .4f);
        Assert.True(inner.B > inner.G && inner.R > inner.G, "lavender: blue and red over green");
        Assert.True(inner.R + inner.G + inner.B > outer.R + outer.G + outer.B, "the inner ring is brighter");
        Assert.Equal(1f, outer.Blend);
        var limbal = PeopleLooks.SiannIris(.112f, .4f);
        Assert.True(limbal.R + limbal.G + limbal.B < .6f * (outer.R + outer.G + outer.B), "a dark limbal ring at the iris edge");
    }
}
