// UNNAMED Domain - seasons over the calendar (WLD-33/58; owner ruling: Hollow Day is the midwinter intercalary day)
// No Godot references - pure C#

namespace UNNAMED.Domain.World;

/// <summary>
/// The year's seasons as keys, not as art (WEATHER_SEASONS_SURVIVAL_AND_ENVIRONMENT.md §4: seasons are systemic, not texture swaps). A temperate
/// Reckoning runs Thaw, Highsun, Harvest, Frostfall, and then the Hollow Day, which belongs to none of them.
/// </summary>
public static class Seasons
{
    public const string Thaw = "thaw";
    public const string Highsun = "highsun";
    public const string Harvest = "harvest";
    public const string Frostfall = "frostfall";

    /// <summary>
    /// The Hollow Day: the day at a Reckoning's end that belongs to no tally - the midwinter intercalary day, by the owner's ruling of 2026-10-31.
    /// It is a season of its own here because nothing seasonal is in force on it.
    /// </summary>
    public const string Intercalary = "intercalary";

    public static readonly System.Collections.Immutable.ImmutableArray<string> All =
        System.Collections.Immutable.ImmutableArray.Create(Thaw, Highsun, Harvest, Frostfall, Intercalary);

    /// <summary>
    /// Where in the year a tally falls. Tallies are numbered from 1; the last third of them is Frostfall, so the Hollow Day closes the year out of
    /// winter. A tally outside the year is clamped rather than thrown: a season is a reading, not a contract.
    /// </summary>
    public static string OfTally(int tally, int talliesPerReckoning)
    {
        if (tally == 0)
            return Intercalary;                       // a Hollow Day is tally 0, and only tally 0
        int tallies = Math.Max(1, talliesPerReckoning);
        int t = Math.Min(Math.Max(1, tally), tallies);
        // Four blocks of about equal length, the remainder belonging to Frostfall: the year ends in winter, and the Hollow Day closes it.
        int block = Math.Max(1, tallies / 4);
        if (t <= block)
            return Thaw;
        if (t <= block * 2)
            return Highsun;
        if (t <= block * 3)
            return Harvest;
        return Frostfall;
    }

    /// <summary>The season of a date, from the facts the calendar already knows: the tally, how many there are, and whether it is a Hollow Day.</summary>
    public static string Of(int tally, int talliesPerReckoning, bool hollowDay) =>
        hollowDay || tally == 0 ? Intercalary : OfTally(tally, talliesPerReckoning);

    /// <summary>
    /// Whether the world is at midwinter: the Hollow Day, and nowhere else. Nothing else in the year is allowed to call itself midwinter, which is
    /// what makes the intercalary day mean something.
    /// </summary>
    public static bool IsMidwinter(int tally, bool hollowDay) => hollowDay || tally == 0;

    /// <summary>Whether a season is one of the four working seasons (rather than the day that belongs to none).</summary>
    public static bool IsWorkingSeason(string season) => season is Thaw or Highsun or Harvest or Frostfall;
}
