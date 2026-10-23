using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Tests;

/// <summary>The calendar line (WP7): numbered days, tallies and Reckonings, as the owner put it, and the weather now in one word.</summary>
public class CalendarWordsTests
{
    private static readonly WorldClock Clock = WorldClock.Default;
    private static readonly CalendarRules Rules = CalendarRules.Default;

    private static long DayStart(long day) => day * Clock.TicksPerDay;

    [Fact]
    public void TheOwnersExample_ReadsAsTheOwnerWroteIt()
    {
        // Day 41 of the world (from 0): two tallies of 14 days passed, then the 14th day of the 3rd.
        var date = Calendar.Of(DayStart(2 * 14 + 13), Clock, Rules);
        Assert.Equal("14th day of the 3rd tally, first Reckoning", CalendarWords.Date(date, Rules));
    }

    [Fact]
    public void TheWorldsFirstDay_IsThe1stOfThe1stTally() =>
        Assert.Equal("1st day of the 1st tally, first Reckoning", CalendarWords.Date(Calendar.Of(0, Clock, Rules), Rules));

    [Fact]
    public void TheHollowDay_BelongsToNoTally()
    {
        var date = Calendar.Of(DayStart(13 * 14), Clock, Rules);
        Assert.True(date.IsHollowDay);
        Assert.Equal("The Hollow Day, first Reckoning", CalendarWords.Date(date, Rules));
        var two = new CalendarRules(14, 13, 2);
        Assert.Equal("The 2nd Hollow Day, first Reckoning", CalendarWords.Date(Calendar.Of(DayStart(13 * 14 + 1), Clock, two), two));
    }

    [Fact]
    public void TheNextReckoning_IsCountedInWords_ThenInNumbers()
    {
        Assert.Equal("1st day of the 1st tally, second Reckoning", CalendarWords.Date(Calendar.Of(DayStart(Rules.DaysPerReckoning), Clock, Rules), Rules));
        Assert.Equal("tenth", CalendarWords.Reckoning(10));
        Assert.Equal("11th", CalendarWords.Reckoning(11));
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(112, "112th")]
    public void Ordinals(long n, string words) => Assert.Equal(words, CalendarWords.Ordinal(n));

    [Fact]
    public void TheLine_AddsTheWeatherWord()
    {
        var date = Calendar.Of(0, Clock, Rules);
        Assert.Equal("1st day of the 1st tally, first Reckoning · 00:00 · Rain", CalendarWords.Line(date, Rules, WeatherState.Rain));
        Assert.Equal("1st day of the 1st tally, first Reckoning · 00:00", CalendarWords.Line(date, Rules, null));
    }

    [Fact]
    public void ClockUsesTheWorldStartOffsetAndRollsOverAtMidnight()
    {
        var clock = Clock with { StartTicks = Clock.TicksPerDay * 15 / 48 };
        Assert.Equal("07:30", CalendarWords.Time(Calendar.Of(0, clock, Rules)));
        var afterMidnight = Calendar.Of(clock.TicksPerDay - clock.StartTicks, clock, Rules);
        Assert.Equal(2, afterMidnight.Day);
        Assert.Equal("00:00", CalendarWords.Time(afterMidnight));
        Assert.Contains("☾ 00:00 · ☂ Rain", CalendarWords.HudClock(afterMidnight, WeatherState.Rain));
    }

    [Fact]
    public void EveryWeatherState_HasAWord_AndItsKeyReadsTheSame()
    {
        foreach (var state in Enum.GetValues<WeatherState>())
        {
            string word = CalendarWords.Weather(state);
            Assert.False(string.IsNullOrWhiteSpace(word));
            Assert.Equal(word, CalendarWords.Weather(WeatherStates.Key(state)));
        }
    }
}
