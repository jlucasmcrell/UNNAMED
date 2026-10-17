// UNNAMED World - the calendar: the world clock's day as a date of the Reckoning (WP7; SYSTEMS_COMPLETENESS_PLAN §2.8 W2)
// No Godot references - pure C#

namespace UNNAMED.World.Runtime;

/// <summary>
/// How the calendar counts its days (config.time): a Reckoning is <see cref="TalliesPerReckoning"/> tallies of <see cref="DaysPerTally"/>
/// days, then <see cref="HollowDays"/> Hollow Days that belong to no tally. Tallies and Reckonings are numbered, not named (the owner,
/// 2026-10-17: "14th day of the 3rd tally, first Reckoning").
/// </summary>
public sealed record CalendarRules(int DaysPerTally, int TalliesPerReckoning, int HollowDays)
{
    /// <summary>The canon Reckoning: 13 tallies of 14 days and one Hollow Day, 183 days.</summary>
    public static readonly CalendarRules Default = new(14, 13, 1);

    public int DaysPerReckoning => DaysPerTally * TalliesPerReckoning + HollowDays;

    /// <summary>The rules, or why they cannot count a day.</summary>
    public CalendarRules Validated() =>
        DaysPerTally > 0 && TalliesPerReckoning > 0 && HollowDays >= 0
            ? this
            : throw new FormatException("a calendar needs at least one tally of at least one day, and never fewer than no Hollow Days");
}

/// <summary>
/// A date: the Reckoning (from 1), the tally (1 to <see cref="CalendarRules.TalliesPerReckoning"/>, or 0 on a Hollow Day), the day (of the
/// tally, from 1; on a Hollow Day, which Hollow Day, from 1), and the time of day.
/// </summary>
public readonly record struct CalendarDate(long Reckoning, int Tally, int Day, int Hour, int Minute)
{
    /// <summary>A Hollow Day: the days at the end of a Reckoning that belong to no tally.</summary>
    public bool IsHollowDay => Tally == 0;
}

/// <summary>The calendar is a pure function of the world tick: nothing of it is state, and nothing is saved.</summary>
public static class Calendar
{
    /// <summary>The date a tick falls on. The world's first day is the 1st day of the 1st tally of the first Reckoning.</summary>
    public static CalendarDate Of(long tick, WorldClock clock, CalendarRules rules)
    {
        long day = clock.Day(tick);
        long reckoning = day / rules.DaysPerReckoning;
        int ofReckoning = (int)(day % rules.DaysPerReckoning);
        int minute = clock.MinuteOfDay(tick);
        int tallied = rules.DaysPerTally * rules.TalliesPerReckoning;
        return ofReckoning < tallied
            ? new CalendarDate(reckoning + 1, ofReckoning / rules.DaysPerTally + 1, ofReckoning % rules.DaysPerTally + 1, minute / 60, minute % 60)
            : new CalendarDate(reckoning + 1, 0, ofReckoning - tallied + 1, minute / 60, minute % 60);
    }
}
