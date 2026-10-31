using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WLD-58, WLD-89 (SYSTEMS_CHECKLIST R13) in the running world: the calendar's dates reaching the living world as festivals - the
/// Hollow Day on the day no tally owns, the tally's turning every fourteen days, and the bloom window over tallies 3 and 4 - with the
/// town walking to the site while the hours are on, the day announced once, and an absence landing on the same day whatever its pieces.
/// </summary>
public class FestivalWorldTests
{
    private const long Day = 57_600;
    private const string Region = "region.starting_frontier";
    private const string Hollow = "festival.frontier.hollow_day";
    private const string Turning = "festival.frontier.tally_turning";
    private const string Bloom = "festival.frontier.bloom_window";

    private static (GameSession Session, Simulation Simulation) NewWorld(TempProfile profile)
    {
        var session = Harness.Boot(profile);
        session.NewGame(Harness.Named("Wanderer"), seed: 7);
        return (session, session.Simulation!);
    }

    /// <summary>The first tick of an hour of a calendar day (day 0 is the world's first day).</summary>
    private static long At(Simulation simulation, long day, int hour) =>
        simulation.Setup.Clock.TickOfDay(day) + hour * simulation.Setup.Clock.TicksPerDay / 24;

    /// <summary>How far the nearest person of a settlement stands from a point, in millimetres; null when the town has nobody.</summary>
    private static long? NearestOf(Simulation simulation, string settlement, long xMm, long zMm)
    {
        var keys = simulation.Lives.Where(l => l.Settlement == settlement).Select(l => l.Key).ToHashSet(StringComparer.Ordinal);
        var near = simulation.Npcs.Where(n => keys.Contains(n.Id)).Select(n =>
        {
            long dx = n.Body.XMm - xMm, dz = n.Body.ZMm - zMm;
            return (long)Math.Sqrt(dx * dx + dz * dz);
        }).ToList();
        return near.Count == 0 ? null : near.Min();
    }

    [Fact]
    public void TheWorldsContent_KeepsTheHollowDay_ATallysTurning_AndABloomWindow()
    {
        using var profile = new TempProfile();
        var (_, simulation) = NewWorld(profile);
        var rules = simulation.Setup.Festivals;
        Assert.Equal(new[] { Bloom, Hollow, Turning }, rules.Of(Region).Select(f => f.Id));
        Assert.Equal(FestivalRules.CanonMaxConcurrent, rules.MaxConcurrent);
    }

    [Fact]
    public void TheHollowDay_KeepsOnTheDayNoTallyOwns_AndGathersAtNoon()
    {
        using var profile = new TempProfile();
        var (_, simulation) = NewWorld(profile);
        var clock = simulation.Setup.Clock;
        var calendar = simulation.Setup.Calendar;

        // The day before it is the 14th day of the 13th tally; the Hollow Day belongs to no tally; the day after opens a Reckoning.
        var eve = Calendar.OfDay(181, 12 * 60, calendar);
        Assert.Equal((1L, 13, 14), (eve.Reckoning, eve.Tally, eve.Day));
        Assert.True(Calendar.OfDay(182, 12 * 60, calendar).IsHollowDay);
        Assert.Equal((2L, 1, 1), (Calendar.OfDay(183, 12 * 60, calendar).Reckoning, 1, 1));

        // At noon the world keeps it and the hollow's people are gathered at its site.
        simulation.Advance(At(simulation, 182, 12) - simulation.WorldTick);
        var festival = simulation.Festival;
        Assert.NotNull(festival);
        Assert.Equal(Hollow, festival!.Id);
        Assert.True(festival.Gathering);
        Assert.Equal((434_000L, 744_000L, 180), (festival.SiteXMm, festival.SiteZMm, festival.SiteFacingMdeg));
        Assert.NotNull(simulation.FestivalOf("ashen_hollow", "people.veth"));
        Assert.Null(simulation.FestivalOf("cairnwold", "people.veth"));

        // Late that evening the day is still kept, but the band has closed: the town is not gathered at it.
        simulation.Advance(At(simulation, 182, 22) - simulation.WorldTick);
        Assert.Equal(Hollow, simulation.Festival?.Id);
        Assert.False(simulation.Festival!.Gathering);
        Assert.Null(simulation.FestivalOf("ashen_hollow", "people.veth"));
    }

    [Fact]
    public void TheTownWalksToTheFestivalSite_WhileTheHoursAreOn_AndNotOnAnOrdinaryDay()
    {
        using var profile = new TempProfile();
        const long siteX = 434_000, siteZ = 744_000;

        // The control: an ordinary day at the same hour, walked for as long. The yard is nobody's work.
        var (controlSession, control) = NewWorld(profile);
        control.Advance(At(control, 1, 10) - control.WorldTick);
        Assert.Null(control.Festival);
        Harness.Ticks(controlSession, 2_400);
        var ordinary = NearestOf(control, "ashen_hollow", siteX, siteZ);
        using (controlSession)
        {
            Assert.NotNull(ordinary);
            Assert.True(ordinary > 10_000, $"on an ordinary day nobody keeps the yard: nearest was {ordinary / 1000.0:F1} m");
        }

        // The festival day: the hollow walks to the yard while its band is on.
        var (festivalSession, festival) = NewWorld(profile);
        festival.Advance(At(festival, 182, 10) - festival.WorldTick);
        Harness.Ticks(festivalSession, 2_400);
        using (festivalSession)
        {
            Assert.Equal(Hollow, festival.Festival?.Id);
            var gathered = NearestOf(festival, "ashen_hollow", siteX, siteZ);
            Assert.NotNull(gathered);
            Assert.True(gathered < ordinary,
                $"the hollow came to the yard: nearest was {gathered / 1000.0:F1} m, against {ordinary / 1000.0:F1} m on an ordinary day");
        }
    }

