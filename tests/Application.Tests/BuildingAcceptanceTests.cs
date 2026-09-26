using System.Collections.Immutable;
using System.Text.RegularExpressions;
using UNNAMED.Application.Evidence;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using static UNNAMED.Application.Evidence.CrossingWorkshop;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The Crossing Workshop (M7 design §4.22), headless: its command table played from the committed start save S0, step group by step
/// group, as <c>--build-shots</c> plays it windowed; the save that starts both; and the new-game building loop from the timber stack.
/// Each test plays every row whose slice has landed.
/// </summary>
public class BuildingAcceptanceTests
{
    private const string Tag = "unnamed.piece/v1";
    private const string S0Slot = "manual_s0";

    private static string StartSave => Path.Combine(Harness.RepoRoot(), "tests", "Application.Tests", "GameSaves", "m7_crossing_start", "save");

    /// <summary>The committed S0 copied into a profile and loaded (a load records its proof beside the slot, so never in place).</summary>
    private static GameSession LoadS0(TempProfile profile)
    {
        string slot = Path.Combine(profile.Root, S0Slot);
        if (!Directory.Exists(slot))
            Copy(StartSave, slot);
        var session = Harness.Boot(profile);
        var loaded = session.Load(S0Slot);
        Assert.True(loaded.IsComplete, $"S0 did not load whole: quarantined [{string.Join(", ", loaded.QuarantinedSections)}], " +
            $"loss [{string.Join("; ", loaded.Report.Loss)}]");
        return session;
    }

    /// <summary>
    /// The table's player: poses as §4.22's notation says (run legs to 300 mm, the pose to 50 mm at a walk, one idle frame facing it),
    /// every command through the session's own queue and frame loop, one tick a frame, and what came of each.
    /// </summary>
    private sealed class WorkshopRun
    {
        private int _next;

        public WorkshopRun(GameSession session)
        {
            Session = session;
            session.Subscribe<PiecePlaced>(Placed.Add);
            session.Subscribe<NavigationRebuilt>(Rebuilt.Add);
            session.Subscribe<RoutePlanned>(Routes.Add);
            session.Subscribe<CompanionCaughtUp>(CaughtUp.Add);
        }

        public GameSession Session { get; }
        public Simulation Simulation => Session.Simulation!;
        public List<PiecePlaced> Placed { get; } = new();
        public List<NavigationRebuilt> Rebuilt { get; } = new();
        public List<RoutePlanned> Routes { get; } = new();
        public List<CompanionCaughtUp> CaughtUp { get; } = new();

        /// <summary>Each action played: the tick it applied at, its refusal (null: accepted), and whether the state digest was the same after it.</summary>
        public Dictionary<WorkshopAction, (long Tick, string? Refused, bool DigestKept)> Outcomes { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>Each row played: the tick its actions began at and the tick it ended at.</summary>
        public Dictionary<string, (long First, long End)> RowTicks { get; } = new();

        public (long XMm, long ZMm, bool OnCreature) Aimed { get; private set; }

        /// <summary>Called after every tick this run frames itself (not during pose walks).</summary>
        public Action<Simulation>? EachTick { get; set; }

        public WorkshopRow Row(string id) => LandedRows.Single(r => r.Id == id);

        public (long Tick, string? Refused, bool DigestKept) Outcome(string rowId, int action = 0) => Outcomes[Row(rowId).Landed.ElementAt(action)];

        public void Frame()
        {
            Session.Frame(Session.TickSeconds);
            EachTick?.Invoke(Simulation);
        }

        /// <summary>Play the landed rows up to and including <paramref name="lastId"/>.</summary>
        public void Through(string lastId)
        {
            var rows = LandedRows.ToList();
            int last = rows.FindIndex(r => r.Id == lastId);
            Assert.True(last >= 0, $"{lastId} is not a landed row");
            for (; _next <= last; _next++)
                Play(rows[_next]);
        }

        private void Play(WorkshopRow row)
        {
            var player = Simulation.PlayerId;
            if (row.Pose is { } pose)
            {
                StandAt(Session, pose, row.Id);
                EachTick?.Invoke(Simulation);
            }
            else
            {
                for (int i = 1; i < row.After; i++)
                    Frame();
            }
            long first = Simulation.WorldTick;
            foreach (var action in row.Landed)
            {
                switch (action)
                {
                    case PlaceAction place:
                        Outcomes[action] = Submit(place.Command(player));
                        break;
                    case OrderAction order:
                        Outcomes[action] = Submit(order.Command(player));
                        break;
                    case HoldAction hold:
                        Outcomes[action] = (Simulation.WorldTick, null, false);
                        for (int i = 0; i < hold.Ticks; i++)
                        {
                            Session.Submit(new MoveCommand(player, hold.Intent));
                            Frame();
                        }
                        break;
                    case AimAction aim:
                        Aimed = Simulation.Aim(aim.FacingMdeg, aim.RangeMm);
                        Outcomes[action] = (Simulation.WorldTick, null, true);
                        break;
                    case SaveAction:
                        Session.Save(SaveSlots.Quick);
                        Outcomes[action] = (Simulation.WorldTick, null, true);
                        break;
                    default:
                        throw new InvalidOperationException($"{row.Id}: no player for {action}");
                }
            }
            RowTicks[row.Id] = (first, Simulation.WorldTick);
        }

        // The command applies at this boundary, as the frame would apply it; the digest is read either side of it, before the tick runs.
        private (long Tick, string? Refused, bool DigestKept) Submit(GameCommand command)
        {
            string before = Simulation.StateDigest();
            Session.Submit(command);
            Simulation.DrainCommands();
            bool kept = before == Simulation.StateDigest();
            Frame();
            var logged = Simulation.CommandLog.Last(e => ReferenceEquals(e.Command, command));
            return (logged.Tick, logged.RejectedReason, kept);
        }
    }

