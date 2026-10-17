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
    }
}
