// UNNAMED World - the festivals: the calendar's dates turned into days the living world keeps (WLD-58, WLD-89; WEATHER
// SEASONS_AND_ENVIRONMENT §4, §29, SYSTEMS_CHECKLIST R13)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.World.Runtime;

/// <summary>Which dates a festival keeps: the Hollow Day, one tally's day, a day of every tally, or a window of tallies.</summary>
public enum FestivalDates
{
    /// <summary>The Hollow Day that closes a Reckoning: the calendar's one day that belongs to no tally.</summary>
    HollowDay,

    /// <summary>One day of one tally, named exactly (the 12th day of the 5th tally).</summary>
    OneDay,

    /// <summary>The same day of every tally: a fortnightly market, a tally's turning.</summary>
    EveryTally,

    /// <summary>A window of whole tallies: the seasonal content windows - bloom, harvest, a migration, an opening gate (WLD-58).</summary>
    TallyWindow,
}

/// <summary>
/// When a festival keeps. Exactly one form is used, and the calendar checks it against the pack's own tallies: the numbers are the
/// <see cref="CalendarRules"/>' numbers, not the canon's, because a pack may count differently.
/// </summary>
public sealed record FestivalWhen(FestivalDates Kind, int Tally = 0, int Day = 0, int TallyFrom = 0, int TallyTo = 0)
{
    /// <summary>The Hollow Day: the canon's big festival, the day a city renews its oaths (the Sworn Counties' Hollow Day).</summary>
    public static readonly FestivalWhen Hollow = new(FestivalDates.HollowDay);

    /// <summary>One named day: <c>the 12th day of the 5th tally</c>.</summary>
    public static FestivalWhen On(int tally, int day) => new(FestivalDates.OneDay, tally, day);

    /// <summary>That day of every tally.</summary>
    public static FestivalWhen EveryTallyDay(int day) => new(FestivalDates.EveryTally, Day: day);

    /// <summary>A window of tallies, both ends included.</summary>
    public static FestivalWhen Tallies(int from, int to) => new(FestivalDates.TallyWindow, TallyFrom: from, TallyTo: to);

    /// <summary>Whether the date falls on this festival's day. A Hollow Day is never a tally's day, and a window never holds one.</summary>
    public bool Matches(CalendarDate date) => Kind switch
    {
        FestivalDates.HollowDay => date.IsHollowDay,
        FestivalDates.OneDay => !date.IsHollowDay && date.Tally == Tally && date.Day == Day,
        FestivalDates.EveryTally => !date.IsHollowDay && date.Day == Day,
        FestivalDates.TallyWindow => !date.IsHollowDay && date.Tally >= TallyFrom && date.Tally <= TallyTo,
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null),
    };

    /// <summary>Whether every day the festival keeps can exist in a calendar that counts <paramref name="calendar"/>'s days.</summary>
    public bool ExistsIn(CalendarRules calendar) => Kind switch
    {
        FestivalDates.HollowDay => calendar.HollowDays > 0,
        FestivalDates.OneDay => Tally >= 1 && Tally <= calendar.TalliesPerReckoning && Day >= 1 && Day <= calendar.DaysPerTally,
        FestivalDates.EveryTally => Day >= 1 && Day <= calendar.DaysPerTally,
        FestivalDates.TallyWindow => TallyFrom >= 1 && TallyTo <= calendar.TalliesPerReckoning && TallyFrom <= TallyTo,
        _ => false,
    };

    /// <summary>The words a citizen would use for when it is kept.</summary>
    public string Describe() => Kind switch
    {
        FestivalDates.HollowDay => "the Hollow Day",
        FestivalDates.OneDay => $"day {Day} of tally {Tally}",
        FestivalDates.EveryTally => $"day {Day} of every tally",
        FestivalDates.TallyWindow => TallyFrom == TallyTo ? $"tally {TallyFrom}" : $"tallies {TallyFrom}-{TallyTo}",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null),
    };
}

