// UNNAMED Presentation - the date and the sky in words (WP7; SYSTEMS_COMPLETENESS_PLAN §2.8)
// No Godot types: the words are tested on their own (Presentation.Tests links this file)

using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The calendar as the System Menu's strip and the map say it: numbered days, tallies and Reckonings (the owner, 2026-10-17: "14th day of
/// the 3rd tally, first Reckoning"), and the weather now as one word.
/// </summary>
public static class CalendarWords
{
    private static readonly string[] Counted = { "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth" };

    /// <summary>"14th day of the 3rd tally, first Reckoning"; a Hollow Day is "the Hollow Day, first Reckoning" (or "the 2nd Hollow Day").</summary>
    public static string Date(CalendarDate date, CalendarRules rules)
    {
        string reckoning = $"{Reckoning(date.Reckoning)} Reckoning";
        if (date.IsHollowDay)
            return rules.HollowDays > 1 ? $"The {Ordinal(date.Day)} Hollow Day, {reckoning}" : $"The Hollow Day, {reckoning}";
        return $"{Ordinal(date.Day)} day of the {Ordinal(date.Tally)} tally, {reckoning}";
    }

    /// <summary>The date, authoritative clock and weather, shared by the map and menu.</summary>
    public static string Line(CalendarDate date, CalendarRules rules, WeatherState? weather) =>
        $"{Date(date, rules)} · {Time(date)}" + (weather is { } sky ? $" · {Weather(sky)}" : "");

    public static string Time(CalendarDate date) => $"{date.Hour:00}:{date.Minute:00}";

    public static string HudClock(CalendarDate date, WeatherState weather) =>
        $"{(date.Hour is >= 6 and < 19 ? "☀" : "☾")} {Time(date)} · {weather switch
        {
            WeatherState.Rain => "☂", WeatherState.Fog => "≋", WeatherState.Wind => "≈",
            WeatherState.Overcast => "☁", _ => ""
        }} {Weather(weather)}";

    /// <summary>The weather as one word: Fair, Overcast, Rain, Fog, Wind - and the reserved fold, named should a save carry it.</summary>
    public static string Weather(WeatherState state) => state switch
    {
        WeatherState.Fair => "Fair",
        WeatherState.Overcast => "Overcast",
        WeatherState.Rain => "Rain",
        WeatherState.Fog => "Fog",
        WeatherState.Wind => "Wind",
        WeatherState.Fold => "Fold",
        _ => state.ToString(),
    };

    /// <summary>The weather by its key, as the away log carries it; an unknown key is read as words.</summary>
    public static string Weather(string key) => WeatherStates.Parse(key) is { } state ? Weather(state) : Names.Words(key);

    /// <summary>A Reckoning in words up to the tenth, as the owner's example says it ("first Reckoning"); then "11th".</summary>
    public static string Reckoning(long reckoning) => reckoning >= 1 && reckoning <= Counted.Length ? Counted[reckoning - 1] : Ordinal(reckoning);

    /// <summary>1st, 2nd, 3rd, 4th ... 11th, 12th, 13th ... 21st.</summary>
    public static string Ordinal(long n)
    {
        long tens = n % 100, ones = n % 10;
        string suffix = tens is >= 11 and <= 13 ? "th" : ones switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{n}{suffix}";
    }
}
