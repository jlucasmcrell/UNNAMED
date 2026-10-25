using System.Collections.Immutable;
using UNNAMED.Content;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP 1H: the World Event Director (QST-48, QST-55..58, WLD-89). A seeded controlled-random scheduler settled hour by hour on the world
/// clock's absolute hours, so an absence caught up at once, in pieces, or lived tick by tick is the same world, and the same notices.
/// </summary>
public class DirectorTests
{
    private const string Region = "region.test";
    private static readonly WorldClock Clock = WorldClock.Default;

    private static WorldEventDefinition Event(string id, int chancePermille = 1000, int minHours = 2, int maxHours = 6, int cooldownHours = 0,
        long minWorldDay = 0, params string[] channels) =>
        new(id, WorldEventScale.Local, ImmutableArray<string>.Empty, minWorldDay, chancePermille, minHours, maxHours, cooldownHours,
            channels.Length == 0 ? ImmutableArray.Create(WorldEventChannels.AwayLog) : channels.ToImmutableArray(),
            ImmutableArray.Create(new WorldEventOutcome("a", 2), new WorldEventOutcome("b", 1)));

    private static DirectorSetup Setup(params WorldEventDefinition[] events) => new(events.ToImmutableArray(), 8, 64, 48, 0);

    private static (DirectorState State, List<WorldEventHappening> Notices) Settle(DirectorSetup setup, ulong seed, params long[] hours)
    {
        var notices = new List<WorldEventHappening>();
        var state = DirectorEngine.Start(0);
        foreach (long hour in hours)
            state = DirectorEngine.Settle(state, setup, seed, Region, Clock, hour, notices.Add);
        return (state, notices);
    }

    /// <summary>A notice as text: its channel list compared by value.</summary>
    private static IEnumerable<string> Told(IEnumerable<WorldEventHappening> notices) =>
        notices.Select(n => $"{n.EventId}|{n.DefId}|{n.Region}|{n.Phase}|{string.Join(",", n.Channels)}|{n.Outcome}|{n.Tick}");

    private static string Digest(DirectorState state)
    {
        using var h = new UNNAMED.Domain.CanonicalHasher();
        state.AddTo(h);
        return h.Finish();
    }

    [Fact]
    public void ASpanSettledAtOnce_InHours_OrInRandomPieces_IsTheSameHistory_AndTheSameNotices()
    {
        var setup = Setup(Event("event.a", 120, 3, 9, 12), Event("event.b", 40, 12, 30, 48), Event("event.c", 300, 1, 2, 0)) with
        {
            IntensityDampingPermille = 150,
        };
        const long span = 183 * 24;
        var whole = Settle(setup, 77, span);
        var hourly = Settle(setup, 77, Enumerable.Range(1, (int)span).Select(h => (long)h).ToArray());
        var random = new Random(5);
        var pieces = new List<long>();
        for (long at = 0; at < span;)
            pieces.Add(at = Math.Min(span, at + random.Next(0, 200)));
        var mixed = Settle(setup, 77, pieces.ToArray());

        Assert.Equal(Digest(whole.State), Digest(hourly.State));
        Assert.Equal(Digest(whole.State), Digest(mixed.State));
        Assert.Equal(Told(whole.Notices), Told(hourly.Notices));
        Assert.Equal(Told(whole.Notices), Told(mixed.Notices));
        Assert.NotEmpty(whole.State.History);
        Assert.Equal(span, whole.State.SettledHour);
    }

    [Fact]
    public void TheSeedDecides_TheSameSeedTheSameWorld_AnotherSeedAnother()
    {
        var setup = Setup(Event("event.a", 100, 2, 8, 6), Event("event.b", 100, 2, 8, 6));
        Assert.Equal(Digest(Settle(setup, 1, 2_000).State), Digest(Settle(setup, 1, 2_000).State));
        Assert.NotEqual(Digest(Settle(setup, 1, 2_000).State), Digest(Settle(setup, 2, 2_000).State));
    }

    [Fact]
    public void NoMoreThanTheCapRun_InOneRegion_AtOnce()
    {
        var setup = Setup(Enumerable.Range(0, 12).Select(i => Event($"event.e{i:00}", 1000, 20, 30)).ToArray());
        var notices = new List<WorldEventHappening>();
        var state = DirectorEngine.Start(0);
        int most = 0;
        for (long hour = 1; hour <= 400; hour++)
        {
            state = DirectorEngine.Settle(state, setup, 9, Region, Clock, hour, notices.Add);
            most = Math.Max(most, state.Active.Length);
        }
        Assert.Equal(8, most);
        // Content cannot raise the cap past WLD-89's eight.
        Assert.Equal(8, DirectorContent.MostPerRegion);
    }

