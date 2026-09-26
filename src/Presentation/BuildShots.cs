// UNNAMED Presentation - M7's scripted proof of navigation and building in the playable build (M7 design §13.3-§13.5)
// Godot presentation only: it submits commands and reads views, as a player's keys would (D-11)

using System.Text;
using System.Text.RegularExpressions;
using Godot;
using UNNAMED.Application;
using UNNAMED.Application.Evidence;
using UNNAMED.Domain.Building;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Ui;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using static UNNAMED.Application.Evidence.CrossingWorkshop;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>--build-shots dir</c>: the M7 beats, played through the real command path one tick a frame, a still at each that asks for one. From
/// E5 the run starts from the committed start save S0 (§13.3), copied into <c>dir/profile/quick</c> before the session boots, and plays the
/// Crossing Workshop's command table (<see cref="CrossingWorkshop.Rows"/>) beat by beat (§13.4): every beat, row and action whose slice has
/// landed. It saves to <c>quick</c> at b19 and goes on 600 ticks. <c>--build-shots-verify dir</c> loads that save and compares (§13.5).
/// Writes <c>transcript.md</c>, <c>commands.tsv</c> and the state files beside the stills. Scripts may name content; the game may not.
/// </summary>
public sealed class BuildShots
{
    private const float Leg = 0.3f;
    private const long PoseMm = 50;
    private static readonly Regex InstanceId = new("[a-z]{3}_[0-9A-Z]{26}", RegexOptions.CultureInvariant);

    /// <summary>Where the committed start save lives, under the repository.</summary>
    public static readonly string[] StartSave = { "tests", "Application.Tests", "GameSaves", "m7_crossing_start", "save" };

    private sealed record Beat(string Name, string Says, int Budget, bool Still, Func<bool> Run);

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly NavigationOverlay _overlay;
    private readonly Action<BuildDebugStage> _stage;
    private readonly BuildMode _build;
    private readonly HelpPanel _help;
    private readonly StructuresView _structures;
    private readonly Art.ArtCoverage _coverage;
    private readonly bool _verify;
    private readonly LoadResult? _loaded;
    private readonly List<Beat> _beats = new();
    private readonly StringBuilder _transcript = new();
    private readonly List<string> _refused = new();
    private readonly List<PiecePlaced> _placed = new();
    private readonly List<NavigationRebuilt> _rebuilt = new();
    private readonly List<RoutePlanned> _routes = new();
    private readonly List<CompanionCaughtUp> _caughtUp = new();
    private readonly List<string> _toasts = new();
    private readonly List<DoorToggled> _toggled = new();
    private readonly List<PieceRemoved> _removed = new();
    private readonly List<PieceDamaged> _damaged = new();
    private readonly List<PieceRepaired> _repaired = new();
    private readonly List<PieceDestroyed> _destroyed = new();
    private readonly List<long> _walkNorth = new();
    private readonly List<Action> _checks = new();
    private readonly List<(long Tick, string Row)> _rowStarts = new();
    private readonly Dictionary<WorkshopAction, long> _appliedAt = new(ReferenceEqualityComparer.Instance);
    private int _beat;
    private long _beatTick;
    private int _waypoint;
    private int _phase;
    private int _stepAt;
    private string? _still;
    private string? _broken;

    // The row being played, and where it has got to.
    private string? _rowId;
    private int _rowStage;
    private int _action;
    private int _held;
    private long _lastActionTick;
    private (long XMm, long ZMm, bool OnCreature) _aimed;
    private long _waitUntil;

    // Tavar from the order on (R45): whether he has come through the doorway's opening, northwards.
    private Body? _tavarLast;
    private bool _tavarPassed;
    private long _continueUntil;