    private static long Mm(double metres) => (long)Math.Round(metres * 1000);

    /// <summary>§4.22's pose: the run legs, each to 300 mm; the pose, to 50 mm at a walk; then one idle frame facing it.</summary>
    private static void StandAt(GameSession session, WorkshopPose pose, string what)
    {
        var simulation = session.Simulation!;
        foreach (var (x, z) in pose.Legs)
            Assert.True(Harness.WalkTo(session, Mm(x), Mm(z)), $"{what}: the leg to ({x}, {z}) stopped at {simulation.Player.Body}");
        Assert.True(Harness.WalkTo(session, Mm(pose.X), Mm(pose.Z), Gait.Walk, toleranceMm: 50),
            $"{what}: the pose ({pose.X}, {pose.Z}) was not reached; stopped at {simulation.Player.Body}");
        session.Submit(new MoveCommand(simulation.PlayerId, MoveIntent.Idle(pose.FacingDeg * 1000 % MoveIntent.FullTurnMdeg)));
        session.Frame(session.TickSeconds);
    }

    /// <summary>One command at the next boundary, and a frame: its refusal, or null.</summary>
    private static string? Submit(GameSession session, GameCommand command)
    {
        session.Submit(command);
        session.Frame(session.TickSeconds);
        return session.Simulation!.CommandLog.Last(e => ReferenceEquals(e.Command, command)).RejectedReason;
    }

    /// <summary>A command log replayed into a simulation at the ticks it was logged at, with something done at every boundary between.</summary>
    private static void Replay(Simulation simulation, IEnumerable<LoggedCommand> log, long until, Action<Simulation>? atEachBoundary = null)
    {
        void StepTo(long tick)
        {
            while (simulation.WorldTick < tick)
            {
                atEachBoundary?.Invoke(simulation);
                simulation.Step();
                simulation.DrainCommands();
            }
        }
        foreach (var entry in log.Where(e => e.Tick < until))
        {
            StepTo(entry.Tick);
            simulation.Enqueue(entry.Command);
            simulation.DrainCommands();
        }
        StepTo(until);
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
    }

    private static int TimberCarried(Simulation simulation) => simulation.Player.Inventory.Where(e => e.DefId == Timber).Sum(e => e.Count);

    private static EntityId[] Derived(Simulation simulation, int count) =>
        Enumerable.Range(1, count).Select(n => EntityId.Derived(EntityKind.Piece, n, Tag, simulation.PlayerId.Value)).ToArray();

