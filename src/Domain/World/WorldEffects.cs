// UNNAMED Domain - what a season and the Director's events do to the world (WEATHER_SEASONS_SURVIVAL_AND_ENVIRONMENT.md §4-§5;
// QUESTS_DUNGEONS_WORLD_EVENTS_AND_REPOPULATION.md "World Event Director"; WLD-37/56/89)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.World;

/// <summary>What the ground is doing underfoot (WEATHER §3-§4: a trail can be trivial in summer, muddy in spring, nearly impassable in winter).</summary>
public static class Grounds
{
    public const string Dry = "dry";
    public const string Mud = "mud";
    public const string Frost = "frost";
    public const string Snow = "snow";

    public static readonly ImmutableArray<string> All = ImmutableArray.Create(Dry, Mud, Frost, Snow);
}

/// <summary>
/// A region's climate reading (WEATHER §5: "do not make the entire world experience identical seasons simultaneously"). It is a reading, not a
/// weather simulation: the season, the temperature it puts on this region, the ground that leaves, and how long the day is.
/// </summary>
public sealed record ClimateReading(
    string RegionId,
    string Season,
    bool Midwinter,
    int TemperatureC,
    string Ground,
    int DayMinutes)
{
    /// <summary>How it reads in a line: the kind of sentence a player sees.</summary>
    public string Line =>
        $"{RegionId}: {Season}{(Midwinter ? " (the Hollow Day)" : "")}, {TemperatureC}°C, {Ground}, {DayMinutes / 60}h {DayMinutes % 60}m of light";
}

/// <summary>
/// What makes one region's climate its own (WEATHER §5): the temperature and the daylight it adds to the season's reading. A region with no
/// offset reads the season plainly.
/// </summary>
public sealed record ClimateOffset(int TemperatureOffsetC = 0, int DayMinutesOffset = 0);

/// <summary>
/// The numbers a season carries (config.seasons). Every one is a permille so it composes with the economy's own factors, and every one is data:
/// a balance pass changes the content file, never the code.
/// </summary>
public sealed record SeasonRules
{
    /// <summary>The temperature each working season reads at, before a region's own offset (WEATHER §4: temperature is a seasonal effect).</summary>
    public ImmutableSortedDictionary<string, int> TemperatureC { get; init; } = ImmutableSortedDictionary<string, int>.Empty;

    /// <summary>Minutes of daylight each season gives, before a region's own offset.</summary>
    public ImmutableSortedDictionary<string, int> DayMinutes { get; init; } = ImmutableSortedDictionary<string, int>.Empty;

    /// <summary>What the ground is in each season, before an event overrides it.</summary>
    public ImmutableSortedDictionary<string, string> Ground { get; init; } = ImmutableSortedDictionary<string, string>.Empty;

    /// <summary>What a season does to a market's asking price, in permille of the price the market would otherwise ask.</summary>
    public ImmutableSortedDictionary<string, int> MarketPermille { get; init; } = ImmutableSortedDictionary<string, int>.Empty;

    /// <summary>What a season does to a node's yield, in permille (WEATHER §4: "crop/forage availability").</summary>
    public ImmutableSortedDictionary<string, int> ForagePermille { get; init; } = ImmutableSortedDictionary<string, int>.Empty;

    /// <summary>What a season does to walking a road, in permille of the pace the road allows (WEATHER §4: "roads and mud").</summary>
    public ImmutableSortedDictionary<string, int> RoadPermille { get; init; } = ImmutableSortedDictionary<string, int>.Empty;

    /// <summary>What an event does to a market's asking price, in permille, on top of the season.</summary>
    public ImmutableSortedDictionary<string, int> EventMarketPermille { get; init; } =
        ImmutableSortedDictionary<string, int>.Empty;

    /// <summary>What an event does to a node's yield, in permille, on top of the season.</summary>
    public ImmutableSortedDictionary<string, int> EventForagePermille { get; init; } =
        ImmutableSortedDictionary<string, int>.Empty;

