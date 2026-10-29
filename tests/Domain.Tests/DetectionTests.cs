// UNNAMED Domain - D23 S7: what a detector can and cannot see, and when a ward alarms

using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;
using Xunit;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// S7 of D23 (canon/STEALTH.md section 10). The two tech detectors and the ward have the blind spots canon gives them, and those blind spots are the
/// whole reason they are devices and not a reveal: a motion sensor misses a still body, a thermal sensor misses one behind stone, and a ward is quiet
/// when something else crosses it first.
/// </summary>
public class DetectionTests
{
    private static readonly SpaceId Exterior = SpaceId.Parse("");

    private static DetectorTarget At(long xMm, long zMm, bool moving = false, bool warm = true, long solidMm = 0, bool prone = false) =>
        new(Exterior, xMm, zMm, 1_700, moving, prone, warm, solidMm);

    [Fact]
    public void AMotionSensor_ReadsAMovingBody_AndIsBlindToAStillOrCrawlingOne()
    {
        // Facing east (0 degrees), three metres away and inside the cone.
        var moving = DetectorRules.Motion(Exterior, 0, 0, 0, At(3_000, 0, moving: true));
        var still = DetectorRules.Motion(Exterior, 0, 0, 0, At(3_000, 0, moving: false));

        Assert.True(moving.Read);
        Assert.Equal("motion", moving.Sense);
        Assert.False(still.Read, "a still body is exactly what a motion sensor cannot read");
        Assert.Contains("still", still.Why);
    }

    [Fact]
    public void AMotionSensor_ReadsNothingOutsideItsSixMetreCone()
    {
        Assert.False(DetectorRules.Motion(Exterior, 0, 0, 0, At(7_000, 0, moving: true)).Read);
        // Behind the lens: the cone is a cone, not a radius.
        Assert.False(DetectorRules.Motion(Exterior, 0, 0, 0, At(-3_000, 0, moving: true)).Read);
    }

    [Fact]
    public void AThermalSensor_ReadsAStillWarmBody_ThroughLightCover_AndNotThroughStone()
    {
        var throughGrass = DetectorRules.Thermal(Exterior, 0, 0, 0, At(5_000, 0, moving: false, solidMm: 200));
        var throughStone = DetectorRules.Thermal(Exterior, 0, 0, 0, At(5_000, 0, moving: false, solidMm: 2_000));
        var cold = DetectorRules.Thermal(Exterior, 0, 0, 0, At(5_000, 0, moving: false, warm: false));

        Assert.True(throughGrass.Read, "a thermal eye is what a still body behind grass cannot hide from");
        Assert.False(throughStone.Read, "stone and water stop a thermal eye");
        Assert.False(cold.Read, "a cold outer shell - a cloak - has no warmth to read");
    }

    [Fact]
    public void AWardAlarmsOnCrossing_AndIsQuietWhenSomethingElseCrossedItFirst()
    {
        Assert.True(WardRules.Alarms(toWardMm: 200, radiusMm: 500, quieted: false, crossedByDistraction: false, hasRuneKey: false));
        Assert.False(WardRules.Alarms(toWardMm: 900, radiusMm: 500, quieted: false, crossedByDistraction: false, hasRuneKey: false));
        Assert.False(WardRules.Alarms(toWardMm: 200, radiusMm: 500, quieted: true, crossedByDistraction: false, hasRuneKey: false));
        Assert.False(WardRules.Alarms(toWardMm: 200, radiusMm: 500, quieted: false, crossedByDistraction: true, hasRuneKey: false));
        Assert.False(WardRules.Alarms(toWardMm: 200, radiusMm: 500, quieted: false, crossedByDistraction: false, hasRuneKey: true));
    }
}
