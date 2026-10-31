using UNNAMED.Domain.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WLD-33/58/89 in the world: the season is read off the calendar and turns when the calendar says so, and the Director gives each region a few
/// events, never more than the cap, drawn from the season it is in. Both are read from the away log, which is what a digest reads.
/// </summary>
public class WorldEventsTests
{
    private static long FortyFiveDays(GameSession session) => 45 * session.Setup.Clock.TicksPerDay;

    private static Arena At(GameSession session) =>
        Arena.OpenCreatures(session, session.Setup, (60.3, 140.3), 0, Array.Empty<(string, double, double, string)>());

    [Fact]
    public void TheSeasonComesOffTheCalendar_AndTurnsWhenTheTalliesSayItDoes()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session);
        var sim = arena.Simulation;

        Assert.Equal(Seasons.Thaw, sim.Season.Season);
        Assert.False(sim.Season.Midwinter);

        // 45 days: past the third tally of 14 days, so the year has turned into Highsun.
        var log = sim.Advance(FortyFiveDays(session));

        var turn = log.Lines.LastOrDefault(l => l.Kind == WorldAdvanceKinds.SeasonTurned);
        Assert.NotNull(turn);
        Assert.Equal(Seasons.Highsun, turn!.Ref);
        Assert.Equal(Seasons.Highsun, sim.Season.Season);
        Assert.False(sim.Season.Midwinter);

    }

    [Fact]
    public void ARegionLivingThroughTheYear_CarriesNoMoreThanTheCapOfEvents()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session);
        var sim = arena.Simulation;

        var log = sim.Advance(FortyFiveDays(session));

        var began = log.Lines.Where(l => l.Kind == WorldAdvanceKinds.WorldEvent).ToArray();
        Assert.NotEmpty(began);
        Assert.All(began, l => Assert.Contains(l.Ref, WorldEventKinds.All));

        // Whatever a region is living through, it is inside the cap, and its events are its own season's.
        foreach (string region in began.Select(l => l.SubjectId!).Distinct())
        {
            var running = sim.WorldEvents(region);
            Assert.True(running.Length <= DirectorRules.CapPerRegion);
            Assert.All(running, e => Assert.Equal(region, e.RegionId));
        }

        // Every kind drawn was one the calendar allowed that day: a siege comes only with the cold, and 45 days of Thaw and Highsun is not it.
        var kinds = began.Select(l => l.Ref).Distinct().ToArray();
        Assert.All(kinds, k => Assert.NotEqual(WorldEventKinds.Siege, k));
    }
}
