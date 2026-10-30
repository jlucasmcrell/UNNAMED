using UNNAMED.Domain.Combat;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// D23 S5's flight (STEALTH.md section 7.3-7.4): an arrow takes time, falls on the way, and the wind moves it - and a bolt is flatter because it is
/// faster, which is the whole of what the extra draw buys.
/// </summary>
public class ProjectileTests
{
    [Fact]
    public void AnArrowTakesTime_AndFallsOnTheWay()
    {
        var flight = ProjectileRules.Of(40_000, ProjectileProfile.Arrow);
        // Forty metres at 65 m/s is a little over half a second: eleven ticks of the fixed 20 Hz.
        Assert.Equal(12, flight.Ticks);
        // And over that time it falls: half of 9.8 m/s2 over 0.615 s squared, about 1.86 m.
        Assert.InRange(flight.DropMm, 1_700, 2_000);
    }

    [Fact]
    public void ABoltIsFaster_SoItFallsLessOverTheSameDistance()
    {
        var arrow = ProjectileRules.Of(40_000, ProjectileProfile.Arrow);
        var bolt = ProjectileRules.Of(40_000, ProjectileProfile.Bolt);
        Assert.True(bolt.Ticks < arrow.Ticks, $"a bolt arrives sooner ({bolt.Ticks} against {arrow.Ticks} ticks)");
        Assert.True(bolt.DropMm < arrow.DropMm, $"and falls less ({bolt.DropMm} against {arrow.DropMm} mm)");
    }

    [Fact]
    public void AThrownKnifeIsSlowEnoughToMatter()
    {
        var knife = ProjectileRules.Of(15_000, ProjectileProfile.Thrown);
        Assert.True(knife.Ticks > 10, $"fifteen metres of thrown knife is most of a second ({knife.Ticks} ticks)");
        Assert.True(knife.DropMm > 1_500, $"and it falls most of two metres ({knife.DropMm} mm)");
    }

    [Fact]
    public void TheWindPushesAShot_AlongItsOwnBearing()
    {
        // A wind blowing toward north (+Z) pushes a shot northward; toward east, eastward.
        var north = ProjectileRules.Of(40_000, ProjectileProfile.Arrow, windBearingMdeg: 0, windPush: 0.05);
        Assert.True(north.DriftZMm > 0);
        Assert.Equal(0, north.DriftXMm);
        var east = ProjectileRules.Of(40_000, ProjectileProfile.Arrow, windBearingMdeg: 90_000, windPush: 0.05);
        Assert.True(east.DriftXMm > 0);
        Assert.InRange(Math.Abs(east.DriftZMm), 0, 2);
        // Calm is calm.
        Assert.Equal(0, ProjectileRules.Of(40_000, ProjectileProfile.Arrow).DriftXMm);
    }

    [Fact]
    public void AShotBeyondItsRange_StopsAtTheRange()
    {
        // A knife thrown at a wall two hundred metres away is a knife thrown twenty metres and dropped.
        var atRange = ProjectileRules.Of(20_000, ProjectileProfile.Thrown);
        var beyond = ProjectileRules.Of(200_000, ProjectileProfile.Thrown);
        Assert.Equal(atRange.Ticks, beyond.Ticks);
        Assert.Equal(atRange.DropMm, beyond.DropMm);
    }

    [Fact]
    public void HoldoverIsTheFall_AndABodyIsBigEnoughToForgiveASmallDrop()
    {
        var profile = ProjectileProfile.Arrow;
        Assert.Equal(ProjectileRules.Of(30_000, profile).DropMm, ProjectileRules.HoldoverMm(30_000, profile));
        // A shot that fell 400 mm still hits a deer 1.8 m tall; one that fell 1.2 m does not.
        Assert.True(ProjectileRules.Lands(400, 1_800));
        Assert.False(ProjectileRules.Lands(1_200, 1_800));
    }
}