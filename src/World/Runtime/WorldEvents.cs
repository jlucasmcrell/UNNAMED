// UNNAMED World - seasons read off the calendar and the events Director that gives them teeth (WLD-33/58/89)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.World;

namespace UNNAMED.World.Runtime;

/// <summary>A region's season changed (WLD-33): what it was, what it is now, and the day it turned.</summary>
public sealed record SeasonTurned(string Season, bool Midwinter, long Day, long Tick);

/// <summary>A world event began in a region (WLD-89): the kind, the region, how long, and how big.</summary>
public sealed record WorldEventBegan(WorldEvent Event, long Tick);

/// <summary>
/// The world events Director (WLD-89) and the season it works in (WLD-33/58). Each region carries a few things at once - a caravan, a migration, an
/// incursion, a siege, a festival - never more than the cap, drawn from the day and the region so a save reproduces the world. The season comes
/// off the calendar, and the Hollow Day is the midwinter intercalary day: a festival, and nothing else in force.
/// </summary>
internal sealed class WorldEventsSystem : IAbstractAdvance
{
    private readonly SystemContext _context;
    private readonly Dictionary<string, ImmutableArray<WorldEvent>> _byRegion = new(StringComparer.Ordinal);

    public WorldEventsSystem(SystemContext context)
    {
        _context = context;
    }

    private RuntimeState State => _context.State;

    private CalendarRules Rules => _context.Setup.Calendar;

    /// <summary>The season a tick falls in, and whether it is the Hollow Day.</summary>
    public (string Season, bool Midwinter) SeasonOf(long tick)
    {
        var date = Calendar.Of(tick, _context.Setup.Clock, Rules);
        return (Seasons.Of(date.Tally, Rules.TalliesPerReckoning, date.IsHollowDay), date.IsHollowDay);
    }

    /// <summary>The events running in one region now, in the order they began.</summary>
    public ImmutableArray<WorldEvent> Running(string regionId, long tick) =>
        _byRegion.TryGetValue(regionId, out var events) ? events.Where(e => e.Running(_context.Setup.Clock.Day(tick))).ToImmutableArray()
            : ImmutableArray<WorldEvent>.Empty;

    /// <summary>Every region the world knows how to talk about: the settlements' own, which is where a player can walk.</summary>
    private ImmutableArray<string> Regions =>
        _context.Setup.Adventurers is null
            ? ImmutableArray<string>.Empty
            : _context.Setup.Adventurers.Settlements.Keys.ToImmutableArray();

    /// <summary>
    /// One midnight at a time: the old events are dropped as they end, the day's are drawn from what its season allows, and a region never carries
    /// more than the cap. The season turning is announced where it happens.
    /// </summary>
    public void Advance(long fromTick, long toTick, AdvanceLog log)
    {
        var clock = _context.Setup.Clock;
        long from = clock.Day(fromTick), to = clock.Day(toTick);
        if (to <= from)
            return;

        var (before, beforeMidwinter) = SeasonOf(fromTick);
        for (long day = from; day <= to; day++)
        {
            long midnight = day * clock.TicksPerDay - clock.StartTicks;
            if (midnight <= fromTick || midnight > toTick)
                continue;
            var (season, midwinter) = SeasonOf(midnight);
            if (season != before || midwinter != beforeMidwinter)
            {
                _context.Events.Publish(new SeasonTurned(season, midwinter, day, midnight));
                log.Add(new AdvanceLine(midnight, WorldAdvanceKinds.SeasonTurned, season) { SubjectId = season });
                (before, beforeMidwinter) = (season, midwinter);
            }

            foreach (string region in Regions)
            {
                var running = _byRegion.TryGetValue(region, out var had) ? had : ImmutableArray<WorldEvent>.Empty;
                var drawn = DirectorRules.Due(region, season, midwinter, day, Sample(day));
                var kept = DirectorRules.Keep(running, drawn, day);
                _byRegion[region] = kept;
                foreach (var begun in drawn.Where(d => kept.Contains(d)))
                {
                    _context.Events.Publish(new WorldEventBegan(begun, midnight));
                    log.Add(new AdvanceLine(midnight, WorldAdvanceKinds.WorldEvent, begun.Kind) { SubjectId = region, Amount = begun.Magnitude });
                }
            }
        }
    }

    /// <summary>The world's own draw: a pure function of the day and the key, exactly as the adventurers' midnight samples.</summary>
    private static Func<string, ulong> Sample(long day)
    {
        ulong salt = (ulong)day * 1099511628211UL;
        return key =>
        {
            ulong h = 1469598103934665603UL ^ salt;
            foreach (char c in key)
                h = (h ^ c) * 1099511628211UL;
            return h;
        };
    }

    /// <summary>Read-only state's own day, for a caller that has a tick and no day.</summary>
    public long Day(long tick) => _context.Setup.Clock.Day(tick);

    /// <summary>The region this world runs in, named as the Director and the climate reader name regions.</summary>
    public string? RegionIdOf(SimulationSetup setup) => setup.Layout.Id;
}

/// <summary>The kinds of advance line the world's clock and Director write.</summary>
public static class WorldAdvanceKinds
{
    public const string SeasonTurned = "world.season";
    public const string WorldEvent = "world.event";
}
