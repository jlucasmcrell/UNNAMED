using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// The "while you were away" sheet's words (WP11, §2.12 item 7): the time away in days and tallies, the date now, and the log's lines in
/// the sheet's groups - weather, prices, hirelings and wages, respawns, the rest - the same words the journal's Recent list receives.
/// </summary>
public class AwayWordsTests
{
    private static readonly WorldClock Clock = WorldClock.Default;
    private static readonly CalendarRules Rules = CalendarRules.Default;
    private static long Days(long days) => days * Clock.TicksPerDay;

    private static readonly Dictionary<string, string> Content = new()
    {
        ["npc.ashen_hollow.bram"] = "Bram",
        ["item.material.iron_ingot"] = "Iron ingot",
        ["item.material.wolf_hide"] = "Wolf hide",
        ["market.ashen_hollow"] = "Ashen Hollow market",
        ["effect.well_fed"] = "Well fed",
    };

    private static string Name(string id) => Content.GetValueOrDefault(id, id);

    private static AwaySummary Of(long ticks, bool capped, params AdvanceLine[] lines) =>
        AwayWords.Of(0, ticks, lines, capped, Clock, Rules, Name);

    [Fact]
    public void TheHead_TellsTheDays_AndTheTallies()
    {
        Assert.Equal("16 days passed while you were away: a tally and 2 days.", Of(Days(16), false).Head);
        Assert.Equal("3 days passed while you were away.", Of(Days(3), false).Head);
        Assert.Equal("1 day passed while you were away.", Of(Days(1), false).Head);
        Assert.Equal("5 hours passed while you were away.", Of(Clock.TicksPerDay * 5 / 24, false).Head);
        Assert.Equal("28 days passed while you were away: 2 tallies.", Of(Days(28), false).Head);
        Assert.Equal("183 days passed while you were away: a whole Reckoning. You were gone longer than that; the world went on no further.",
            Of(Days(Rules.DaysPerReckoning), true).Head);
    }

    [Fact]
    public void TheDateNow_IsTheCalendarsAtTheEndOfTheAbsence()
    {
        Assert.Equal("It is the 3rd day of the 2nd tally, first Reckoning.", Of(Days(16), false).Now);
        Assert.Equal("It is the Hollow Day, first Reckoning.", Of(Days(13 * 14), false).Now);
    }

    [Fact]
    public void Lines_FallIntoTheSheetsGroups_InTheSheetsOrder()
    {
        long at = Days(3);
        var summary = Of(at, false,
            new AdvanceLine(at, AdvanceKinds.TimePassed, "", at),
            new AdvanceLine(at, AdvanceKinds.DaysPassed, "", 3),
            new AdvanceLine(at, PartyAdvanceKinds.WagesPaid, "npc.ashen_hollow.bram", 6),
            new AdvanceLine(at, AdvanceKinds.EffectExpired, "effect.well_fed"),
            new AdvanceLine(at, MarketAdvanceKinds.PriceMoved, "market.ashen_hollow/item.material.iron_ingot", 120),
            new AdvanceLine(at, MarketAdvanceKinds.Short, "market.ashen_hollow/item.material.wolf_hide", 1400),
            new AdvanceLine(at, "respawn.creature", "creature.ridge_wolf"),
            new AdvanceLine(at, AdvanceKinds.WeatherNow, "rain"));

        Assert.Equal(new[] { AwayWords.Weather, AwayWords.Prices, AwayWords.Party, AwayWords.Respawns, AwayWords.Notable },
            summary.Groups.Select(g => g.Key));
        Assert.Equal("Your hirelings and wages", summary.Groups.Single(g => g.Key == AwayWords.Party).Heading);
        Assert.Equal(new[] { "The weather now: rain." }, summary.Groups[0].Lines);
        Assert.Equal(new[]
        {
            "Iron ingot costs more at the Ashen Hollow market: up 12% on its usual price.",
            "The Ashen Hollow market is short of wolf hide.",
        }, summary.Groups[1].Lines);
        Assert.Equal(new[] { "You paid Bram 6 coin in wages." }, summary.Groups[2].Lines);
        Assert.Equal(new[] { "Well fed wore off." }, summary.Groups[4].Lines);
        Assert.True(summary.WorthShowing);
    }

