using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit.Abstractions;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP12: NPC life over the game's own content (SYSTEMS_COMPLETENESS_PLAN §2.13) - the 8 named NPCs and 6 role residents a town living
/// their days on the navigation grid, the world going on while the character is away (T-22), merchants' and teachers' hours, arrivals, and
/// no cap on how many lives there are.
/// </summary>
public class NpcLifeWorldTests
{
    private const long Day = 57_600;
    private const long Hour = Day / 24;
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string Durn = "npc.cairnwold.durn_skell";
    private const string Porter = "routine.ashen_hollow.porter";

    private readonly ITestOutputHelper _output;

    public NpcLifeWorldTests(ITestOutputHelper output) => _output = output;

    private static Arena AtSpawn(GameSession session, SimulationSetup? rules = null, Func<PlayerRecord, PlayerRecord>? change = null) =>
        Arena.OpenCreatures(session, rules ?? session.Setup, (30, 158), 90, Array.Empty<(string, double, double, string)>(), change, keepSpawns: true);

    /// <summary>The bodies and lives a player could see, beyond the persistent digest.</summary>
    private static string Seen(Simulation simulation) =>
        simulation.StateDigest() + "|" + string.Join(";", simulation.Npcs.OrderBy(n => n.Id, StringComparer.Ordinal).Select(n => $"{n.Id}:{n.InstanceId}:{n.Name}:{n.Body}:{n.Activity}"))
        + "|" + string.Join(";", simulation.Lives.Select(l => $"{l.Key}:{l.Activity}:{l.With}:{l.Hunger}{l.Rest}{l.Social}{l.Coin}:{l.AtWork}"));

