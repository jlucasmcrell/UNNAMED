using System.Collections.Immutable;
using UNNAMED.World.Runtime;

namespace UNNAMED.World.Tests;

/// <summary>
/// WP7: the calendar is a pure function of the tick, with numbered tallies (§2.8 W2); the weather a pure function of (seed, region, game
/// hour) through <see cref="RngChannel"/>, with fronts of 4-12 hours and no two neighbouring fronts alike (W3, D6).
/// </summary>
public class CalendarAndWeatherTests
{
    private const long Day = 57_600;
    private const ulong Seed = 0x5C1A9E7B4D2F0083;
    private const string Region = "region.starting_frontier";

    /// <summary>The game's clock: a 48-minute day at 20 Hz that reads half past seven at tick 0 (config.time).</summary>
    private static readonly WorldClock Clock = new(Day, Day * 75 / 240);

    private static readonly CellKey RegionCell = new(RegionKey.Parse("r_0_0"), 0, 0);

    // ── the calendar ────────────────────────────────────────────────────────

    [Fact]
    public void TheWorldBegins_OnTheFirstDayOfTheFirstTally_OfTheFirstReckoning_AtHalfPastSeven()
    {
        var date = Calendar.Of(0, Clock, CalendarRules.Default);
        Assert.Equal(new CalendarDate(1, 1, 1, 7, 30), date);
        Assert.False(date.IsHollowDay);
    }

    [Fact]
    public void TalliesAreNumbered_FourteenDaysEach_ThenTheHollowDay_ThenTheNextReckoning()
    {
        // The owner's example: "14th day of the 3rd tally, first Reckoning" - the world's 42nd day.
        Assert.Equal((1L, 3, 14), Date(41));
        Assert.Equal((1L, 1, 14), Date(13));
        Assert.Equal((1L, 2, 1), Date(14));
        Assert.Equal((1L, 13, 14), Date(181));
        var hollow = Calendar.Of(DayStart(182), Clock, CalendarRules.Default);
        Assert.True(hollow.IsHollowDay);
        Assert.Equal((1L, 0, 1), (hollow.Reckoning, hollow.Tally, hollow.Day));
        Assert.Equal((2L, 1, 1), Date(183));
        Assert.Equal(183, CalendarRules.Default.DaysPerReckoning);
    }

    [Fact]
    public void TheDate_IsAPureFunctionOfTheTick_AndTheDayTurnsAtMidnight()
    {
        long midnight = DayStart(5);
        Assert.Equal(new CalendarDate(1, 1, 5, 23, 59), Calendar.Of(midnight - 40, Clock, CalendarRules.Default));
        Assert.Equal(new CalendarDate(1, 1, 6, 0, 0), Calendar.Of(midnight, Clock, CalendarRules.Default));
        Assert.Equal(Calendar.Of(midnight + 12_345, Clock, CalendarRules.Default), Calendar.Of(midnight + 12_345, Clock, CalendarRules.Default));
    }

    [Fact]
    public void ACalendarThatCannotCountADay_IsRefused()
    {
        Assert.Throws<FormatException>(() => new CalendarRules(0, 13, 1).Validated());
        Assert.Throws<FormatException>(() => new CalendarRules(14, 0, 1).Validated());
        Assert.Throws<FormatException>(() => new CalendarRules(14, 13, -1).Validated());
        Assert.Equal(new CalendarRules(14, 13, 0), new CalendarRules(14, 13, 0).Validated());
    }

    [Fact]
    public void TheClock_CountsGameHours_AndFindsTheFirstTickOfEach()
    {
        Assert.Equal(7, Clock.Hour(0));
        Assert.Equal(8, Clock.Hour(Clock.TickOfHour(8)));
        Assert.Equal(7, Clock.Hour(Clock.TickOfHour(8) - 1));
        Assert.Equal(24, Clock.Hour(DayStart(1)));
        Assert.Equal(DayStart(1), Clock.TickOfHour(24));
    }

    // ── the weather ─────────────────────────────────────────────────────────

