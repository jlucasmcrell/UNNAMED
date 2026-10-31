// UNNAMED Content tests - the seasons as content (config.seasons, WLD-37/56).

using UNNAMED.Domain.World;

namespace UNNAMED.Content.Tests;

/// <summary>
/// The shipped seasons are data: every season has its reading, every event kind the Director draws has its effect, the prices and forage are the
/// balance pass the pack intends, and each named region carries its own climate offset (WEATHER §5).
/// </summary>
public class SeasonContentTests
{
    private static ContentLoader Game()
    {
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(RepoPaths.Root(), "content"));
        return loader;
    }

    [Fact]
    public void ThePackDescribesAWholeYear_AndEveryEventKindTheDirectorDraws()
    {
        var rules = WorldContent.BuildSeasons(Game());

        foreach (string season in Seasons.All)
        {
            Assert.True(rules.TemperatureC.ContainsKey(season), $"{season} has no temperature");
            Assert.True(rules.DayMinutes[season] > 0, $"{season} has no daylight");
            Assert.Contains(rules.Ground[season], Grounds.All);
            Assert.True(rules.MarketPermille[season] > 0);
            Assert.True(rules.ForagePermille[season] > 0);
            Assert.True(rules.RoadPermille[season] > 0);
        }
        foreach (string kind in WorldEventKinds.All)
        {
            Assert.True(rules.EventMarketPermille.ContainsKey(kind), $"{kind} does not move a price");
            Assert.True(rules.EventForagePermille.ContainsKey(kind), $"{kind} does not move the forage");
            Assert.True(rules.EventRoadPermille.ContainsKey(kind), $"{kind} does not move a road");
        }
        Assert.Equal(WorldEventKinds.Siege, Assert.Single(rules.ClosingEvents));
        Assert.Contains(WorldEventKinds.Siege, rules.ShelteringEvents);
        Assert.Equal(WorldEventKinds.Festival, rules.FestivalEvent);
    }

    [Fact]
    public void TheShippedNumbersAreTheBalancePassTheyClaimToBe()
    {
        var rules = WorldContent.BuildSeasons(Game());

        // The harvest is what there is to forage; the Hollow Day is what there is not.
        Assert.Equal(1300, rules.ForagePermille[Seasons.Harvest]);
        Assert.Equal(350, rules.ForagePermille[Seasons.Intercalary]);
        // Winter is dear and the harvest is cheap.
        Assert.Equal(1250, rules.MarketPermille[Seasons.Intercalary]);
        Assert.Equal(900, rules.MarketPermille[Seasons.Harvest]);
        // A caravan brings goods down and a siege puts them up.
        Assert.True(rules.EventMarketPermille[WorldEventKinds.Caravan] < 1000);
        Assert.True(rules.EventMarketPermille[WorldEventKinds.Siege] > 1000);
    }

    [Fact]
    public void EveryNamedRegionHasItsOwnClimateOffset_AndTheBarrowworksIsColder()
    {
        var climates = WorldContent.BuildClimates(Game());

        Assert.NotEmpty(climates);
        Assert.Equal(-4, climates["barrowworks"].TemperatureOffsetC);
        Assert.True(climates["barrowworks"].TemperatureOffsetC < climates["starting_frontier"].TemperatureOffsetC);
        Assert.True(climates["otherhome"].DayMinutesOffset > 0, "the harbour's days read longer than the plain's");
    }
}
