using System.Collections.Immutable;
using UNNAMED.Persistence;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP7 in the running world: the weather over the region played is derived each game hour, announced by <see cref="WeatherChanged"/>
/// when a new front arrives, and caught up closed-form in an absence, whatever its pieces (T-22). The calendar reads the clock.
/// </summary>
public class WeatherTests
{
    private const long Day = 57_600;

    private static (GameSession Session, List<WeatherChanged> Changes) NewWorld(TempProfile profile)
    {
        var session = Harness.Boot(profile);
        var changes = Harness.Record<WeatherChanged>(session);
        session.NewGame(Harness.Named("Wanderer"), seed: 7);
        return (session, changes);
    }

    [Fact]
    public void ANewWorld_AnnouncesTheWeatherOverhead_ThenOnlyEachNewFront()
    {
        using var profile = new TempProfile();
        var (session, changes) = NewWorld(profile);
        var simulation = session.Simulation!;
        Assert.Null(simulation.WeatherCursor);

        Harness.Ticks(session, 1);
        var first = Assert.Single(changes);
        var now = simulation.Weather;
        Assert.Equal(("region.starting_frontier", (WeatherState?)null, now.State), (first.RegionId, first.From, first.To));
        Assert.InRange(simulation.WorldTick, now.FrontStartTick, now.FrontEndTick - 1);
        Assert.Equal((now.FrontStartTick, now.FrontEndTick), (first.FrontStartTick, first.FrontEndTick));
        Assert.NotNull(simulation.WeatherCursor);

        // Through the rest of the front, nothing more; at its end, the next one, which is never the same weather.
        simulation.Advance(now.FrontEndTick - simulation.WorldTick - 3);
        Harness.Ticks(session, 2);
        Assert.Single(changes);
        Harness.Ticks(session, 2);
        Assert.Equal(2, changes.Count);
        var next = changes[1];
        Assert.Equal((now.State, now.FrontEndTick), (next.From, next.FrontStartTick));
        Assert.NotEqual(now.State, next.To);
        Assert.Equal(next.To, simulation.Weather.State);
        Assert.InRange(next.FrontEndTick - next.FrontStartTick, 4 * Day / 24, 12 * Day / 24);
    }

    [Fact]
    public void T22_AnAbsence_InAnyPieces_EndsUnderTheSameSky_AndTheLogSaysWhatItIs()
    {
        using var profile = new TempProfile();
        long absence = 9 * Day + 4_321;
        var (once, _) = NewWorld(profile);
        Harness.Ticks(once, 1);
        var log = once.Simulation!.Advance(absence);

        foreach (var pieces in new[] { new[] { 1L, absence - 1 }, new[] { Day / 3, 0, 4 * Day, absence - 4 * Day - Day / 3 }, Enumerable.Repeat(absence / 9, 9).Append(absence % 9).ToArray() })
        {
            using var other = new TempProfile();
            var (chunked, _) = NewWorld(other);
            Harness.Ticks(chunked, 1);
            foreach (long piece in pieces)
                chunked.Simulation!.Advance(piece);
            Assert.Equal(once.Simulation.WorldTick, chunked.Simulation!.WorldTick);
            Assert.Equal(once.Simulation.Weather, chunked.Simulation.Weather);
            Assert.Equal(once.Simulation.WeatherCursor, chunked.Simulation.WeatherCursor);
        }

        var line = Assert.Single(log.Lines, l => l.Kind == AdvanceKinds.WeatherNow);
        Assert.Equal((log.ToTick, WeatherStates.Key(once.Simulation.Weather.State)), (line.Tick, line.Ref));
        Assert.Equal(once.Simulation.WeatherCursor, once.Setup.Clock.Hour(once.Simulation.Weather.FrontStartTick));
    }

    // ── WP 1D: fronts on ticks that survive a reload, seasons, windows, the markets' seasons ─────

