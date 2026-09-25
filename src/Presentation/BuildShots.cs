// UNNAMED Presentation - M7's scripted proof of navigation and building in the playable build (M7 design §13.3, §13.4)
// Godot presentation only: it submits commands and reads views, as a player's keys would (D-11)

using System.Text;
using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>--build-shots dir</c>: the M7 beats, played through the real command path one tick a frame, a still at each that asks for one.
/// The beats grow slice by slice (§13.4); a run plays every beat whose slice has landed. In E1 there are two, both on a new game at
/// <see cref="Playthrough.Seed"/>: the grid drawn around the smithy (b01), and across the four-cell corner at the crossing (b02).
/// Writes <c>transcript.md</c> beside the stills. Scripts may name content; the game may not.
/// </summary>
public sealed class BuildShots
{
    private const float Leg = 0.3f;
    private const long PoseMm = 50;

    private static readonly (double X, double Z)[] ToTheSmithy = { (40, 140), (47, 139), (51.8, 139) };
    private static readonly (double X, double Z)[] ToTheCorner = { (51.8, 136.0), (62.0, 130.0), (80.0, 118.0), (90.0, 112.0), (96.0, 96.0) };

    private sealed record Beat(string Name, string Says, int Budget, bool Still, Func<bool> Run);

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly NavigationOverlay _overlay;
    private readonly Action<BuildDebugStage> _stage;
    private readonly List<Beat> _beats = new();
    private readonly StringBuilder _transcript = new();
    private readonly List<string> _refused = new();
    private int _beat;
    private long _beatTick;
    private int _waypoint;
    private int _phase;
    private string? _broken;