    /// <summary>Every item instance ID the state holds, anywhere: the window minted none when no new one appears (G8).</summary>
    private static HashSet<string> ItemIds(Simulation simulation) =>
        Regex.Matches(StateDump.Render(simulation), "itm_[0-9A-Za-z]{26}").Select(m => m.Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>A row's refusal the placement ghost gives in the same state: the rule, for the rows whose Expect names one.</summary>
    private static PlacementRule? RuleOf(Simulation simulation, PlaceAction place) =>
        simulation.PreviewPlacement(place.DefId, place.XMm, place.ZMm, place.Rotation, checkNavigability: false).Failed;

    [Fact]
    public void CrossingWorkshop_1to3_BuildRefuseAndWalkIn()
    {
        using var profile = new TempProfile();
        var run = new WorkshopRun(LoadS0(profile));
        var simulation = run.Simulation;
        Assert.Equal(45, TimberCarried(simulation));

        // Step 1: four pads, the doorway across x = 100, seven walls, four roofs.
        run.Through("R02");
        Assert.All(run.Row("R02").Landed, a => Assert.Null(run.Outcomes[a].Refused));
        Assert.Equal(new long[] { 1, 2, 3, 4 }, run.Placed.Select(p => p.Revision));
        Assert.Equal("r_0_0:c_01_01", simulation.World.Piece(run.Placed[0].PieceId)!.HostCell);
        Assert.Empty(run.Rebuilt);

        run.Through("R03");
        Assert.Null(run.Outcome("R03").Refused);
        Assert.Equal(5, run.Placed[^1].Revision);
        var doorway = simulation.Pieces.Single(p => p.Id == run.Placed[^1].PieceId);
        var jambs = doorway.Parts.OrderBy(p => p.MinXMm).ToList();
        Assert.Equal((99_700L, 101_300L), (jambs[0].MaxXMm, jambs[^1].MinXMm));

        run.Through("R04");
        Assert.All(run.Row("R04").Landed, a => Assert.Null(run.Outcomes[a].Refused));
        Assert.Equal(Enumerable.Range(6, 7).Select(n => (long)n), run.Placed.Skip(5).Select(p => p.Revision));
        Assert.Equal(8, run.Rebuilt.Count);

        run.Through("R05");
        Assert.All(run.Row("R05").Landed, a => Assert.Null(run.Outcomes[a].Refused));
        Assert.Equal(Enumerable.Range(13, 4).Select(n => (long)n), run.Placed.Skip(12).Select(p => p.Revision));
        Assert.Equal(8, run.Rebuilt.Count);

        // The step-1 counts, and one PiecePlaced a row.
        var (pieces, spent, sequence) = Counts.StepOne;
        Assert.Equal(pieces, simulation.Pieces.Length);
        Assert.Equal(spent, 45 - TimberCarried(simulation));
        Assert.Equal(sequence, simulation.World.StructureSequence);
        Assert.Equal(pieces, run.Placed.Count);
        Assert.Equal(Derived(simulation, pieces), simulation.Pieces.Select(p => p.Id).Order());

        // Step 2: a wall on the doorway's edge is refused, in words, and changes nothing.
        var overlap = (PlaceAction)run.Row("R09").Landed.Single();
        Assert.Equal(PlacementRule.Slot, RuleOf(simulation, overlap));
        run.Through("R09");
        Assert.Equal(("a Timber Doorway already stands there", true), (run.Outcome("R09").Refused, run.Outcome("R09").DigestKept));
        Assert.Equal(pieces, run.Placed.Count);

        // Step 3: in by the doorway, out again, north through it (no door until E6), and the aim stops at the west wall.
        run.Through("R12");
        Assert.True(simulation.Player.Body.ZMm > 99_200, $"the walk north ended at {simulation.Player.Body}");
        run.Through("R14");
        Assert.InRange(run.Aimed.XMm, 99_200, 99_210);
        Assert.False(run.Aimed.OnCreature);
        Assert.Equal(0, run.Session.SubscriberFailures);
    }

    [Fact]
    public void CrossingWorkshop_9_ANewWallChangesHerWayHome()
    {
        using var profile = new TempProfile();
        var run = new WorkshopRun(LoadS0(profile));
        var simulation = run.Simulation;
        run.Through("R40");
        Assert.All(new[] { "R39", "R40" }.SelectMany(r => run.Row(r).Landed), a => Assert.Null(run.Outcomes[a].Refused));

        // Standing on the line: the wall is refused for the body, and placed once the character steps off it.
        run.Through("R41");
        Assert.Equal("someone is standing there", run.Outcome("R41").Refused);
        run.Through("R42");
        Assert.Null(run.Outcome("R42").Refused);
        var line = simulation.Pieces.Where(p => p.DefId == Wall && p.XMm == 96_000).ToList();
        Assert.Equal(2, line.Count);
        Assert.Equal((98_800L, 105_200L), (line.Min(p => p.MinZMm), line.Max(p => p.MaxZMm)));
        Assert.Equal(0, run.Session.SubscriberFailures);
    }

    [Fact]
    public void CrossingWorkshop_10_SaveQuitReloadGoesOnTheSame()
    {
        using var profile = new TempProfile();
        var run = new WorkshopRun(LoadS0(profile));
        run.Through("R42");

        // From Tavar's order on, whether he has come through the doorway's opening northwards.
        static bool Through(Body from, Body to) => from.ZMm < 99_000 && to.ZMm >= 99_000 && to.XMm is >= 99_700 and <= 101_300;
        bool passed = false;
        Body? last = null;
        run.EachTick = s =>
        {
            var tavar = s.Companions.Single().Body;
            passed |= last is not null && Through(last, tavar);
            last = tavar;
        };
        run.Through("R45");
        Assert.Null(run.Outcome("R45").Refused);
        Assert.Contains(run.Routes, r => r.MoverKey == Tavar);

        // R46: the save, Tavar still on his route; then W1 goes on 600 ticks, and W2 - a fresh session loading the save - goes on the same.
        var w1 = run.Simulation;
        var save = (SaveAction)run.Row("R46").Landed.Single();
        run.Through("R46");
        string saved = StateDump.Render(w1);
        Assert.Equal(NavRouteStatus.Active, w1.CaptureRecord().Companions.Single().Route.Status);
        bool passedAtSave = passed;
        var rows = w1.World.Pieces.Select(p => (p.InstanceId, p.DefId, p.HostCell, p.XMm, p.ZMm, p.Rotation, p.Owner, p.HealthCurrent, p.DoorOpen)).ToList();

        var w2Session = Harness.Boot(profile);
        var loaded = w2Session.Load(SaveSlots.Quick);
        Assert.True(loaded.IsComplete);
        var w2 = w2Session.Simulation!;
        Assert.Empty(StateDump.Compare(saved, StateDump.Render(w2), out _));
        Assert.Equal(rows, w2.World.Pieces.Select(p => (p.InstanceId, p.DefId, p.HostCell, p.XMm, p.ZMm, p.Rotation, p.Owner, p.HealthCurrent, p.DoorOpen)));
        Assert.Equal(w1.World.StructureSequence, w2.World.StructureSequence);
        Assert.Equal(w1.Navigation.Grid.Digest(), w2.Navigation.Grid.Digest());

        for (int i = 0; i < save.ContinueTicks; i++)
            run.Frame();
        bool passedInW1 = passed;
        var caughtUpInW2 = new List<CompanionCaughtUp>();
        w2Session.Subscribe<CompanionCaughtUp>(caughtUpInW2.Add);
        passed = passedAtSave;
        var at = w2.Companions.Single().Body;
        for (int i = 0; i < save.ContinueTicks; i++)
        {
            w2Session.Frame(w2Session.TickSeconds);
            var tavar = w2.Companions.Single().Body;
            passed |= Through(at, tavar);
            at = tavar;
        }
        bool passedInW2 = passed;

        Assert.Equal(w1.WorldTick, w2.WorldTick);
        Assert.Equal(w1.StateDigest(), w2.StateDigest());
        var differences = StateDump.Compare(StateDump.Render(w1), StateDump.Render(w2), out int leaves);
        Assert.True(differences.Count == 0, $"{differences.Count} of {leaves} fields differ: {string.Join("; ", differences.Take(5))}");
        Assert.Equal(Counts.End.Sequence, w2.World.StructureSequence);
        foreach (var (world, through) in new[] { (w1, passedInW1), (w2, passedInW2) })
        {
            var tavar = world.Companions.Single().Body;
            Assert.True(tavar.XMm is >= 99_200 and <= 104_800 && tavar.ZMm is >= 99_200 and <= 104_800, $"Tavar ended at {tavar}");
            Assert.True(through, "Tavar never came through the doorway's opening");
        }
        Assert.Empty(run.CaughtUp);
        Assert.Empty(caughtUpInW2);
        Assert.Equal(0, run.Session.SubscriberFailures);
        Assert.Equal(0, w2Session.SubscriberFailures);
    }

    /// <summary>
    /// Step 0: step 1 replayed from a second load of S0 at its logged ticks mints nothing, so the raw digest is equal and the pieces carry
    /// the IDs their sequence derives. Step 11: the whole table to R45 replayed the same way gives an equal replayable dump, equal piece
    /// IDs and equal refusals at equal ticks.
    /// </summary>
    [Fact]
    public void CrossingWorkshop_0and11_ReplaysFromTheLog()
    {
        using var profile = new TempProfile();
        var run = new WorkshopRun(LoadS0(profile));
        var played = run.Simulation;
        var held = ItemIds(played);
        run.Through("R05");
        long stepOne = run.RowTicks["R05"].End;
        string digestAtStepOne = played.StateDigest();
        var piecesAtStepOne = played.Pieces.Select(p => p.Id).ToList();
        Assert.Subset(held, ItemIds(played));
        run.Through("R45");
        long end = played.WorldTick;
        var log = played.CommandLog.ToList();

        var zero = LoadS0(profile).Simulation!;
        Replay(zero, log, stepOne);
        Assert.Equal(digestAtStepOne, zero.StateDigest());
        Assert.Equal(piecesAtStepOne, zero.Pieces.Select(p => p.Id));
        Assert.Equal(Derived(zero, Counts.StepOne.Pieces), zero.Pieces.Select(p => p.Id).Order());

        var eleven = LoadS0(profile).Simulation!;
        Replay(eleven, log, end);
        var differences = StateDump.Compare(StateDump.Render(played, replayable: true), StateDump.Render(eleven, replayable: true), out int leaves);
        Assert.True(differences.Count == 0, $"{differences.Count} of {leaves} fields differ: {string.Join("; ", differences.Take(5))}");
        Assert.Equal(played.Pieces.Select(p => p.Id), eleven.Pieces.Select(p => p.Id));
        Assert.Equal(log.Select(e => (e.Tick, e.RejectedReason)), eleven.CommandLog.Select(e => (e.Tick, e.RejectedReason)));
        Assert.Equal(Counts.End.Pieces, eleven.Pieces.Length);
        Assert.Equal(Counts.End.Carried, TimberCarried(eleven));
        Assert.Equal(Counts.End.Sequence, eleven.World.StructureSequence);
    }

    /// <summary>
    /// G7: the table's log replayed plain and with 1,000 placement previews interleaved at its boundaries - every piece, over and around
    /// the build area - gives equal dumps, piece IDs, refusals and navigation counters; and, the window minting no item, an equal raw digest.
    /// </summary>
    [Fact]
    public void PreviewsInterleaved_ChangeNothing()
    {
        using var profile = new TempProfile();
        var run = new WorkshopRun(LoadS0(profile));
        run.Through("R45");
        long end = run.Simulation.WorldTick;
        var log = run.Simulation.CommandLog.ToList();

        var plain = LoadS0(profile).Simulation!;
        var held = ItemIds(plain);
        Replay(plain, log, end);
        var previewed = LoadS0(profile).Simulation!;
        string[] defs = { Pad, Wall, Doorway, Roof, "piece.nowhere" };
        int asked = 0;
        Replay(previewed, log, end, s =>
        {
            for (int i = 0; i < 2 && asked < 1_000; i++, asked++)
                Assert.NotNull(s.PreviewPlacement(defs[asked % defs.Length], 84_000 + asked * 1_500 % 36_000, 84_000 + asked * 3_000 % 36_000,
                    asked % 4, checkNavigability: false));
        });
        Assert.Equal(1_000, asked);

        Assert.Empty(StateDump.Compare(StateDump.Render(plain, replayable: true), StateDump.Render(previewed, replayable: true), out _));
        Assert.Equal(plain.Pieces.Select(p => p.Id), previewed.Pieces.Select(p => p.Id));
        Assert.Equal(plain.CommandLog.Select(e => (e.Tick, e.RejectedReason)), previewed.CommandLog.Select(e => (e.Tick, e.RejectedReason)));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(plain.Navigation.Counters), System.Text.Json.JsonSerializer.Serialize(previewed.Navigation.Counters));
        Assert.Subset(held, ItemIds(plain));
        Assert.Equal(plain.StateDigest(), previewed.StateDigest());
    }