    public BuildShots(GameSession session, PlayerController controller, CameraRig camera, NavigationOverlay overlay, Action<BuildDebugStage> stage,
        BuildMode build, HelpPanel help, Hud hud, StructuresView structures, Art.ArtCoverage coverage, string directory, bool verify, LoadResult? loaded)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        _overlay = overlay;
        _stage = stage;
        _build = build;
        _help = help;
        _structures = structures;
        _coverage = coverage;
        _verify = verify;
        _loaded = loaded;
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
        session.Subscribe<CommandRejected>(e => _refused.Add($"{e.Command.GetType().Name}: {e.Reason}"));
        session.Subscribe<DoorToggled>(e =>
        {
            _toggled.Add(e);
            Row($"`DoorToggled` {e.DoorKey} {(e.Open ? "open" : "shut")} by {e.Actor}", e.Tick);
        });
        session.Subscribe<PiecePlaced>(e =>
        {
            _placed.Add(e);
            Row($"`PiecePlaced` {e.DefId} at ({e.XMm}, {e.ZMm}) r{e.Rotation}, sequence {e.Revision}", e.Tick);
        });
        session.Subscribe<PieceRemoved>(e =>
        {
            _removed.Add(e);
            Row($"`PieceRemoved` {e.DefId}, refund {e.Refund.Sum(c => c.Count)}, sequence {e.Revision}", e.Tick);
        });
        session.Subscribe<PieceDamaged>(e =>
        {
            _damaged.Add(e);
            Row($"`PieceDamaged` {e.DefId} -{e.Amount} ({e.Source}), now {e.HealthNow}", e.Tick);
        });
        session.Subscribe<PieceRepaired>(e =>
        {
            _repaired.Add(e);
            Row($"`PieceRepaired` {e.From} -> {e.To}", e.Tick);
        });
        session.Subscribe<PieceDestroyed>(e =>
        {
            _destroyed.Add(e);
            Row($"`PieceDestroyed` {e.DefId} ({e.Source}), sequence {e.Revision}", e.Tick);
        });
        session.Subscribe<NavigationRebuilt>(e =>
        {
            _rebuilt.Add(e);
            Row($"`NavigationRebuilt` {e.NodesRestamped} nodes", e.Tick);
        });
        session.Subscribe<RoutePlanned>(e =>
        {
            _routes.Add(e);
            Row($"`RoutePlanned` {e.MoverKey}: {e.Outcome}, {e.Reason}, {e.Corners} corners, {e.Expansions} expansions", e.Tick);
        });
        session.Subscribe<CompanionCaughtUp>(e =>
        {
            _caughtUp.Add(e);
            Row($"`CompanionCaughtUp` {e.NpcId}: {e.Reason}", e.Tick);
        });
        hud.Toasted += text => _toasts.Add(text);
        if (verify)
            VerifyBeats();
        else
            PlayBeats();
        var simulation = session.Simulation!;
        _beatTick = simulation.WorldTick;
        _transcript.AppendLine(verify ? "# M7 build shots: the relaunch" : "# M7 build shots").AppendLine()
            .AppendLine($"Content {session.Content.Version} ({session.Content.Hash}); world seed {WorldSeed.Format(simulation.World.WorldSeed)}; " +
                        (verify ? "the run's quick save loaded" : "from the committed start save S0") + $" at tick {simulation.WorldTick}; slice E{Landed}; " +
                        "one tick a frame, the real command path.").AppendLine()
            .AppendLine("| Tick | What happened |").AppendLine("|---|---|");
    }

    public string Directory { get; }

    /// <summary>Whether this is the relaunch (<c>--build-shots-verify</c>).</summary>
    public bool Verify => _verify;

    /// <summary>
    /// Clear what a previous run left in <paramref name="directory"/> before the session boots (L-20): its profile, transcript, command log,
    /// state files and stills. Then S0 goes into <c>profile/quick</c>. Null, or why the start save could not be put there.
    /// </summary>
    public static string? Prepare(string directory, string home)
    {
        if (System.IO.Directory.Exists(directory))
        {
            string old = Path.Combine(directory, "profile");
            if (System.IO.Directory.Exists(old))
                System.IO.Directory.Delete(old, recursive: true);
            foreach (string file in System.IO.Directory.EnumerateFiles(directory)
                         .Where(f => Path.GetExtension(f) is ".jpg" or ".json" or ".tsv" or ".md" or ".txt"))
                File.Delete(file);
        }
        string source = Path.Combine(new[] { home }.Concat(StartSave).ToArray());
        if (!System.IO.Directory.Exists(source) || System.IO.Directory.GetFiles(source).Length == 0)
            return $"the start save is not at {source}";
        string quick = Path.Combine(directory, "profile", SaveSlots.Quick);
        System.IO.Directory.CreateDirectory(quick);
        foreach (string file in System.IO.Directory.GetFiles(source))
            File.Copy(file, Path.Combine(quick, Path.GetFileName(file)));
        return null;
    }

    /// <summary>Advance one frame: the name of a still to take now, "done", "failed", or null.</summary>
    public string? Update()
    {
        var simulation = _session.Simulation!;
        RunChecks();
        if (_beat >= _beats.Count)
        {
            if (_broken is null && _session.SubscriberFailures != 0)
                _broken = $"{_session.SubscriberFailures} subscriber failures";
            if (_broken is { } finalWhy)
                return Failed("the end", finalWhy);
            Row($"Done: {_beats.Count} beats; subscriber failures {_session.SubscriberFailures}; " +
                $"piece art fallbacks (piece:*) {PieceFallbacks()}", simulation.WorldTick);
            Write();
            return "done";
        }
        var beat = _beats[_beat];
        if (_stepAt == 0 && _phase == 0)
            _stage(BuildDebugStage.Off);
        if (_broken is null && simulation.WorldTick - _beatTick > beat.Budget)
            _broken = $"it did not finish in {beat.Budget} ticks (refusals: {string.Join("; ", _refused.TakeLast(3))})";
        bool done = false;
        if (_broken is null)
        {
            try
            {
                done = beat.Run();
                EveryFrame();
            }
            catch (Exception e)
            {
                _broken = $"{e.GetType().Name}: {e.Message}";
            }
        }
        if (_broken is { } why)
            return Failed(beat.Name, why);
        if (_still is { } extra)
        {
            _still = null;
            return extra;
        }
        if (!done)
            return null;
        Row($"**{beat.Name}**: {beat.Says} (in {simulation.WorldTick - _beatTick} ticks){(beat.Still ? $" ![{beat.Name}]({beat.Name}.jpg)" : "")}", simulation.WorldTick);
        _beat++;
        _beatTick = simulation.WorldTick;
        _waypoint = 0;
        _phase = 0;
        _stepAt = 0;
        Write();
        return beat.Still ? beat.Name : null;
    }

    private string Failed(string beat, string why)
    {
        var simulation = _session.Simulation!;
        Row($"FAILED at '{beat}': {why}, at ({simulation.Player.Body.XMm / 1000.0:0.00}, {simulation.Player.Body.ZMm / 1000.0:0.00})", simulation.WorldTick);
        Write();
        GD.PushError($"UNNAMED build shots: '{beat}' failed: {why}");
        return "failed";
    }

    /// <summary>The <c>piece:*</c> art fallbacks recorded so far (R16): counted and reported, never allowlisted, never a failure.</summary>
    private string PieceFallbacks() =>
        $"{_coverage.Entries.Count(e => e.Kind == "piece" && e.Fallbacks > 0)} of {_coverage.Entries.Count(e => e.Kind == "piece")} piece entries";

    // ── the beats ───────────────────────────────────────────────────────────

    private void PlayBeats()
    {
        _beats.AddRange(new[]
        {
            new Beat("b02_grid_corner", "F2's navigation stage where the four cells meet at (100, 100), from S0: walkable on both sides of both seams, no gap, no row twice",
                60, true, GridAtTheCorner),
            new Beat("b03_build_mode", "Build mode at (102, 102): the area outlined, the pad's ghost allowed over square (33, 33), its cost in words; F1's BUILDING section",
                300, false, () => Steps(() => Play("R00"), () => Play("R01"), BuildModeOpened, () => Still("b03_build_mode"), HelpOpened,
                    () => Still("b03_help"), HelpClosed)),
            new Beat("b04_pads", "Four pads over the four-cell corner: sequence 1-4, the (33, 33) pad hosted in c_01_01, no rebuild", 60, false,
                () => Steps(() => Play("R02"), () => Then(PadsPlaced))),
            new Beat("b05_edges", "The doorway across x = 100 and seven walls: sequence 5-12, exactly 8 rebuilds; seen from 9 m (CameraRig.Cap)", 120, true,
                () => Steps(() => Play("R03"), () => Play("R04"), () => Then(EdgesPlaced), () => Look(200, CameraRig.BuildMaxDistance))),
            new Beat("b06_roofs_and_door", "Four roofs, sequence 13-16 with no rebuild; the door hung in the doorway, sequence 17, one rebuild, shut: 17 pieces for 25 timber so far",
                120, true, () => Steps(() => Play("R05"), () => Play("R06"), () => Then(RoofsAndDoorPlaced), () => Look(200, CameraRig.BuildMaxDistance))),
            new Beat("b07_bench_and_chest", "The bench across x = 100 and the chest in the north-east square, sequence 18 and 19: step 1 is 19 pieces for 31 timber, their IDs derived 1-19",
                60, true, () => Steps(() => Play("R07"), () => Play("R08"), () => Then(BenchAndChestPlaced), () => Look(0, CameraRig.BuildMaxDistance))),
            new Beat("b08_overlap_refused", "A wall on the doorway's edge: refused in words - the ghost and the toast say why - and nothing changes", 60, true,
                () => Steps(OverlapGhost, () => Play("R09"), () => Then(OverlapRefused), () => Look(200, CameraRig.BuildMaxDistance))),
            new Beat("b09_door_and_inside", "The door opened from inside and shut from outside; the walk north stopped by the shut leaf; opened with E; the aim from inside stops at the west wall",
                600, true, () => Steps(ExitBuildMode, () => Play("R10"), () => Then(() => DoorIs(true, "R10")), () => Play("R11"),
                    () => Then(() => DoorIs(false, "R11")), () => Play("R12"), StoppedByTheDoor, () => Play("R13"), () => Then(() => DoorIs(true, "R13")),
                    () => Play("R14"), AimStopsAtTheWall, () => Look(90, 4f))),
            new Beat("b10_craft_at_home", "The March Spear made at the placed bench, 1.36 m from its site, no authored anvil in reach", 300, false,
                () => Steps(() => Play("R15"), () => Then(CraftedAtHome))),
            new Beat("b14_vestibule", "South of the door, a pad and two walls; the wall that would close the vestibule refused as unnavigable - the red ghost and the toast say why - then all taken down for 1, 1 and 0 timber",
                600, false, () => Steps(() => Play("R22"), () => Play("R23"), () => Then(() => ExpectAccepted("R22", "R23")), VestibuleGhost, () => Play("R24"),
                    () => Then(VestibuleRefused), () => Look(0, CameraRig.BuildMaxDistance), () => Still("b14_vestibule"), ExitBuildMode, () => Play("R25"),
                    () => Then(VestibuleDown))),
            new Beat("b15_blows_and_mending", "Round the east side to the north wall: three blows (170), mended with T for one timber (200), a fourth blow (190); its target line in words",
                600, false, () => Steps(() => Play("R26"), () => Play("R27"), () => Play("R28"), () => Then(BlowsAndMend), () => Play("R29"), () => Wait(20),
                    () => Then(FourthBlow), TargetLineShown, () => Look(180, 4f), () => Still("b15_blows_and_mending"), ExitBuildMode)),
            new Beat("b16_chest_cycle_and_spill", "In by the doorway to the chest: two timber stored, taken back, stored again under one derived ID; ten blows destroy it, and the two timber lie where it stood, picked up",
                800, false, () => Steps(() => Play("R30"), () => Play("R31"), () => Then(ChestStored), () => Play("R32"), () => Then(ChestEmptied), () => Play("R33"),
                    () => Then(ChestRefilled), () => Play("R34"), () => Wait(20), () => Then(ChestDestroyed), () => Look(0, 4f), () => Still("b16_chest_cycle_and_spill"),
                    () => Play("R35"), () => Then(SpillPickedUp))),
            new Beat("b17_route_west", "A second chest with two timber in it; west of the workshop: two pads and a wall; the second wall refused while the character stands on its line, then placed",
                1_500, true, () => Steps(() => Play("R36"), () => Play("R37"), () => Then(SecondChest), () => Play("R39"), () => Play("R40"), () => Play("R41"), () => Then(StandingOnTheLine), () => Play("R42"),
                    () => Then(TheLineAtX96), () => Look(90, CameraRig.MaxDistance, BuildDebugStage.Navigation))),
            new Beat("b18_companion_in", "Tavar told to follow from inside: he plans a route round to the doorway", 400, true,
                () => Steps(() => Play("R45"), () => Then(TavarPlans), () => Look(0, CameraRig.MaxDistance, BuildDebugStage.Navigation))),
            new Beat("b19_save", "Saved to quick with Tavar on his route; 600 ticks on he is inside, having come through the doorway", 800, false,
                () => Steps(() => Play("R46"), SaveAndDump, ContinueAndDump)),
        });
    }

    private void VerifyBeats()
    {
        _beats.AddRange(new[]
        {
            new Beat("v1_loaded", "The quick save loaded: complete, the same digest, and field by field as saved", 60, false, LoadedAsSaved),
            new Beat("v2_continued", "600 ticks on from the load: field by field as the run went on, and the same digest", 700, false, ContinuedAsRun),
        });
    }

    /// <summary>b02: at S0 (100.5, 94.5) facing 0, no walk; F2's navigation stage; the grid across both seams within 3 m of the corner.</summary>
    private bool GridAtTheCorner()
    {
        if (!Settle(0))
            return false;
        var grid = _session.Simulation!.Navigation.Grid;
        var sampled = _overlay.Sampled;
        var unique = sampled.Select(n => (n.I, n.J)).ToHashSet();
        Expect(unique.Count == sampled.Count, $"{sampled.Count - unique.Count} nodes drawn twice");
        var near = sampled.Where(n => Math.Abs(grid.CentreOf(n.I) - 100_000) <= 3_000 && Math.Abs(grid.CentreOf(n.J) - 100_000) <= 3_000).ToList();
        var columns = near.Select(n => n.I).Distinct().Order().ToList();
        var rows = near.Select(n => n.J).Distinct().Order().ToList();
        Expect(columns.Count == 24 && rows.Count == 24 && near.Count == 24 * 24, $"{columns.Count} columns, {rows.Count} rows and {near.Count} nodes within 3 m of the corner, not 24, 24 and 576");
        Expect(columns.Zip(columns.Skip(1)).All(p => p.Second == p.First + 1) && rows.Zip(rows.Skip(1)).All(p => p.Second == p.First + 1),
            "a column or row is missing near the corner");
        Expect(columns.Contains(grid.NodeOf(99_999)) && columns.Contains(grid.NodeOf(100_000)) && rows.Contains(grid.NodeOf(99_999)) && rows.Contains(grid.NodeOf(100_000)),
            "the nodes either side of a seam were not both drawn");
        Expect(near.All(n => n.Walkable), $"{near.Count(n => !n.Walkable)} nodes near the corner drawn unwalkable on open ground");
        var tiles = near.Select(n => grid.TryLocate(n.I, n.J, out var tile, out _) ? tile.Key : default).Distinct().Count();
        Expect(tiles == 4, $"the nodes near the corner lie in {tiles} tiles, not 4");
        Row($"b02: {near.Count} nodes within 3 m of (100, 100), columns {columns.First()}-{columns.Last()} and rows {rows.First()}-{rows.Last()}, " +
            $"in {tiles} tiles, all walkable, none twice", _session.Simulation.WorldTick);
        return _broken is null;
    }

    /// <summary>b03: build mode entered, the pad chosen and aimed at (100 500, 100 500).</summary>
    private bool BuildModeOpened()
    {
        if (_phase == 0)
        {
            Expect(_build.Enter(_camera), "build mode would not open");
            Aim(Pad, 100_500, 100_500);
            _phase = 1;
        }
        // A few frames for the ghost to be asked and drawn.
        if (++_phase < 12)
            return false;
        string carried = $"{TimberCarried()} carried";
        Expect(_structures.OutlinesShown, "the build area's outline is not shown");
        Expect(_build.Tone == BuildTone.Allowed, $"the pad's ghost is {_build.Tone}, not allowed");
        Expect(_build.Status == $"Timber Pad: can be built here - 1 Rough Timber ({carried})",
            $"the status line reads \"{_build.Status}\"");
        Expect(TimberCarried() == 45, $"S0 carries {TimberCarried()} timber, not 45");
        Row($"b03: the outline shown; the ghost {_build.Tone}; \"{_build.Status}\"", _session.Simulation!.WorldTick);
        return _broken is null;
    }

    /// <summary>F1 opened over build mode (it is no panel that ends it, §8.4) for its still: the BUILDING section listed.</summary>
    private bool HelpOpened()
    {
        if (_phase == 0)
        {
            _help.Toggle();
            _phase = 1;
            return false;
        }
        if (++_phase < 6)
            return false;
        var headings = _help.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text).ToList();
        Expect(_help.Visible && headings.Contains("BUILDING"), "F1 does not list a BUILDING section");
        return _broken is null;
    }

    private bool HelpClosed()
    {
        _help.Toggle();
        Expect(_build.Active, "build mode ended under F1");
        return true;
    }

    private void PadsPlaced()
    {
        var simulation = _session.Simulation!;
        ExpectAccepted("R02");
        Expect(_placed.Select(p => p.Revision).SequenceEqual(new long[] { 1, 2, 3, 4 }), $"the pads' sequence is {string.Join(", ", _placed.Select(p => p.Revision))}");
        Expect(_placed.Count > 0 && simulation.World.Piece(_placed[0].PieceId)?.HostCell == "r_0_0:c_01_01", "the (33, 33) pad is not hosted in c_01_01");
        Expect(_rebuilt.Count == 0, $"{_rebuilt.Count} rebuilds for pads");
    }

    private void EdgesPlaced()
    {
        ExpectAccepted("R03", "R04");
        Expect(_placed.Skip(4).Select(p => p.Revision).SequenceEqual(Enumerable.Range(5, 8).Select(n => (long)n)),
            $"the edges' sequence is {string.Join(", ", _placed.Skip(4).Select(p => p.Revision))}");
        Expect(_rebuilt.Count == 8, $"{_rebuilt.Count} rebuilds for the doorway and seven walls, not 8");
    }

    private void RoofsAndDoorPlaced()
    {
        var simulation = _session.Simulation!;
        ExpectAccepted("R05", "R06");
        Expect(_placed.Skip(12).Take(4).Select(p => p.Revision).SequenceEqual(Enumerable.Range(13, 4).Select(n => (long)n)),
            $"the roofs' sequence is {string.Join(", ", _placed.Skip(12).Take(4).Select(p => p.Revision))}");
        Expect(_placed.Count == 17 && _placed[16].DefId == Door && _placed[16].Revision == 17, "the door was not placed 17th");
        Expect(_rebuilt.Count == 9, $"{_rebuilt.Count} rebuilds after the roofs and the door, not 9 (the door's one)");
        Expect(simulation.Pieces.SingleOrDefault(p => p.DefId == Door) is { DoorOpen: false }, "the door is not hung shut");
        Expect(simulation.Pieces.Length == 17 && 45 - TimberCarried() == 25 && simulation.World.StructureSequence == 17,
            $"the roofs and the door left {simulation.Pieces.Length} pieces, {45 - TimberCarried()} timber spent and sequence " +
            $"{simulation.World.StructureSequence}, not 17 / 25 / 17");
    }

    /// <summary>b07: the bench and the chest end step 1 (§4.22's counts), a rebuild each; every piece's ID derived from its ordinal.</summary>
    private void BenchAndChestPlaced()
    {
        var simulation = _session.Simulation!;
        ExpectAccepted("R07", "R08");
        Expect(_placed.Count == 19 && _placed[17].DefId == Bench && _placed[17].Revision == 18 && _placed[18].DefId == Chest && _placed[18].Revision == 19,
            "the bench and the chest were not placed 18th and 19th");
        Expect(_rebuilt.Count == 11, $"{_rebuilt.Count} rebuilds after the bench and the chest, not 11");
        var (pieces, spent, sequence) = Counts.StepOne;
        Expect(simulation.Pieces.Length == pieces && 45 - TimberCarried() == spent && simulation.World.StructureSequence == sequence,
            $"step 1 left {simulation.Pieces.Length} pieces, {45 - TimberCarried()} timber spent and sequence {simulation.World.StructureSequence}, " +
            $"not {pieces} / {spent} / {sequence}");
        var derived = Enumerable.Range(1, pieces).Select(n => Domain.EntityId.Derived(Domain.EntityKind.Piece, n, "unnamed.piece/v1", simulation.PlayerId.Value));
        Expect(simulation.Pieces.Select(p => p.Id).Order().SequenceEqual(derived.Order()), "the pieces' IDs are not Derived(Piece, 1..19)");
        Row($"Step 1: {simulation.Pieces.Length} pieces, {45 - TimberCarried()} timber spent, sequence {simulation.World.StructureSequence}", simulation.WorldTick);
    }

    private int _ingotsBefore = -1;

    /// <summary>b10: the spear made at the placed bench, 1.36 m from its site; every authored anvil out of reach; an ingot spent.</summary>
    private void CraftedAtHome()
    {
        var simulation = _session.Simulation!;
        Expect(Outcome("R15") is null, $"the spear at the bench: \"{Outcome("R15")}\"");
        var body = simulation.Player.Body;
        var bench = simulation.Pieces.Single(p => p.DefId == Bench);
        var site = simulation.Stations.Single(s => s.Key == bench.StationKey);
        double distance = Math.Sqrt(Math.Pow(site.XMm - body.XMm, 2) + Math.Pow(site.ZMm - body.ZMm, 2));
        Expect(distance is >= 1_300 and <= 1_420, $"the spear was made {distance:0} mm from the bench's site, not about 1 360");
        Expect(_session.Setup.Layout.Stations.Where(s => s.Kind == "anvil")
                .All(s => Math.Sqrt(Math.Pow(s.XMm - body.XMm, 2) + Math.Pow(s.ZMm - body.ZMm, 2)) > _session.Setup.Items.Inventory.ReachMm),
            "an authored anvil is in reach");
        Expect(Ingots() == _ingotsBefore - 1, $"{Ingots()} ingots carried after the spear, not {_ingotsBefore - 1}");
        Row($"R15: the March Spear made {distance:0} mm from the bench's site", simulation.WorldTick);
    }

    private int Ingots() => _session.Simulation!.Player.Inventory.Where(e => e.DefId == IronIngot).Sum(e => e.Count);

    private (Domain.EntityId? North, int Timber) _mend;

    /// <summary>b15, after R28: three melee blows of 10 on the north wall (170), then its mending for one timber (170 to 200).</summary>
    private void BlowsAndMend()
    {
        var simulation = _session.Simulation!;
        var north = simulation.Pieces.Single(p => p.DefId == Wall && p.XMm == 100_500 && p.ZMm == 105_000);
        Expect(_damaged.Select(d => d.HealthNow).SequenceEqual(new[] { 190, 180, 170 }), $"the blows left {string.Join(", ", _damaged.Select(d => d.HealthNow))}");
        Expect(_damaged.All(d => d.PieceId == north.Id && d.Source == "melee" && d.Amount == 10), "a blow was not a melee 10 on the north wall");
        Expect(Outcome("R28") is null, $"the mending: \"{Outcome("R28")}\"");
        Expect(_repaired.Count == 1 && _repaired[0].PieceId == north.Id && _repaired[0].From == 170 && _repaired[0].To == 200,
            $"the mending was {string.Join("; ", _repaired.Select(r => $"{r.From} -> {r.To}"))}, not 170 -> 200");
        Expect(TimberCarried() == _mend.Timber - 1, $"the mending cost {_mend.Timber - TimberCarried()} timber, not 1");
        _mend.North = north.Id;
    }

    private void FourthBlow()
    {
        var wall = _session.Simulation!.Pieces.Single(p => p.Id == _mend.North);
        Expect(wall.HealthCurrent == 190, $"after the fourth blow the north wall is at {wall.HealthCurrent}, not 190");
    }

    /// <summary>b15's still: build mode, facing the north wall; its target line names it, its health and the keys, in words.</summary>
    private bool TargetLineShown()
    {
        if (_phase == 0)
        {
            Expect(_build.Enter(_camera), "build mode would not open");
            Aim(Pad, 100_500, 106_500);
            _phase = 1;
        }
        if (++_phase < 12)
            return false;
        const string want = "Timber Wall 190/200 - [T] mend   [Z] take down";
        Expect(_build.TargetLine == want, $"the target line reads \"{_build.TargetLine}\", not \"{want}\"");
        Row($"b15: the target line \"{_build.TargetLine}\"", _session.Simulation!.WorldTick);
        return _broken is null;
    }

    private (string Key, Domain.EntityId? Derived, Domain.EntityId? Chest) _chest;

    /// <summary>b16, after R31: the chest's record materialises under the ID derived from the piece's, with the two timber in it.</summary>
    private void ChestStored()
    {
        var simulation = _session.Simulation!;
        var chest = simulation.Pieces.Single(p => p.DefId == Chest && p.XMm == 103_500 && p.ZMm == 103_500);
        _chest = (chest.ContainerKey!, Domain.EntityId.Derived(Domain.EntityKind.Container, chest.Id.Timestamp, "unnamed.piece-container/v1", chest.Id.Value), chest.Id);
        Expect(Outcome("R31") is null, $"storing two timber: \"{Outcome("R31")}\"");
        Expect(simulation.World.Container(_chest.Key) is { } record && record.InstanceId == _chest.Derived && record.Items.Sum(i => i.Count) == 2,
            "the chest's record did not materialise with its derived ID and two timber");
    }

    private void ChestEmptied()
    {
        var simulation = _session.Simulation!;
        Expect(Outcome("R32") is null, $"taking all: \"{Outcome("R32")}\"");
        Expect(simulation.World.Container(_chest.Key) is { } record && record.InstanceId == _chest.Derived && record.Items.IsEmpty,
            "the emptied chest's record did not stay, empty, with its derived ID");
    }

    private ContainerItem? _stored;

    private void ChestRefilled()
    {
        var simulation = _session.Simulation!;
        Expect(Outcome("R33") is null, $"storing two timber again: \"{Outcome("R33")}\"");
        var record = simulation.World.Container(_chest.Key);
        Expect(record is { Items.Length: 1 } && record.InstanceId == _chest.Derived && record.Items[0].Count == 2,
            "the refilled chest does not hold one stack of two under its derived ID");
        _stored = record?.Items.FirstOrDefault();
    }

    /// <summary>b16, 20 ticks after R34's tenth blow: the chest destroyed by it, its record gone, the two timber lying at its site.</summary>
    private void ChestDestroyed()
    {
        var simulation = _session.Simulation!;
        Expect(_destroyed.Count == 1 && _destroyed[0].PieceId == _chest.Chest && _destroyed[0].DefId == Chest && _destroyed[0].Source == "melee",
            "the tenth blow did not destroy the chest");
        Expect(_damaged.Skip(4).Select(d => d.HealthNow).SequenceEqual(Enumerable.Range(1, 9).Select(n => 100 - 10 * n)),
            $"the chest's blows left {string.Join(", ", _damaged.Skip(4).Select(d => d.HealthNow))}");
        Expect(simulation.World.Container(_chest.Key) is null, "the destroyed chest's record remains");
        Expect(_stored is { } stored && simulation.WorldItems.Any(i => i.Id == stored.ItemId && i.DefId == Timber && i.Count == 2 && i.XMm == 103_500 && i.ZMm == 104_400),
            "the two timber do not lie at (103.5, 104.4) with their item ID");
    }

    private void SpillPickedUp()
    {
        var simulation = _session.Simulation!;
        Expect(Outcome("R35") is null, $"the pick-up: \"{Outcome("R35")}\"");
        Expect(_stored is { } stored && simulation.WorldItems.All(i => i.Id != stored.ItemId), "the spilled timber still lies there");
    }

    /// <summary>b17's chest rows: a second chest east of the doorway, with two timber in it.</summary>
    private void SecondChest()
    {
        var simulation = _session.Simulation!;
        ExpectAccepted("R36", "R37");
        var chest = simulation.Pieces.SingleOrDefault(p => p.DefId == Chest);
        Expect(chest is not null && simulation.World.Container(chest.ContainerKey!) is { } record && record.Items.Sum(i => i.Count) == 2,
            "the second chest does not hold two timber");
    }

    /// <summary>b08: the wall chosen and aimed at the doorway's edge: the ghost refuses, in the words the command will.</summary>
    private bool OverlapGhost()
    {
        if (_phase == 0)
        {
            Aim(Wall, 100_500, 99_000);
            _phase = 1;
        }
        if (++_phase < 12)
            return false;
        Expect(_build.Tone == BuildTone.Refused, $"the wall's ghost on the doorway is {_build.Tone}, not refused");
        Expect(_build.Status?.Contains("a Timber Doorway already stands there", StringComparison.Ordinal) == true,
            $"the ghost's status reads \"{_build.Status}\"");
        _overlapBefore = (_session.Simulation!.Pieces.Select(p => p.Id).ToList(), _session.Simulation.World.StructureSequence, TimberCarried());
        return _broken is null;
    }

    private (List<Domain.EntityId> Pieces, long Sequence, int Timber) _overlapBefore;

    private void OverlapRefused()
    {
        var simulation = _session.Simulation!;
        Expect(Outcome("R09") == "a Timber Doorway already stands there", $"the overlapping wall: \"{Outcome("R09") ?? "(accepted)"}\"");
        Expect(_toasts.Any(t => t.Contains("a Timber Doorway already stands there", StringComparison.Ordinal)),
            $"no toast said why; the last: \"{_toasts.LastOrDefault()}\"");
        // The digest carries the tick, and a command applies only as a tick runs, so the headless twin (CrossingWorkshop_1to3) holds the
        // digest; here the pieces, the sequence and the pack are as they were.
        Expect(simulation.Pieces.Select(p => p.Id).SequenceEqual(_overlapBefore.Pieces) && simulation.World.StructureSequence == _overlapBefore.Sequence
               && TimberCarried() == _overlapBefore.Timber, "the refused wall changed the pieces, the sequence or the pack");
    }

    private bool ExitBuildMode()
    {
        _build.ScriptedAim = null;
        _build.Exit(_camera);
        return true;
    }

    private (BuildTone Tone, string? Status, long Refusals) _vestibuleGhost;

    /// <summary>b14: build mode, the wall chosen and aimed across the vestibule's open side; the ghost, asked with navigability, refuses.</summary>
    private bool VestibuleGhost()
    {
        if (_phase == 0)
        {
            Expect(_build.Enter(_camera), "build mode would not open");
            Aim(Wall, 100_500, 96_000);
            _phase = 1;
        }
        if (++_phase < 12)
            return false;
        _vestibuleGhost = (_build.Tone, _build.Status, _session.Simulation!.Navigation.Counters.EditRefusalsByRule.GetValueOrDefault("V-N1"));
        Expect(_build.Tone == BuildTone.Refused, $"the vestibule wall's ghost is {_build.Tone}, not refused");
        return _broken is null;
    }

    /// <summary>
    /// The command refuses as the ghost did, in the same words, as an unnavigable edit (rule V-N1, counted by the command path; E9 names
    /// Kera's work place), and nothing is built.
    /// </summary>
    private void VestibuleRefused()
    {
        var simulation = _session.Simulation!;
        string? refused = Outcome("R24");
        Expect(refused is not null, "the vestibule wall was placed");
        string words = $"Timber Wall: {BuildMode.Words(_session, refused ?? "")}";
        Expect(_vestibuleGhost.Status == words, $"the ghost said \"{_vestibuleGhost.Status}\", the command \"{words}\"");
        Expect(_toasts.Any(t => t.Contains(BuildMode.Words(_session, refused ?? "?"), StringComparison.Ordinal)), "no toast said why");
        Expect(simulation.Navigation.Counters.EditRefusalsByRule.GetValueOrDefault("V-N1") == _vestibuleGhost.Refusals + 1,
            "the command's refusal was not counted as rule V-N1");
        Expect(!simulation.Pieces.Any(p => p.XMm == 100_500 && p.ZMm == 96_000), "a wall stands across the vestibule");
        Row($"R24: the vestibule refused - \"{refused}\"", simulation.WorldTick);
    }

    private void VestibuleDown()
    {
        ExpectAccepted("R25");
        Expect(_removed.TakeLast(3).Select(r => r.Refund.Sum(c => c.Count)).SequenceEqual(new[] { 1, 1, 0 }),
            $"the vestibule came down for {string.Join(", ", _removed.TakeLast(3).Select(r => r.Refund.Sum(c => c.Count)))} timber, not 1, 1, 0");
    }

    /// <summary>The door as a row left it, and who worked it: the character.</summary>
    private void DoorIs(bool open, string row)
    {
        Expect(Outcome(row) is null, $"{row}: the door would not work: \"{Outcome(row)}\"");
        var door = _session.Simulation!.Pieces.Single(p => p.DefId == Door);
        Expect(door.DoorOpen == open, $"{row}: the door is {(door.DoorOpen ? "open" : "shut")}");
        Expect(_toggled.LastOrDefault() is { } t && t.DoorKey == door.Id.Value && t.Open == open && t.Actor == _session.Simulation.PlayerId,
            $"{row}: no toggle of the door by the character");
        // Drawn on its hinge (§9.3.3): the leaf's centre R_r(+800, 0) from the hinge shut, R_r(0, +800) open.
        var (hx, hz) = QuarterTurn.Apply(-800, 0, door.Rotation);
        var (dx, dz) = open ? QuarterTurn.Apply(0, 800, door.Rotation) : QuarterTurn.Apply(800, 0, door.Rotation);
        var want = new Vector2((door.XMm + hx + dx) / 1000f, (door.ZMm + hz + dz) / 1000f);
        Expect(_structures.LeafCentre(door.Id.Value) is { } leaf && new Vector2(leaf.X, leaf.Z).DistanceTo(want) < 0.01f,
            $"{row}: the leaf is drawn at {_structures.LeafCentre(door.Id.Value)}, not at {want}");
    }

    /// <summary>R12 from E6: 40 ticks' walk north from (100.5, 97.0) is stopped short of the shut leaf on every tick, as at a wall.</summary>
    private bool StoppedByTheDoor()
    {
        var body = _session.Simulation!.Player.Body;
        Expect(_walkNorth.Count >= 40 && _walkNorth.All(z => z <= 98_450), $"the walk north went to z {_walkNorth.DefaultIfEmpty().Max()}, into the shut door");
        Expect(body.ZMm is >= 98_400 and <= 98_450, $"the walk north ended at z {body.ZMm}, not against the door");
        Row($"R12: stopped at z {body.ZMm} by the shut door, {_walkNorth.Count} ticks", _session.Simulation.WorldTick);
        return _broken is null;
    }

    private bool AimStopsAtTheWall()
    {
        Expect(_aimed.XMm is >= 99_200 and <= 99_210 && !_aimed.OnCreature, $"the aim west stopped at ({_aimed.XMm}, {_aimed.ZMm}), on a creature {_aimed.OnCreature}");
        Row($"R14: the aim west from (100.5, 101.0) stops at x {_aimed.XMm}", _session.Simulation!.WorldTick);
        return _broken is null;
    }

    private void StandingOnTheLine()
    {
        ExpectAccepted("R39", "R40");
        Expect(Outcome("R41") == "someone is standing there", $"the wall on the character's line: \"{Outcome("R41") ?? "(accepted)"}\"");
    }

    private void TheLineAtX96()
    {
        ExpectAccepted("R42");
        var line = _session.Simulation!.Pieces.Where(p => p.DefId == Wall && p.XMm == 96_000).ToList();
        Expect(line.Count == 2 && line.Min(p => p.MinZMm) == 98_800 && line.Max(p => p.MaxZMm) == 105_200,
            $"the x = 96 line is {line.Count} walls over z {(line.Count > 0 ? $"{line.Min(p => p.MinZMm)}-{line.Max(p => p.MaxZMm)}" : "-")}");
    }

    private void TavarPlans()
    {
        Expect(Outcome("R45") is null, $"the order to follow: \"{Outcome("R45")}\"");
        Expect(_routes.Any(r => r.MoverKey == Tavar), "Tavar planned no route");
        var simulation = _session.Simulation!;
        var (pieces, carried, sequence) = Counts.End;
        Expect(simulation.Pieces.Length == pieces && TimberCarried() == carried && simulation.World.StructureSequence == sequence,
            $"the table ended with {simulation.Pieces.Length} pieces, {TimberCarried()} timber carried and sequence {simulation.World.StructureSequence}, " +
            $"not {pieces} / {carried} / {sequence}");
    }

    private bool SaveAndDump()
    {
        var simulation = _session.Simulation!;
        var tavar = simulation.CaptureRecord().Companions.Single(c => c.NpcId == Tavar);
        Expect(tavar.Route.Status == NavRouteStatus.Active, $"at the save Tavar's route is {tavar.Route.Status}, not active");
        File.WriteAllText(Path.Combine(Directory, "state_saved.json"), StateDump.Render(simulation));
        File.WriteAllText(Path.Combine(Directory, "state_replay.json"), StateDump.Render(simulation, replayable: true));
        File.WriteAllText(Path.Combine(Directory, "state_digest.txt"), simulation.StateDigest());
        Row($"Saved to quick at tick {simulation.WorldTick}, Tavar's route {tavar.Route.Status}; digest `{simulation.StateDigest()}`", simulation.WorldTick);
        _continueUntil = simulation.WorldTick + ((SaveAction)LandedRows.Single(r => r.Id == "R46").Landed.Single()).ContinueTicks;
        return _broken is null;
    }

    private bool ContinueAndDump()
    {
        var simulation = _session.Simulation!;
        if (simulation.WorldTick < _continueUntil)
            return false;
        File.WriteAllText(Path.Combine(Directory, "state_continued.json"), StateDump.Render(simulation));
        File.WriteAllText(Path.Combine(Directory, "state_continued_digest.txt"), simulation.StateDigest());
        var tavar = simulation.Companions.Single(c => c.NpcId == Tavar).Body;
        Expect(tavar.XMm is >= 99_200 and <= 104_800 && tavar.ZMm is >= 99_200 and <= 104_800, $"600 ticks on, Tavar is at ({tavar.XMm}, {tavar.ZMm}), not inside");
        Expect(_tavarPassed, "Tavar never came through the doorway's opening");
        Expect(_caughtUp.Count == 0, $"{_caughtUp.Count} companion catch-ups in the run");
        Row($"600 ticks on: Tavar at ({tavar.XMm}, {tavar.ZMm}); digest `{simulation.StateDigest()}`", simulation.WorldTick);
        return _broken is null;
    }

    // ── the relaunch ────────────────────────────────────────────────────────

    private bool LoadedAsSaved()
    {
        var simulation = _session.Simulation!;
        Expect(_loaded is { IsComplete: true }, $"the quick save did not load whole: {(_loaded is null ? "nothing loaded" : string.Join("; ", _loaded.Report.Loss))}");
        Expect(Compare("state_saved.json", "state_loaded.json", "state_diff.txt"), "the load differs from the save");
        Expect(Read("state_digest.txt") == simulation.StateDigest(), $"the digest after the load, {simulation.StateDigest()}, is not the save's");
        _continueUntil = simulation.WorldTick + ((SaveAction)LandedRows.Single(r => r.Id == "R46").Landed.Single()).ContinueTicks;
        return _broken is null;
    }

    private bool ContinuedAsRun()
    {
        var simulation = _session.Simulation!;
        if (simulation.WorldTick < _continueUntil)
            return false;
        Expect(Compare("state_continued.json", "state_continued_loaded.json", "state_continued_diff.txt"), "600 ticks on, the loaded world differs from the run's");
        Expect(Read("state_continued_digest.txt") == simulation.StateDigest(), $"600 ticks on, the digest {simulation.StateDigest()} is not the run's");
        Expect(_session.SubscriberFailures == 0, $"{_session.SubscriberFailures} subscriber failures");
        return _broken is null;
    }

    private string? Read(string file)
    {
        string path = Path.Combine(Directory, file);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    /// <summary>The live state against a file the run wrote, field by field; the dump and the differences written beside it.</summary>
    private bool Compare(string expectedFile, string actualFile, string diffFile)
    {
        var simulation = _session.Simulation!;
        if (Read(expectedFile) is not { } expected)
        {
            _broken ??= $"there is no {expectedFile}: the run never got that far";
            return false;
        }
        string actual = StateDump.Render(simulation);
        File.WriteAllText(Path.Combine(Directory, actualFile), actual);
        var differences = StateDump.Compare(expected, actual, out int leaves);
        var diff = new StringBuilder()
            .AppendLine($"Field-by-field comparison: expected ({expectedFile}) against actual ({actualFile}).").AppendLine()
            .AppendLine($"Fields compared: {leaves}").AppendLine($"Differences: {differences.Count}");
        foreach (string difference in differences)
            diff.AppendLine(difference);
        File.WriteAllText(Path.Combine(Directory, diffFile), diff.ToString());
        Row($"{actualFile}: {leaves} fields compared with {expectedFile}, {differences.Count} differences ({diffFile})", simulation.WorldTick);
        return differences.Count == 0;
    }

    // ── the table's rows ────────────────────────────────────────────────────

    /// <summary>
    /// Play one landed row, a tick a frame: its pose (the run legs, the pose to 50 mm, a frame facing it), or its wait counted in ticks from
    /// the previous row's last action; then its actions, one a tick. True in the frame its last action is submitted: what came of it is read
    /// a frame later, by a check queued with <see cref="Then"/>.
    /// </summary>
    private bool Play(string id)
    {
        var simulation = _session.Simulation!;
        var row = LandedRows.Single(r => r.Id == id);
        if (_rowId != id)
        {
            _rowId = id;
            _rowStage = 0;
            _action = 0;
            _held = 0;
            _waypoint = 0;
        }
        var actions = row.Landed.ToList();
        if (_rowStage == 0)
        {
            if (row.Pose is { } pose)
            {
                if (_waypoint == 0 && simulation.WorldTick < _lastActionTick + row.After)
                    return false;
                if (!Walk(pose.Legs) || !Pose(Mm(pose.X), Mm(pose.Z)))
                    return false;
                Face(pose.FacingDeg);
                _rowStage = 1;
                _rowStarts.Add((simulation.WorldTick, id));
                return actions.Count == 0;
            }
            if (simulation.WorldTick < _lastActionTick + row.After)
                return false;
            _rowStage = 1;
            _rowStarts.Add((simulation.WorldTick, id));
        }
        if (_action >= actions.Count)
            return true;
        var action = actions[_action];
        if (_action > 0 && simulation.WorldTick < _lastActionTick + action.Gap)
            return false;
        if (_action == 0 && action is RepairAction)
            _mend.Timber = TimberCarried();
        if (_action == 0 && action is CraftAction)
            _ingotsBefore = Ingots();
        _appliedAt[action] = simulation.WorldTick;
        switch (action)
        {
            case PlaceAction place:
                if (_build.Active)
                    Aim(place.DefId, place.XMm, place.ZMm);
                _controller.Place(place.DefId, place.XMm, place.ZMm, place.Rotation);
                break;
            case OrderAction order:
                _controller.Order(order.NpcId, order.Order);
                _tavarLast = null;
                _tavarPassed = false;
                break;
            case DismantleAction dismantle:
                var piece = dismantle.Target(simulation) ?? throw new InvalidOperationException($"{row.Id}: no {dismantle.DefId} at ({dismantle.XMm}, {dismantle.ZMm})");
                _controller.Dismantle(piece);
                break;
            case InteractPieceAction interact:
                var target = interact.Target(simulation) ?? throw new InvalidOperationException($"{row.Id}: no {interact.DefId} at ({interact.XMm}, {interact.ZMm})");
                _controller.Interact(target.Value);
                break;
            case HoldAction hold:
                if (_held > 0)
                    _walkNorth.Add(simulation.Player.Body.ZMm);
                if (_held++ < hold.Ticks)
                {
                    _controller.SteerWorld(new Vector3(hold.Intent.DirXPermille, 0, hold.Intent.DirZPermille) / MoveIntent.FullDeflection, hold.Intent.Gait,
                        _camera);
                    return false;
                }
                _controller.SteerWorld(Vector3.Zero, hold.Intent.Gait, _camera);
                break;
            case AimAction aim:
                _aimed = simulation.Aim(aim.FacingMdeg, aim.RangeMm);
                break;
            case SaveAction:
                _session.Save(SaveSlots.Quick);
                break;
            case CraftAction craft:
                _controller.Craft(craft.RecipeId);
                break;
            case AttackAction:
                _controller.Attack();
                break;
            case RepairAction repair:
                _controller.Repair((repair.Piece(simulation) ?? throw new InvalidOperationException($"{row.Id}: no {repair.DefId} at ({repair.XMm}, {repair.ZMm})")).Id);
                break;
            case StoreAction store:
                // What the container panel sends when a stack is moved into the chest.
                _session.Submit(store.Command(simulation) ?? throw new InvalidOperationException($"{row.Id}: no chest, or no stack of {store.Count} timber"));
                break;
            case TakeAllAction takeAll:
                string key = takeAll.Piece(simulation)?.ContainerKey ?? throw new InvalidOperationException($"{row.Id}: no chest at ({takeAll.XMm}, {takeAll.ZMm})");
                _session.Submit(new TakeAllCommand(simulation.PlayerId, key));
                break;
            case PickUpAction pickUp:
                var take = (MoveItemCommand?)pickUp.Command(simulation) ?? throw new InvalidOperationException($"{row.Id}: no timber at ({pickUp.XMm}, {pickUp.ZMm})");
                _controller.PickUp(take.Item);
                break;
            default:
                throw new InvalidOperationException($"{row.Id}: no player for {action}");
        }
        _lastActionTick = simulation.WorldTick;
        return ++_action >= actions.Count;
    }

    /// <summary>What came of an action (its refusal, or null), read once it has applied.</summary>
    private string? Outcome(string rowId, int index = 0)
    {
        var action = LandedRows.Single(r => r.Id == rowId).Landed.ElementAt(index);
        long tick = _appliedAt[action];
        return _session.Simulation!.CommandLog
            .Last(e => e.Tick == tick && e.Command is PlacePieceCommand or OrderCompanionCommand or InteractCommand or DismantlePieceCommand or CraftCommand
                or AttackCommand or RepairPieceCommand or MoveItemCommand or TakeAllCommand).RejectedReason;
    }

    private void ExpectAccepted(params string[] rows)
    {
        foreach (string id in rows)
        {
            var row = LandedRows.Single(r => r.Id == id);
            for (int i = 0; i < row.Landed.Count(); i++)
            {
                if (Outcome(id, i) is { } why)
                    _broken ??= $"{id}'s action {i + 1} was refused: {why}";
            }
        }
    }

    /// <summary>Queue a check for the next frame, when what was just submitted has applied.</summary>
    private bool Then(Action check)
    {
        _checks.Add(check);
        return true;
    }

    private void RunChecks()
    {
        var due = _checks.ToList();
        _checks.Clear();
        foreach (var check in due)
        {
            if (_broken is not null)
                return;
            try
            {
                check();
            }
            catch (Exception e)
            {
                _broken = $"{e.GetType().Name}: {e.Message}";
            }
        }
    }

    /// <summary>Choose a piece in build mode and aim its ghost at a pose, as a player's aim would.</summary>
    private void Aim(string defId, long xMm, long zMm)
    {
        _build.Select(_build.Pieces.ToList().FindIndex(p => p.Id == defId));
        _build.ScriptedAim = (xMm, zMm);
    }

    private bool Steps(params Func<bool>[] steps)
    {
        while (_stepAt < steps.Length)
        {
            if (!steps[_stepAt]())
                return false;
            _stepAt++;
            _waypoint = 0;
            _phase = 0;
        }
        return true;
    }

    /// <summary>Some ticks inside a beat: a blow lands at its swing's end, 8 ticks after it is asked for.</summary>
    private bool Wait(int ticks)
    {
        var simulation = _session.Simulation!;
        if (_phase == 0)
        {
            _waitUntil = simulation.WorldTick + ticks;
            _phase = 1;
        }
        return simulation.WorldTick >= _waitUntil;
    }

    /// <summary>A still in the middle of a beat: taken this frame; the beat goes on next frame.</summary>
    private bool Still(string name)
    {
        if (_phase == 0)
        {
            _still = name;
            Row($"![{name}]({name}.jpg)", _session.Simulation!.WorldTick);
            _phase = 1;
            return false;
        }
        return true;
    }

    // ── every frame ─────────────────────────────────────────────────────────

    /// <summary>
    /// The run-level assertions (§13.4): words only in every toast and status line; the camera's eye never inside a piece's part; and, from
    /// the order on, whether Tavar has come through the doorway's opening.
    /// </summary>
    private void EveryFrame()
    {
        var simulation = _session.Simulation!;
        foreach (string? line in _toasts.Concat(new[] { _build.Status, _build.TargetLine }))
        {
            if (line is null)
                continue;
            if (BuildMode.DottedId.IsMatch(line) || InstanceId.IsMatch(line))
                _broken ??= $"a line shows an ID, not words: \"{line}\"";
        }
        _toasts.Clear();
        var eye = _camera.Camera.GlobalPosition;
        long ex = (long)Math.Round(eye.X * 1000), ez = (long)Math.Round(eye.Z * 1000), ey = (long)Math.Round(eye.Y * 1000);
        long ground = _session.Setup.Layout.Space.Terrain.HeightAtMm(ex, ez);
        // An open door's leaf has swung out of its shut box: only a shut leaf stands there.
        foreach (var part in simulation.Pieces.SelectMany(p => p.Parts.Where(q => q.Traversal == TraversalClass.Solid || !p.DoorOpen)))
        {
            if (ex > part.MinXMm && ex < part.MaxXMm && ez > part.MinZMm && ez < part.MaxZMm && ey < ground + part.HeightMm)
                _broken ??= $"the camera's eye ({ex}, {ey}, {ez}) is inside a piece's part";
        }
        if (simulation.Companions.FirstOrDefault(c => c.NpcId == Tavar) is { Order: CompanionOrder.Follow } tavar)
        {
            if (_tavarLast is { } last && last.ZMm < 99_000 && tavar.Body.ZMm >= 99_000 && tavar.Body.XMm is >= 99_700 and <= 101_300)
                _tavarPassed = true;
            _tavarLast = tavar.Body;
        }
    }

    // ── moving and looking ──────────────────────────────────────────────────

    /// <summary>Turn to face a bearing (0 = +Z, clockwise), F2's navigation stage on, the view set for the still; true once it has drawn.</summary>
    private bool Settle(int bearingDeg)
    {
        _camera.TargetDistance = 9f;
        _camera.Pitch = -0.75f;
        _camera.Yaw = Mathf.DegToRad(bearingDeg + 180);
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera, faceCamera: true);
        if (_phase == 0)
            _stage(BuildDebugStage.Navigation);
        // Enough frames for the view to settle and the overlay to redraw where the body now stands (at most twice a second).
        return ++_phase > 30;
    }

    /// <summary>The view for a still: from behind a bearing, at a distance; F2 at a stage; true once it has had time to draw.</summary>
    private bool Look(int bearingDeg, float distance, BuildDebugStage stage = BuildDebugStage.Off)
    {
        if (_phase == 0)
            _stage(stage);
        _camera.TargetDistance = distance;
        _camera.Pitch = -0.75f;
        _camera.Yaw = Mathf.DegToRad(bearingDeg + 180);
        return ++_phase > 20;
    }

    /// <summary>One frame facing a bearing, standing still.</summary>
    private void Face(int bearingDeg)
    {
        _camera.Yaw = Mathf.DegToRad(bearingDeg + 180);
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera, faceCamera: true);
    }

    /// <summary>Run the legs, each to within 300 mm.</summary>
    private bool Walk(IReadOnlyList<(double X, double Z)> route)
    {
        if (_waypoint >= route.Count)
            return true;
        var body = _controller.Authoritative;
        var (x, z) = route[_waypoint];
        var to = new Vector3((float)(x - body.XMm / 1000.0), 0, (float)(z - body.ZMm / 1000.0));
        if (to.Length() < Leg)
            _waypoint++;
        else
            _controller.SteerWorld(to.Normalized(), Gait.Run, _camera);
        return false;
    }

    /// <summary>The final pose, to within 50 mm: the last steps shortened so the body stops on it rather than past it.</summary>
    private bool Pose(long xMm, long zMm)
    {
        var body = _controller.Authoritative;
        var to = new Vector3((xMm - body.XMm) / 1000f, 0, (zMm - body.ZMm) / 1000f);
        if (Math.Abs(xMm - body.XMm) <= PoseMm && Math.Abs(zMm - body.ZMm) <= PoseMm && to.Length() * 1000 <= PoseMm)
        {
            _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
            return true;
        }
        float step = (float)(_session.Setup.Movement.SpeedMmPerSecond(Gait.Run) * _session.TickSeconds / 1000.0);
        _controller.SteerWorld(to.Length() > step ? to.Normalized() : to / step, Gait.Run, _camera);
        return false;
    }

    private static long Mm(double metres) => (long)Math.Round(metres * 1000);

    private int TimberCarried() => _session.Simulation!.Player.Inventory.Where(e => e.DefId == Timber).Sum(e => e.Count);

    private void Expect(bool holds, string otherwise)
    {
        if (!holds)
            _broken ??= otherwise;
    }

    private void Row(string what, long tick) => _transcript.AppendLine($"| {tick} | {what} |");

    /// <summary>The transcript and the command log (tick, row, command), so far.</summary>
    private void Write()
    {
        string suffix = _verify ? "_verify" : "";
        File.WriteAllText(Path.Combine(Directory, $"transcript{suffix}.md"), _transcript.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        var log = new StringBuilder();
        foreach (var entry in _session.Simulation!.CommandLog)
        {
            string row = _rowStarts.LastOrDefault(r => r.Tick <= entry.Tick).Row ?? "-";
            log.Append(entry.Tick).Append('\t').Append(row).Append('\t').Append(entry.Command)
                .Append(entry.RejectedReason is { } why ? $"\trejected: {why}" : "").Append('\n');
        }
        File.WriteAllText(Path.Combine(Directory, $"commands{suffix}.tsv"), log.ToString(), new UTF8Encoding(false));
    }
}