/// <summary>Where a festival gathers, in the region's own coordinates: a point and the way the crowd faces.</summary>
public readonly record struct FestivalSite(long XMm, long ZMm, int FacingMdeg);

/// <summary>
/// One festival of content: what it is called, where and when it keeps, and whose people keep it. <see cref="Settlements"/> empty
/// means every settlement of the region; <see cref="People"/> empty means every people. <see cref="Gathers"/> is whether the town
/// comes to the site instead of to its work while the hours are on.
/// </summary>
public sealed record FestivalDefinition(
    string Id,
    string Name,
    string RegionId,
    FestivalWhen When,
    int FromHour,
    int ToHour,
    FestivalSite Site,
    ImmutableArray<string> Settlements,
    ImmutableArray<string> People,
    bool Gathers)
{
    /// <summary>The region and the festival's own fields, or why the festival cannot be kept.</summary>
    public FestivalDefinition Validated(CalendarRules calendar)
    {
        if (string.IsNullOrWhiteSpace(Id) || !Id.StartsWith("festival.", StringComparison.Ordinal))
            throw new FormatException("a festival's id starts with 'festival.'");
        if (string.IsNullOrWhiteSpace(Name))
            throw new FormatException($"{Id} needs a name: it is what the world says when the day comes");
        if (string.IsNullOrWhiteSpace(RegionId))
            throw new FormatException($"{Id} keeps in a region, and names none");
        if (!When.ExistsIn(calendar))
            throw new FormatException($"{Id} keeps on {When.Describe()}, which this calendar has no day for (13 tallies of 14 days and a Hollow Day unless the pack says otherwise)");
        if (FromHour is < 0 or > 23 || ToHour is < 1 or > 24 || FromHour == ToHour)
            throw new FormatException($"{Id} gathers from hour {FromHour} to {ToHour}: hours run 0 to 23, the end after the beginning and before 24");
        if (Site.FacingMdeg is < 0 or >= 360)
            throw new FormatException($"{Id} gathers facing {Site.FacingMdeg} degrees, which is not a facing");
        return this;
    }

    /// <summary>Whether the festival is on at this hour of a matching day: its band may cross midnight.</summary>
    public bool KeepAt(int hourOfDay) => FromHour <= ToHour ? hourOfDay >= FromHour && hourOfDay < ToHour : hourOfDay >= FromHour || hourOfDay < ToHour;

    /// <summary>Whether these people keep this festival: no settlement named keeps it everywhere, and so for people.</summary>
    public bool KeptBy(string settlement, string? peopleId) =>
        (Settlements.IsDefaultOrEmpty || Settlements.Contains(settlement, StringComparer.Ordinal))
        && (People.IsDefaultOrEmpty || (peopleId is not null && People.Contains(peopleId, StringComparer.Ordinal)));
}

/// <summary>
/// Every festival a pack declares, and how many the world keeps at once in a region. WLD-89 caps world events at eight per region,
/// festivals among them, so a pack that authors a pile of them on one day still keeps eight.
/// </summary>
public sealed record FestivalRules(ImmutableArray<FestivalDefinition> All, int MaxConcurrent)
{
    /// <summary>No festivals: a pack that declares none keeps no days, and the living world runs as it did.</summary>
    public static readonly FestivalRules None = new(ImmutableArray<FestivalDefinition>.Empty, 8);

    /// <summary>The canon cap (WLD-89): eight world events of any kind in a region at once.</summary>
    public const int CanonMaxConcurrent = 8;

