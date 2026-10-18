using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Greybox;

/// <summary>Presentation coordinates from WP7 time. No clock, saved state or random draws of its own.</summary>
public readonly record struct SkyCycle(double Day, double Hour, double ReckoningPhase, double AstronomyDay)
{
    public static SkyCycle At(long tick, WorldClock clock, CalendarRules calendar)
    {
        double day = (double)tick / clock.TicksPerDay + (double)clock.StartTicks / clock.TicksPerDay;
        double fraction = day - Math.Floor(day);
        // Sky3D's existing 2025-01-01 epoch is day 9133 in its orbital calculation.
        // A whole WP7 Reckoning, including Hollow Day, traverses a seasonal cycle.
        // This maps rendering coordinates; the player's date stays the WP7 date.
        return new SkyCycle(day, fraction * 24,
            (day % calendar.DaysPerReckoning) / calendar.DaysPerReckoning,
            9133 + day * 365.2425 / calendar.DaysPerReckoning);
    }
}

public readonly record struct SkyWeatherBlend(string From, string To, float Weight)
{
    public static SkyWeatherBlend At(long tick, WeatherView front, WeatherState previous, WorldClock clock)
    {
        // Ten game minutes (20 real seconds with the shipped clock), always shorter than a front.
        double duration = Math.Max(1, clock.TicksPerDay / (clock.HoursPerDay * 6.0));
        double t = Math.Clamp((tick - (double)front.FrontStartTick) / duration, 0, 1);
        return new SkyWeatherBlend(UNNAMED.World.Runtime.WeatherStates.Key(previous),
            UNNAMED.World.Runtime.WeatherStates.Key(front.State), (float)(t * t * (3 - 2 * t)));
    }

    public float Amount(string state) => From == To ? (To == state ? 1 : 0)
        : (From == state ? 1 - Weight : 0) + (To == state ? Weight : 0);
}
