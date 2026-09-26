using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using UNNAMED.Domain;
using UNNAMED.Domain.Building;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit.Abstractions;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// M7's required instrumentation beside N-A10 (M7 design §14.12): T10, the building commands under a random script. T2, the timed tick,
/// is <see cref="CapWorkshopBudgetTests"/> below.
/// </summary>
public class M7BudgetTests
{
    private static readonly string[] Pieces =
    {
        "piece.pad.timber", "piece.wall.timber", "piece.doorway.timber", "piece.door.timber", "piece.roof.timber", "piece.storage.chest",
        "piece.station.anvil",
    };

    private const string Timber = "item.material.timber";

    /// <summary>A test-local generator (Knuth's MMIX constants): the script depends on the seed and the world, nothing else.</summary>
    private sealed class Lcg(ulong seed)
    {
        private ulong _x = seed;

        public int Next(int bound)
        {
            _x = _x * 6364136223846793005UL + 1442695040888963407UL;
            return (int)((_x >> 33) % (ulong)bound);
        }
    }

    /// <summary>What a run leaves to compare: the replayable dump, every refusal by tick, and the navigation counters at each save.</summary>
    private sealed record Outcome(string Dump, List<(long Tick, string Reason)> Rejected, List<string> Counters, int Saves, Dictionary<string, int> Done);

    // T10 (R-B3, the chest-crash class)
    /// <summary>
    /// From the Crossing Workshop's start, 500 commands from a seeded script - placements of every piece on the area's lattice and one
    /// module outside it, take-downs and repairs of standing and unknown pieces, stores and takes at the chests, doors worked, blows
    /// at pieces until they fall, and walking - with a save through the session and a load into a fresh one every 100th: nothing
    /// throws, every refusal arrives as a <see cref="CommandRejected"/>, each load equals the world saved, and the same script run again
    /// ends with an equal replayable dump, equal refusals and equal navigation counters. The raw digest is not compared: splitting a
    /// stack mints an item ID (G8).
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    public void BuildingCommands_NeverThrow(ulong seed)
    {
        var first = Run(seed);
        var second = Run(seed);
        Assert.Equal(4, first.Saves);
        Assert.Empty(StateDump.Compare(first.Dump, second.Dump, out _));
        Assert.Equal(first.Rejected, second.Rejected);
        Assert.Equal(first.Counters, second.Counters);
        Assert.Equal(first.Done, second.Done);
        // The script reaches what it is for: building, blows to destruction, mending, doors and the chests (seeds 1-5 place 20-54
        // pieces, strike 157-198 times, destroy 1-6 and store 2-9 times).
        foreach (string what in new[] { "placed", "damaged", "destroyed", "repaired", "door", "stored" })
            Assert.True(first.Done.GetValueOrDefault(what) > 0, $"seed {seed}: nothing {what}");
        Assert.True(first.Done["placed"] >= 10 && first.Done["damaged"] >= 50, $"seed {seed}: {first.Done["placed"]} placed, {first.Done["damaged"]} blows");
    }

