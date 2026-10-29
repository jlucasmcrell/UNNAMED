using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// D23 S2's rules without a world (STEALTH.md §3.1, §3.4): the floor decides how far a step carries, the smallest authored patch wins, and what
/// a place covers up masks a sound without silencing it - except a shot, which is louder than the weather.
/// </summary>
public class SoundTests
{
    [Theory]
    [InlineData("gravel", 1.5)]
    [InlineData("metal", 1.8)]
    [InlineData("stone", 1.2)]
    [InlineData("wood", 1.25)]
    [InlineData("water", 1.3)]
    [InlineData("grass", 0.8)]
    [InlineData("mud", 0.7)]
    [InlineData("earth", 1.0)]
    [InlineData("something_nobody_authored", 1.0)]
    public void EverySurfaceHasItsOwnCarry(string kind, double factor) => Assert.Equal(factor, Surfaces.Factor(kind), 3);

    [Fact]
    public void TheSmallestPatchWins_AndUnwrittenGroundIsEarth()
    {
        var yard = new SurfacePatch("surface.yard", "gravel", 0, 0, 20_000, 20_000);
        var floor = new SurfacePatch("surface.floor", "wood", 5_000, 5_000, 9_000, 9_000);
        var patches = new[] { yard, floor };
        Assert.Equal("wood", Surfaces.At(patches, 6_000, 6_000));
        Assert.Equal("gravel", Surfaces.At(patches, 2_000, 2_000));
        Assert.Equal("earth", Surfaces.At(patches, 40_000, 40_000));
        Assert.Equal("earth", Surfaces.At(Array.Empty<SurfacePatch>(), 0, 0));
    }

    [Fact]
    public void MaskingShortensTheReach_AndNeverSilencesIt()
    {
        var step = new Noise(0, 0, 3_000) { Source = SoundSource.Footfall };
        Assert.Equal(3_000, Perception.MaskedRadiusMm(step, 0));
        Assert.Equal(2_100, Perception.MaskedRadiusMm(step, 0.3));   // rain
        Assert.Equal(1_500, Perception.MaskedRadiusMm(step, 0.5));   // standing at the creek
        // Nothing masks more than four fifths of a sound, so a step is never free.
        Assert.Equal(600, Perception.MaskedRadiusMm(step, 5.0));
    }

    [Fact]
    public void ASoundLouderThanTheWeatherKeepsHalfItsReach()
    {
        var shot = new Noise(0, 0, 24_000) { Source = SoundSource.Shot };
        // Past twenty metres a shot is louder than the rain: masking takes half of what it would have taken.
        Assert.Equal(20_400, Perception.MaskedRadiusMm(shot, 0.3));
        var quiet = new Noise(0, 0, 24_000) { Source = SoundSource.Footfall };
        Assert.Equal(16_800, Perception.MaskedRadiusMm(quiet, 0.3));
    }
}