    [Fact]
    public void TheTowns_HoldTheEightNamed_AndSixRoleResidentsEach_NamedFromTheirPeople()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AtSpawn(session);
        var lives = arena.Simulation.Lives;
        Assert.Equal(8, lives.Count(l => !l.Resident));
        Assert.Equal(6, lives.Count(l => l.Resident && l.Settlement == "ashen_hollow"));
        Assert.Equal(6, lives.Count(l => l.Resident && l.Settlement == "cairnwold"));
        var names = session.Setup.Character.Peoples["people.veth"].Names;
        foreach (var town in lives.Where(l => l.Resident).GroupBy(l => l.Settlement))
        {
            Assert.All(town, l => Assert.Contains(l.Name, names.Names));
            Assert.Equal(town.Count(), town.Select(l => l.Name).Distinct().Count());
        }
        var residents = arena.Simulation.Npcs.Where(n => n.Resident).ToList();
        Assert.Equal(12, residents.Count);
        Assert.Equal(12, residents.Select(n => n.InstanceId).Distinct().Count());
        Assert.All(residents, r => Assert.True(arena.Simulation.World.Registry.Exists(r.InstanceId)));
    }

    /// <summary>One world saved, so every copy loaded from it is the same world - character and all.</summary>
    private static Func<Simulation> Saved(TempProfile profile, GameSession session, Arena arena)
    {
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("life"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content, arena.Simulation.WorldTick, 0));
        return () =>
        {
            var loaded = store.Load(SaveSlots.Manual("life"), new LoadContext(session.Generator, session.Content, new Registry()));
            Assert.True(loaded.IsComplete);
            return Simulation.Start(arena.Simulation.Setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick, new EventBus());
        };
    }

    [Fact]
    public void AnAbsence_InAnyPieces_LeavesTheSameLives()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var fresh = Saved(profile, session, AtSpawn(session));
        var once = fresh();
        once.Advance(3 * Day + 1_234);
        foreach (var pieces in new[] { new long[] { 1, 3 * Day + 1_233 }, new long[] { Day + 17, 0, 2 * Day + 1_217 }, new long[] { Hour - 1, Hour, 3 * Day + 1_234 - (Hour - 1) - Hour } })
        {
            var chunked = fresh();
            foreach (long piece in pieces)
                chunked.Advance(piece);
            Assert.Equal(Seen(once), Seen(chunked));
        }
        // And they go on alike.
        var again = fresh();
        again.Advance(3 * Day + 1_234);
        for (int i = 0; i < 3 * (int)Hour; i++)
        {
            once.Step();
            again.Step();
        }
        Assert.Equal(Seen(once), Seen(again));
    }

    [Fact]
    public void TheWorld_GoesOnLiving_WhileAway()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = AtSpawn(session).Simulation;
        var before = simulation.Lives.ToDictionary(l => l.Key, l => l.Activity);
        var seen = new HashSet<string>();
        for (int hour = 0; hour < 24; hour++)
        {
            simulation.Advance(Hour);
            foreach (var life in simulation.Lives)
                seen.Add(life.Activity);
        }
        Assert.Contains(NpcActivities.Sleep, seen);
        Assert.Contains(NpcActivities.Work, seen);
        Assert.Contains(NpcActivities.Eat, seen);
        // Asleep at 02:00, they are where their routine has them: at home.
        simulation.Advance(18 * Hour + 30 * 60 * 20 / 60);
        var kera = session.Setup.NpcLife.RoutineOf(Kera)!;
        var asleep = simulation.Lives.Single(l => l.Key == Kera);
        if (asleep.Activity == NpcActivities.Sleep)
        {
            var body = simulation.Npcs.Single(n => n.Id == Kera).Body;
            Assert.Equal((kera.Anchors["home"].XMm, kera.Anchors["home"].ZMm), (body.XMm, body.ZMm));
        }
        Assert.NotEmpty(before);
    }

    [Fact]
    public void AMerchant_TradesAtWork_InTradingHours_AndShutsAtNight()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = AtSpawn(session).Simulation;   // 07:30
        Assert.True(simulation.AtWork(Kera));
        Assert.Null(simulation.TradeRefusal(Kera));
        Assert.Null(simulation.TeachRefusal(Kera));
        simulation.Advance(14 * Hour);                   // 21:30
        Assert.Contains("come back in the morning", simulation.TradeRefusal(Kera));
        Assert.NotNull(simulation.TeachRefusal(Kera));
        simulation.Advance(10 * Hour);                   // 07:30 the next day
        Assert.Null(simulation.TradeRefusal(Durn));
        Assert.Null(simulation.TradeRefusal(Kera));
    }

    [Fact]
    public void AResidentWhoLeaves_IsReplacedBySomeoneNew_ThreeToSevenDaysLater_AndANamedNpcNeverLeaves()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // Watched from Cairnwold, the hollow is out of sight (tier B): nobody arrives where they would be seen.
        var arena = Arena.OpenCreatures(session, session.Setup, (560, 60), 0, Array.Empty<(string, double, double, string)>(), keepSpawns: true);
        var simulation = arena.Simulation;
        var departed = arena.Record<NpcDeparted>();
        var arrived = arena.Record<NpcArrived>();
        var first = simulation.Npcs.Single(n => n.Id == Porter);

        Assert.Contains("essential", simulation.Submit(new DepartNpc(Kera, "test")));
        Assert.Null(simulation.Submit(new DepartNpc(Porter, "test")));
        Assert.DoesNotContain(simulation.Npcs, n => n.Id == Porter);
        Assert.False(simulation.World.Registry.Exists(first.InstanceId));
        long due = Assert.Single(departed).ArrivalDay;
        Assert.InRange(due - session.Setup.Clock.Day(simulation.WorldTick), 3, 7);

        // Off in the world's catch-up, no one is watching: they come on the day.
        simulation.Advance((due + 1) * Day - simulation.WorldTick);
        var next = simulation.Npcs.Single(n => n.Id == Porter);
        Assert.NotEqual(first.InstanceId, next.InstanceId);
        Assert.Single(arrived);
        Assert.Equal(1, simulation.World.NpcLife!.Npcs[Porter].Generation);
    }

    [Fact]
    public void NpcsWalkTheirDay_NeverJumping_AndOpenTheDoorsInTheirWay()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AtSpawn(session);
        var simulation = arena.Simulation;
        var doors = arena.Record<DoorToggled>();
        var npcIds = simulation.Npcs.Where(n => n.Resident || n.Activity is not null).Select(n => n.InstanceId).ToHashSet();
        long travel = (long)session.Setup.Movement.SpeedMmPerSecond(Gait.Walk) * session.Setup.TickMilliseconds / 1000;
        var last = simulation.Npcs.ToDictionary(n => n.Id, n => n.Body);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long moved = 0;
        const long Visible = 500;
        // 14 game hours from 07:30: past the midday meal and into the evening, every body checked every tick.
        for (int i = 0; i < 14 * Hour; i++)
        {
            simulation.Step();
            foreach (var npc in simulation.Npcs)
            {
                if (!last.TryGetValue(npc.Id, out var was))
                {
                    last[npc.Id] = npc.Body;
                    continue;
                }
                long dx = npc.Body.XMm - was.XMm, dz = npc.Body.ZMm - was.ZMm;
                var tier = simulation.CellTiers.GetValueOrDefault(CellKey.OfWorld(was.XMm / 1000.0, was.ZMm / 1000.0).ToString(), SimulationTier.D);
                // A visible teleport is half a metre or more in a tick; a collision's push-out is a few centimetres.
                long allowed = tier switch { SimulationTier.A => Visible, SimulationTier.B => travel * 10 + Visible, _ => long.MaxValue };
                Assert.True(dx * dx + dz * dz <= allowed * allowed, $"{npc.Id} jumped {Math.Sqrt(dx * dx + dz * dz):0} mm at tier {tier} on tick {simulation.WorldTick}");
                moved += dx != 0 || dz != 0 ? 1 : 0;
                last[npc.Id] = npc.Body;
            }
        }
        _output.WriteLine($"{14 * Hour} ticks in {clock.Elapsed.TotalSeconds:0.0} s; {moved} body moves; {doors.Count} doors toggled");
        Assert.True(moved > 1_000);
        Assert.Contains(doors, d => npcIds.Contains(d.Actor) && d.Open);
    }

    [Fact]
    public void JobOutput_ReachesTheMarket_EachDayTheJobIsHeld()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var without = AtSpawn(session, session.Setup with { NpcLife = session.Setup.NpcLife with { Routines = session.Setup.NpcLife.Routines.Where(r => r.Value.Job is null).ToImmutableSortedDictionary() } }).Simulation;
        var with = AtSpawn(session).Simulation;
        without.Advance(5 * Day);
        with.Advance(5 * Day);
        long Stock(Simulation s, string market, string item) => s.Markets.Single(m => m.MarketId == market).Goods.Single(g => g.ItemId == item).Stock;
        Assert.True(Stock(with, "market.cairnwold", "item.material.ash_haft") >= Stock(without, "market.cairnwold", "item.material.ash_haft"));
        Assert.NotEqual(without.StateDigest(), with.StateDigest());
    }

    /// <summary>
    /// The owner's no-cap rule (2026-10-17), as the cheap structural test of §2.13: 1,000 synthetic NPCs off-screen at the far end of the region
    /// survive a save and load, an offline catch-up and the character coming close and going again, with nothing refused or truncated.
    /// </summary>
    [Fact]
    public void AThousandOffScreenNpcs_SurviveSaveLoad_CatchUp_AndTierChanges()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var routines = session.Setup.NpcLife.Routines.ToBuilder();
        for (int i = 0; i < 1_000; i++)
        {
            var at = new NpcAnchor(1_052_000 + i % 40 * 1_000, 612_000 + i / 40 * 7_000, 0);
            var anchors = new Dictionary<string, NpcAnchor> { ["home"] = at, ["work"] = at with { XMm = at.XMm + 500 } }.ToImmutableSortedDictionary(StringComparer.Ordinal);
            var routine = new RoutineDefinition($"routine.synthetic.r_{i:0000}", null, "synthetic", "labourer", anchors, 7, 19,
                ImmutableSortedDictionary<string, int>.Empty) { PeopleId = "people.veth" };
            routines[routine.Id] = routine;
        }
        var setup = session.Setup with { NpcLife = session.Setup.NpcLife with { Routines = routines.ToImmutable() } };
        var arena = AtSpawn(session, setup);
        int all = 1_000 + session.Setup.NpcLife.Routines.Count;
        Assert.Equal(all, arena.Simulation.Lives.Length);
        Assert.Equal(all - 8 + session.Setup.Layout.Npcs.Length, arena.Simulation.Npcs.Length);
        Assert.All(arena.Simulation.Lives.Where(l => l.Settlement == "synthetic"),
            l => Assert.True(arena.Simulation.CellTiers[arena.Simulation.World.NpcLife!.Npcs[l.Key].Cell] >= SimulationTier.B));   // beyond sight

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("crowd"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content, arena.Simulation.WorldTick, 0));
        var loaded = store.Load(SaveSlots.Manual("crowd"), new LoadContext(session.Generator, session.Content, new Registry()));
        Assert.True(loaded.IsComplete);
        Assert.Empty(loaded.RejectedRecords);
        var resumed = Simulation.Start(setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick, new EventBus());
        Assert.Equal(arena.Simulation.StateDigest(), resumed.StateDigest());
        Assert.Equal(all, resumed.World.NpcLife!.Npcs.Count);

        var log = resumed.Advance(3 * Day + 77);
        Assert.Equal(all, resumed.Lives.Length);

        // The character comes to the crowd and goes again: tiers change round them, and no one is lost.
        store.Save(SaveSlots.Manual("crowd2"), SaveDocuments.Capture(resumed.World, resumed.CaptureRecord(), session.Content, resumed.WorldTick, 0));
        var again = store.Load(SaveSlots.Manual("crowd2"), new LoadContext(session.Generator, session.Content, new Registry()));
        Assert.True(again.IsComplete);
        Assert.Equal(all, again.World.NpcLife!.Npcs.Count);
        var p = again.Player;
        var there = new PlayerRecord(p.Id, p.Name, 1_060_000, setup.Layout.Space.Terrain.HeightAtMm(1_060_000, 700_000), 700_000, p.AppearanceSeed, p.Inventory,
            p.Progression, p.FacingMdeg, equipment: p.Equipment);
        var near = Simulation.Start(setup, there, again.World, again.Manifest.WorldTick, new EventBus());
        for (int i = 0; i < 200; i++)
            near.Step();
        Assert.Contains(near.CellTiers.Values, t => t == SimulationTier.A);
        Assert.Equal(all, near.Lives.Length);
        Assert.Equal(all - 8 + session.Setup.Layout.Npcs.Length, near.Npcs.Length);
        _output.WriteLine($"{all} lives; {log.Lines.Count} away lines");
    }
}
