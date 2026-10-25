// UNNAMED World - the calendar: the world clock's day as a date of the Reckoning (WP7; SYSTEMS_COMPLETENESS_PLAN §2.8 W2)
// No Godot references - pure C#

using System.Collections.Immutable;

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

/// <summary>
/// The four seasons (WLD-33): one cycle per Reckoning on the one calendar. Which tallies each covers is content (config.seasons), because the
/// owner has not yet settled the mapping; the Hollow Day falls in whichever season holds the end of the Reckoning.
/// </summary>
public enum Season
{
    Spring,
    Summer,
    Autumn,
    Winter,
}

public static class Seasons
{
    public static string Key(Season season) => season switch
    {
        Season.Spring => "spring",
        Season.Summer => "summer",
        Season.Autumn => "autumn",
        Season.Winter => "winter",
        _ => throw new ArgumentOutOfRangeException(nameof(season), season, null),
    };

    public static Season? Parse(string key) => key switch
    {
        "spring" => Season.Spring,
        "summer" => Season.Summer,
        "autumn" => Season.Autumn,
        "winter" => Season.Winter,
        _ => null,
    };
}

/// <summary>
/// A stretch of the Reckoning that recurs every Reckoning (WLD-58): a festival on a date (the Hollow Day) or a seasonal window (bloom,
/// harvest, migration). From the first day of <see cref="FromDay"/> to the end of <see cref="ToDay"/>, both days of the Reckoning counted
/// from 0; a window whose end comes before its start runs over the turn of the Reckoning.
/// </summary>
public sealed record CalendarWindow(string Id, string Kind, int FromDay, int ToDay)
{
    public const string Festival = "festival";
    public const string Seasonal = "seasonal";

    public bool Covers(int dayOfReckoning) =>
        FromDay <= ToDay ? dayOfReckoning >= FromDay && dayOfReckoning <= ToDay : dayOfReckoning >= FromDay || dayOfReckoning <= ToDay;
}

/// <summary>
/// The seasons and the dated windows (config.seasons): the day of the Reckoning each season starts on, in order from spring, the windows,
/// and what each season does to the town markets each day - a good's stock moved by a count, as the season's harvests, hunting and
/// hearth-fires change what is made and used (WLD-56).
/// </summary>
public sealed record SeasonRules(
    ImmutableArray<int> StartDays,
    ImmutableArray<CalendarWindow> Windows,
    ImmutableSortedDictionary<Season, ImmutableArray<(string MarketId, string ItemId, long Delta)>> MarketFlows)
{
    /// <summary>
    /// The owner's mapping (approved 2026-10-25): spring from the 1st tally, summer from the 4th, autumn from the 7th, winter from the 10th
    /// through the Hollow Day. Only the Hollow Day is a window: it is the one dated day the canon has.
    /// </summary>
    public static readonly SeasonRules Default = new(ImmutableArray.Create(0, 42, 84, 126),
        ImmutableArray.Create(new CalendarWindow("window.hollow_day", CalendarWindow.Festival, 182, 182)),
        ImmutableSortedDictionary<Season, ImmutableArray<(string, string, long)>>.Empty);

    public Season SeasonOn(int dayOfReckoning)
    {
        for (int s = StartDays.Length - 1; s >= 0; s--)
        {
            if (dayOfReckoning >= StartDays[s])
                return (Season)s;
        }
        return Season.Spring;
    }

    /// <summary>The rules, or why they cannot place every day of a Reckoning of <paramref name="daysPerReckoning"/> in a season.</summary>
    public SeasonRules Validated(int daysPerReckoning)
    {
        if (StartDays.Length != 4 || StartDays[0] != 0)
            throw new FormatException("the seasons are four, spring first, and spring starts the Reckoning");
        for (int s = 1; s < 4; s++)
        {
            if (StartDays[s] <= StartDays[s - 1] || StartDays[s] >= daysPerReckoning)
                throw new FormatException($"the seasons start in order inside the Reckoning's {daysPerReckoning} days");
        }
        foreach (var window in Windows)
        {
            if (window.FromDay < 0 || window.ToDay < 0 || window.FromDay >= daysPerReckoning || window.ToDay >= daysPerReckoning)
                throw new FormatException($"{window.Id} falls outside the Reckoning's {daysPerReckoning} days");
            if (window.Kind is not (CalendarWindow.Festival or CalendarWindow.Seasonal))
                throw new FormatException($"{window.Id} is a '{window.Kind}': a window is a festival or seasonal");
        }
        if (Windows.Select(w => w.Id).Distinct(StringComparer.Ordinal).Count() != Windows.Length)
            throw new FormatException("a window is named twice");
        return this;
    }
}

/// <summary>
/// Where the year stands, for the sky (WLD-34): the Reckoning's seasons laid over the sky's own year, season by season, each centred on
/// its solstice or equinox - spring from early February, summer from early May, autumn from early August, winter from early November - so
/// the sun stands highest mid-summer and lowest mid-winter however long each season runs. Sky3D computes the sun's declination and its
/// moon from this date.
/// </summary>
public readonly record struct SkyDate(int Year, int Month, int Day, double DayOfYear);

public static class SkyCalendar
{
    /// <summary>
    /// Days into the sky's year (0 = 1 January) where each season begins, half-way between its equinox and solstice: 4 February, 6 May,
    /// 7 August, 7 November, then 4 February again.
    /// </summary>
    private static readonly ImmutableArray<int> Anchors = ImmutableArray.Create(34, 125, 218, 310, 399);