    public BuildShots(GameSession session, PlayerController controller, CameraRig camera, NavigationOverlay overlay, Action<BuildDebugStage> stage,
        string directory)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        _overlay = overlay;
        _stage = stage;
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
        session.Subscribe<CommandRejected>(e => _refused.Add($"{e.Command.GetType().Name}: {e.Reason}"));
        session.Subscribe<DoorToggled>(e => Row($"`DoorToggled` {e.DoorKey} {(e.Open ? "open" : "shut")} by {e.Actor}", e.Tick));
        _beats.AddRange(new[]
        {
            new Beat("b01_grid_smithy", "F2's navigation stage at the smithy: the walls' nodes drawn unwalkable, the forge shed's door shut (red)", 1_500, true,
                GridAtTheSmithy),
            new Beat("b02_grid_corner", "F2's navigation stage where the four cells meet at (100, 100): walkable on both sides of both seams, no gap, no row twice",
                3_000, true, GridAtTheCorner),
        });
        _beatTick = session.Simulation!.WorldTick;
        _transcript.AppendLine("# M7 build shots").AppendLine()
            .AppendLine($"Content {session.Content.Version} ({session.Content.Hash}); a new game at seed {Playthrough.Seed:X}; one tick a frame, the real " +
                        "command path.").AppendLine()
            .AppendLine("| Tick | What happened |").AppendLine("|---|---|");
    }

    public string Directory { get; }

    /// <summary>Clear what a previous run left in <paramref name="directory"/>: its profile, transcript and stills.</summary>
    public static void Clear(string directory)
    {
        if (!System.IO.Directory.Exists(directory))
            return;
        string profile = Path.Combine(directory, "profile");
        if (System.IO.Directory.Exists(profile))
            System.IO.Directory.Delete(profile, recursive: true);
        foreach (string file in System.IO.Directory.EnumerateFiles(directory)
                     .Where(f => Path.GetFileName(f) == "transcript.md" || Path.GetExtension(f) == ".jpg"))
            File.Delete(file);
    }

    /// <summary>Advance one frame: the name of a still to take now, "done", "failed", or null.</summary>
    public string? Update()
    {
        var simulation = _session.Simulation!;
        if (_beat >= _beats.Count)
        {
            Row($"Done: {_beats.Count} beats; subscriber failures {_session.SubscriberFailures}", simulation.WorldTick);
            Write();
            if (_session.SubscriberFailures != 0)
            {
                GD.PushError($"UNNAMED build shots: {_session.SubscriberFailures} subscriber failures");
                return "failed";
            }
            return "done";
        }
        var beat = _beats[_beat];
        if (_broken is null && simulation.WorldTick - _beatTick > beat.Budget)
            _broken = $"it did not finish in {beat.Budget} ticks (refusals: {string.Join("; ", _refused.TakeLast(3))})";
        bool done = false;
        if (_broken is null)
        {
            try
            {
                done = beat.Run();
            }
            catch (Exception e)
            {
                _broken = $"{e.GetType().Name}: {e.Message}";
            }
        }
        if (_broken is { } why)
        {
            Row($"FAILED at '{beat.Name}': {why}, at ({simulation.Player.Body.XMm / 1000.0:0.00}, {simulation.Player.Body.ZMm / 1000.0:0.00})", simulation.WorldTick);
            Write();
            GD.PushError($"UNNAMED build shots: '{beat.Name}' failed: {why}");
            return "failed";
        }
        if (!done)
            return null;
        Row($"**{beat.Name}**: {beat.Says} (in {simulation.WorldTick - _beatTick} ticks){(beat.Still ? $" ![{beat.Name}]({beat.Name}.jpg)" : "")}", simulation.WorldTick);
        _beat++;
        _beatTick = simulation.WorldTick;
        _waypoint = 0;
        _phase = 0;
        return beat.Still ? beat.Name : null;
    }

    // ── the beats ───────────────────────────────────────────────────────────

    /// <summary>b01: legs to the smithy's west door; @(51.8, 142.0) facing 90; F2's navigation stage.</summary>
    private bool GridAtTheSmithy()
    {
        if (_phase == 0)
        {
            if (!Travel(ToTheSmithy) || !Pose(51_800, 142_000))
                return false;
            _phase = 1;
        }
        if (!Settle(90))
            return false;
        var view = _session.Simulation!.Navigation;
        Expect(view.Grid.Tiles.Length == 4, $"the grid holds {view.Grid.Tiles.Length} tiles, not the region's 4");
        var sampled = _overlay.Sampled.ToDictionary(n => (n.I, n.J), n => n.Walkable);
        var walls = _session.Setup.Layout.Space.Blockers.OfType<BoxBlocker>().Where(b => b.Id.StartsWith("forge_", StringComparison.Ordinal)).ToList();
        Expect(walls.Count == 5, $"{walls.Count} smithy walls in the layout, not 5");
        int inWalls = 0;
        foreach (var wall in walls)
        {
            foreach (var (node, walkable) in sampled)
            {
                long x = view.Grid.CentreOf(node.I), z = view.Grid.CentreOf(node.J);
                if (x < wall.MinXMm || x > wall.MaxXMm || z < wall.MinZMm || z > wall.MaxZMm)
                    continue;
                inWalls++;
                Expect(!walkable, $"node ({node.I}, {node.J}) inside {wall.Id} is drawn walkable");
            }
        }
        Expect(inWalls > 50, $"only {inWalls} nodes of the smithy walls were drawn");
        Expect(_overlay.Quads == sampled.Count(n => !n.Value), $"{_overlay.Quads} quads drawn for {sampled.Count(n => !n.Value)} unwalkable nodes");
        var door = view.Gates.Single(g => g.Key == "door.forge_shed");
        Expect(!door.Open, "the forge shed's door is open");
        Expect(_overlay.GateColours.GetValueOrDefault("door.forge_shed") == NavigationOverlay.Shut, "the forge shed's door is not drawn shut (red)");
        Row($"b01: {inWalls} nodes of the five smithy walls drawn unwalkable; {_overlay.Quads} quads within {NavigationOverlay.RadiusM} m; " +
            $"door.forge_shed drawn shut", _session.Simulation.WorldTick);
        return _broken is null;
    }

    /// <summary>b02: legs to the crossing; @(100.0, 96.0) facing 0; the grid across both seams within 3 m of the corner.</summary>
    private bool GridAtTheCorner()
    {
        if (_phase == 0)
        {
            if (!Travel(ToTheCorner) || !Pose(100_000, 96_000))
                return false;
            _phase = 1;
        }
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

    // ── moving and looking ──────────────────────────────────────────────────

    /// <summary>Turn to face a bearing (0 = +Z, clockwise), F2's navigation stage on, the view set for the still; true once it has drawn.</summary>
    private bool Settle(int bearingDeg)
    {
        _camera.TargetDistance = 9f;
        _camera.Pitch = -0.75f;
        _camera.Yaw = Mathf.DegToRad(bearingDeg + 180);
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera, faceCamera: true);
        if (_phase == 1)
            _stage(BuildDebugStage.Navigation);
        // Enough frames for the view to settle and the overlay to redraw where the body now stands (at most twice a second).
        return ++_phase > 30;
    }

    private bool Travel((double X, double Z)[] route) => !Defend() && Walk(route);

    /// <summary>Run the legs, each to within 300 mm.</summary>
    private bool Walk((double X, double Z)[] route)
    {
        if (_waypoint >= route.Length)
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

    /// <summary>Fight the nearest creature hunting the character, as the other scripted runs do; strays and sentinels are walked past.</summary>
    private bool Defend()
    {
        var simulation = _session.Simulation!;
        var body = _controller.Authoritative;
        var threat = simulation.Creatures
            .Where(c => c.Alive && c.Hostile && c.RoleId is not ("sentinel" or "stray") && Distance(body, c.Body) < 25_000)
            .OrderBy(c => Distance(body, c.Body))
            .FirstOrDefault();
        if (threat is null)
            return false;
        var to = new Vector3((float)((threat.Body.XMm - body.XMm) / 1000.0), 0, (float)((threat.Body.ZMm - body.ZMm) / 1000.0));
        _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
        var combat = simulation.Combat;
        long radius = _session.Setup.Combat.Creatures[threat.DefId].RadiusMm;
        bool inReach = combat.Weapon.Ranged || to.Length() <= (combat.Weapon.ReachMm + radius - 150) / 1000f;
        _controller.SteerWorld(inReach ? Vector3.Zero : to.Normalized(), Gait.Run, _camera, faceCamera: true);
        if (inReach && combat.Phase == CombatPhase.Idle)
            _controller.Attack();
        return true;
    }

    private static double Distance(Body a, Body b) => Math.Sqrt(Math.Pow(a.XMm - b.XMm, 2) + Math.Pow(a.ZMm - b.ZMm, 2));

    private void Expect(bool holds, string otherwise)
    {
        if (!holds)
            _broken ??= otherwise;
    }

    private void Row(string what, long tick) => _transcript.AppendLine($"| {tick} | {what} |");

    private void Write() =>
        File.WriteAllText(Path.Combine(Directory, "transcript.md"), _transcript.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
}
