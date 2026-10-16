using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Tests;

/// <summary>A journey by the stones in words (S-38): the road's walk and the world time it costs, in game time, before it is taken.</summary>
public class TravelWordsTests
{
    private const double TicksPerDay = 1440.0 * 2.0 * 20.0;   // config.time: 40 ticks a game minute

    [Fact]
    public void TheAshenToRimJourney_ReadsInHoursAndMinutes()
    {
        // 6,800 ticks of walking is 170 game minutes; a quarter of it, 1,700 ticks, is 42.5, which to the nearest five (half to even) is 40.
        Assert.Equal("The road would take you about 2 h 50 min. You arrive about 40 min later than you left.", TravelWords.Journey(6_800, 1_700, TicksPerDay));
    }

    [Theory]
    [InlineData(2_400, "1 h")]   // 60 minutes exactly
    [InlineData(40, "5 min")]    // never less than five minutes
    [InlineData(0, "5 min")]
    public void GameTime_IsRoundedToFiveMinutes_AndNeverNothing(long ticks, string words) =>
        Assert.Contains($"about {words} later", TravelWords.Journey(ticks, ticks, TicksPerDay));
}
