using UNNAMED.Presentation.Greybox;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Tests;

public sealed class SkyCycleTests
{
    [Fact]
    public void HollowDayAndMidnightContinueTheSeasonAndOrbit()
    {
        var clock = WorldClock.Default with { StartTicks = 18_000 };
        var rules = CalendarRules.Default;
        long hollow = 182 * clock.TicksPerDay - clock.StartTicks;
        Assert.True(Calendar.Of(hollow, clock, rules).IsHollowDay);
        var before = SkyCycle.At(hollow + clock.TicksPerDay - 1, clock, rules);
        var after = SkyCycle.At(hollow + clock.TicksPerDay, clock, rules);
        Assert.InRange(after.AstronomyDay - before.AstronomyDay, 0, 0.0001);
        Assert.InRange(before.ReckoningPhase, .999, 1);
        Assert.Equal(0, after.ReckoningPhase);
        Assert.Equal(0, after.Hour);
        Assert.Equal(7.5, SkyCycle.At(0, clock, rules).Hour);
        Assert.Equal(after, SkyCycle.At(hollow + clock.TicksPerDay, clock, rules));
    }

    [Fact]
    public void WeatherBlendIsSmoothBoundedAndIndependentOfRenderedHistory()
    {
        var front = new WeatherView("region.starting_frontier", WeatherState.Rain, 1200, 10800);
        var clock = WorldClock.Default;
        var first = SkyWeatherBlend.At(1200, front, WeatherState.Fair, clock);
        var mid = SkyWeatherBlend.At(1400, front, WeatherState.Fair, clock);
        var end = SkyWeatherBlend.At(1600, front, WeatherState.Fair, clock);
        Assert.Equal(0, first.Amount("rain"));
        Assert.Equal(.5f, mid.Amount("rain"));
        Assert.Equal(1, end.Amount("rain"));
        Assert.Equal(0, end.Amount("fair"));
        Assert.Equal(mid, SkyWeatherBlend.At(1400, front, WeatherState.Fair, clock));
        foreach (long tick in new long[] { 0, 1200, 1201, 1400, 1599, 1600, 100000 })
        {
            var blend = SkyWeatherBlend.At(tick, front, WeatherState.Fair, clock);
            Assert.InRange(blend.Weight, 0, 1);
            Assert.Equal(1, blend.Amount("fair") + blend.Amount("rain"));
            Assert.Equal(0, blend.Amount("fog"));
        }
    }
}