    /// <summary>
    /// S0 (§13.3), written by this test only when <c>UNNAMED_WRITE_BUILD_START=1</c> - never under CI - from <see cref="CrossingWorkshop.Start"/>
    /// at tick 0, through the game's own save path, into <c>GameSaves/m7_crossing_start/save</c>. Otherwise it does nothing.
    /// </summary>
    [Fact]
    public void TheCrossingWorkshopStart_IsWrittenOnlyWhenAsked()
    {
        if (Environment.GetEnvironmentVariable("UNNAMED_WRITE_BUILD_START") != "1")
            return;
        Assert.False(string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase),
            "the committed build start is never written under CI");
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = Simulation.Start(session.Setup, Start(session.Setup), StartWorld(session.Generator), 0, new EventBus());
        new SaveStore(profile.Root).Save(SaveSlots.Quick,
            SaveDocuments.Capture(simulation.World, simulation.CaptureRecord(), session.Content, 0, 0) with { CapturedAt = DateTimeOffset.UtcNow });
        if (Directory.Exists(StartSave))
            Directory.Delete(StartSave, recursive: true);
        Copy(Path.Combine(profile.Root, SaveSlots.Quick), StartSave);
    }

    /// <summary>The committed S0, loaded under today's content (the definition pass included), is the builder's start, field by field.</summary>
    [Fact]
    public void TheCommittedBuildStart_IsTheCrossingWorkshopStart()
    {
        using var profile = new TempProfile();
        var session = LoadS0(profile);
        var loaded = session.Simulation!;
        Assert.Equal(0, loaded.WorldTick);
        Assert.Equal(Seed, loaded.World.WorldSeed);

        var built = Simulation.Start(session.Setup, Start(session.Setup), StartWorld(session.Generator), 0, new EventBus());
        var differences = StateDump.Compare(StateDump.Render(built, replayable: true), StateDump.Render(loaded, replayable: true), out int leaves);
        Assert.True(differences.Count == 0, $"{differences.Count} of {leaves} fields differ: {string.Join("; ", differences.Take(5))}");

        // What the replayable dump holds only as values: the pose, the pack, the recipe and Tavar waiting.
        var player = loaded.Player;
        Assert.Equal((Character.XMm, Character.ZMm, Character.FacingMdeg), (player.Body.XMm, player.Body.ZMm, player.Body.FacingMdeg));
        Assert.Equal(new[] { 5, 20, 20 }, player.Inventory.Where(e => e.DefId == Timber).Select(e => e.Count).Order());
        Assert.Equal(1, player.Inventory.Where(e => e.DefId == IronIngot).Sum(e => e.Count));
        Assert.Equal(1, player.Inventory.Where(e => e.DefId == AshHaft).Sum(e => e.Count));
        Assert.True(player.Progression.Known.ContainsKey(SpearRecipe));
        var tavar = Assert.Single(loaded.Companions);
        Assert.Equal((Tavar, CompanionOrder.Wait, CompanionCondition.Up), (tavar.NpcId, tavar.Order, tavar.Condition));
        Assert.Equal(TavarWaits, (tavar.Body.XMm, tavar.Body.ZMm, tavar.Body.FacingMdeg));
        Assert.Empty(loaded.Pieces);
        Assert.Equal(0, session.SubscriberFailures);
    }

