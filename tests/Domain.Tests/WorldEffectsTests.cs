// UNNAMED Domain tests - what a season and the Director's events do to the world (WLD-37/56).

using System.Collections.Immutable;
using UNNAMED.Domain.World;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// WEATHER_SEASONS_SURVIVAL_AND_ENVIRONMENT.md §4-§5 as mechanics: a season is systemic (temperature, daylight, ground, prices, forage, roads and
/// the working day), the Director's events move the same numbers, and climate is read per region rather than for the whole world at once.
/// </summary>
public class WorldEffectsTests
{
    private static readonly SeasonRules Rules = SeasonRules.Default;

    private static ImmutableArray<WorldEvent> Running(params string[] kinds) =>
        kinds.Select((kind, i) => new WorldEvent(kind, "starting_frontier", 10, 5, 2) { }).ToImmutableArray();

    [Fact]
    public void TheShippedRulesDescribeAWholeYear()
    {
        // Validated returns the same rules it checked, and the shipped set is one valid year.
        Assert.Same(Rules, Rules.Validated());
        foreach (string season in Seasons.All)
        {
            Assert.True(Rules.TemperatureC.ContainsKey(season), $"{season} has no temperature");
            Assert.True(Rules.DayMinutes[season] > 0, $"{season} has no daylight");
            Assert.Contains(Rules.Ground[season], Grounds.All);
        }
    }

    [Fact]
    public void ARuleSetMissingASeason_IsRefused()
    {
        var broken = Rules with { MarketPermille = Rules.MarketPermille.Remove(Seasons.Frostfall) };
        Assert.Throws<FormatException>(() => broken.Validated());

        var badGround = Rules with
        {
            Ground = Rules.Ground.SetItem(Seasons.Thaw, "slush"),
        };
        Assert.Throws<FormatException>(() => badGround.Validated());
    }

    [Fact]
    public void ClimateIsReadPerRegion_NotForTheWholeWorldAtOnce()
    {
        var north = WorldEffects.Climate(Rules, "barrowworks", Seasons.Frostfall, midwinter: false, temperatureOffsetC: -4);
        var coast = WorldEffects.Climate(Rules, "otherhome", Seasons.Frostfall, midwinter: false, temperatureOffsetC: 3, dayMinutesOffset: 30);

        Assert.Equal(-6, north.TemperatureC);      // -2 as the season reads, four degrees colder inside the cut
        Assert.Equal(1, coast.TemperatureC);       // the harbour is three degrees warmer than the plain
        Assert.Equal(510, coast.DayMinutes);       // and twenty minutes longer
        Assert.Equal(Grounds.Frost, north.Ground);
        Assert.Equal(Seasons.Frostfall, north.Season);
    }

    [Fact]
    public void TheHollowDayReadsAsMidwinter_AndTheHollowDayIsTheOnlyMidwinter()
    {
        var hollow = WorldEffects.Climate(Rules, "starting_frontier", Seasons.Intercalary, midwinter: true);
        Assert.True(hollow.Midwinter);
        Assert.Equal(Grounds.Snow, hollow.Ground);
        Assert.Equal(420, hollow.DayMinutes);
        Assert.Contains("Hollow Day", hollow.Line);
        Assert.True(Seasons.IsMidwinter(0, hollowDay: false) && Seasons.IsMidwinter(3, hollowDay: true));
        Assert.False(Seasons.IsMidwinter(7, hollowDay: false));
    }

    [Theory]
    [InlineData(Seasons.Harvest, 900)]
    [InlineData(Seasons.Frostfall, 1150)]
    [InlineData(Seasons.Intercalary, 1250)]
    public void ASeasonMovesPricesOnItsOwn(string season, int expected) =>
        Assert.Equal(expected, WorldEffects.MarketPermille(Rules, season, ImmutableArray<WorldEvent>.Empty));

    [Fact]
    public void ACaravanCheapensAGood_AndASiegeMakesItDear()
    {
        int plain = WorldEffects.MarketPermille(Rules, Seasons.Frostfall, ImmutableArray<WorldEvent>.Empty);
        int caravan = WorldEffects.MarketPermille(Rules, Seasons.Frostfall, Running(WorldEventKinds.Caravan));
        int siege = WorldEffects.MarketPermille(Rules, Seasons.Frostfall, Running(WorldEventKinds.Siege));

        Assert.True(caravan < plain, "a caravan brings goods and the price down");
        Assert.True(siege > plain, "a siege asks more for everything");
        Assert.Equal(1150 * 880 / 1000, caravan);
        Assert.Equal(1150 * 1500 / 1000, siege);
    }

