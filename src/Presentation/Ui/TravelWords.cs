// UNNAMED Presentation - a journey by the stones in words (S-38)
// No Godot or domain types: the words are tested on their own (Presentation.Tests links this file)

namespace UNNAMED.Presentation.Ui;

/// <summary>A journey by the stones in words: game time from world ticks, as the Waystone panel and the arrival notice say it.</summary>
public static class TravelWords
{
    /// <summary>
    /// A journey in words, before it is taken (NEXT_AREA_TRAVEL_CITY_PLAN.md D.3): "The road would take you about 2 h 50 min. You arrive
    /// about 40 min later than you left." Game time, to the nearest five minutes, and never "0 min".
    /// </summary>
    public static string Journey(long walkTicks, long worldTicks, double ticksPerDay) =>
        $"The road would take you about {GameTime(walkTicks, ticksPerDay)}. You arrive about {GameTime(worldTicks, ticksPerDay)} later than you left.";

    private static string GameTime(long ticks, double ticksPerDay)
    {
        long minutes = Math.Max(5, (long)Math.Round(ticks * 1440.0 / ticksPerDay / 5) * 5);
        return minutes < 60 ? $"{minutes} min" : minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min";
    }
}
