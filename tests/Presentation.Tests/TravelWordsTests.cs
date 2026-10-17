using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Tests;

/// <summary>A journey by the stones in words (S-38): the road's walk in game time, before it is taken; the stones take no time (DN8).</summary>
public class TravelWordsTests
{
    private const double TicksPerDay = 1440.0 * 2.0 * 20.0;   // config.time: 40 ticks a game minute

    [Fact]
    public void TheAshenToRimJourney_ReadsInHoursAndMinutes()
    {
        // 6,800 ticks of walking is 170 game minutes.
        Assert.Equal("The road would take you about 2 h 50 min. You arrive the moment you leave.", TravelWords.Journey(6_800, TicksPerDay));
    }

    [Theory]
    [InlineData(2_400, "1 h")]   // 60 minutes exactly
    [InlineData(40, "5 min")]    // never less than five minutes
    [InlineData(0, "5 min")]
    public void GameTime_IsRoundedToFiveMinutes_AndNeverNothing(long ticks, string words) =>
        Assert.Contains($"about {words}.", TravelWords.Journey(ticks, TicksPerDay));
}
