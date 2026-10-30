using UNNAMED.Domain.Creatures;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// D23 S3's scent on the wind (STEALTH.md section 4.6): the one sense that ignores light, cover and posture, and the counter to the stealth player's
/// best tricks. The wind is derived from the world seed and the day, so it is the same for everyone in the same world and costs no saved state.
/// </summary>
public class ScentTests
{
    [Fact]
    public void ScentComesFromUpwind_InsideACone()
    {
        // The wind blows toward north (+Z), so a nose at the origin smells what lies SOUTH of it - upwind - and nothing that lies north.
        const long north = 0;
        Assert.True(ScentRules.Carries(north, 0, 0, 0, -10_000));
        Assert.False(ScentRules.Carries(north, 0, 0, 0, 10_000));
        // Directly across the wind is the edge of the cone, not inside it.
        Assert.False(ScentRules.Carries(north, 0, 0, 10_000, 0));
        Assert.False(ScentRules.Carries(north, 0, 0, -10_000, 0));
        // Stood on top of it, no wind is needed.
        Assert.True(ScentRules.Carries(north, 100, 100, 100, 100));
    }

    [Fact]
    public void TheConeIsWiderThanALine_AndNarrowerThanAHalfTurn()
    {
        const long north = 0;
        // Twenty degrees off the upwind line is inside the cone; seventy is not.
        double inside = 20 * Math.PI / 180, outside = 70 * Math.PI / 180;
        Assert.True(ScentRules.Carries(north, 0, 0, (long)(Math.Sin(inside) * -10_000), (long)(Math.Cos(inside) * -10_000)));
        Assert.False(ScentRules.Carries(north, 0, 0, (long)(Math.Sin(outside) * -10_000), (long)(Math.Cos(outside) * -10_000)));
    }

    [Fact]
    public void TheWindTurnsWithTheDays_AndIsTheSameForEveryoneInAWorld()
    {
        ulong seed = 0xDEADBEEFUL;
        long a = WindRules.Bearing(seed, 1), b = WindRules.Bearing(seed, 1);
        Assert.Equal(a, b);
        Assert.InRange(a, 0, 359_999);
        // Over a fortnight it is not one bearing repeated.
        var bearings = Enumerable.Range(0, 14).Select(day => WindRules.Bearing(seed, day)).Distinct().Count();
        Assert.True(bearings > 8, $"the wind has to move over a fortnight ({bearings} bearings in 14 days)");
        // And a different world is a different wind.
        Assert.NotEqual(WindRules.Bearing(seed, 3), WindRules.Bearing(seed + 1, 3));
    }
}