    [Fact]
    public void EveryFront_LastsFourToTwelveHours_TheFrontsTileTime_AndNoTwoNeighboursAreAlike()
    {
        var fronts = Enumerable.Range(0, 400).SelectMany(span => Weather.Fronts(Seed, RegionCell, Region, span, WeatherRules.Default)).ToList();

        Assert.Equal(0, fronts[0].StartHour);
        for (int i = 0; i < fronts.Count; i++)
        {
            Assert.InRange(fronts[i].EndHour - fronts[i].StartHour, 4, 12);
            if (i > 0)
            {
                Assert.Equal(fronts[i - 1].EndHour, fronts[i].StartHour);   // across a span's edge too
                Assert.NotEqual(fronts[i - 1].State, fronts[i].State);
            }
        }
        // Every drawn state comes, fair most often; the reserved fold never does.
        var counts = fronts.GroupBy(f => f.State).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new[] { WeatherState.Fair, WeatherState.Overcast, WeatherState.Rain, WeatherState.Fog, WeatherState.Wind }, counts.Keys.Order());
        Assert.Equal(WeatherState.Fair, counts.MaxBy(c => c.Value).Key);
        Assert.Equal(WeatherState.Fog, counts.MinBy(c => c.Value).Key);
    }

    [Fact]
    public void TheWeatherOfAnHour_IsReadWithoutTheHoursBeforeIt_InAnyOrder()
    {
        var fronts = Enumerable.Range(0, 30).SelectMany(span => Weather.Fronts(Seed, RegionCell, Region, span, WeatherRules.Default)).ToList();
        var hours = Enumerable.Range(0, 30 * 24).Reverse().ToList();
        foreach (int hour in hours)
            Assert.Equal(fronts.Single(f => f.StartHour <= hour && hour < f.EndHour), Weather.At(Seed, RegionCell, Region, hour, WeatherRules.Default));
    }

    [Fact]
    public void AnotherSeed_OrAnotherRegion_HasOtherWeather()
    {
        string Sky(ulong seed, CellKey cell, string region) =>
            string.Join(",", Enumerable.Range(0, 20).SelectMany(s => Weather.Fronts(seed, cell, region, s, WeatherRules.Default)).Select(f => $"{f.State}@{f.StartHour}"));

        string here = Sky(Seed, RegionCell, Region);
        Assert.Equal(here, Sky(Seed, RegionCell, Region));
        Assert.NotEqual(here, Sky(Seed + 1, RegionCell, Region));
        Assert.NotEqual(here, Sky(Seed, new CellKey(RegionKey.Parse("r_1_0"), 0, 0), "region.elsewhere"));
    }

    [Fact]
    public void TheWeather_IsPinned_SoAChangeToItsDrawIsSeen()
    {
        // Pinned from this build. A change here moves every world's weather: it is a deliberate change, made with this table.
        var first = Weather.Fronts(Seed, RegionCell, Region, 0, WeatherRules.Default).Concat(Weather.Fronts(Seed, RegionCell, Region, 1, WeatherRules.Default))
            .Select(f => $"{WeatherStates.Key(f.State)}:{f.StartHour}-{f.EndHour}");
        Assert.Equal(Pinned, string.Join(" ", first));
    }

    private const string Pinned = "overcast:0-12 rain:12-24 fair:24-31 overcast:31-36 fog:36-48";

    [Fact]
    public void WeatherRules_ThatCannotLayOutFronts_OrDrawTheReservedFold_AreRefused()
    {
        var rules = WeatherRules.Default;
        Assert.Same(rules, rules.Validated());
        Assert.Throws<FormatException>(() => (rules with { SpanHours = 12 }).Validated());
        Assert.Throws<FormatException>(() => (rules with { FrontHoursMin = 7 }).Validated());
        Assert.Throws<FormatException>(() => (rules with { Weights = rules.Weights.SetItem(WeatherState.Fold, 1) }).Validated());
        Assert.Throws<FormatException>(() => (rules with { Weights = rules.Weights.SetItem(WeatherState.Rain, -1) }).Validated());
        Assert.Throws<FormatException>(() => (rules with
        {
            Weights = ImmutableSortedDictionary.CreateRange(new Dictionary<WeatherState, int> { [WeatherState.Fair] = 1, [WeatherState.Rain] = 1 }),
        }).Validated());
    }

    [Fact]
    public void TheStateKeys_RoundTrip()
    {
        foreach (var state in Enum.GetValues<WeatherState>())
            Assert.Equal(state, WeatherStates.Parse(WeatherStates.Key(state)));
        Assert.Null(WeatherStates.Parse("storm"));
    }

    private static long DayStart(long day) => day * Day - Clock.StartTicks;

    private static (long Reckoning, int Tally, int Day) Date(long day)
    {
        var date = Calendar.Of(DayStart(day) + 1, Clock, CalendarRules.Default);
        return (date.Reckoning, date.Tally, date.Day);
    }
}