    private static readonly ImmutableArray<int> MonthDays = ImmutableArray.Create(31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31);

    /// <summary>The first sky year: any will do, and a year with no leap day keeps every mapped day a real date.</summary>
    public const int FirstYear = 2001;

    /// <summary>Days since the world's first midnight, fractional: what the extra moons' orbits run on.</summary>
    public static double Days(long tick, WorldClock clock) => (tick + clock.StartTicks) / (double)clock.TicksPerDay;

    /// <summary>The sky's date for a tick.</summary>
    public static SkyDate Of(long tick, WorldClock clock, CalendarRules calendar, SeasonRules seasons)
    {
        double days = Days(tick, clock);
        long reckoning = (long)Math.Floor(days / calendar.DaysPerReckoning);
        double into = days - reckoning * calendar.DaysPerReckoning;
        int season = (int)seasons.SeasonOn((int)Math.Floor(into));
        double start = seasons.StartDays[season];
        double end = season == 3 ? calendar.DaysPerReckoning : seasons.StartDays[season + 1];
        double dayOfSkyYear = Anchors[season] + (into - start) / (end - start) * (Anchors[season + 1] - Anchors[season]);
        int year = FirstYear + (int)reckoning + (dayOfSkyYear >= 365 ? 1 : 0);
        dayOfSkyYear %= 365;
        int whole = (int)Math.Floor(dayOfSkyYear), month = 0;
        while (whole >= MonthDays[month])
            whole -= MonthDays[month++];
        return new SkyDate(year, month + 1, whole + 1, dayOfSkyYear);
    }
}

/// <summary>The season turned (WLD-33). Raised at the midnight it turns on, by <see cref="CalendarSystem"/>.</summary>
public sealed record SeasonChanged(Season From, Season To, long Tick);

/// <summary>A festival or seasonal window began (WLD-58): what quests, chatter and the World Event Director key their timing to.</summary>
public sealed record CalendarWindowOpened(string WindowId, string Kind, long Tick);

/// <summary>A festival or seasonal window ended (WLD-58).</summary>
public sealed record CalendarWindowClosed(string WindowId, string Kind, long Tick);

/// <summary>The kinds of <see cref="AdvanceLine"/> the calendar writes.</summary>
public static class CalendarAdvanceKinds
{
    /// <summary>The season at the end of an absence; <see cref="AdvanceLine.Ref"/> is its key.</summary>
    public const string SeasonNow = "calendar.season";

    /// <summary>A window open at the end of an absence; <see cref="AdvanceLine.Ref"/> is its id.</summary>
    public const string WindowOpen = "calendar.window";
}

/// <summary>
/// The calendar in the running world (WLD-33, WLD-58). It holds no state: the season and the open windows are functions of the day, so a
/// load reads them where they stand and announces nothing. Each midnight it raises <see cref="SeasonChanged"/> and the windows that opened
/// or closed; after an absence (closed-form) it raises the difference between the day it last saw and the day it comes back to. What the
/// season does to the markets is <see cref="On"/>, the markets' daily seasonal hook (a pure function of the day).
/// </summary>
internal sealed class CalendarSystem : IAbstractAdvance, ISeasonalGoods
{
    private readonly SystemContext _context;
    private long _day;

    public CalendarSystem(SystemContext context)
    {
        _context = context;
        _day = Clock.Day(context.State.WorldTick);
    }

    private WorldClock Clock => _context.Setup.Clock;
    private CalendarRules Rules => _context.Setup.Calendar;
    private SeasonRules SeasonRules => _context.Setup.Seasons;

    public int DayOfReckoning(long day) => (int)(day % Rules.DaysPerReckoning);

    public Season SeasonOfDay(long day) => SeasonRules.SeasonOn(DayOfReckoning(day));

    public ImmutableArray<CalendarWindow> OpenOn(long day) =>
        SeasonRules.Windows.Where(w => w.Covers(DayOfReckoning(day))).ToImmutableArray();

    public void Tick(long tick)
    {
        long day = Clock.Day(tick);
        if (day != _day)
            Turn(day, tick);
    }

    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
        long day = Clock.Day(toTick);
        log.Add(new AdvanceLine(toTick, CalendarAdvanceKinds.SeasonNow, Seasons.Key(SeasonOfDay(day))));
        foreach (var window in OpenOn(day))
            log.Add(new AdvanceLine(toTick, CalendarAdvanceKinds.WindowOpen, window.Id));
        if (day != _day)
            Turn(day, toTick);
    }

    /// <summary>The goods each season moves in each market on a calendar day (WLD-56): the markets' seasonal hook.</summary>
    public IEnumerable<(string MarketId, string ItemId, long Delta)> On(long day) =>
        SeasonRules.MarketFlows.TryGetValue(SeasonOfDay(day), out var flows) ? flows : Enumerable.Empty<(string, string, long)>();

    private void Turn(long day, long tick)
    {
        Season from = SeasonOfDay(_day), to = SeasonOfDay(day);
        var before = OpenOn(_day);
        var after = OpenOn(day);
        _day = day;
        if (from != to)
            _context.Events.Publish(new SeasonChanged(from, to, tick));
        foreach (var window in before.Where(w => !after.Contains(w)))
            _context.Events.Publish(new CalendarWindowClosed(window.Id, window.Kind, tick));
        foreach (var window in after.Where(w => !before.Contains(w)))
            _context.Events.Publish(new CalendarWindowOpened(window.Id, window.Kind, tick));
    }
}
