// Acceptance driver: ordinary commands, the shipped frontier, real portal and collision, gameplay camera/environment.
using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public sealed class BarrowworksReview
{
    private sealed record Stop(string Name, long X, long Z, string? Interact = null);
    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly Queue<Stop> _stops = new();
    private readonly Queue<(long X, long Z)> _path = new();
    private int _settle, _frames;
    private bool _entered, _arrived;
    private long _lastProgress;
    private readonly int _roomLimit;
    private int _roomsDrawn;
    private readonly List<double> _frameMs = new();
    private readonly HashSet<string> _attackShots = new(StringComparer.Ordinal);
    public void RecordFrame(double seconds)
    {
        if (_entered && double.IsFinite(seconds) && seconds > 0) _frameMs.Add(seconds * 1000);
    }
    public void WriteTiming()
    {
        var samples = _frameMs.OrderBy(x => x).ToArray();
        if (samples.Length == 0) return;
        File.WriteAllText(Path.Combine(Directory, "frame_timing.txt"),
            $"samples={samples.Length}; median_ms={samples[samples.Length/2]:F2}; p95_ms={samples[(int)((samples.Length-1)*.95)]:F2}; max_ms={samples[^1]:F2}; renderer={DisplayServer.GetName()}; tier={VisualOptions.Tier}\n");
    }
    public string Directory { get; }

    public BarrowworksReview(GameSession session, PlayerController controller, CameraRig camera, string directory)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        Directory = directory;
        _roomLimit = int.TryParse(OS.GetEnvironment("BARROWWORKS_REVIEW_LIMIT"), out int limit) ? limit : 13;
        System.IO.Directory.CreateDirectory(directory);
        Add("R01_threshold", 50, 10);
        Add("R02_intake_hall", 50, 28);
        Add("notice", 55, 24, "switch.barrowworks.notice");
        Add("R03_wash_passage", 28, 38);
        Add("R04_drain_mouth", 8, 38);
        Add("R05_sorting_floor", 28, 60);
        Add("R06_still_bay", 50, 60);
        Add("R07_gauge_archive", 72, 60);
        Add("fitting_note", 67, 59, "switch.barrowworks.fitting_note");
        Add("diagram_rack", 77, 59, "switch.barrowworks.diagram_rack");
        Add("R08_return_gallery", 72, 38);
        Add("gallery_latch", 68, 38, "door.barrowworks.g1");
        Add("archive_cover", 78, 62, "door.barrowworks.g2");
        Add("R09_blind_store", 92, 60);
        Add("blind_ledger", 95, 60, "switch.barrowworks.blind_ledger");
        Add("R13_inspection_bay", 72, 84);
        Add("R10_service_vestibule", 50, 84);
        Add("R11_measuring_court", 50, 100);
        // Combat and the earned interlock remain ordinary gameplay; no flag or position is written by this driver.
        Add("custodian", 50, 106);
        Add("court_interlock", 50, 117, "door.barrowworks.g3");
        Add("R12_headworks", 50, 131);
        _session.Subscribe<CommandRejected>(e => Note($"REFUSED {e.Command.GetType().Name}: {e.Reason}"));
        Note($"region={session.Setup.Layout.Id}; gate={session.Options.Barrowworks}; seed={session.Simulation!.World.WorldSeed}");
    }

    private void Add(string name, double x, double z, string? interact = null) =>
        _stops.Enqueue(new Stop(name, (long)(x*1000), (long)(z*1000), interact));

    private void Note(string text)
    {
        var sim = _session.Simulation!;
        var body = sim.Player.Body;
        File.AppendAllText(Path.Combine(Directory, "route.log"), $"tick={sim.WorldTick} space={sim.ActiveLayout.SpaceId} body={body.XMm},{body.YMm},{body.ZMm} {text}\n");
        GD.Print("BARROWWORKS REVIEW " + text);
    }

    public string? Update()
    {
        var sim = _session.Simulation!;
        if (++_frames > 100_000 || sim.Combat.Defeated)
        {
            Note("FAILED: frame budget or defeat");
            return "failed";
        }
        if (!_entered)
        {
            _entered = true;
            _session.Submit(new EnterSpaceCommand(sim.PlayerId, "portal.barrowworks.threshold"));
            _lastProgress = sim.WorldTick;
            return null;
        }
        if (sim.PlayerInExterior)
            return "failed";
        if (sim.Creatures.FirstOrDefault(c => c.DefId == "creature.construct.ring_custodian") is
            { Phase: CombatPhase.Windup, AttackSource: { } source } telling && telling.PhaseTicksLeft < 10 && _attackShots.Add(source))
            return "attack_" + source.Split('.').Last();
        if (!_stops.TryPeek(out var stop))
            return "done";
        if (_arrived)
        {
            _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
            if (--_settle > 0)
                return null;
            _arrived = false;
            _stops.Dequeue();
            if (stop.Interact is { } key)
                _session.Submit(new InteractCommand(sim.PlayerId, key));
            Note("reached " + stop.Name);
            _lastProgress = sim.WorldTick;
            if (stop.Name.StartsWith("R", StringComparison.Ordinal) && ++_roomsDrawn >= _roomLimit)
                _stops.Clear();
            return stop.Name;
        }
        if (stop.Name == "custodian" && sim.Creatures.FirstOrDefault(c => c.DefId == "creature.construct.ring_custodian") is { Alive: true } boss)
        {
            Fight(boss);
            return null;
        }
        var body = sim.Player.Body;
        while (_path.TryPeek(out var step) && Distance(body.XMm, body.ZMm, step.X, step.Z) < 220)
        {
            _path.Dequeue();
            _lastProgress = sim.WorldTick;
        }
        if (Distance(body.XMm, body.ZMm, stop.X, stop.Z) < 300)
        {
            _path.Clear();
            _arrived = true;
            _settle = 25;
            _camera.Yaw = Mathf.Pi;
            _camera.Pitch = -.06f;
            return null;
        }
        if (_path.Count == 0 && !Plan(stop))
        {
            Note("FAILED: no collision-clear route to " + stop.Name);
            return "failed";
        }
        if (sim.WorldTick - _lastProgress > 500)
        {
            Note("FAILED: movement stuck en route to " + stop.Name);
            return "failed";
        }
        var next = _path.Peek();
        var direction = new Vector3(next.X-body.XMm, 0, next.Z-body.ZMm).Normalized();
        _camera.Yaw = Mathf.Atan2(-direction.X, -direction.Z);
        _controller.SteerWorld(direction, Gait.Run, _camera);
        return null;
    }

    private void Fight(CreatureView boss)
    {
        var sim = _session.Simulation!;
        if (sim.WorldTick % 200 == 0) Note($"fight player_hp={sim.Combat.Health} boss_hp={boss.Health} attack={boss.AttackSource}");
        var body = sim.Player.Body;
        var towards = new Vector3(boss.Body.XMm-body.XMm, 0, boss.Body.ZMm-body.ZMm);
        float distance = towards.Length();
        _camera.Yaw = Mathf.Atan2(-towards.X, -towards.Z);
        if (sim.Combat.Health < 35 && sim.WorldTick % 20 == 0)
            _controller.UseConsumable();
        // Circle the slow housing toward its exposed rear; its planted windup is the warning to clear the arm.
        var tangent = new Vector3(towards.Z, 0, -towards.X).Normalized();
        var movement = distance > 1600 ? towards.Normalized() : tangent;
        _controller.SteerWorld(movement, Gait.Run, _camera, faceCamera: true);
        if (boss.Phase == CombatPhase.Windup && boss.PhaseTicksLeft < 7 && sim.Combat.Phase == CombatPhase.Idle)
            _controller.Dodge(tangent);
        else if (distance < 1900 && sim.Combat.Phase == CombatPhase.Idle && sim.Player.Progression.Pools.Stamina is null or > 15)
            _controller.Attack();
        _lastProgress = sim.WorldTick;
    }

    private bool Plan(Stop stop)
    {
        var sim = _session.Simulation!;
        var layout = sim.ActiveLayout;
        var closed = layout.ClosedDoors(d => sim.Doors.Single(v => v.Site == d).Open,
            b => !sim.Barriers.Single(v => v.Site == b).Standing).ToArray();
        var body = sim.Player.Body;
        var start = ((int)Math.Round(body.XMm/1000.0), (int)Math.Round(body.ZMm/1000.0));
        var end = ((int)Math.Round(stop.X/1000.0), (int)Math.Round(stop.Z/1000.0));
        var parents = new Dictionary<(int X, int Z), (int X, int Z)?> { [start] = null };
        var queue = new Queue<(int X, int Z)>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var at))
        {
            if (at == end)
            {
                var path = new List<(int X, int Z)>();
                for (var current = ((int X, int Z)?)end; current is { } p; current = parents[p])
                    path.Add(p);
                path.Reverse();
                foreach (var p in path.Skip(1)) _path.Enqueue((p.X*1000L, p.Z*1000L));
                return _path.Count > 0;
            }
            foreach (var (dx, dz) in new[] { (1,0), (-1,0), (0,1), (0,-1) })
            {
                var next = (at.X+dx, at.Z+dz);
                if (parents.ContainsKey(next) || !Kinematics.IsClear(next.Item1*1000L, next.Item2*1000L,
                        sim.Setup.Movement.BodyRadiusMm, layout.Space, closed)) continue;
                parents[next] = at;
                queue.Enqueue(next);
            }
        }
        return false;
    }

    private static double Distance(long x, long z, long tx, long tz) => Math.Sqrt(Math.Pow(tx-x, 2)+Math.Pow(tz-z, 2));
}