    /// <summary>The rules, or why they cannot be kept: unique ids, and every festival valid in this calendar.</summary>
    public FestivalRules Validated(CalendarRules calendar)
    {
        if (MaxConcurrent < 1 || MaxConcurrent > CanonMaxConcurrent)
            throw new FormatException($"at most {CanonMaxConcurrent} world events are kept in a region at once (WLD-89): {MaxConcurrent} is not a cap");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var festival in All)
        {
            festival.Validated(calendar);
            if (!seen.Add(festival.Id))
                throw new FormatException($"two festivals share the id {festival.Id}");
        }
        return this;
    }

    // The living world asks this for every NPC every tick, so a region's festivals are worked out once.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImmutableArray<FestivalDefinition>> _byRegion =
        new(StringComparer.Ordinal);

    /// <summary>The festivals authored for one region, in the order they were written.</summary>
    public ImmutableArray<FestivalDefinition> Of(string regionId) =>
        _byRegion.GetOrAdd(regionId, id => All.Where(f => string.Equals(f.RegionId, id, StringComparison.Ordinal)).ToImmutableArray());
}

/// <summary>
/// A festival as presentation and the living world read it: its name and site, whether the town is gathered at it now, and the day.
/// </summary>
public sealed record FestivalView(
    string Id,
    string Name,
    string RegionId,
    long SiteXMm,
    long SiteZMm,
    int SiteFacingMdeg,
    bool Gathering,
    int FromHour,
    int ToHour,
    long Day)
{
    /// <summary>Where the crowd stands, as a body's point.</summary>
    public FestivalSite Site => new(SiteXMm, SiteZMm, SiteFacingMdeg);
}

/// <summary>A festival's day began (or the world was loaded on it): what it is and when. Raised by <see cref="FestivalSystem"/>.</summary>
public sealed record FestivalBegan(FestivalView Festival, long Tick);

/// <summary>A festival's day ended. Raised by <see cref="FestivalSystem"/>.</summary>
public sealed record FestivalEnded(string FestivalId, long Tick);

/// <summary>The calendar's days turned into what the living world keeps: which festivals are on, and what they gather (WLD-58).</summary>
public static class Festivals
{
    /// <summary>The festivals kept on a date, in authored order, capped at <see cref="FestivalRules.MaxConcurrent"/> (WLD-89).</summary>
    public static ImmutableArray<FestivalDefinition> Observed(FestivalRules rules, string regionId, CalendarDate date) =>
        rules.Of(regionId).Where(f => f.When.Matches(date)).Take(rules.MaxConcurrent).ToImmutableArray();

    /// <summary>
    /// The festival a town is gathered at this hour, if any: the first kept festival whose people these are, whose day it is and
    /// whose hours are on. Null is the ordinary day.
    /// </summary>
    public static FestivalView? Gathering(FestivalRules rules, string regionId, CalendarDate date, string settlement, string? peopleId)
    {
        foreach (var festival in Observed(rules, regionId, date))
        {
            if (festival.Gathers && festival.KeepAt(date.Hour) && festival.KeptBy(settlement, peopleId))
                return View(festival, gathered: true, date);
        }
        return null;
    }

    /// <summary>The view of a festival on a date: the town gathers or it does not.</summary>
    public static FestivalView View(FestivalDefinition festival, bool gathered, CalendarDate date) =>
        new(festival.Id, festival.Name, festival.RegionId, festival.Site.XMm, festival.Site.ZMm, festival.Site.FacingMdeg, gathered,
            festival.FromHour, festival.ToHour, date.Reckoning * 1000 + date.Tally * 100 + date.Day);
}

/// <summary>
/// Owns no state: the calendar's days are a pure function of the tick, so which festivals are on is derived, and an absence of any
/// length is caught up at once. What it holds is only what it has already announced, so that a day's beginning is raised once.
/// </summary>
internal sealed class FestivalSystem : IAbstractAdvance
{
    private readonly SystemContext _context;
    private long _announcedDay = long.MinValue;
    private ImmutableArray<string> _on = ImmutableArray<string>.Empty;

    public FestivalSystem(SystemContext context)
    {
        _context = context;
    }

    private FestivalRules Rules => _context.Setup.Festivals;
    private WorldClock Clock => _context.Setup.Clock;
    private CalendarRules Calendar => _context.Setup.Calendar;
    private string RegionId => _context.Setup.Layout.Id;