    [Fact]
    public void AReloadInTheMiddleOfAChange_DrawsTheSameBlend_AsTheRunThatWentThroughIt()
    {
        using var profile = new TempProfile();
        var (session, _) = NewWorld(profile);
        Harness.Ticks(session, 1);
        var simulation = session.Simulation!;
        long fade = (long)Math.Round(Greybox_FadeSeconds / session.TickSeconds);
        // 300 ticks into the next front: the sky is part way from the old weather to the new.
        simulation.Advance(simulation.Weather.FrontEndTick - simulation.WorldTick + 300);
        var lived = simulation.Weather;
        double blend = Weather.Blend(lived, simulation.WorldTick, fade);
        Assert.NotEqual(lived.Previous, lived.State);
        Assert.InRange(blend, 0.3, 0.45);

        session.Save(SaveSlots.Quick);
        session.Load(SaveSlots.Quick);
        var loaded = session.Simulation!;
        Assert.Equal(lived, loaded.Weather);
        Assert.Equal(blend, Weather.Blend(loaded.Weather, loaded.WorldTick, fade));
        Harness.Ticks(session, 500);
        Assert.Equal(1.0, Weather.Blend(loaded.Weather, loaded.WorldTick, fade));
        Assert.Equal(lived.State, loaded.Weather.State);
    }

    /// <summary>The presentation's cross-fade (Greybox.WeatherStates.FadeSeconds), which this test project cannot reference.</summary>
    private const double Greybox_FadeSeconds = 40;

    [Fact]
    public void TheSeasons_Turn_AndTheWindowsOpenAndClose_AtMidnight_AndALoadAnnouncesNothing()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var turned = Harness.Record<SeasonChanged>(session);
        var opened = Harness.Record<CalendarWindowOpened>(session);
        var closed = Harness.Record<CalendarWindowClosed>(session);
        session.NewGame(Harness.Named("Wanderer"), seed: 7);
        var simulation = session.Simulation!;
        Assert.Equal(Season.Spring, simulation.Season);
        Assert.Empty(simulation.OpenWindows);

        // An absence over the bloom and into summer: the season it ends in is announced; the bloom came and went while away, so nothing of it.
        simulation.Advance(42 * Day);
        Assert.Equal(new[] { (Season.Spring, Season.Summer) }, turned.Select(t => (t.From, t.To)));
        Assert.Empty(opened);
        Assert.Equal(Season.Summer, simulation.Season);

        // Live, the midsummer window opens at the midnight that starts its day, and closes at the next.
        long midnight = 63 * Day - session.Setup.Clock.StartTicks;
        simulation.Advance(midnight - simulation.WorldTick - 2);
        Harness.Ticks(session, 4);
        Assert.Equal("window.midsummer", Assert.Single(opened).WindowId);
        Assert.Equal(CalendarWindow.Seasonal, opened[0].Kind);
        Assert.Equal("window.midsummer", Assert.Single(simulation.OpenWindows).Id);

