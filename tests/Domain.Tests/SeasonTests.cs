// UNNAMED Domain tests - seasons and the midwinter Hollow Day
using UNNAMED.Domain.World;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// The owner's ruling made testable: Hollow Day is the midwinter intercalary day, the one day of a Reckoning that belongs to no tally and no
/// season. Thirteen tallies divide into Thaw, Highsun, Harvest and Frostfall, and the year closes out of winter.
/// </summary>
public class SeasonTests
{
    private const int Tallies = 13;   // content/config/time.yaml: 13 tallies of 14 days, then the Hollow Day

    [Fact]
    public void AReckoningRunsThawHighsunHarvestFrostfall_AndThenTheHollowDay()
    {
        Assert.Equal(Seasons.Thaw, Seasons.Of(1, Tallies, hollowDay: false));
        Assert.Equal(Seasons.Thaw, Seasons.Of(3, Tallies, hollowDay: false));
        Assert.Equal(Seasons.Highsun, Seasons.Of(4, Tallies, hollowDay: false));
        Assert.Equal(Seasons.Harvest, Seasons.Of(8, Tallies, hollowDay: false));
        Assert.Equal(Seasons.Frostfall, Seasons.Of(12, Tallies, hollowDay: false));
        Assert.Equal(Seasons.Frostfall, Seasons.Of(13, Tallies, hollowDay: false));

        // The year closes out of winter: the Hollow Day follows the last tally of Frostfall.
        Assert.Equal(Seasons.Intercalary, Seasons.Of(tally: 0, Tallies, hollowDay: true));
    }

    [Fact]
    public void HollowDayIsMidwinter_AndNothingElseInTheYearIs()
    {
        Assert.True(Seasons.IsMidwinter(tally: 0, hollowDay: true));
        for (int tally = 1; tally <= Tallies; tally++)
            Assert.False(Seasons.IsMidwinter(tally, hollowDay: false));

        // Even the coldest working season is not midwinter: the intercalary day is where the year turns.
        Assert.Equal(Seasons.Frostfall, Seasons.Of(13, Tallies, hollowDay: false));
        Assert.Equal(Seasons.Intercalary, Seasons.Of(13, Tallies, hollowDay: true));
    }

    [Fact]
    public void EveryTallyOfTheYearFallsInASeason_AndTheHollowDayInNone()
    {
        for (int tally = 1; tally <= Tallies; tally++)
        {
            string season = Seasons.Of(tally, Tallies, hollowDay: false);
            Assert.True(Seasons.IsWorkingSeason(season), $"tally {tally} fell in '{season}'");
        }
        Assert.False(Seasons.IsWorkingSeason(Seasons.Intercalary));
        Assert.Equal(5, Seasons.All.Length);
    }

    [Fact]
    public void AReadingIsClamped_NotThrown_WhenTheTallyIsOutsideTheYear()
    {
        Assert.Equal(Seasons.Thaw, Seasons.Of(-3, Tallies, hollowDay: false));
        Assert.Equal(Seasons.Frostfall, Seasons.Of(99, Tallies, hollowDay: false));
        Assert.Equal(Seasons.Intercalary, Seasons.Of(0, Tallies, hollowDay: false));
        Assert.Equal(Seasons.Thaw, Seasons.Of(1, 1, hollowDay: false));
    }
}