    /// <summary>What an event does to a road, in permille, on top of the season; a kind absent from here closes nothing.</summary>
    public ImmutableSortedDictionary<string, int> EventRoadPermille { get; init; } = ImmutableSortedDictionary<string, int>.Empty;

    /// <summary>The event kinds that close a road outright, whatever the season allows.</summary>
    public ImmutableArray<string> ClosingEvents { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>How many hours a season moves the working day at each end (negative closes it early, positive opens it earlier).</summary>
    public ImmutableSortedDictionary<string, int> WorkHourShift { get; init; } = ImmutableSortedDictionary<string, int>.Empty;

    /// <summary>The event kinds that keep a region's people indoors, out of work and off the roads.</summary>
    public ImmutableArray<string> ShelteringEvents { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>The event kind that empties the working day into a festival.</summary>
    public string FestivalEvent { get; init; } = WorldEventKinds.Festival;

    /// <summary>
    /// The shipped numbers. Seasons are systemic, so these are the balance pass (WEATHER §4): forage peaks at the harvest and nearly stops on the
    /// Hollow Day, winter prices run high and caravans bring them down, and a siege or an incursion is what actually closes a road.
    /// </summary>
    public static SeasonRules Default => new()
    {
        TemperatureC = new Dictionary<string, int>
        {
            [Seasons.Thaw] = 8,
            [Seasons.Highsun] = 19,
            [Seasons.Harvest] = 12,
            [Seasons.Frostfall] = -2,
            [Seasons.Intercalary] = -6,
        }.ToImmutableSortedDictionary(StringComparer.Ordinal),
        DayMinutes = new Dictionary<string, int>
        {
            [Seasons.Thaw] = 720,
            [Seasons.Highsun] = 960,
            [Seasons.Harvest] = 720,
            [Seasons.Frostfall] = 480,
            [Seasons.Intercalary] = 420,
        }.ToImmutableSortedDictionary(StringComparer.Ordinal),
        Ground = new Dictionary<string, string>
        {
            [Seasons.Thaw] = Grounds.Mud,
            [Seasons.Highsun] = Grounds.Dry,
            [Seasons.Harvest] = Grounds.Dry,
            [Seasons.Frostfall] = Grounds.Frost,
            [Seasons.Intercalary] = Grounds.Snow,
        }.ToImmutableSortedDictionary(StringComparer.Ordinal),
        MarketPermille = new Dictionary<string, int>
        {
            [Seasons.Thaw] = 1000,
            [Seasons.Highsun] = 950,
            [Seasons.Harvest] = 900,
            [Seasons.Frostfall] = 1150,
            [Seasons.Intercalary] = 1250,
        }.ToImmutableSortedDictionary(StringComparer.Ordinal),
        ForagePermille = new Dictionary<string, int>
        {
            [Seasons.Thaw] = 900,
            [Seasons.Highsun] = 1000,
            [Seasons.Harvest] = 1300,
            [Seasons.Frostfall] = 600,
            [Seasons.Intercalary] = 350,
        }.ToImmutableSortedDictionary(StringComparer.Ordinal),
        RoadPermille = new Dictionary<string, int>
        {
            [Seasons.Thaw] = 850,        // mud
            [Seasons.Highsun] = 1000,
            [Seasons.Harvest] = 1000,
            [Seasons.Frostfall] = 700,   // ice and short days
            [Seasons.Intercalary] = 500,
        }.ToImmutableSortedDictionary(StringComparer.Ordinal),
        EventMarketPermille = new Dictionary<string, int>
        {
            [WorldEventKinds.Caravan] = 880,      // goods on the road: cheaper
            [WorldEventKinds.Migration] = 950,
            [WorldEventKinds.Incursion] = 1250,   // the road is not safe: dearer
            [WorldEventKinds.Siege] = 1500,
            [WorldEventKinds.Festival] = 1050,    // a festival asks a little more, and everyone is buying
        }.ToImmutableSortedDictionary(StringComparer.Ordinal),
        EventForagePermille = new Dictionary<string, int>
        {
            [WorldEventKinds.Caravan] = 1000,
            [WorldEventKinds.Migration] = 900,    // the herds have moved through
            [WorldEventKinds.Incursion] = 800,
            [WorldEventKinds.Siege] = 600,
            [WorldEventKinds.Festival] = 1000,
        }.ToImmutableSortedDictionary(StringComparer.Ordinal),
        EventRoadPermille = new Dictionary<string, int>
        {
            [WorldEventKinds.Caravan] = 950,      // a caravan is traffic on the road
            [WorldEventKinds.Migration] = 900,
            [WorldEventKinds.Incursion] = 600,
            [WorldEventKinds.Siege] = 300,
            [WorldEventKinds.Festival] = 900,     // the road into a festival town is busy
        }.ToImmutableSortedDictionary(StringComparer.Ordinal),
        ClosingEvents = ImmutableArray.Create(WorldEventKinds.Siege),
        WorkHourShift = new Dictionary<string, int>
        {
            [Seasons.Thaw] = 0,
            [Seasons.Highsun] = 1,       // long light, early start
            [Seasons.Harvest] = -1,      // the harvest takes the evenings
            [Seasons.Frostfall] = -2,    // dark by mid-afternoon
            [Seasons.Intercalary] = -3,
        }.ToImmutableSortedDictionary(StringComparer.Ordinal),
        ShelteringEvents = ImmutableArray.Create(WorldEventKinds.Siege, WorldEventKinds.Incursion),
    };

    /// <summary>The rules, or why they cannot describe a year: every season needs a reading, and no permille may be negative.</summary>
    public SeasonRules Validated()
    {
        foreach (string season in Seasons.All)
        {
            if (!TemperatureC.ContainsKey(season) || !DayMinutes.ContainsKey(season) || !Ground.ContainsKey(season))
                throw new FormatException($"the season '{season}' has no temperature, day length or ground");
            if (!MarketPermille.ContainsKey(season) || !ForagePermille.ContainsKey(season) || !RoadPermille.ContainsKey(season))
                throw new FormatException($"the season '{season}' has no market, forage or road reading");
            if (DayMinutes[season] < 1 || DayMinutes[season] > 1440)
                throw new FormatException($"the season '{season}' has {DayMinutes[season]} minutes of daylight");
            if (!Grounds.All.Contains(Ground[season]))
                throw new FormatException($"the season '{season}' calls its ground '{Ground[season]}', which is not ground");
        }
        if (MarketPermille.Values.Concat(ForagePermille.Values).Concat(RoadPermille.Values).Any(v => v < 0))
            throw new FormatException("a season's permille is not negative");
        if (EventMarketPermille.Values.Concat(EventForagePermille.Values).Concat(EventRoadPermille.Values).Any(v => v < 0))
            throw new FormatException("an event's permille is not negative");
        return this;
    }
}

/// <summary>
/// What a season and a region's events do to the four things the world feels them in - prices, roads, forage and the working day - and the climate
/// reading that says so. Everything here is a pure function of the season, the day and the region's running events, so a save reproduces the world.
/// </summary>
public static class WorldEffects
{
    /// <summary>A region's climate: its season's reading, moved by what makes the region itself different (config.seasons' own offsets).</summary>
    public static ClimateReading Climate(SeasonRules rules, string regionId, string season, bool midwinter, int temperatureOffsetC = 0, int dayMinutesOffset = 0)
    {
        string key = rules.TemperatureC.ContainsKey(season) ? season : Seasons.Thaw;
        int temperature = rules.TemperatureC[key] + temperatureOffsetC;
        int dayMinutes = Math.Clamp(rules.DayMinutes[key] + dayMinutesOffset, 1, 1440);
        return new ClimateReading(regionId, season, midwinter, temperature, rules.Ground[key], dayMinutes);
    }

    /// <summary>
    /// What a region's market asks, in permille of the price it would ask with no season and no event: the season's own reading, then every
    /// running event's, compounded in the order the events are given (they are sorted by kind, so the product is stable).
    /// </summary>
    public static int MarketPermille(SeasonRules rules, string season, IEnumerable<WorldEvent> events)
    {
        long permille = rules.MarketPermille.TryGetValue(season, out int bySeason) ? bySeason : 1000;
        foreach (var kind in EventKinds(events))
            if (rules.EventMarketPermille.TryGetValue(kind, out int byEvent))
                permille = permille * byEvent / 1000;
        return (int)Math.Clamp(permille, 1, 100_000);
    }

    /// <summary>What a season and its events leave to forage, in permille of what the node would otherwise give.</summary>
    public static int ForagePermille(SeasonRules rules, string season, IEnumerable<WorldEvent> events)
    {
        long permille = rules.ForagePermille.TryGetValue(season, out int bySeason) ? bySeason : 1000;
        foreach (var kind in EventKinds(events))
            if (rules.EventForagePermille.TryGetValue(kind, out int byEvent))
                permille = permille * byEvent / 1000;
        return (int)Math.Clamp(permille, 1, 100_000);
    }

    /// <summary>Whether a road is shut: an event of a closing kind is running, whatever the season says about the going.</summary>
    public static bool RoadClosed(SeasonRules rules, IEnumerable<WorldEvent> events) =>
        EventKinds(events).Any(rules.ClosingEvents.Contains);

    /// <summary>What the road allows, in permille of its dry-season pace; zero when it is shut.</summary>
    public static int RoadPermille(SeasonRules rules, string season, IEnumerable<WorldEvent> events)
    {
        if (RoadClosed(rules, events))
            return 0;
        long permille = rules.RoadPermille.TryGetValue(season, out int bySeason) ? bySeason : 1000;
        foreach (var kind in EventKinds(events))
            if (rules.EventRoadPermille.TryGetValue(kind, out int byEvent))
                permille = permille * byEvent / 1000;
        return (int)Math.Clamp(permille, 0, 100_000);
    }

    /// <summary>
    /// How a region spends its day: whether an event has everyone indoors, whether it is the festival instead of work, and how many hours the season
    /// opens the working day early (positive) or shuts it early (negative).
    /// </summary>
    public static (bool Shelter, bool Festival, int WorkHourShift) Schedule(SeasonRules rules, string season, IEnumerable<WorldEvent> events)
    {
        var kinds = EventKinds(events);
        bool shelter = kinds.Any(rules.ShelteringEvents.Contains);
        bool festival = kinds.Contains(rules.FestivalEvent);
        int shift = rules.WorkHourShift.TryGetValue(season, out int bySeason) ? bySeason : 0;
        return (shelter, festival, shift);
    }

    /// <summary>The same routine under the season and its events: work hours moved by the season, and shut altogether by a siege or a festival.</summary>
    public static (int FromHour, int ToHour) WorkHours(int fromHour, int toHour, (bool Shelter, bool Festival, int WorkHourShift) schedule)
    {
        if (schedule.Shelter || schedule.Festival)
            return (fromHour, fromHour);        // nobody goes to work: the day is spent sheltering, or at the festival
        int from = Math.Clamp(fromHour - schedule.WorkHourShift, 0, 23);
        int to = Math.Clamp(toHour + schedule.WorkHourShift, 0, 23);
        return (from, to == from ? (from + 1) % 24 : to);
    }

    /// <summary>The event kinds running, sorted, so every product above is stable whatever order the Director kept them in.</summary>
    private static ImmutableArray<string> EventKinds(IEnumerable<WorldEvent> events) =>
        events.Select(e => e.Kind).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToImmutableArray();
}