    /// <summary>
    /// L8: a new game walks to the timber stack, takes 3 timber, and builds a pad and a wall from (88.5, 116.0); the save loads equal.
    /// The playthrough's <c>m7_build</c> beat plays the same.
    /// </summary>
    [Fact]
    public void ANewGame_TakesTimber_BuildsAPadAndAWall_AndLoadsEqual()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", Seed);
        var placed = new List<PiecePlaced>();
        var rebuilt = new List<NavigationRebuilt>();
        session.Subscribe<PiecePlaced>(placed.Add);
        session.Subscribe<NavigationRebuilt>(rebuilt.Add);
        var player = simulation.PlayerId;

        StandAt(session, new WorkshopPose(new[] { (40.0, 140.0), (47.0, 139.0), (51.8, 136.0), (65.0, 136.0) }.ToImmutableArray(), 84.0, 116.6, 0),
            "the timber stack");
        Assert.Null(Submit(session, new MoveItemCommand(player, "container.timber_stack#00", ItemPlace.In("container.timber_stack"), ItemPlace.Carried, 3)));
        Assert.Equal(3, TimberCarried(simulation));
        StandAt(session, new WorkshopPose(ImmutableArray<(double, double)>.Empty, 88.5, 116.0, 180), "the building place");
        Assert.Null(Submit(session, new PlacePieceCommand(player, Pad, 88_500, 112_500, 0)));
        Assert.Null(Submit(session, new PlacePieceCommand(player, Wall, 87_000, 112_500, 1)));