    [Fact]
    public void AKind_WaitsOutItsCooldown_AndDoesNotRunTwiceAtOnce()
    {
        var setup = Setup(Event("event.a", 1000, 3, 3, cooldownHours: 10));
        var (state, notices) = Settle(setup, 3, 100);
        var starts = notices.Where(n => n.Phase == WorldEventPhase.Began).Select(n => n.Tick).ToList();
        Assert.True(starts.Count >= 8);
        Assert.All(starts.Zip(starts.Skip(1)), pair => Assert.Equal(10 * Clock.TicksPerDay / 24, pair.Second - pair.First));
        Assert.True(state.Active.Length <= 1);
    }

    [Fact]
    public void ANewWorld_WaitsForItsMinimumAge()
    {
        var setup = Setup(Event("event.a", 1000, 1, 1, minWorldDay: 2));
        var (state, notices) = Settle(setup, 3, 24 * 2 - 1);
        Assert.Empty(notices);
        Assert.Empty(state.Active);
        Assert.NotEmpty(Settle(setup, 3, 24 * 2).Notices);
    }

    [Fact]
    public void RecentIntensity_DampsTheOdds()
    {
        var events = Enumerable.Range(0, 6).Select(i => Event($"event.e{i}", 200, 4, 10, 12)).ToArray();
        int calm = Settle(Setup(events), 21, 2_000).State.History.Length;
        int damped = Settle(Setup(events) with { IntensityDampingPermille = 200, HistoryKept = 10_000 }, 21, 2_000).State.History.Length;
        int undamped = Settle(Setup(events) with { HistoryKept = 10_000 }, 21, 2_000).State.History.Length;
        Assert.Equal(64, calm);   // the history keeps only what config says
        Assert.True(damped < undamped * 3 / 4, $"damped {damped} against {undamped}");
    }

