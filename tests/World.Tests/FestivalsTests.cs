using System.Collections.Immutable;
using UNNAMED.World.Runtime;

namespace UNNAMED.World.Tests;

/// <summary>
/// WLD-58, WLD-89 (SYSTEMS_CHECKLIST R13): the calendar's dates turned into days the living world keeps - the Hollow Day, a named day,
/// a day of every tally, a seasonal window of tallies - with WLD-89's cap of eight world events in a region at once.
/// </summary>
public class FestivalsTests
{
    private const long Day = 57_600;
    private const string Region = "region.starting_frontier";

    /// <summary>The game's clock: a 48-minute day at 20 Hz that reads half past seven at tick 0 (config.time).</summary>
    private static readonly WorldClock Clock = new(Day, Day * 75 / 240);

    private static readonly CalendarRules Reckoning = CalendarRules.Default;

    private static CalendarDate Date(long day) => Calendar.OfDay(day, 12 * 60, Reckoning);

    private static long DayStart(long day) => Clock.TickOfDay(day);

    private static FestivalDefinition Festival(string id, FestivalWhen when, bool gathers = true, int from = 10, int to = 16,
        ImmutableArray<string> settlements = default, ImmutableArray<string> people = default) =>
        new(id, "A Day", Region, when, from, to, new FestivalSite(434_000, 744_000, 180),
            settlements.IsDefault ? ImmutableArray<string>.Empty : settlements,
            people.IsDefault ? ImmutableArray<string>.Empty : people, gathers);

    // ── the date rules ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheHollowDay_KeepsOnTheDayNoTallyOwns_AndOnNoOther()
    {
        var hollow = FestivalWhen.Hollow;
        Assert.True(hollow.Matches(Calendar.Of(DayStart(182), Clock, Reckoning)));
        Assert.False(hollow.Matches(Date(181)));
        Assert.False(hollow.Matches(Date(183)));
        Assert.False(hollow.Matches(Date(0)));
    }

    [Fact]
    public void ANamedDay_KeepsOnThatTallyAndDay_OfEveryReckoning()
    {
        var when = FestivalWhen.On(3, 14);
        Assert.True(when.Matches(Date(41)));     // the 14th day of the 3rd tally, 1st Reckoning
        Assert.True(when.Matches(Date(41 + 183)));
        Assert.False(when.Matches(Date(40)));
        Assert.False(when.Matches(Date(41 + 14)));
        Assert.Equal("day 14 of tally 3", when.Describe());
    }

    [Fact]
    public void ADayOfEveryTally_KeepsThirteenTimesAReckoning_AndNeverOnTheHollowDay()
    {
        var when = FestivalWhen.EveryTallyDay(1);
        var days = Enumerable.Range(0, 183).Where(d => when.Matches(Date(d))).ToList();
        Assert.Equal(13, days.Count);
        Assert.Equal(new[] { 0, 14, 28, 42, 56, 70, 84, 98, 112, 126, 140, 154, 168 }, days);
        Assert.False(when.Matches(Calendar.Of(DayStart(182), Clock, Reckoning)));
    }

    [Fact]
    public void AWindowOfTallies_KeepsEveryDayOfThem_AndNotTheHollowDay()
    {
        var when = FestivalWhen.Tallies(3, 4);
        Assert.True(when.Matches(Date(28)));       // day 1 of tally 3
        Assert.True(when.Matches(Date(55)));       // day 14 of tally 4
        Assert.False(when.Matches(Date(27)));      // day 14 of tally 2
        Assert.False(when.Matches(Date(56)));      // day 1 of tally 5
        Assert.False(when.Matches(Calendar.Of(DayStart(182), Clock, Reckoning)));
        Assert.Equal("tallies 3-4", when.Describe());
    }

    [Fact]
    public void ADateTheCalendarCannotKeep_IsRefusedWithTheReason()
    {
        var fourteenTallies = new FestivalWhen(FestivalDates.OneDay, Tally: 14, Day: 1);
        Assert.False(fourteenTallies.ExistsIn(Reckoning));
        var fifteenthDay = new FestivalWhen(FestivalDates.OneDay, Tally: 1, Day: 15);
        Assert.False(fifteenthDay.ExistsIn(Reckoning));
        var noHollowDays = new CalendarRules(14, 13, 0);
        Assert.False(FestivalWhen.Hollow.ExistsIn(noHollowDays));
        var throws = Assert.Throws<FormatException>(() => Festival("festival.test.bad", fifteenthDay).Validated(Reckoning));
        Assert.Contains("no day for", throws.Message);
    }

    // ── what a festival must be ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AFestival_NeedsAnIdANameARegionAndRealHours()
    {
        var day = FestivalWhen.On(1, 1);
        Assert.Throws<FormatException>(() => Festival("hollow_day", day).Validated(Reckoning));
        Assert.Throws<FormatException>(() => new FestivalDefinition("festival.test", "", Region, day, 10, 16,
            new FestivalSite(0, 0, 0), ImmutableArray<string>.Empty, ImmutableArray<string>.Empty, true).Validated(Reckoning));
        Assert.Throws<FormatException>(() => Festival("festival.test", day, from: 16, to: 16).Validated(Reckoning));
        Assert.Throws<FormatException>(() => Festival("festival.test", day, from: 24).Validated(Reckoning));
        Festival("festival.test", day).Validated(Reckoning);
    }