        Assert.Equal(2, placed.Count);
        Assert.Single(rebuilt);
        Assert.Equal(2, simulation.World.StructureSequence);
        var stack = simulation.Containers.Single(c => c.Site.Key == "container.timber_stack");
        Assert.NotNull(stack.Id);
        Assert.Equal(77, stack.Items.Where(i => i.DefId == Timber).Sum(i => i.Count));
        Assert.Equal(0, TimberCarried(simulation));

        string before = StateDump.Render(simulation);
        session.Save(SaveSlots.Manual("first_build"));
        var fresh = Harness.Boot(profile);
        Assert.True(fresh.Load(SaveSlots.Manual("first_build")).IsComplete);
        var differences = StateDump.Compare(before, StateDump.Render(fresh.Simulation!), out int leaves);
        Assert.True(differences.Count == 0, $"{differences.Count} of {leaves} fields differ: {string.Join("; ", differences.Take(5))}");
        Assert.Equal(0, session.SubscriberFailures);
        Assert.Equal(0, fresh.SubscriberFailures);
    }

    /// <summary>
    /// R-A7: a wolf chasing the character round the built workshop steers straight at them - creatures never path - so it runs into the
    /// walls and slides along them, and on no tick does its body overlap any part of any piece.
    /// </summary>
    [Fact]
    public void ACreatureChasingRoundTheWorkshop_NeverOverlapsAPiece()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (102.0, 102.0), 0, new[] { (Arena.Wolf, 104.0, 93.0, "pack_hunter") },
            r => r.WithInventory(r.Inventory.Concat(new[] { Arena.Stack(Timber, 20), Arena.Stack(Timber, 20), Arena.Stack(Timber, 5) })));
        BuildingTests.WorkshopStepOne(arena);
        var simulation = arena.Simulation;
        long radius = session.Setup.Combat.Creatures[Arena.Wolf].RadiusMm;
        var parts = simulation.Pieces.SelectMany(p => p.Parts).Select(p => new BoxBlocker("part", p.MinXMm, p.MinZMm, p.MaxXMm, p.MaxZMm, p.HeightMm)).ToList();
        Assert.NotEmpty(parts);

        // Out by the doorway (the wolf, 5 m off, hears the run), then laps hugging the outside of the workshop with the wolf behind.
        // Kinematics.Step resolves a push in doubles and rounds the body to whole millimetres, so at a convex corner a body may sit up to
        // a millimetre inside contact - against an authored box as against a piece (Phase 1); anything more is an overlap.
        double closest = double.MaxValue;
        int watched = 0;
        bool engaged = false;
        void Watch()
        {
            var wolf = arena.Creature();
            if (!wolf.Alive)
                return;
            watched++;
            engaged |= wolf.Mind == CreatureMind.Engaged;
            foreach (var part in parts)
            {
                double gap = part.DistanceTo(wolf.Body.XMm, wolf.Body.ZMm) - radius;
                Assert.True(gap > -1, $"the wolf at {wolf.Body} overlaps the part x [{part.MinXMm}, {part.MaxXMm}] z [{part.MinZMm}, {part.MaxZMm}] " +
                    $"by {-gap:0.##} mm at tick {simulation.WorldTick}");
                closest = Math.Min(closest, gap);
            }
        }
        var lap = new[] { (100.5, 100.3), (100.5, 98.1), (105.9, 98.1), (105.9, 105.9), (98.1, 105.9), (98.1, 98.1), (105.9, 98.1), (105.9, 105.9), (98.1, 105.9) };
        foreach (var (x, z) in lap)
        {
            for (int i = 0; i < 600; i++)
            {
                var body = simulation.Player.Body;
                double dx = x * 1000 - body.XMm, dz = z * 1000 - body.ZMm;
                if (Math.Sqrt(dx * dx + dz * dz) <= 300 || simulation.Combat.Health <= 0)
                    break;
                simulation.Enqueue(new MoveCommand(arena.Player, Harness.Toward(dx, dz)));
                arena.Tick();
                Watch();
            }
        }
        Assert.True(engaged, "the wolf never took up the chase");
        Assert.True(watched > 200, $"the wolf was watched for only {watched} ticks");
        // It met the workshop: pressed against a part, not merely near one.
        Assert.True(closest <= 60, $"the wolf came no closer than {closest:0} mm to a part");
    }
}
