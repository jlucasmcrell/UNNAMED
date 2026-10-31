using UNNAMED.Domain.Combat;
using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// D23 S1's HUD from the outside (STEALTH.md §2.7): the gem reads the same four bands the sight check uses, each with its own shape so colour
/// is not the only carrier; the noise ring is the real radius in metres; and an observer's own awareness is what the ring fills with, aimed
/// at the place it thinks the character is rather than at the character.
/// </summary>
public class StealthHudModelTests
{
    [Theory]
    [InlineData(LightBand.Dark, "flat")]
    [InlineData(LightBand.Dim, "soft")]
    [InlineData(LightBand.Lit, "edged")]
    [InlineData(LightBand.Bright, "washed")]
    public void TheSilhouetteHasAShapeOfItsOwnForEveryBand(LightBand band, string shape)
    {
        var gem = ShadowModel.From(band);
        Assert.Equal(shape, gem.Shape);
        Assert.Equal(band, gem.Band);
        Assert.False(string.IsNullOrWhiteSpace(gem.Words));
    }

    [Fact]
    public void TheInkFallsMonotonically_SoABrighterBandNeverReadsDarker()
    {
        var fills = ShadowModel.Bands.Select(b => ShadowModel.From(b).Ink).ToArray();
        for (int i = 1; i < fills.Length; i++)
            Assert.True(fills[i] < fills[i - 1], $"{ShadowModel.Bands[i]} must read fainter than {ShadowModel.Bands[i - 1]}");
        // All four bands are distinct in shape, so a colour-blind player still reads four steps.
        Assert.Equal(4, ShadowModel.Bands.Select(b => ShadowModel.From(b).Shape).Distinct().Count());
    }

    [Theory]
    [InlineData(0, "Silent")]
    [InlineData(500, "0.5 m")]
    [InlineData(1_500, "2 m")]
    [InlineData(16_000, "16 m")]
    public void TheNoiseRingIsTheRadiusInMetres(long radiusMm, string words)
    {
        Assert.Equal(words, NoiseRingModel.From(radiusMm).Words);
    }

    [Fact]
    public void TheNoiseRingNamesTheSurface_WhenThereIsOne()
    {
        Assert.Equal("2 m - gravel", NoiseRingModel.From(1_500, "gravel").Words);
        Assert.Equal(1.5f, NoiseRingModel.From(1_500).RadiusMetres, 3);
        Assert.Equal(0f, NoiseRingModel.From(-20).RadiusMetres);
    }

    [Fact]
    public void TheAwarenessRingFillsWithTheRealNumber()
    {
        Assert.Equal(0f, new AwarenessRead(0, 100, NoticeSense.Eye, 0, false).Fill, 3);
        Assert.Equal(0.3f, new AwarenessRead(30, 100, NoticeSense.Ear, 90, true).Fill, 3);
        Assert.Equal(1f, new AwarenessRead(100, 100, NoticeSense.Eye, 90, true).Fill, 3);
        // A number beyond full cannot read beyond full, and a zero scale cannot divide by it.
        Assert.Equal(1f, new AwarenessRead(340, 100, NoticeSense.Eye, 0, true).Fill, 3);
        Assert.Equal(0f, new AwarenessRead(30, 0, NoticeSense.Eye, 0, true).Fill, 3);
    }

    [Fact]
    public void TheDirectionWedgePointsAtWhatItNoticed_NotAtTheCharacter()
    {
        // North is +Z, east is +X: the world's own bearing convention, which the compass shares.
        Assert.Equal(0f, AwarenessRead.Toward(0, 0, 0, 1_000).Bearing, 2);
        Assert.Equal(90f, AwarenessRead.Toward(0, 0, 1_000, 0).Bearing, 2);
        Assert.Equal(180f, AwarenessRead.Toward(0, 0, 0, -1_000).Bearing, 2);
        Assert.Equal(270f, AwarenessRead.Toward(0, 0, -1_000, 0).Bearing, 2);
        // Standing on the place itself gives no direction at all, so no wedge is drawn.
        Assert.False(AwarenessRead.Toward(50, 50, 50, 50).Has);
        Assert.True(AwarenessRead.Toward(0, 0, 0, 10).Has);
    }
}