    [Fact]
    public void TheHoursMayCrossMidnight_AndTheSiteIsFaced()
    {
        var overnight = Festival("festival.test.night", FestivalWhen.On(1, 1), from: 22, to: 4);
        Assert.True(overnight.KeepAt(22));
        Assert.True(overnight.KeepAt(0));
        Assert.True(overnight.KeepAt(3));
        Assert.False(overnight.KeepAt(4));
        Assert.False(overnight.KeepAt(21));
        Assert.Throws<FormatException>(() => new FestivalDefinition("festival.test", "A Day", Region, FestivalWhen.On(1, 1), 10, 16,
            new FestivalSite(0, 0, 400), ImmutableArray<string>.Empty, ImmutableArray<string>.Empty, true).Validated(Reckoning));
    }

    [Fact]
    public void WhoseDayItIs_IsSettledBySettlementAndPeople_AndNoNamesMeansEveryone()
    {
        var everyone = Festival("festival.test.all", FestivalWhen.On(1, 1));
        Assert.True(everyone.KeptBy("ashen_hollow", "people.veth"));
        Assert.True(everyone.KeptBy("cairnwold", "people.kal"));

        var vethOnly = Festival("festival.test.veth", FestivalWhen.On(1, 1), people: ImmutableArray.Create("people.veth"));
        Assert.True(vethOnly.KeptBy("ashen_hollow", "people.veth"));
        Assert.False(vethOnly.KeptBy("ashen_hollow", "people.kal"));
        Assert.False(vethOnly.KeptBy("ashen_hollow", null));

        var townOnly = Festival("festival.test.town", FestivalWhen.On(1, 1), settlements: ImmutableArray.Create("ashen_hollow"));
        Assert.True(townOnly.KeptBy("ashen_hollow", "people.kal"));
        Assert.False(townOnly.KeptBy("cairnwold", "people.veth"));
    }

    // ── the world's cap and what is on ──────────────────────────────────────────────────────────

    [Fact]
    public void Wld89_EightWorldEventsAreKeptInARegionAtOnce_AndTheNinthWaits()
    {
        var nine = Enumerable.Range(1, 9)
            .Select(i => Festival($"festival.test.{i}", FestivalWhen.On(1, 1), from: 9 + i, to: 10 + i))
            .ToImmutableArray();
        var capped = new FestivalRules(nine, FestivalRules.CanonMaxConcurrent).Validated(Reckoning);
        Assert.Equal(8, Festivals.Observed(capped, Region, Date(0)).Length);
        Assert.Equal(Enumerable.Range(1, 8).Select(i => $"festival.test.{i}"), Festivals.Observed(capped, Region, Date(0)).Select(f => f.Id));

        // A cap above the canon's is refused, and so is a duplicate id.
        Assert.Throws<FormatException>(() => new FestivalRules(nine, 9).Validated(Reckoning));
        Assert.Throws<FormatException>(() => new FestivalRules(ImmutableArray.Create(nine[0], nine[0]), 8).Validated(Reckoning));
    }

    [Fact]
    public void OnlyTheFestivalsOfTheRegionPlayed_AndOnlyOnTheirDay()
    {
        var rules = new FestivalRules(ImmutableArray.Create(
            Festival("festival.frontier.hollow", FestivalWhen.Hollow),
            Festival("festival.frontier.turning", FestivalWhen.EveryTallyDay(1)),
            new FestivalDefinition("festival.elsewhere.turning", "Elsewhere", "region.cairnwold", FestivalWhen.On(1, 1), 10, 16,
                new FestivalSite(0, 0, 0), ImmutableArray<string>.Empty, ImmutableArray<string>.Empty, true)), 8).Validated(Reckoning);

        Assert.Equal(new[] { "festival.frontier.turning" }, Festivals.Observed(rules, Region, Date(0)).Select(f => f.Id));
        Assert.Equal(new[] { "festival.frontier.hollow" }, Festivals.Observed(rules, Region, Calendar.Of(DayStart(182), Clock, Reckoning)).Select(f => f.Id));
        Assert.Empty(Festivals.Observed(rules, Region, Date(1)));
        Assert.Empty(Festivals.Observed(rules, "region.cairnwold", Date(1)));
    }

    [Fact]
    public void TheTownGathers_OnlyInTheBandsHours_AndOnlyForItsOwnPeople()
    {
        var rules = new FestivalRules(ImmutableArray.Create(
            Festival("festival.frontier.hollow", FestivalWhen.Hollow, settlements: ImmutableArray.Create("ashen_hollow"))), 8).Validated(Reckoning);
        var hollow = Calendar.Of(DayStart(182), Clock, Reckoning);

        var atNoon = Festivals.Gathering(rules, Region, hollow with { Hour = 12 }, "ashen_hollow", "people.veth");
        Assert.NotNull(atNoon);
        Assert.True(atNoon!.Gathering);
        Assert.Equal((434_000L, 744_000L), (atNoon.SiteXMm, atNoon.SiteZMm));

        // Before the band opens and after it closes, the day is kept but nobody is gathered at it.
        Assert.False(Festivals.Gathering(rules, Region, hollow with { Hour = 9 }, "ashen_hollow", "people.veth")?.Gathering ?? false);
        Assert.Null(Festivals.Gathering(rules, Region, hollow with { Hour = 17 }, "ashen_hollow", "people.veth"));
        // Another settlement keeps nothing of it.
        Assert.Null(Festivals.Gathering(rules, Region, hollow with { Hour = 12 }, "cairnwold", "people.veth"));
    }
}