    [Fact]
    public void TheDayIsAnnouncedOnce_WhenItBegins()
    {
        using var profile = new TempProfile();
        var (session, simulation) = NewWorld(profile);
        var began = Harness.Record<FestivalBegan>(session);
        var ended = Harness.Record<FestivalEnded>(session);

        // Arrive just before the day begins, then tick across its midnight: the day is announced once, when it begins.
        simulation.Advance(At(simulation, 182, 0) - simulation.WorldTick - 2);
        Harness.Ticks(session, 4);
        var announced = Assert.Single(began);
        Assert.Equal(Hollow, announced.Festival.Id);
        Assert.False(announced.Festival.Gathering);   // midnight: the day has begun, its hours have not
        Harness.Ticks(session, 40);
        Assert.Single(began);
        Assert.Empty(ended);

        // The next midnight, crossed on a live tick, ends it. (An absence does not raise events: it writes lines in its log.)
        simulation.Advance(At(simulation, 183, 0) - simulation.WorldTick - 2);
        Harness.Ticks(session, 4);
        Assert.Contains(ended, e => e.FestivalId == Hollow);
        // The next day is the first day of the next Reckoning's first tally, which is a turning of its own.
        Assert.Equal(Turning, simulation.Festival?.Id);
    }

    [Fact]
    public void TheTallysTurning_KeepsOnTheFirstDayOfEveryTally_AndNotElsewhere()
    {
        using var profile = new TempProfile();
        var (_, simulation) = NewWorld(profile);

        // Day 1 of the 2nd tally is the tally's turning; the day before is the 1st tally's last day.
        simulation.Advance(At(simulation, 13, 10) - simulation.WorldTick);
        Assert.NotEqual(Turning, simulation.Festival?.Id);
        simulation.Advance(At(simulation, 14, 10) - simulation.WorldTick);
        Assert.Equal(Turning, simulation.Festival?.Id);
        Assert.NotNull(simulation.FestivalOf("ashen_hollow", "people.veth"));

        // The bloom window keeps all of tallies 3 and 4, so a turning inside it shares the day with it.
        simulation.Advance(At(simulation, 28, 14) - simulation.WorldTick);
        Assert.Equal(new[] { Bloom, Turning }, simulation.Festivals.Select(f => f.Id));
        simulation.Advance(At(simulation, 29, 14) - simulation.WorldTick);
        Assert.Equal(new[] { Bloom }, simulation.Festivals.Select(f => f.Id));
        Assert.Equal((465_500L, 750_000L, 90), (simulation.Festival!.SiteXMm, simulation.Festival.SiteZMm, simulation.Festival.SiteFacingMdeg));
    }

    [Fact]
    public void T22_AnAbsence_InAnyPieces_LandsOnTheSameFestival_AndTheLogSaysWhich()
    {
        using var profile = new TempProfile();
        long absence = 37 * Day + 4_321;

        // An absence in pieces writes a "now" line per piece, so what must agree is the tally of days passed per festival, and
        // the festival named at the very end.
        string Passed(IEnumerable<AdvanceLine> lines) =>
            string.Join(";", lines.Where(l => l.Kind == AdvanceKinds.FestivalPassed)
                .GroupBy(l => l.Ref, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key}:{g.Sum(l => l.Amount)}"));

        string? Now(IEnumerable<AdvanceLine> lines) =>
            lines.LastOrDefault(l => l.Kind == AdvanceKinds.FestivalNow)?.Ref;

        var (onceSession, once) = NewWorld(profile);
        Harness.Ticks(onceSession, 1);
        var onceLog = once.Advance(absence);
        var onceFestival = once.Festival?.Id;
        using (onceSession)
        {
            foreach (var pieces in new[]
                     {
                         new[] { 1L, absence - 1 },
                         new[] { Day / 3, 0, 4 * Day, absence - 4 * Day - Day / 3 },
                         Enumerable.Repeat(absence / 7, 7).Append(absence % 7).ToArray(),
                     })
            {
                var (session, simulation) = NewWorld(profile);
                Harness.Ticks(session, 1);
                var lines = new List<AdvanceLine>();
                foreach (long piece in pieces)
                    lines.AddRange(simulation.Advance(piece).Lines);
                using (session)
                {
                    Assert.Equal(Passed(onceLog.Lines), Passed(lines));
                    Assert.Equal(Now(onceLog.Lines), Now(lines));
                    Assert.Equal(onceFestival, simulation.Festival?.Id);
                }
            }
            Assert.Contains(onceLog.Lines, l => l.Kind == AdvanceKinds.FestivalNow);
            Assert.Contains(onceLog.Lines, l => l.Kind == AdvanceKinds.FestivalPassed && l.Ref == Turning);
        }
    }
}
