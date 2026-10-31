// UNNAMED Domain tests - the world events Director (WLD-89, RK-A9)
using System.Collections.Immutable;
using UNNAMED.Domain.World;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// The Director's rules: the season decides what can happen, a region carries at most eight events, the least time left gives way first, and the
/// same region and day always draw the same event. Hollow Day is the festival, and nothing else is in force on it.
/// </summary>
public class DirectorTests
{
    // A sample function of the same shape the adventurers' day uses: a pure function of the key.
    private static ulong Sample(string key)
    {
        ulong h = 1469598103934665603UL;
        foreach (char c in key)
            h = (h ^ c) * 1099511628211UL;
        return h;
    }

    [Fact]
    public void TheSeasonDecidesWhatCanHappen()
    {
        var thaw = DirectorRules.Possible(Seasons.Thaw, midwinter: false);
        Assert.Contains(WorldEventKinds.Caravan, thaw);
        Assert.Contains(WorldEventKinds.Migration, thaw);
        Assert.DoesNotContain(WorldEventKinds.Siege, thaw);

        var frostfall = DirectorRules.Possible(Seasons.Frostfall, midwinter: false);
        Assert.Contains(WorldEventKinds.Incursion, frostfall);
        Assert.Contains(WorldEventKinds.Siege, frostfall);
        Assert.DoesNotContain(WorldEventKinds.Caravan, frostfall);

        // The Hollow Day is the festival: nothing else happens in a region that day.
        Assert.Equal(ImmutableArray.Create(WorldEventKinds.Festival), DirectorRules.Possible(Seasons.Intercalary, midwinter: true));
    }

    [Fact]
    public void ARegionDrawsNothingOnMostDays_AndTheSameDayAlwaysDrawsTheSame()
    {
        var drawn = DirectorRules.Due("region.ashen_hollow", Seasons.Thaw, midwinter: false, day: 40, Sample);
        var again = DirectorRules.Due("region.ashen_hollow", Seasons.Thaw, midwinter: false, day: 40, Sample);
        Assert.Equal(drawn, again);

        int busy = 0;
        for (long day = 0; day < 60; day++)
            busy += DirectorRules.Due("region.ashen_hollow", Seasons.Thaw, midwinter: false, day, Sample).Length;
        Assert.InRange(busy, 1, 40);   // it happens, but it is not a firework every midnight
    }

    [Fact]
    public void AHollowDayFestivalIsDrawnOnTheDayItself_AndCannotBeAStorm()
    {
        var festival = DirectorRules.Due("region.ashen_hollow", Seasons.Intercalary, midwinter: true, day: 183, Sample)
            .Where(e => e.Kind == WorldEventKinds.Festival);
        Assert.All(festival, e => Assert.InRange(e.Days, 1, 3));

        // Whatever a midwinter day draws, it is never an incursion or a siege.
        for (long day = 0; day < 50; day++)
            foreach (var e in DirectorRules.Due("region.ashen_hollow", Seasons.Intercalary, midwinter: true, day, Sample))
                Assert.Equal(WorldEventKinds.Festival, e.Kind);
    }

    [Fact]
    public void ARegionCarriesAtMostEightEvents_AndTheLeastLikelyToLastGivesWay()
    {
        var running = Enumerable.Range(0, 8)
            .Select(i => new WorldEvent(WorldEventKinds.Caravan, "region.a", StartDay: 10, Days: 5 + i, Magnitude: 1))
            .ToImmutableArray();
        var drawn = ImmutableArray.Create(new WorldEvent(WorldEventKinds.Incursion, "region.a", StartDay: 12, Days: 30, Magnitude: 4));

        var kept = DirectorRules.Keep(running, drawn, day: 12);
        Assert.Equal(DirectorRules.CapPerRegion, kept.Length);
        Assert.Contains(kept, e => e.Kind == WorldEventKinds.Incursion);   // the new one still matters, so it stays
        Assert.DoesNotContain(kept, e => e.Days == 5);                     // the one with the least time left gave way
    }

    [Fact]
    public void EventsThatHaveEnded_AreGone_AndALineSaysWhatIsLeft()
    {
        var ended = new WorldEvent(WorldEventKinds.Migration, "region.a", StartDay: 1, Days: 2, Magnitude: 2);
        Assert.False(ended.Running(day: 3));
        Assert.Empty(DirectorRules.Keep(ImmutableArray.Create(ended), ImmutableArray<WorldEvent>.Empty, day: 3));

        var running = new WorldEvent(WorldEventKinds.Siege, "region.cairnwold", StartDay: 5, Days: 6, Magnitude: 3);
        Assert.True(running.Running(day: 5));
        Assert.True(running.Running(day: 10));
        Assert.False(running.Running(day: 11));
        Assert.Equal("siege in region.cairnwold: 3 for 1 more day", running.Line(day: 10));
    }

    [Fact]
    public void EveryKindIsDrawableAndNamed()
    {
        Assert.Equal(5, WorldEventKinds.All.Length);
        foreach (string kind in WorldEventKinds.All)
        {
            var (min, max) = DirectorRules.Span(kind);
            Assert.True(min >= 1 && max >= min, kind);
        }
    }
}
