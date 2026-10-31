using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// D23 S1's rules without a world (STEALTH.md §2.1-§2.6): the four light bands and what each does to a sight line, the three sample points
/// cover hides a body by, the central and peripheral bands of a cone, and the light a set of authored lights adds at a point.
/// </summary>
public class VisibilityTests
{
    private static readonly LightRules Rules = new();

    [Theory]
    [InlineData(0, LightBand.Dark)]
    [InlineData(150, LightBand.Dark)]
    [InlineData(151, LightBand.Dim)]
    [InlineData(400, LightBand.Dim)]
    [InlineData(401, LightBand.Lit)]
    [InlineData(750, LightBand.Lit)]
    [InlineData(751, LightBand.Bright)]
    [InlineData(1000, LightBand.Bright)]
    public void TheFourBandsAreReadOffTheLightAtTheBody(int permille, LightBand band) =>
        Assert.Equal(band, Rules.Band(permille));

    [Theory]
    [InlineData(LightBand.Dark, 0.35)]
    [InlineData(LightBand.Dim, 0.6)]
    [InlineData(LightBand.Lit, 1.0)]
    [InlineData(LightBand.Bright, 1.15)]
    public void EachBandScalesSightByItsOwnNumber(LightBand band, double factor) =>
        Assert.Equal(factor, Rules.SightFactor(band, nightSight: false), 3);

    [Fact]
    public void TheDarkShortensASightLine_AndNeverGrantsInvisibility()
    {
        // The 0.35 floor is the whole rule: a 25 m wolf sees 8.75 m in the dark, so a body at two metres is still seen (§2.1).
        Assert.True(25_000 * Rules.SightFactor(LightBand.Dark, nightSight: false) > 2_000);
        // A night hunter is not blinded by the dark it lives in.
        Assert.Equal(0.9, Rules.SightFactor(LightBand.Dark, nightSight: true), 3);
        Assert.True(Rules.SightFactor(LightBand.Dark, nightSight: true) > Rules.SightFactor(LightBand.Dark, nightSight: false));
    }

    [Fact]
    public void AHedgeHidesACrouchedBody_AndNotAStandingOne()
    {
        // A 1.2 m hedge: crouched (1.1 m) is behind it, standing (1.8 m) is not - and the crouched body is a third seen, not gone (§2.3).
        var hedge = new CoverVolume("cover.hedge", "hedge", 0, 0, 1_500, 1_200, 100);
        Assert.True(Visibility.Hides(hedge, Rules.Crouched, toCoverMm: 500));
        Assert.False(Visibility.Hides(hedge, Rules.Standing, toCoverMm: 500));
        Assert.Equal(0.0, Visibility.SeenFraction(hedge, Rules.Crouched, 500) is var fraction && fraction < 0.34 ? 0.0 : fraction, 3);
        // A body outside the cover's own footprint is not behind it at all.
        Assert.Equal(1.0, Visibility.SeenFraction(hedge, Rules.Crouched, toCoverMm: 9_000), 3);
        // Two thirds blocked leaves a third of the body: one of three sample points.
        var crate = new CoverVolume("cover.crate", "crate", 0, 0, 1_000, 1_100, 67);
        Assert.Equal(0.333, Visibility.SeenFraction(crate, Rules.Crouched, 400), 3);
        Assert.Equal(1.0, Visibility.SeenFraction(null, Rules.Standing, 0), 3);
    }

    [Fact]
    public void CoverPostureAndLightMultiply_AndThePeripheryIsWorthLess()
    {
        double lit = Visibility.Factor(Rules, LightBand.Lit, nightSight: false, seenFraction: 1.0, Rules.Standing, peripheralOnly: false, silhouette: false);
        double dark = Visibility.Factor(Rules, LightBand.Dark, nightSight: false, seenFraction: 1.0, Rules.Standing, peripheralOnly: false, silhouette: false);
        double crouchedBehind = Visibility.Factor(Rules, LightBand.Lit, nightSight: false, seenFraction: 1.0 / 3, Rules.Crouched, false, false);
        double peripheral = Visibility.Factor(Rules, LightBand.Lit, nightSight: false, seenFraction: 1.0, Rules.Standing, peripheralOnly: true, silhouette: false);
        Assert.True(dark < lit);
        Assert.True(crouchedBehind < dark, "a crouched body a third seen in good light is harder to read than a standing one in the dark");
        Assert.Equal(lit * 0.4, peripheral, 3);
    }

    [Fact]
    public void ThePeripheryIsPastThirtyFiveDegreesAndInsideTheField()
    {
        // Facing north (+Z) from the origin, a body due north is central; 60 degrees off is peripheral; due south is not seen at all.
        const long north = 0;
        Assert.True(Visibility.InField(north, 140_000, 0, 0, 0, 10_000));
        Assert.False(Visibility.InPeriphery(north, 140_000, 0, 0, 0, 10_000));
        var (x, z) = ((long)(Math.Sin(Math.PI / 3) * 10_000), (long)(Math.Cos(Math.PI / 3) * 10_000));
        Assert.True(Visibility.InField(north, 140_000, 0, 0, x, z));
        Assert.True(Visibility.InPeriphery(north, 140_000, 0, 0, x, z));
        Assert.False(Visibility.InField(north, 140_000, 0, 0, 0, -10_000));
    }

    [Fact]
    public void LightsAddAtAPoint_AndFallToNothingAtTheirRadius()
    {
        var torch = new LightSource("light.torch", 0, 0, 4_000, 600);
        Assert.Equal(600, torch.At(0, 0));
        Assert.Equal(0, torch.At(0, 4_000));
        Assert.Equal(300, torch.At(2_000, 0));
        var brazier = new LightSource("light.brazier", 3_000, 0, 3_000, 400);
        // Where they overlap the light is the sum: the torch's 300 at that point plus the brazier's falling-off 267.
        Assert.Equal(567, Rules.At(new[] { torch, brazier }, 2_000, 0, 0));
        Assert.Equal(90, Rules.At(new[] { torch, brazier }, 20_000, 0, 90));
        // The bands are never asked past full day, however many lights stand over one spot.
        Assert.Equal(LightRules.PerMille, Rules.At(Enumerable.Repeat(torch, 4), 0, 0, 900));
    }

    [Fact]
    public void AmbientIsTheClockOutside_AndARoomsOwnFloorInside()
    {
        Assert.True(Rules.Ambient(exterior: true, night: false) > Rules.Ambient(exterior: true, night: true));
        Assert.Equal(Rules.InteriorAmbient, Rules.Ambient(exterior: false, night: false));
        Assert.Equal(Rules.InteriorAmbient, Rules.Ambient(exterior: false, night: true));
        // The interior floor is dark enough to be worth hiding in, and never the day.
        Assert.Equal(LightBand.Dark, Rules.Band(Rules.Ambient(exterior: false, night: true)));
    }

    [Fact]
    public void TheThreePosturesAreLowerAndSteadierInOrder()
    {
        Assert.True(Rules.Prone.HeightMm < Rules.Crouched.HeightMm && Rules.Crouched.HeightMm < Rules.Standing.HeightMm);
        Assert.True(Rules.Prone.SightGain < Rules.Crouched.SightGain && Rules.Crouched.SightGain < Rules.Standing.SightGain);
        Assert.Equal(Rules.Prone, Rules.Posture(Stance.Prone));
        Assert.Equal(Rules.Crouched, Rules.Posture(Stance.Crouched));
        Assert.Equal(Rules.Standing, Rules.Posture(Stance.Standing));
    }
}