    [Fact]
    public void AnEnd_IsRecordedWithItsSeededOutcome_AndAnnouncedOnTheEventsChannels()
    {
        var setup = Setup(Event("event.a", 1000, 2, 2, 0, 0, WorldEventChannels.NoticeBoard, WorldEventChannels.AwayLog));
        var (state, notices) = Settle(setup, 4, 50);
        var record = state.History.First();
        var ended = notices.First(n => n.Phase == WorldEventPhase.Ended);
        Assert.Equal((record.Id, record.Outcome, record.EndHour - record.StartHour), (ended.EventId, ended.Outcome!, 2L));
        Assert.Contains(record.Outcome, new[] { "a", "b" });
        Assert.Equal(new[] { WorldEventChannels.NoticeBoard, WorldEventChannels.AwayLog }, ended.Channels.ToArray());
        Assert.Equal(UNNAMED.Domain.EntityKind.WorldEvent, record.Id.Kind);
        Assert.Equal(state.History.Length, state.History.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void TheGamesOwnDirector_IsCappedAtEight_WithTwoProofEvents_AndRefusesWhatItCannotAnnounce()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var director = session.Setup.Director;
        Assert.Equal(8, director.MaxActivePerRegion);
        Assert.Equal(new[] { "event.travelling_trader", "event.creature_migration" }, director.Events.Select(e => e.Id));
        Assert.All(director.Events, e => Assert.Contains(WorldEventChannels.AwayLog, e.Channels));

        string root = Path.Combine(profile.Root, "content");
        Copy(Path.Combine(Harness.RepoRoot(), "content"), root);
        string path = Path.Combine(root, "config", "director.yaml");
        string yaml = File.ReadAllText(path);
        File.WriteAllText(path, yaml.Replace("channels: [rumour, away_log]", "channels: [rumour, carrier_pigeon]"));
        var loader = new ContentLoader();
        loader.LoadAll(root);
        Assert.Contains(loader.Errors, e => e.Code == "DIR001" && e.Message.Contains("carrier_pigeon", StringComparison.Ordinal));

        File.WriteAllText(path, yaml.Replace("max_active_per_region: 8", "max_active_per_region: 9"));
        loader = new ContentLoader();
        loader.LoadAll(root);
        Assert.Contains(loader.Errors, e => e.Code == "DIR001" && e.Message.Contains("WLD-89", StringComparison.Ordinal));
    }

    private static void Copy(string from, string to)
    {
        foreach (string directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
    }

    /// <summary>A session's rules with events that happen at once and end within the hour after, so a short run sees several.</summary>
    private static SimulationSetup Busy(SimulationSetup setup) => setup with
    {
        Director = Setup(Event("event.travelling_trader", 1000, 1, 2, 2, 0, WorldEventChannels.Chat, WorldEventChannels.AwayLog),
            Event("event.creature_migration", 500, 2, 3, 4, 0, WorldEventChannels.Rumour)),
    };

    [Fact]
    public void AnAbsenceCaughtUp_IsTheHoursLived_EventForEvent()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var setup = Busy(session.Setup);
        Arena World() => Arena.OpenCreatures(session, setup, (120, 60), 0, Array.Empty<(string, double, double, string)>(), seed: 1234);
        var livedArena = World();
        var awayArena = World();
        var (lived, away) = (livedArena.Simulation, awayArena.Simulation);
        var livedNotices = livedArena.Record<WorldEventNotice>();
        var awayNotices = awayArena.Record<WorldEventNotice>();

        long ticks = 6 * Clock.TicksPerDay / 24;   // six game hours
        for (long i = 0; i < ticks; i++)
            lived.Step();
        var log = away.Advance(ticks);

        Assert.Equal(lived.WorldTick, away.WorldTick);
        Assert.Equal(Digest(lived.World.Director!), Digest(away.World.Director!));
        Assert.Equal(livedNotices.Select(n => n with { Channels = default }), awayNotices.Select(n => n with { Channels = default }));
        Assert.Equal(livedNotices.Select(n => string.Join(",", n.Channels)), awayNotices.Select(n => string.Join(",", n.Channels)));
        Assert.NotEmpty(awayNotices);
        Assert.Equal(lived.WorldEvents.ToArray(), away.WorldEvents.ToArray());
        Assert.Equal(lived.WorldHistory.ToArray(), away.WorldHistory.ToArray());
        Assert.NotEmpty(lived.WorldHistory);

        // The away log has only what names it: the trader, not the migration's rumour.
        var lines = log.Lines.Where(l => l.Kind is DirectorAdvanceKinds.Began or DirectorAdvanceKinds.Ended).ToList();
        Assert.NotEmpty(lines);
        Assert.All(lines, l => Assert.Equal("event.travelling_trader", l.Ref));
        var told = OfflineWorldEvents.From(log.Lines);
        Assert.Equal(awayNotices.Where(n => n.DefId == "event.travelling_trader").Select(n => n.EventId.Value).Distinct(), told.Select(t => t.EventId));
        var whole = told.First(t => t.EndedTick is not null);
        Assert.True(whole.BeganTick < whole.EndedTick);
        Assert.Contains(whole.Outcome, new[] { "a", "b" });
    }

    [Fact]
    public void ASavedWorld_KeepsItsEventsAndHistory_AndGoesOnAsTheUnsavedOneDoes()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var setup = Busy(session.Setup);
        var arena = Arena.OpenCreatures(session, setup, (120, 60), 0, Array.Empty<(string, double, double, string)>(), seed: 99);
        arena.Simulation.Advance(5 * Clock.TicksPerDay / 24);
        Assert.NotEmpty(arena.Simulation.WorldHistory);
        var store = new UNNAMED.Persistence.SaveStore(profile.Root);
        store.Save(UNNAMED.Persistence.SaveSlots.Manual("events"), UNNAMED.Persistence.SaveDocuments.Capture(arena.Simulation.World,
            arena.Simulation.CaptureRecord(), session.Content, arena.Simulation.WorldTick, 0));
        var loaded = store.Load(UNNAMED.Persistence.SaveSlots.Manual("events"),
            new UNNAMED.Persistence.LoadContext(session.Generator, session.Content, new UNNAMED.EntityRegistry.EntityRegistry()));
        Assert.True(loaded.IsComplete);
        var again = Arena.Resume(setup, loaded).Simulation;
        Assert.Equal(Digest(arena.Simulation.World.Director!), Digest(again.World.Director!));

        arena.Simulation.Advance(9 * Clock.TicksPerDay / 24);
        again.Advance(9 * Clock.TicksPerDay / 24);
        Assert.Equal(arena.Simulation.WorldHistory.ToArray(), again.WorldHistory.ToArray());
        Assert.Equal(Digest(arena.Simulation.World.Director!), Digest(again.World.Director!));
    }

    [Fact]
    public void TheAwaySheet_TellsAnEventThatBeganBeforeTheAbsence_AsEndedOnly()
    {
        var lines = new[]
        {
            new AdvanceLine(100, DirectorAdvanceKinds.Ended, "event.a") { SubjectId = "evt_1", PlaceId = Region, Outcome = "a" },
            new AdvanceLine(150, DirectorAdvanceKinds.Began, "event.b") { SubjectId = "evt_2", PlaceId = Region },
            new AdvanceLine(160, AdvanceKinds.TimePassed, string.Empty, 60),
        };
        Assert.Equal(new[]
        {
            new AwayWorldEvent("evt_1", "event.a", Region, null, 100, "a"),
            new AwayWorldEvent("evt_2", "event.b", Region, 150, null, null),
        }, OfflineWorldEvents.From(lines));
    }
}
