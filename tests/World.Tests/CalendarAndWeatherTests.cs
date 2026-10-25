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

    // ── seasons, the sky's year and the dated windows (WP 1D: WLD-33, 34, 58) ─────

    [Fact]
    public void TheSeasons_RunOnceAReckoning_FromSpring_AndTheHollowDayIsWinters()
    {
        var seasons = SeasonRules.Default;
        Assert.Equal(Season.Spring, seasons.SeasonOn(0));
        Assert.Equal(Season.Spring, seasons.SeasonOn(41));
        Assert.Equal(Season.Summer, seasons.SeasonOn(42));
        Assert.Equal(Season.Autumn, seasons.SeasonOn(84));
        Assert.Equal(Season.Winter, seasons.SeasonOn(126));
        Assert.Equal(Season.Winter, seasons.SeasonOn(182));   // the Hollow Day
        Assert.Same(seasons, seasons.Validated(183));
        foreach (var season in Enum.GetValues<Season>())
            Assert.Equal(season, Seasons.Parse(Seasons.Key(season)));
    }

    [Fact]
    public void SeasonsThatDoNotTileTheReckoning_OrWindowsOutsideIt_AreRefused()
    {
        var seasons = SeasonRules.Default;
        Assert.Throws<FormatException>(() => (seasons with { StartDays = ImmutableArray.Create(1, 42, 84, 126) }).Validated(183));
        Assert.Throws<FormatException>(() => (seasons with { StartDays = ImmutableArray.Create(0, 84, 42, 126) }).Validated(183));
        Assert.Throws<FormatException>(() => (seasons with { StartDays = ImmutableArray.Create(0, 42, 84) }).Validated(183));
        Assert.Throws<FormatException>(() => seasons.Validated(100));
        Assert.Throws<FormatException>(() => (seasons with { Windows = ImmutableArray.Create(new CalendarWindow("w", CalendarWindow.Festival, 0, 183)) }).Validated(183));
        Assert.Throws<FormatException>(() => (seasons with { Windows = ImmutableArray.Create(new CalendarWindow("w", "fair", 0, 1)) }).Validated(183));
        Assert.Throws<FormatException>(() => (seasons with
        {
            Windows = ImmutableArray.Create(new CalendarWindow("w", CalendarWindow.Seasonal, 0, 1), new CalendarWindow("w", CalendarWindow.Seasonal, 3, 4)),
        }).Validated(183));
    }

    [Fact]
    public void AWindow_CoversItsDays_AndOneThatRunsOverTheTurnOfTheReckoning_CoversBothEnds()
    {
        var harvest = new CalendarWindow("window.harvest", CalendarWindow.Seasonal, 98, 125);
        Assert.False(harvest.Covers(97));
        Assert.True(harvest.Covers(98));
        Assert.True(harvest.Covers(125));
        Assert.False(harvest.Covers(126));
        var turn = new CalendarWindow("window.turn", CalendarWindow.Festival, 180, 2);
        Assert.True(turn.Covers(182));
        Assert.True(turn.Covers(0));
        Assert.True(turn.Covers(2));
        Assert.False(turn.Covers(3));
        Assert.False(turn.Covers(179));
    }

    [Fact]
    public void TheSkysYear_TurnsWithTheReckoning_SeasonOnSeason_SoTheSunsHighestDayIsMidSummer()
    {
        SkyDate On(long day) => SkyCalendar.Of(DayStart(day), Clock, CalendarRules.Default, SeasonRules.Default);

        Assert.Equal((2001, 2, 4), (On(0).Year, On(0).Month, On(0).Day));   // spring begins half-way to the sky's spring equinox
        Assert.Equal((2001, 5, 6), (On(42).Year, On(42).Month, On(42).Day));
        Assert.Equal((2001, 8, 7), (On(84).Year, On(84).Month, On(84).Day));
        Assert.Equal((2001, 11, 7), (On(126).Year, On(126).Month, On(126).Day));
        Assert.Equal((2002, 2), (On(182).Year, On(182).Month));             // the Hollow Day: the end of the sky's winter
        Assert.Equal((2002, 2, 4), (On(183).Year, On(183).Month, On(183).Day));   // the next Reckoning's spring, a year on
        // Midsummer and midwinter fall on the solstices (about 21 June and 21 December), inside the windows named for them.
        Assert.Equal(6, On(63).Month);
        Assert.InRange(On(63).Day, 20, 22);
        Assert.Equal(12, On(154).Month);
        Assert.InRange(On(154).Day, 19, 22);
        // Monotonic through the year: every day stands later in the sky's year than the one before.
        for (long day = 1; day < 183; day++)
            Assert.True(On(day).DayOfYear > On(day - 1).DayOfYear || On(day).Year > On(day - 1).Year, $"day {day}");
    }

    [Fact]
    public void ASeason_DrawsItsOwnWeather_AndTheOthersKeepTheBaseWeights()
    {
        var winter = new Dictionary<WeatherState, int> { [WeatherState.Fair] = 1, [WeatherState.Overcast] = 6, [WeatherState.Fog] = 6, [WeatherState.Rain] = 1 }
            .ToImmutableSortedDictionary();
        var rules = WeatherRules.Default with { SeasonWeights = ImmutableSortedDictionary<Season, ImmutableSortedDictionary<WeatherState, int>>.Empty.Add(Season.Winter, winter) };
        Assert.Same(rules, rules.Validated());

        // Without a season every span draws as before; spring has no weights of its own, so it draws as before too.
        Assert.Equal(Weather.Fronts(Seed, RegionCell, Region, 3, WeatherRules.Default).ToArray(), Weather.Fronts(Seed, RegionCell, Region, 3, rules, _ => Season.Spring).ToArray());
        var all = Enumerable.Range(0, 300).SelectMany(s => Weather.Fronts(Seed, RegionCell, Region, s, rules, _ => Season.Winter)).ToList();
        Assert.DoesNotContain(all, f => f.State == WeatherState.Wind);   // winter has no weight for it
        Assert.True(all.Count(f => f.State is WeatherState.Overcast or WeatherState.Fog) > all.Count * 2 / 3);   // neighbours differ, so not all of it
        Assert.Throws<FormatException>(() => (rules with
        {
            SeasonWeights = rules.SeasonWeights.SetItem(Season.Summer, winter.SetItem(WeatherState.Fold, 2)),
        }).Validated());
    }

    [Fact]
    public void TheSkyBlends_FromThePreviousFront_ByWorldTicks_SoAnyTickReadsTheSameBlend()
    {
        var view = new WeatherView(Region, WeatherState.Rain, 10_000, 30_000) { Previous = WeatherState.Fair };
        Assert.Equal(0.0, Weather.Blend(view, 10_000, 800));
        Assert.Equal(0.5, Weather.Blend(view, 10_400, 800));
        Assert.Equal(1.0, Weather.Blend(view, 10_800, 800));
        Assert.Equal(1.0, Weather.Blend(view, 29_999, 800));
        Assert.Equal(1.0, Weather.Blend(view with { Previous = WeatherState.Rain }, 10_000, 800));   // nothing to blend from
        Assert.Equal(WeatherState.Rain, new WeatherView(Region, WeatherState.Rain, 0, 1).Previous);
    }

    private static long DayStart(long day) => day * Day - Clock.StartTicks;

    private static (long Reckoning, int Tally, int Day) Date(long day)
    {
        var date = Calendar.Of(DayStart(day) + 1, Clock, CalendarRules.Default);
        return (date.Reckoning, date.Tally, date.Day);
    }
}