    /// <summary>The festivals kept on the day the tick falls in, in authored order and capped (WLD-89).</summary>
    public ImmutableArray<FestivalDefinition> On(long tick) =>
        Festivals.Observed(Rules, RegionId, Runtime.Calendar.Of(tick, Clock, Calendar));

    /// <summary>Every festival of the day, in authored order - a window and a named day can fall on the same day.</summary>
    public ImmutableArray<FestivalView> Views(long tick)
    {
        var date = Runtime.Calendar.Of(tick, Clock, Calendar);
        return Festivals.Observed(Rules, RegionId, date)
            .Select(festival => Festivals.View(festival, festival.Gathers && festival.KeepAt(date.Hour), date))
            .ToImmutableArray();
    }

    /// <summary>The first festival on now, with whether its people are gathered at it: what a HUD line or a talk would say.</summary>
    public FestivalView? View(long tick)
    {
        var views = Views(tick);
        return views.IsDefaultOrEmpty ? null : views[0];
    }

    /// <summary>The festival a settlement of this region gathers at this hour, if any - what <see cref="NpcMover"/> reads.</summary>
    public FestivalView? GatheringAt(long tick, string settlement, string? peopleId) =>
        Festivals.Gathering(Rules, RegionId, Runtime.Calendar.Of(tick, Clock, Calendar), settlement, peopleId);

    /// <summary>Each new calendar day, raise what began and what ended; a pack with no festivals raises nothing.</summary>
    public void Tick(long tick)
    {
        if (Rules.All.IsDefaultOrEmpty)
            return;
        long day = Clock.Day(tick);
        if (day == _announcedDay)
            return;
        Announce(day, tick);
    }

    private void Announce(long day, long tick)
    {
        _announcedDay = day;
        var date = Runtime.Calendar.Of(tick, Clock, Calendar);
        var now = Ids(Festivals.Observed(Rules, RegionId, date));
        foreach (var ended in _on.Where(id => !now.Contains(id, StringComparer.Ordinal)))
            _context.Events.Publish(new FestivalEnded(ended, tick));
        foreach (var festival in Festivals.Observed(Rules, RegionId, date))
        {
            if (!_on.Contains(festival.Id, StringComparer.Ordinal))
                _context.Events.Publish(new FestivalBegan(Festivals.View(festival, festival.Gathers && festival.KeepAt(date.Hour), date), tick));
        }
        _on = now;
    }

    private static ImmutableArray<string> Ids(ImmutableArray<FestivalDefinition> festivals) =>
        festivals.Select(f => f.Id).ToImmutableArray();

    /// <summary>
    /// The world going on while the character was away: the festivals kept at the end are named, and every day that began in the
    /// absence is counted per festival - a pure walk of the days, so one long advance and many short ones agree (T-22).
    /// </summary>
    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
        if (Rules.All.IsDefaultOrEmpty || toTick <= fromTick)
            return;
        long fromDay = Clock.Day(fromTick), toDay = Clock.Day(toTick);
        var beganOf = new Dictionary<string, int>(StringComparer.Ordinal);
        // The midnights strictly after the absence begins, and at or before it ends: a day on the boundary is counted once,
        // however the absence is cut into pieces (T-22).
        for (long day = fromDay + 1; day <= toDay; day++)
        {
            var date = Runtime.Calendar.OfDay(day, 0, Calendar);
            foreach (var festival in Festivals.Observed(Rules, RegionId, date))
                beganOf[festival.Id] = beganOf.GetValueOrDefault(festival.Id) + 1;
        }
        foreach (var (id, days) in beganOf.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            log.Add(new AdvanceLine(toTick, AdvanceKinds.FestivalPassed, id, days));
        var now = View(toTick);
        log.Add(new AdvanceLine(toTick, AdvanceKinds.FestivalNow, now?.Id ?? ""));
        _announcedDay = toDay;
        _on = Ids(On(toTick));
    }
}
