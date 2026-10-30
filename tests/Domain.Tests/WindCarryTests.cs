using UNNAMED.Domain.Combat;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// D23 S5's wind (STEALTH.md section 7.5): the air carries a shot for as long as it is in the air, toward the bearing it blows. It moves where a shot that
/// hits nothing comes down, and it is nothing at all on a still day.
/// </summary>
public class WindCarryTests
{
    [Fact]
    public void AWindOutOfTheEast_CarriesTheArrowWest()
    {
        // Bearing 270 degrees is the wind blowing toward the west, so the arrow drifts west too.
        var carried = ProjectileRules.CarriedMm(bearingMdeg: 270_000, strengthMmPerSecond: 1_000, distanceMm: 40_000, ProjectileProfile.Arrow);
        Assert.True(carried.XMm < -300, $"a hundred metres of flight in a metre-per-second wind moves it west: {carried.XMm} mm");
        Assert.InRange(carried.ZMm, -20, 20);
    }

    [Fact]
    public void AStillDay_CarriesItNowhere()
    {
        var (x, z) = ProjectileRules.CarriedMm(bearingMdeg: 90_000, strengthMmPerSecond: 0, distanceMm: 40_000, ProjectileProfile.Arrow);
        Assert.Equal(0, x);
        Assert.Equal(0, z);
    }

    [Fact]
    public void TheCarryGrowsWithTheFlight_NotWithTheWindAlcove()
    {
        var near = ProjectileRules.CarriedMm(0, 1_000, 10_000, ProjectileProfile.Arrow);
        var far = ProjectileRules.CarriedMm(0, 1_000, 40_000, ProjectileProfile.Arrow);
        Assert.True(far.ZMm > near.ZMm, $"four times the flight is four times the carry ({near.ZMm} then {far.ZMm})");
    }
}