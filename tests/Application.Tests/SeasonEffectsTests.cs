// UNNAMED Application tests - the season and the Director reaching the running world (WLD-37/56).

using UNNAMED.Domain.World;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The readings a running world gives about its season: a region's climate, what the season leaves of prices, forage and roads, and how the
/// working day stands. These are the numbers the traders, the nodes, the waystones and the routines read, so this is where the wiring is proven to
/// be live rather than merely compiled.
/// </summary>
public sealed class SeasonEffectsTests
{
    private static Arena At(GameSession session) =>
        Arena.OpenCreatures(session, session.Setup, (60.3, 140.3), 0, Array.Empty<(string, double, double, string)>());

    [Fact]
    public void TheWorldReadsItsSeason_AndEveryRegionReadsItDifferently()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session);
        var (season, midwinter) = arena.Simulation.Season;

        Assert.Contains(season, Seasons.All);
        var plain = arena.Simulation.Climate("starting_frontier");
        var cold = arena.Simulation.Climate("barrowworks");
        var warm = arena.Simulation.Climate("ashen_hollow");
        var shortDay = arena.Simulation.Climate("den_gorge_cave");

        Assert.Equal(season, plain.Season);
        Assert.Equal(midwinter, plain.Midwinter);
        Assert.Equal(plain.TemperatureC - 4, cold.TemperatureC);   // config.seasons gives the Barrowworks -4
        Assert.Equal(plain.TemperatureC + 1, warm.TemperatureC);   // and the hollow +1
        Assert.Equal(plain.DayMinutes - 20, shortDay.DayMinutes);  // the cave has less light than the plain
        Assert.Equal(plain.Ground, cold.Ground);                   // the same season, the same ground
    }

    [Fact]
    public void TheRegionsNumbersMatchTheSeasonItIsIn()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session);
        var (season, _) = arena.Simulation.Season;
        var rules = session.Setup.Seasons;
        string region = arena.Simulation.Region;

        Assert.Equal(rules.MarketPermille[season], arena.Simulation.MarketPermille(region));
        Assert.Equal(rules.ForagePermille[season], arena.Simulation.ForagePermille(region));
        Assert.Equal(rules.RoadPermille[season], arena.Simulation.RoadPermille(region));
        Assert.Equal(rules.WorkHourShift[season], arena.Simulation.ScheduleOf(region).WorkHourShift);

        // a fresh world has no event running, so nothing has shut a road and nothing has emptied the working day
        Assert.False(arena.Simulation.RoadClosed(region));
        Assert.False(arena.Simulation.ScheduleOf(region).Shelter);
        Assert.False(arena.Simulation.ScheduleOf(region).Festival);
    }

    [Fact]
    public void ASeasonTurnsWithTheCalendar_AndTheClimateFollows()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session);
        var sim = arena.Simulation;
        var before = sim.Climate(sim.Region);
        Assert.Equal(Seasons.Thaw, before.Season);

        // 45 days: past the third tally of 14, so the year has turned into Highsun (the same arithmetic WorldEventsTests holds).
        sim.Advance(45 * session.Setup.Clock.TicksPerDay);
        var after = sim.Climate(sim.Region);

        Assert.Equal(Seasons.Highsun, after.Season);
        Assert.True(after.TemperatureC > before.TemperatureC, $"Highsun reads warmer than Thaw: {before.Line} then {after.Line}");
        Assert.True(after.DayMinutes > before.DayMinutes, "and the days are longer");
        Assert.Equal(session.Setup.Seasons.TemperatureC[after.Season]
                     + (session.Setup.Climates.TryGetValue(after.RegionId, out var offset) ? offset.TemperatureOffsetC : 0),
            after.TemperatureC);
    }
}