    private static Outcome Run(ulong seed)
    {
        using var profile = new TempProfile();
        var random = new Lcg(seed);
        var session = BuildingAcceptanceTests.LoadS0(profile);
        var rejected = new List<(long Tick, string Reason)>();
        var counters = new List<string>();
        int saves = 0, since = 0;
        var done = new Dictionary<string, int>();
        void Count(string what) => done[what] = done.GetValueOrDefault(what) + 1;
        void Listen(GameSession s)
        {
            s.Subscribe<CommandRejected>(r => rejected.Add((r.Tick, r.Reason)));
            s.Subscribe<PiecePlaced>(_ => Count("placed"));
            s.Subscribe<PieceRemoved>(_ => Count("dismantled"));
            s.Subscribe<PieceDamaged>(_ => Count("damaged"));
            s.Subscribe<PieceDestroyed>(_ => Count("destroyed"));
            s.Subscribe<PieceRepaired>(_ => Count("repaired"));
            s.Subscribe<DoorToggled>(_ => Count("door"));
            s.Subscribe<ItemMoved>(m => Count(m.To.ContainerKey?.StartsWith("container.pce_", StringComparison.Ordinal) == true ? "stored" : "moved"));
        }
        // Every refusal a session's world logged arrived as an event, and nothing else did.
        void Refusals(Simulation simulation) =>
            Assert.Equal(simulation.CommandLog.Count(e => e.RejectedReason is not null), rejected.Count - since);
        Listen(session);

        for (int step = 1; step <= 500; step++)
        {
            var simulation = session.Simulation!;
            Act(session, simulation, random);
            if (step % 100 == 0 && step < 500)
            {
                // A save through the session, loaded by a fresh one that carries on: the load is the world saved.
                counters.Add(JsonSerializer.Serialize(simulation.Navigation.Counters));
                string saved = StateDump.Render(simulation);
                session.Save(SaveSlots.Quick);
                saves++;
                var next = Harness.Boot(profile);
                Assert.True(next.Load(SaveSlots.Quick).IsComplete);
                Assert.Empty(StateDump.Compare(saved, StateDump.Render(next.Simulation!), out _));
                Assert.Equal(0, session.SubscriberFailures);
                Refusals(simulation);
                since = rejected.Count;
                session = next;
                Listen(session);
            }
        }
        var end = session.Simulation!;
        counters.Add(JsonSerializer.Serialize(end.Navigation.Counters));
        Assert.Equal(0, session.SubscriberFailures);
        Refusals(end);
        return new Outcome(StateDump.Render(end, replayable: true), rejected, counters, saves, done);
    }