    [Fact]
    public void TheTimeLines_AreTheHeads_NotGroupLines()
    {
        long at = Days(2);
        var summary = Of(at, false, new AdvanceLine(at, AdvanceKinds.TimePassed, "", at), new AdvanceLine(at, AdvanceKinds.DaysPassed, "", 2));
        Assert.Empty(summary.Groups);
        Assert.True(summary.WorthShowing);
    }

    [Fact]
    public void AFallingPrice_SaysSo()
    {
        var line = new AdvanceLine(0, MarketAdvanceKinds.PriceMoved, "market.ashen_hollow/item.material.iron_ingot", -75);
        Assert.Equal("Iron ingot costs less at the Ashen Hollow market: down 7.5% on its usual price.", AwayWords.Line(line, Name));
    }

    [Fact]
    public void AHirelingsWork_IsTold_OncePerWorkerAndGood_ForTheWholeAbsence()
    {
        long day = Clock.TicksPerDay;
        var summary = Of(Days(3), false,
            new AdvanceLine(day, PartyAdvanceKinds.Delivered, "npc.ashen_hollow.bram|item.material.wolf_hide", 2),
            new AdvanceLine(day, PartyAdvanceKinds.Left, "npc.ashen_hollow.bram|lapsed"),
            new AdvanceLine(2 * day, PartyAdvanceKinds.Delivered, "npc.ashen_hollow.bram|item.material.wolf_hide", 3));
        Assert.Equal(new[] { "Bram brought in wolf hide ×5.", "Bram left: the wages went unpaid." }, summary.Groups.Single().Lines);
    }

    [Theory]
    [InlineData("ended", "Bram's contract ran its course, and they have gone.")]
    [InlineData("lapsed", "Bram left: the wages went unpaid.")]
    [InlineData("dismissed", "You let Bram go.")]
    [InlineData("something_new", "Bram left the party.")]
    public void AHirelingLeaving_SaysWhy(string reason, string words) =>
        Assert.Equal(words, AwayWords.Line(new AdvanceLine(0, PartyAdvanceKinds.Left, $"npc.ashen_hollow.bram|{reason}"), Name));

    [Fact]
    public void ACompanionsFall_AndRecovery_AreTheirs()
    {
        Assert.Equal("Bram was gravely wounded, and has gone home to recover.",
            AwayWords.Line(new AdvanceLine(0, PartyAdvanceKinds.Wounded, "npc.ashen_hollow.bram"), Name));
        Assert.Equal("Bram has recovered.", AwayWords.Line(new AdvanceLine(0, PartyAdvanceKinds.Recovered, "npc.ashen_hollow.bram"), Name));
        Assert.Equal("Bram died.", AwayWords.Line(new AdvanceLine(0, PartyAdvanceKinds.Died, "npc.ashen_hollow.bram"), Name));
        Assert.Equal(AwayWords.Party, AwayWords.GroupOf(PartyAdvanceKinds.Wounded));
    }

    [Fact]
    public void AKindWithNoWordsYet_IsStillTold_UnderNotable()
    {
        var line = new AdvanceLine(0, "town.news", "npc.ashen_hollow.bram");
        Assert.Equal(AwayWords.Notable, AwayWords.GroupOf(line.Kind));
        Assert.Equal("Town news: Bram.", AwayWords.Line(line, Name));
    }

    [Fact]
    public void AShortAbsence_WithOnlyTheWeather_IsNotWorthTheSheet()
    {
        long minutes = Clock.TicksPerDay / 24 / 2;
        Assert.False(Of(minutes, false, new AdvanceLine(minutes, AdvanceKinds.WeatherNow, "fair")).WorthShowing);
        Assert.True(Of(minutes, false, new AdvanceLine(minutes, PartyAdvanceKinds.WagesPaid, "npc.ashen_hollow.bram", 1)).WorthShowing);
        Assert.Equal("Less than an hour passed while you were away.", Of(minutes, false).Head);
    }

    [Fact]
    public void TheJournal_GetsTheHeadThenEveryLine_InTheSheetsOrder()
    {
        long at = Days(1);
        var summary = Of(at, false,
            new AdvanceLine(at, PartyAdvanceKinds.WagesPaid, "npc.ashen_hollow.bram", 2),
            new AdvanceLine(at, AdvanceKinds.WeatherNow, "fog"));
        Assert.Equal(new[] { "1 day passed while you were away.", "The weather now: fog.", "You paid Bram 2 coin in wages." }, summary.JournalLines);
    }
}