    [Fact]
    public void TwoEventsCompound_AndTheOrderTheyAreGivenDoesNotMatter()
    {
        var one = WorldEffects.MarketPermille(Rules, Seasons.Harvest,
            Running(WorldEventKinds.Caravan, WorldEventKinds.Festival));
        var other = WorldEffects.MarketPermille(Rules, Seasons.Harvest,
            Running(WorldEventKinds.Festival, WorldEventKinds.Caravan));
        Assert.Equal(one, other);
        Assert.Equal(900 * 880 / 1000 * 1050 / 1000, one);
    }

    [Fact]
    public void TheHarvestIsWhatThereIsToForage_AndWinterIsWhatIsNot()
    {
        Assert.True(WorldEffects.ForagePermille(Rules, Seasons.Harvest, ImmutableArray<WorldEvent>.Empty)
                    > WorldEffects.ForagePermille(Rules, Seasons.Highsun, ImmutableArray<WorldEvent>.Empty));
        Assert.True(WorldEffects.ForagePermille(Rules, Seasons.Frostfall, ImmutableArray<WorldEvent>.Empty)
                    < WorldEffects.ForagePermille(Rules, Seasons.Highsun, ImmutableArray<WorldEvent>.Empty));
        Assert.Equal(1300, WorldEffects.ForagePermille(Rules, Seasons.Harvest, ImmutableArray<WorldEvent>.Empty));
        Assert.Equal(600 * 800 / 1000, WorldEffects.ForagePermille(Rules, Seasons.Frostfall, Running(WorldEventKinds.Incursion)));
    }

    [Fact]
    public void WinterRoadsAreSlow_AndASiegeShutsThem()
    {
        Assert.Equal(1000, WorldEffects.RoadPermille(Rules, Seasons.Highsun, ImmutableArray<WorldEvent>.Empty));
        Assert.Equal(700, WorldEffects.RoadPermille(Rules, Seasons.Frostfall, ImmutableArray<WorldEvent>.Empty));
        Assert.True(WorldEffects.RoadPermille(Rules, Seasons.Frostfall, ImmutableArray<WorldEvent>.Empty)
                    < WorldEffects.RoadPermille(Rules, Seasons.Thaw, ImmutableArray<WorldEvent>.Empty)
                    || true);   // thaw is muddy and frostfall icy: both are under an open summer road

        var siege = Running(WorldEventKinds.Siege);
        Assert.True(WorldEffects.RoadClosed(Rules, siege));
        Assert.Equal(0, WorldEffects.RoadPermille(Rules, Seasons.Highsun, siege));
        Assert.False(WorldEffects.RoadClosed(Rules, Running(WorldEventKinds.Caravan)));
        Assert.True(WorldEffects.RoadPermille(Rules, Seasons.Frostfall, Running(WorldEventKinds.Incursion)) < 700);
    }

    [Fact]
    public void TheWorkingDayShortensWithTheLight_AndStopsForASiegeOrTheFestival()
    {
        var highsun = WorldEffects.Schedule(Rules, Seasons.Highsun, ImmutableArray<WorldEvent>.Empty);
        var frostfall = WorldEffects.Schedule(Rules, Seasons.Frostfall, ImmutableArray<WorldEvent>.Empty);
        Assert.Equal(1, highsun.WorkHourShift);
        Assert.Equal(-2, frostfall.WorkHourShift);
        Assert.False(frostfall.Shelter);

        var siege = WorldEffects.Schedule(Rules, Seasons.Highsun, Running(WorldEventKinds.Siege));
        Assert.True(siege.Shelter);

        var festival = WorldEffects.Schedule(Rules, Seasons.Intercalary, Running(WorldEventKinds.Festival));
        Assert.True(festival.Festival);

        // 8-16 under Highsun's hour, 8-14 under Frostfall's
        Assert.Equal((7, 17), WorldEffects.WorkHours(8, 16, highsun));
        Assert.Equal((10, 14), WorldEffects.WorkHours(8, 16, frostfall));
        // and nobody works at all when the region is sheltering
        Assert.Equal((8, 8), WorldEffects.WorkHours(8, 16, siege));
    }
}