    /// <summary>
    /// One command of the script, and the frames it takes. The script aims at the world: it builds on the lattice round the player
    /// (pads, then walls and doorways on their edges, doors in doorways, roofs, chests and benches on pads), walks up to a piece before
    /// striking, storing, taking or working a door, and mends what is damaged; asks someone to work at a bench or lets them go (E9) - and,
    /// one time in six, places anything anywhere on the lattice, inside the area or a module beyond it, and takes down or mends an unknown
    /// piece, for the refusals.
    /// </summary>
    private static void Act(GameSession session, Simulation simulation, Lcg random)
    {
        var player = simulation.PlayerId;
        var pieces = simulation.Pieces;
        void Do(GameCommand command, int frames = 1)
        {
            session.Submit(command);
            for (int f = 0; f < frames; f++)
                session.Frame(session.TickSeconds);
        }
        void Idle() => Do(new MoveCommand(player, MoveIntent.Idle(simulation.Player.Body.FacingMdeg)));
        // Run towards a point until within reach of it, or for a while, then stand.
        void Approach(long x, long z, long withinMm)
        {
            for (int t = 0; t < 120; t++)
            {
                var body = simulation.Player.Body;
                double dx = x - body.XMm, dz = z - body.ZMm;
                if (Math.Sqrt(dx * dx + dz * dz) <= withinMm)
                    break;
                Do(new MoveCommand(player, Harness.Toward(dx, dz)));
            }
            Idle();
        }
        PieceView? Some(Func<PieceView, bool> which)
        {
            var some = pieces.Where(which).ToList();
            return some.Count == 0 ? null : some[random.Next(some.Count)];
        }
        EntityId Any() => pieces.IsEmpty || random.Next(6) == 0 ? EntityId.NewId(EntityKind.Piece) : pieces[random.Next(pieces.Length)].Id;
        (long X, long Z) Centre(PieceView p) => ((p.MinXMm + p.MaxXMm) / 2, (p.MinZMm + p.MaxZMm) / 2);

        int roll = random.Next(100);
        switch (roll)
        {
            case < 35:
            {
                string def;
                long x, z;
                int r = random.Next(4);
                var here = simulation.Player.Body;
                var pad = Some(p => p.Family == PieceFamily.Pad && Math.Abs(p.XMm - here.XMm) < 4_500 && Math.Abs(p.ZMm - here.ZMm) < 4_500);
                int kind = random.Next(6);
                if (kind == 0 || pad is null)
                {
                    // A pad on a square near the player - or, now and then, anything anywhere on the lattice. With no pad in reach, a pad.
                    var body = simulation.Player.Body;
                    int i = (int)Math.Round((body.XMm - 88_500) / 3_000.0) + random.Next(5) - 2;
                    int j = (int)Math.Round((body.ZMm - 88_500) / 3_000.0) + random.Next(5) - 2;
                    def = random.Next(6) == 0 ? Pieces[random.Next(Pieces.Length)] : "piece.pad.timber";
                    if (random.Next(6) == 0)
                        (i, j) = (random.Next(11) - 1, random.Next(11) - 1);
                    (x, z) = (88_500 + 3_000L * i, 88_500 + 3_000L * j);
                    if (def is "piece.wall.timber" or "piece.doorway.timber" or "piece.door.timber")
                        (x, z) = r % 2 == 0 ? (x, z - 1_500) : (x - 1_500, z);
                }
                else if (kind <= 2)
                {
                    // A wall or doorway on one of a pad's edges.
                    def = random.Next(3) == 0 ? "piece.doorway.timber" : "piece.wall.timber";
                    int edge = random.Next(4);
                    (x, z, r) = edge switch
                    {
                        0 => (pad.XMm, pad.ZMm - 1_500, 0),
                        1 => (pad.XMm, pad.ZMm + 1_500, 2),
                        2 => (pad.XMm - 1_500, pad.ZMm, 1),
                        _ => (pad.XMm + 1_500, pad.ZMm, 3),
                    };
                }
                else if (kind == 3 && Some(p => p.Family == PieceFamily.Doorway) is { } doorway)
                    (def, x, z, r) = ("piece.door.timber", doorway.XMm, doorway.ZMm, doorway.Rotation);
                else if (kind == 4)
                    (def, x, z) = ("piece.roof.timber", pad.XMm, pad.ZMm);
                else
                    (def, x, z) = (random.Next(3) == 0 ? "piece.station.anvil" : "piece.storage.chest", pad.XMm, pad.ZMm);
                Do(new PlacePieceCommand(player, def, x, z, r));
                break;
            }
            case < 38:
            {
                // Timber from the crossing's stack, when the pack runs low.
                var stack = simulation.Containers.Single(c => c.Site.Key == "container.timber_stack");
                Approach(stack.Site.XMm, stack.Site.ZMm, 1_200);
                if (stack.Items.FirstOrDefault(i => i.DefId == Timber) is { } wood)
                    Do(new MoveItemCommand(player, wood.Ref, ItemPlace.In(stack.Site.Key), ItemPlace.Carried, Math.Min(10, wood.Count)));
                break;
            }
            case < 42:
                Do(new DismantlePieceCommand(player, Any()));
                break;
            case < 47:
                Do(new RepairPieceCommand(player, Some(p => p.HealthCurrent < p.HealthMax)?.Id ?? Any()));
                break;
            case < 55:
            {
                // At a chest: store a little timber, or take everything back.
                if (Some(p => p.ContainerKey is not null) is not { } chest)
                {
                    Idle();
                    break;
                }
                var site = simulation.Containers.Single(c => c.Site.Key == chest.ContainerKey).Site;
                Approach(site.XMm, site.ZMm, 1_200);
                var timber = simulation.Player.Inventory.Where(e => e.DefId == Timber).OrderBy(e => e.Count).FirstOrDefault();
                if (random.Next(3) == 0 || timber is null)
                    Do(new TakeAllCommand(player, chest.ContainerKey!));
                else
                    Do(new MoveItemCommand(player, timber.ItemId.Value, ItemPlace.Carried, ItemPlace.In(chest.ContainerKey!), 1 + random.Next(2)));
                break;
            }
            case < 61:
            {
                if (Some(p => p.Family == PieceFamily.Door) is not { } door)
                {
                    Idle();
                    break;
                }
                var (x, z) = Centre(door);
                Approach(x, z, 1_200);
                Do(new InteractCommand(player, door.Id.Value));
                break;
            }
            case < 78:
            {
                // Up to a piece with parts, facing its middle, and one to four blows, each waited out: pieces fall.
                if (Some(p => !p.Parts.IsEmpty) is not { } target)
                {
                    Do(new AttackCommand(player), simulation.Combat.Weapon.TotalTicks + 1);
                    break;
                }
                var (x, z) = Centre(target);
                Approach(x, z, 1_300);
                var body = simulation.Player.Body;
                Do(new MoveCommand(player, MoveIntent.Idle(CombatRules.FacingTowards(body.XMm, body.ZMm, x, z))));
                for (int blow = 1 + random.Next(4); blow > 0; blow--)
                    Do(new AttackCommand(player), simulation.Combat.Weapon.TotalTicks + 1);
                break;
            }
            case < 80:
            {
                // E9: ask someone to work at a piece, or let someone go - mostly refused (out of reach, no station, not working), never thrown.
                string[] npcs = { "npc.ashen_hollow.kera_voss", "npc.ashen_hollow.renn_vale", "npc.ashen_hollow.tavar_orr", "npc.ashen_hollow.nobody" };
                string npc = npcs[random.Next(npcs.Length)];
                Do(random.Next(2) == 0 ? new AssignWorkerCommand(player, npc, Some(p => p.Family == PieceFamily.Station)?.Id ?? Any()) : new ReleaseWorkerCommand(player, npc));
                break;
            }
            default:
            {
                // A walk towards a point of the area and a little round it, then a stop.
                long tx = 86_000 + random.Next(29_000), tz = 86_000 + random.Next(29_000);
                int ticks = 5 + random.Next(20);
                for (int t = 0; t < ticks; t++)
                {
                    var body = simulation.Player.Body;
                    double dx = tx - body.XMm, dz = tz - body.ZMm;
                    if (Math.Sqrt(dx * dx + dz * dz) < 300)
                        break;
                    Do(new MoveCommand(player, Harness.Toward(dx, dz)));
                }
                Idle();
                break;
            }
        }
    }
}