        session.Save(SaveSlots.Quick);
        session.Load(SaveSlots.Quick);
        Harness.Ticks(session, 3);
        Assert.Single(opened);   // the load read the open window where it stands
        session.Simulation!.Advance(Day);
        Assert.Equal("window.midsummer", Assert.Single(closed).WindowId);
    }

    [Fact]
    public void TheHollowDay_IsAFestival_InWinter()
    {
        using var profile = new TempProfile();
        var (session, _) = NewWorld(profile);
        var simulation = session.Simulation!;
        simulation.Advance(182 * Day);
        Assert.True(simulation.Calendar.IsHollowDay);
        Assert.Equal(Season.Winter, simulation.Season);
        var festival = Assert.Single(simulation.OpenWindows);
        Assert.Equal(("window.hollow_day", CalendarWindow.Festival), (festival.Id, festival.Kind));
        simulation.Advance(Day);
        Assert.Equal(Season.Spring, simulation.Season);
        Assert.Equal(2, simulation.Calendar.Reckoning);
    }

    [Fact]
    public void Winter_DrawsDownTheMarkets_AndAnAbsenceInPiecesSettlesAlike()
    {
        using var profile = new TempProfile();
        var (session, _) = NewWorld(profile);
        session.Save(SaveSlots.Quick);
        var store = new SaveStore(session.SaveFolder);
        Simulation Load(SimulationSetup setup)
        {
            var loaded = store.Load(SaveSlots.Quick, new LoadContext(session.Generator, session.Content, new UNNAMED.EntityRegistry.EntityRegistry()));
            return Simulation.Start(setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick, new EventBus());
        }
        long Salve(Simulation s) => s.Markets.Single(m => m.MarketId == "market.ashen_hollow").Goods.Single(g => g.ItemId == "item.consumable.salve_minor").Stock;
        string Markets(Simulation s) => string.Join(";", s.Markets.SelectMany(m => m.Goods.Select(g => $"{m.MarketId}/{g.ItemId}:{g.Stock}")));

        var seasonal = Load(session.Setup);
        var unseasonal = Load(session.Setup with { Seasons = session.Setup.Seasons with { MarketFlows = ImmutableSortedDictionary<Season, ImmutableArray<(string, string, long)>>.Empty } });
        long absence = 150 * Day;   // deep into winter
        seasonal.Advance(absence);
        unseasonal.Advance(absence);
        Assert.Equal(Season.Winter, seasonal.Season);
        Assert.True(Salve(seasonal) < Salve(unseasonal), $"winter salve {Salve(seasonal)} against {Salve(unseasonal)}");

        var chunked = Load(session.Setup);
        foreach (long piece in new[] { 40 * Day + 17, 0, 60 * Day, absence - 100 * Day - 17 })
            chunked.Advance(piece);
        Assert.Equal(Markets(seasonal), Markets(chunked));
        Assert.Equal((seasonal.Season, seasonal.Weather, seasonal.SkyDate), (chunked.Season, chunked.Weather, chunked.SkyDate));
        Assert.Equal(seasonal.OpenWindows.Select(w => w.Id), chunked.OpenWindows.Select(w => w.Id));
    }

    [Fact]
    public void TheCalendar_ReadsTheWorldClock()
    {
        using var profile = new TempProfile();
        var (session, _) = NewWorld(profile);
        var simulation = session.Simulation!;
        Assert.Equal(new CalendarDate(1, 1, 1, 7, 30), simulation.Calendar);

        simulation.Advance(41 * Day);   // the owner's "14th day of the 3rd tally, first Reckoning"
        Assert.Equal(new CalendarDate(1, 3, 14, 7, 30), simulation.Calendar);
    }

    [Fact]
    public void TheGamesOwnContent_HasD6sFronts_AndTheCanonReckoning()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        Assert.Equal((4, 12, 24), (session.Setup.Weather.FrontHoursMin, session.Setup.Weather.FrontHoursMax, session.Setup.Weather.SpanHours));
        Assert.Equal(0, session.Setup.Weather.Weights.GetValueOrDefault(WeatherState.Fold));
        Assert.Equal(CalendarRules.Default, session.Setup.Calendar);
        Assert.Equal(183, session.Setup.Calendar.DaysPerReckoning);
        Assert.Equal(session.Setup.Offline.CapTicks, session.Setup.Calendar.DaysPerReckoning * session.Setup.Clock.TicksPerDay);   // DN1: one Reckoning
        Assert.Equal(24, session.Setup.Clock.HoursPerDay);
        // WP 1D: four seasons on the Reckoning, the Hollow Day a festival, and the seasons' own weather.
        Assert.Equal(new[] { 0, 42, 84, 126 }, session.Setup.Seasons.StartDays);
        Assert.Contains(session.Setup.Seasons.Windows, w => w.Id == "window.hollow_day" && w.Kind == CalendarWindow.Festival && w.FromDay == 182);
        Assert.Equal(new[] { Season.Summer, Season.Autumn, Season.Winter }, session.Setup.Weather.SeasonWeights.Keys);
        Assert.All(session.Setup.Weather.SeasonWeights.Values, w => Assert.Equal(0, w.GetValueOrDefault(WeatherState.Fold)));
    }
}
