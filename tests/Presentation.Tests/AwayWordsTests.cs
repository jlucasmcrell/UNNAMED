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
    public void TheHead_TellsTheAbsenceInDaysAndHours()
    {
        Assert.Equal("You were away 16 days.", Of(Days(16), false).Head);
        Assert.Equal("You were away 3 days.", Of(Days(3), false).Head);
        Assert.Equal("You were away 1 day.", Of(Days(1), false).Head);
        Assert.Equal("You were away 5 hours.", Of(Clock.TicksPerDay * 5 / 24, false).Head);
        Assert.Equal("You were away 28 days.", Of(Days(28), false).Head);
        Assert.Equal("You were away 183 days. The world advanced no further than this.",
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

        Assert.Equal(new[] { AwayWords.Party, AwayWords.Prices, AwayWords.Weather, AwayWords.Respawns, AwayWords.Notable },
            summary.Groups.Select(g => g.Key));
        Assert.Equal("Your companions", summary.Groups.Single(g => g.Key == AwayWords.Party).Heading);
        Assert.Equal(new[] { "The weather now: rain." }, summary.Groups.Single(g=>g.Key==AwayWords.Weather).Lines);
        Assert.Equal(new[]
        {
            "Iron ingot costs more at the Ashen Hollow market: up 12% on its usual price.",
            "The Ashen Hollow market is short of wolf hide.",
        }, summary.Groups[1].Lines);
        Assert.Equal(new[] { "You paid Bram 6 coin in wages." }, summary.Groups.Single(g=>g.Key==AwayWords.Party).Lines);
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
        Assert.Equal("You were away less than an hour.", Of(minutes, false).Head);
    }

    [Fact]
    public void TheJournal_GetsTheHeadThenEveryLine_InTheSheetsOrder()
    {
        long at = Days(1);
        var summary = Of(at, false,
            new AdvanceLine(at, PartyAdvanceKinds.WagesPaid, "npc.ashen_hollow.bram", 2),
            new AdvanceLine(at, AdvanceKinds.WeatherNow, "fog"));
        Assert.Equal(new[] { "You were away 1 day.", "You paid Bram 2 coin in wages.", "The weather now: fog." }, summary.JournalLines);
    }

    [Fact]
    public void TheDigest_HasAHumanDuration_NamedNews_CollapsedClaims_AndNoSlotIds()
    {
        string Display(string id)=>id switch { "quest.den"=>"Blackvein Den", "location.inn"=>"Ashen Hollow", "creature.hound"=>"Ash Ember Hound", _=>id };
        var lines=Enumerable.Range(0,3).Select(i=>new AdvanceLine(i,AdventurerAdvanceKinds.Claimed,"quest.den|adventurer.slot_g") {
            ActorName="Brenna Holt",PlaceId="location.inn",SubjectId="quest.den" }).Append(
            new AdvanceLine(9,AdventurerAdvanceKinds.Arrived,"adventurer.slot_k") { ActorName="Kell Marrow",PlaceId="location.inn",Level=3 }).ToArray();
        var summary=AwayWords.Of(0,Days(2)+Clock.TicksPerDay*6/24,lines,false,Clock,Rules,Display);
        Assert.Equal("You were away 2 days, 6 hours.",summary.Head);
        Assert.Contains("Brenna Holt claimed 3 contracts at Ashen Hollow.",summary.Groups.SelectMany(g=>g.Lines));
        Assert.Contains("A new adventurer, Kell Marrow (level 3), arrived at Ashen Hollow.",summary.Groups.SelectMany(g=>g.Lines));
        Assert.Contains("Brenna Holt took the “Blackvein Den” contract at Ashen Hollow.",summary.DetailGroups.SelectMany(g=>g.Lines));
        foreach(string text in summary.JournalLines.Concat(summary.Groups.SelectMany(g=>g.Lines)))
            Assert.DoesNotMatch(@"(?i)\bslot\s+\w|\b[a-z_]+\.[a-z0-9_.]+|\b[a-z]{3}_[0-9a-hjkmnp-tv-z]{26}",text);
        Assert.DoesNotContain("Slot",AwayWords.Line(new AdvanceLine(1,AdventurerAdvanceKinds.Died,"slot.g"),id=>"Slot g"));
    }

    [Fact]
    public void ANewResident_IsNamedInsteadOfTheirVacatedRoleSlot()
    {
        string Display(string id)=>id=="location.inn" ? "Ashen Hollow" : "Slot g";
        Assert.Equal("Sella Ashcombe arrived at Ashen Hollow.",AwayWords.Line(
            new AdvanceLine(0,NpcAdvanceKinds.Arrived,"npc.slot.g") { ActorName="Sella Ashcombe",PlaceId="location.inn" },Display));
        Assert.Equal("A new resident arrived.",AwayWords.Line(new AdvanceLine(0,NpcAdvanceKinds.Arrived,"npc.slot.g"),Display));
    }

    [Fact]
    public void ThePreview_StopsAtTwelveLines_AndAllDetailsRemainAvailable()
    {
        var lines=Enumerable.Range(0,20).Select(i=>new AdvanceLine(i,AdventurerAdvanceKinds.Arrived,"slot."+i) {
            ActorName="Traveller "+i,PlaceId="inn",Level=1 }).ToArray();
        var summary=Of(Days(2),false,lines);
        Assert.Equal(12,summary.Groups.Sum(g=>g.Lines.Length));
        Assert.Equal(20,summary.DetailGroups.Sum(g=>g.Lines.Length));
        Assert.Equal(21,summary.JournalLines.Count());
    }

    [Fact]
    public void WagesAcrossSeveralDays_AreOneSummary_AndKeepTheOriginalDetails()
    {
        var summary=Of(Days(2),false,new AdvanceLine(1,PartyAdvanceKinds.WagesPaid,"npc.ashen_hollow.bram",5),
            new AdvanceLine(2,PartyAdvanceKinds.WagesPaid,"npc.ashen_hollow.bram",5));
        Assert.Equal("You paid Bram 10 coin in wages.",Assert.Single(summary.Groups.SelectMany(g=>g.Lines)));
        Assert.Equal(2,summary.DetailGroups.Sum(g=>g.Lines.Length));
    }
}