/// <summary>
/// T2 (M7 design §14.12.3): a sibling of <c>SixtyCreatures_TickWithinTheBudget</c>, which stays unmodified. Runs alone, after the parallel
/// tests, with N-A10: its times are measurements of single calls and ticks.
/// </summary>
[Collection(nameof(NavigationTimings))]
public class CapWorkshopBudgetTests
{
    private const string Kera = "npc.ashen_hollow.kera_voss", Tavar = "npc.ashen_hollow.tavar_orr";

    private readonly ITestOutputHelper _output;

    public CapWorkshopBudgetTests(ITestOutputHelper output) => _output = output;

    private static double Percentile(IEnumerable<double> values, double p)
    {
        var sorted = values.Order().ToList();
        return sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];
    }

    /// <summary>A timing's distribution for the log: p50, p90, p95, p99 and max, and how many exceed a target.</summary>
    private static string Distribution(IReadOnlyCollection<double> values, double target) =>
        $"p50 {Percentile(values, 0.5):F3}, p90 {Percentile(values, 0.9):F3}, p95 {Percentile(values, 0.95):F3}, p99 {Percentile(values, 0.99):F3}, " +
        $"max {values.Max():F3} ms; {values.Count(v => v > target)} of {values.Count} over {target} ms";

    // T2
    /// <summary>
    /// The build area full to its 256 pieces, Kera on her way to the bench over both seams, Tavar following, and the 60-creature crowd east
    /// of the area: the tick stays within the budget (CI: mean under 6 ms, three times the 2 ms ASTRAL target). Folded in and logged: each
    /// placement's drain (T3), the preview with and without navigability (T5), the edit tick in which the movers replan (T4), and a save's
    /// capture and encode (T7). The character resumes at (84.0, 110.0) and the edit takes down the west wall at (87000, 106500): the owner's
    /// ruling on the E10 STOP, as §14.12.3's (126.0, 100.0) lies inside the crowd and 35 m from Tavar, beyond his catch-up.
    /// </summary>
    [Fact]
    public void TheCapWorkshop_SixtyCreaturesAndTwoMovers_TickWithinTheBudget()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        // 1. W0, no creatures: the full-area layout, each command's drain timed.
        var w0 = Arena.OpenCreatures(session, session.Setup, (100.5, 94.5), 0, Array.Empty<(string, double, double, string)>(),
            r => FullAreaLayout.Start(session, r));
        var layout = FullAreaLayout.Build(w0);
        var drains = layout.Drains.Select(d => d.Ms).ToList();
        _output.WriteLine($"placements: {drains.Count}; drain {Distribution(drains, 10)} (ASTRAL target: median 2 ms, max 10 ms)");
        foreach (var (d, i) in layout.Drains.Select((d, i) => (d, i)).OrderByDescending(x => x.d.Ms).Take(6))
            _output.WriteLine($"  slowest: #{i + 1} {d.Def} at ({d.X}, {d.Z}) {d.Ms:F3} ms; collections gen0 {d.Gen0}, gen1 {d.Gen1}, gen2 {d.Gen2}, paused {d.PauseMs:F3} ms" +
                (layout.Drains.FindIndex(e => e.Def == d.Def) == i ? " (the first of its kind)" : ""));
        _output.WriteLine($"  placements with a gen-2 collection: {layout.Drains.Count(d => d.Gen2 > 0)}; with gen-1 only: " +
            $"{layout.Drains.Count(d => d.Gen1 > 0 && d.Gen2 == 0)}; the drain without them: " +
            Distribution(layout.Drains.Where(d => d.Gen1 == 0 && d.Gen2 == 0).Select(d => d.Ms).ToList(), 10));
        foreach (var group in layout.Drains.GroupBy(d => d.Def))
            _output.WriteLine($"  {group.Key}: {group.Count()} placed, {Distribution(group.Select(d => d.Ms).ToList(), 10)}");

        // 2. The preview: 1,000 without navigability, cycling the 81 squares, 7 definitions and 4 rotations; 100 with it.
        var squares = FullAreaLayout.Squares().ToList();
        string[] defs = { FullAreaLayout.Pad, FullAreaLayout.Doorway, FullAreaLayout.Wall, FullAreaLayout.Door, FullAreaLayout.Chest,
            FullAreaLayout.Bench, FullAreaLayout.Roof };
        var clock = Stopwatch.StartNew();
        for (int n = 0; n < 1_000; n++)
            w0.Simulation.PreviewPlacement(defs[n % defs.Length], squares[n % squares.Count].X, squares[n % squares.Count].Z, n % 4, checkNavigability: false);
        double previewMean = clock.Elapsed.TotalMilliseconds / 1_000;
        var checks = new List<double>();
        for (int n = 0; n < 100; n++)
        {
            clock.Restart();
            w0.Simulation.PreviewPlacement(defs[n % defs.Length], squares[n % squares.Count].X, squares[n % squares.Count].Z, n % 4, checkNavigability: true);
            checks.Add(clock.Elapsed.TotalMilliseconds);
        }
        _output.WriteLine($"preview: checks 1-14 mean {previewMean:F4} ms of 1,000 (ASTRAL target 0.05 ms); with navigability median " +
            $"{Percentile(checks, 0.5):F3} ms, max {checks.Max():F3} ms of 100");

        // 3. Out by the west door to Kera, who is asked to the bench; saved at once, while she is on her way.
        foreach (var (x, z) in new[] { (84.0, 116.0), (80.0, 118.0), (62.0, 130.0), (51.8, 136.0), (51.8, 142.0) })
            Assert.True(w0.WalkTo(x, z), $"the walk to the shed stopped at {w0.Simulation.Player.Body}");
        w0.TurnTo(90);
        w0.Tick();
        if (!w0.Simulation.Doors.Single(d => d.Site.Key == "door.forge_shed").Open)
            Assert.Null(w0.Submit(new InteractCommand(w0.Player, "door.forge_shed")));
        Assert.True(w0.WalkTo(54.5, 142.0) && w0.WalkTo(60.4, 140.0), $"the walk into the shed stopped at {w0.Simulation.Player.Body}");
        w0.TurnTo(110);
        w0.Tick();
        var bench = w0.Simulation.Pieces.Single(p => p.DefId == FullAreaLayout.Bench);
        string? refused = w0.Submit(new AssignWorkerCommand(w0.Player, Kera, bench.Id));
        if (refused is not null)
        {
            var body = w0.Simulation.Npcs.Single(n => n.Id == Kera).Body;
            var plan = NavSearch.Plan(new NavQuery(w0.Simulation.Navigation.Grid, _ => true, session.Setup.Navigation, new NavScratch(), null),
                new NavAgent(0, true), new NavPoint(body.XMm, body.ZMm), new NavPoint(100_500, 100_250));
            Assert.Fail($"Kera was not taken on: \"{refused}\"; her plan to the anchor: {plan.Outcome}, {plan.Expansions} expansions");
        }
        Assert.Equal(NpcErrandPhase.ToWork, w0.Simulation.World.NpcErrand(Kera)!.Phase);
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("cap"), SaveDocuments.Capture(w0.Simulation.World, w0.Simulation.CaptureRecord(), session.Content,
            w0.Simulation.WorldTick, 0));
        var loaded = store.Load(SaveSlots.Manual("cap"), new LoadContext(session.Generator, session.Content, new Registry()));

        // 4. W1: resumed under the 60-creature crowd of SixtyCreatures_TickWithinTheBudget with its x origin moved from 90 to 120 m, clear
        // of the area; the character at (84.0, 110.0) facing 90, west of the area: 36 m from the crowd's nearest column, 7.9 m from Tavar.
        string[] roles = { "pack_hunter", "roamer", "hunter", "sentinel", "stray", "territorial" };
        string[] kinds = { Arena.Wolf, "creature.beast.ash_ember_hound", "creature.undead.bone_walker_husk", "creature.construct.animated_armour",
            "creature.beast.bristleback_boar", "creature.beast.cave_hunting_spider" };
        var crowd = Enumerable.Range(0, 60).Select(i => new SpawnSite($"spawn.test.creature_{i}", (long)((120.0 + i % 10 * 8) * 1000),
            (long)((80.0 + i / 10 * 8) * 1000), 0, ImmutableArray.Create(new SpawnMember(kinds[i % kinds.Length], roles[i % roles.Length])))).ToImmutableArray();
        var rules = session.Setup with { Combat = session.Setup.Combat with { Spawns = crowd } };
        var p = loaded.Player;
        var w1 = Arena.Resume(rules, loaded with
        {
            Player = new PlayerRecord(p.Id, p.Name, 84_000, session.Setup.Layout.Space.Terrain.HeightAtMm(84_000, 110_000), 110_000, p.AppearanceSeed,
                p.Inventory, p.Progression, 90_000, p.Discoveries, p.Equipment, p.Currency, p.Effects, p.Relationships, p.Conversations, p.Quests,
                p.Companions),
        });
        Assert.Equal(60, w1.Simulation.Creatures.Count(c => c.Alive));
        var routes = w1.Record<RoutePlanned>();
        var rebuilt = w1.Record<NavigationRebuilt>();
        var died = w1.Record<PlayerDied>();

        // 5. 20 warm-up ticks, Tavar told to follow at the first; then 400 timed.
        Assert.Null(w1.Submit(new OrderCompanionCommand(w1.Player, Tavar, CompanionOrder.Follow)));
        w1.Tick(20);
        Assert.Equal(NpcErrandPhase.ToWork, w1.Simulation.World.NpcErrand(Kera)!.Phase);
        Assert.Contains(routes, r => r.MoverKey == Tavar);
        int gen2 = GC.CollectionCount(2);
        var ticks = new List<double>();
        for (int n = 0; n < 400; n++)
        {
            clock.Restart();
            w1.Tick();
            ticks.Add(clock.Elapsed.TotalMilliseconds);
        }
        gen2 = GC.CollectionCount(2) - gen2;
        double mean = ticks.Average();
        _output.WriteLine($"the cap workshop's tick: mean {mean:F3} ms, p95 {Percentile(ticks, 0.95):F3} ms, max {ticks.Max():F3} ms over 400 " +
            $"(ASTRAL target mean 2 ms); gen-2 collections {gen2}; {w1.Simulation.Creatures.Count(c => c.Alive)} creatures alive, " +
            $"{routes.Count} routes planned, Kera {w1.Simulation.World.NpcErrand(Kera)?.Phase}");
        Assert.Empty(died);
        Assert.True(mean < 6, $"the cap workshop's tick took {mean:F3} ms on average (CI bound 6 ms; ASTRAL target 2 ms)");

        // 6. The edit tick: from (85.2, 106.5) the west wall at (87000, 106500) r1 taken down at boundary N; tick N + 1 timed. The movers
        // whose route is active and whose watch meets a tile the edit restamps (the wall's box and the 600 mm influence) replan for geometry.
        Assert.True(w1.WalkTo(85.2, 106.5), $"the walk to the west wall stopped at {w1.Simulation.Player.Body}");
        w1.TurnTo(90);
        w1.Tick(40);
        var wall = w1.Simulation.Pieces.Single(q => q.DefId == FullAreaLayout.Wall && q.XMm == 87_000 && q.ZMm == 106_500);
        var reach = new NavRect(wall.MinXMm - 600, wall.MinZMm - 600, wall.MaxXMm + 600, wall.MaxZMm + 600);
        var tiles = new[] { (reach.MinXMm, reach.MinZMm), (reach.MaxXMm, reach.MinZMm), (reach.MinXMm, reach.MaxZMm), (reach.MaxXMm, reach.MaxZMm) }
            .Select(c => new NavTileKey(NavGeometry.FloorDiv(c.Item1, 100_000), NavGeometry.FloorDiv(c.Item2, 100_000))).Distinct().Order().ToList();
        var expected = w1.Simulation.Navigation.Movers
            .Where(m => m.Route.Status == NavRouteStatus.Active
                        && tiles.Any(t => m.Route.Watch.Meets(new NavRect(t.Tx * 100_000, t.Tz * 100_000, t.Tx * 100_000 + 99_999, t.Tz * 100_000 + 99_999))))
            .Select(m => m.NpcId).Order(StringComparer.Ordinal).ToList();
        _output.WriteLine($"the edit restamps {string.Join(", ", tiles)}; replanning for geometry is expected of: {string.Join(", ", expected)}");
        long n0 = w1.Simulation.WorldTick;
        int planned = routes.Count, rebuilds = rebuilt.Count;
        Assert.Null(w1.Submit(new DismantlePieceCommand(w1.Player, wall.Id)));
        var edit = Assert.Single(rebuilt.Skip(rebuilds));
        Assert.Equal(n0, edit.Tick);
        Assert.Equal(tiles, edit.Tiles.Order());
        clock.Restart();
        w1.Tick();
        double editMs = clock.Elapsed.TotalMilliseconds;
        var geometry = routes.Skip(planned).Where(r => r.Reason == NavFollower.Geometry).ToList();
        Assert.All(geometry, r => Assert.Equal(n0 + 1, r.Tick));
        Assert.Equal(expected, geometry.Select(r => r.MoverKey).Order(StringComparer.Ordinal).ToList());
        _output.WriteLine($"the edit tick: {editMs:F3} ms; {string.Join("; ", routes.Skip(planned).Select(r => $"{r.MoverKey} {r.Reason} {r.Expansions} expansions"))}");
        Assert.Null(w1.Submit(new PlacePieceCommand(w1.Player, FullAreaLayout.Wall, 87_000, 106_500, 1)));
        Assert.Equal(256, w1.Simulation.Pieces.Length);

        // 7. A save's capture and encode, no disk, 100 times.
        var saves = new List<double>();
        var captures = new List<double>();
        var saveGcs = new List<int>();
        int entitiesBytes = 0, playerBytes = 0;
        for (int n = 0; n < 100; n++)
        {
            int collections = GC.CollectionCount(0);
            clock.Restart();
            var document = SaveDocuments.Capture(w1.Simulation.World, w1.Simulation.CaptureRecord(), session.Content, w1.Simulation.WorldTick, 0);
            captures.Add(clock.Elapsed.TotalMilliseconds);
            entitiesBytes = SectionCodec.EncodeEntities(document.Delta).Length;
            playerBytes = SectionCodec.EncodePlayer(document.Player).Length;
            saves.Add(clock.Elapsed.TotalMilliseconds);
            saveGcs.Add(GC.CollectionCount(0) - collections);
        }
        _output.WriteLine($"a save's capture and encode: {Distribution(saves, 1)} (ASTRAL target p99 1 ms); the first {saves[0]:F3} ms; " +
            $"entities.msgpack {entitiesBytes:N0} B, player.msgpack {playerBytes:N0} B");
        _output.WriteLine($"  the capture alone (what a frame pays in play; the encode runs on the save lane): {Distribution(captures, 1)}");
        _output.WriteLine($"  saves with a collection: {saveGcs.Count(c => c > 0)}; the saves over 1 ms: " +
            string.Join(", ", saves.Select((ms, i) => (ms, i)).Where(x => x.ms > 1).Select(x => $"#{x.i + 1} {x.ms:F3} ms{(saveGcs[x.i] > 0 ? " (collected)" : "")}")));
        Assert.Equal(0, session.SubscriberFailures);
    }
}
