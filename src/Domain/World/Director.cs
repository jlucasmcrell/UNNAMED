// UNNAMED Domain - the world events Director (WLD-89, RK-A9): events a region lives through, capped and resolved abstractly
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.World;

/// <summary>The kinds of thing that happen to a region (WORLD_ARCHITECTURE.md:396, 476 - RK-A9's list).</summary>
public static class WorldEventKinds
{
    public const string Caravan = "caravan";
    public const string Incursion = "incursion";
    public const string Migration = "migration";
    public const string Siege = "siege";

    /// <summary>A festival: tied to the calendar, and Hollow Day is the one the year is built around.</summary>
    public const string Festival = "festival";

    public static readonly ImmutableArray<string> All = ImmutableArray.Create(Caravan, Incursion, Migration, Siege, Festival);
}

/// <summary>
/// One thing happening to a region: what, where, from when for how long, how big, and the sentence a player reads. Nothing here is simulated -
/// the event is resolved as a magnitude and a line, which is what "resolved abstractly" means.
/// </summary>
public sealed record WorldEvent(string Kind, string RegionId, long StartDay, int Days, int Magnitude)
{
    public long EndDay => StartDay + Days;

    public bool Running(long day) => day >= StartDay && day < EndDay;

    /// <summary>How it reads in a digest: the kind, the region, and how long is left.</summary>
    public string Line(long day) =>
        $"{Kind} in {RegionId}: {Magnitude} for {Math.Max(0, EndDay - day)} more day{(EndDay - day == 1 ? "" : "s")}";
}

/// <summary>
/// The Director (WLD-89): each region may be living through a few things at once - a caravan on the road, a migration through the valley, an
/// incursion, a siege, a festival - but no more than eight, and the ones it keeps are the ones that matter. It draws from the day and the region,
/// so the same save gets the same world.
/// </summary>
public static class DirectorRules
{
    /// <summary>The owner's cap (RK-A9): a region carries at most eight events, and a ninth must displace one.</summary>
    public const int CapPerRegion = 8;

    /// <summary>How long each kind runs, in days, at the smallest and largest it can be drawn.</summary>
    public static (int MinDays, int MaxDays) Span(string kind) => kind switch
    {
        WorldEventKinds.Festival => (1, 3),
        WorldEventKinds.Caravan => (2, 6),
        WorldEventKinds.Migration => (3, 10),
        WorldEventKinds.Incursion => (2, 5),
        WorldEventKinds.Siege => (5, 12),
        _ => (1, 1),
    };

    /// <summary>
    /// What a season can bring. The calendar is the reason for the event, not decoration: caravans travel in the open seasons, migrations move with
    /// the grass, incursions come with the cold, and a festival belongs to the Hollow Day.
    /// </summary>
    public static ImmutableArray<string> Possible(string season, bool midwinter)
    {
        var kinds = ImmutableArray.CreateBuilder<string>();
        if (midwinter)
            return ImmutableArray.Create(WorldEventKinds.Festival);   // the Hollow Day is the festival: nothing else is in force on it
        if (season is Seasons.Thaw or Seasons.Highsun)
            kinds.Add(WorldEventKinds.Caravan);
        if (season is Seasons.Thaw or Seasons.Harvest)
            kinds.Add(WorldEventKinds.Migration);
        if (season is Seasons.Frostfall or Seasons.Harvest)
            kinds.Add(WorldEventKinds.Incursion);
        if (season == Seasons.Frostfall)
            kinds.Add(WorldEventKinds.Siege);
        if (season == Seasons.Harvest)
            kinds.Add(WorldEventKinds.Festival);
        return kinds.ToImmutable();
    }

    /// <summary>
    /// The events a region starts on a day: none most days, one on a day the region is lucky, drawn from what the season allows. The sample
    /// function is the world's own (as the adventurers' day uses), so this is deterministic without carrying a random number generator around.
    /// </summary>
    public static ImmutableArray<WorldEvent> Due(string regionId, string season, bool midwinter, long day, Func<string, ulong> sample)
    {
        ulong roll = sample($"{regionId}|{day}|event") % 100;
        if (roll >= (midwinter ? 90UL : 25UL))
            return ImmutableArray<WorldEvent>.Empty;   // most days nothing happens; the Hollow Day almost always has its festival

        var possible = Possible(season, midwinter);
        if (possible.IsEmpty)
            return ImmutableArray<WorldEvent>.Empty;
        var kind = possible[(int)(sample($"{regionId}|{day}|kind") % (ulong)possible.Length)];
        var (min, max) = Span(kind);
        int days = min + (int)(sample($"{regionId}|{day}|days") % (ulong)Math.Max(1, max - min + 1));
        int magnitude = 1 + (int)(sample($"{regionId}|{day}|size") % 5);
        return ImmutableArray.Create(new WorldEvent(kind, regionId, day, days, magnitude));
    }

    /// <summary>
    /// A region's running events after a day's draws: the old ones that have ended are gone, the day's new ones are added, and if the cap is
    /// exceeded the events that have the least time left give way first - a region keeps what still matters to it.
    /// </summary>
    public static ImmutableArray<WorldEvent> Keep(ImmutableArray<WorldEvent> running, ImmutableArray<WorldEvent> drawn, long day)
    {
        var live = running.Where(e => e.Running(day)).Concat(drawn)
            .OrderBy(e => e.StartDay)
            .ThenBy(e => e.Kind, StringComparer.Ordinal)
            .ToList();
        while (live.Count > CapPerRegion)
        {
            // The one with the least time left gives way: a region keeps what still matters to it.
            var shortest = live.OrderBy(e => e.EndDay).ThenBy(e => e.Kind, StringComparer.Ordinal).First();
            live.Remove(shortest);
        }
        return live.ToImmutableArray();
    }
}
