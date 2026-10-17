// Acceptance driver: ordinary commands, the shipped frontier, real portal and collision, gameplay camera/environment.
using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Perf;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public sealed class BarrowworksReview
{
    private sealed record Stop(string Name, long X, long Z, string? Interact = null, Vector2? LookAt = null);
    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly Queue<Stop> _stops = new();
    private readonly Queue<(long X, long Z)> _path = new();
    private int _settle, _frames;
    private double _frameSeconds, _settleSeconds;
    private double _captureSeconds;
    private ulong _lastFrameUsec;
    private readonly bool _realtime;
    private readonly bool _returnBaseline;
    private readonly Performance.Monitor[] PipelineMonitors =
    {
        Performance.Monitor.PipelineCompilationsCanvas, Performance.Monitor.PipelineCompilationsMesh,
        Performance.Monitor.PipelineCompilationsSurface, Performance.Monitor.PipelineCompilationsDraw,
        Performance.Monitor.PipelineCompilationsSpecialization,
    };
    private readonly long[] _pipelines;
    private readonly int[] _collections = new int[3];
    private bool _entered, _arrived, _thresholdShot, _leaving, _memoryReported, _reentered;
    private double _exteriorSettle;
    private long _lastProgress;
    private readonly int _roomLimit;
    private int _roomsDrawn;
    private readonly FrameStats _stats;
    private readonly HashSet<string> _attackShots = new(StringComparer.Ordinal);
    public void RecordFrame(double seconds)
    {
        // Diagnostic only: engine process delta can clamp long stalls. Never feed this interval to GameSession.
        ulong now = Time.GetTicksUsec();
        double measured = _lastFrameUsec == 0 ? seconds : (now - _lastFrameUsec) / 1_000_000.0;
        _lastFrameUsec = now;
        _frameSeconds = seconds;
        // Start immediately after the threshold photo so its exclusion is consumed BEFORE portal loading.
        // Starting only after EnterSpace masked the entry stall as if it were screenshot readback.
        if (_thresholdShot && double.IsFinite(seconds) && seconds > 0)
        {
            _captureSeconds += measured;
            for (int i=0;i<PipelineMonitors.Length;i++)
            {
                long count=(long)Performance.GetMonitor(PipelineMonitors[i]);
                if (count!=_pipelines[i]) _stats.Mark($"{PipelineMonitors[i]} +{count-_pipelines[i]}");
                _pipelines[i]=count;
            }
            for (int i=0;i<_collections.Length;i++)
            {
                int count=GC.CollectionCount(i);
                if (count!=_collections[i]) _stats.Mark($"managed GC gen{i} +{count-_collections[i]}");
                _collections[i]=count;
            }
            _stats.Record(measured);
            if (!_memoryReported && _stats.Count >= 120)
            {
                _memoryReported = true;
                GD.Print($"BARROWWORKS memory MB: total={Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576:0.0}; "
                    + $"textures={Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) / 1048576:0.0}; "
                    + $"buffers={Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed) / 1048576:0.0}");
            }
        }
    }
    public void Screenshot() => _stats.SkipNext();
    public void Mark(string text) => _stats.Mark(text);
    public void WriteTiming() => _stats.Write(Directory, new Dictionary<string, string>
    {
        ["route"] = _returnBaseline ? "Fresh exterior drain launch; no portal used; ten-second same-view control"
            : "Frontier threshold -> all thirteen Barrowworks rooms -> solo Custodian -> headworks -> drain return",
        ["clock"] = _returnBaseline ? "Fresh-launch control; initial boot/first-draw warmup retained in raw data; compare the steady tail separately"
            : _realtime ? "Normal fixed 20 Hz through GameSession.Frame(delta); monotonic frame timing; boot/screenshot readback excluded, portal entry/return included"
            : "One ordinary simulation tick per rendered frame; monotonic frame timing; boot/screenshot readback excluded, portal entry/return included",
        ["tier"] = VisualOptions.Tier,
    });
    public string Directory { get; }

    public BarrowworksReview(GameSession session, PlayerController controller, CameraRig camera, string directory, bool realtime = false)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        _realtime = realtime;
        _returnBaseline = OS.GetEnvironment("BARROWWORKS_REVIEW_RETURN_BASELINE") == "1";
        if (_returnBaseline) _entered = _leaving = _thresholdShot = true;
        if (DisplayServer.GetName() != "headless")
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0; // Capture headroom rather than the window's refresh/cap, as ConnectedCheck does.
        _stats = new FrameStats(camera.GetViewport()) { Segment = "barrowworks-route" };
        _pipelines = new long[PipelineMonitors.Length];
        for (int i=0;i<PipelineMonitors.Length;i++) _pipelines[i]=(long)Performance.GetMonitor(PipelineMonitors[i]);
        for (int i=0;i<_collections.Length;i++) _collections[i]=GC.CollectionCount(i);
        Directory = directory;
        _roomLimit = int.TryParse(OS.GetEnvironment("BARROWWORKS_REVIEW_LIMIT"), out int limit) ? limit : 13;
        System.IO.Directory.CreateDirectory(directory);
        Add("R01_threshold", 50, 10);
        Add("R02_intake_hall", 50, 28);
        Add("notice", 55, 24, "switch.barrowworks.notice");
        Add("R03_wash_passage", 28, 38, lookAt: new Vector2(22.9f, 36.5f));
        Add("R04_drain_mouth", 8, 38, lookAt: new Vector2(5.5f, 41.6f));
        Add("R05_sorting_floor", 28, 60, lookAt: new Vector2(24, 54.5f));
        Add("R06_still_bay", 50, 60);
        Add("R07_gauge_archive", 72, 60);
        Add("fitting_note", 67, 59, "switch.barrowworks.fitting_note");
        Add("diagram_rack", 77, 59, "switch.barrowworks.diagram_rack");
        Add("archive_supplies", 70, 62, lookAt: new Vector2(74, 64));
        Add("R08_return_gallery", 72, 38, lookAt: new Vector2(76, 41));
        Add("gallery_latch", 68, 38, "door.barrowworks.g1");
        Add("archive_cover", 78, 62, "door.barrowworks.g2");
        Add("R09_blind_store", 92, 60, lookAt: new Vector2(95, 61));
        Add("blind_ledger", 95, 60, "switch.barrowworks.blind_ledger");
        Add("R13_inspection_bay", 72, 84, lookAt: new Vector2(76.5f, 86.5f));
        Add("R10_service_vestibule", 50, 84, lookAt: new Vector2(54, 88.5f));
        Add("R11_measuring_court", 50, 100);
        // Combat and the earned interlock remain ordinary gameplay; no flag or position is written by this driver.
        Add("custodian", 50, 106);
        Add("court_interlock", 50, 117, "door.barrowworks.g3");
        Add("R12_headworks", 50, 131, lookAt: new Vector2(42.6f, 131));
        Add("drain_return",8,38);
        _session.Subscribe<CommandRejected>(e => Note($"REFUSED {e.Command.GetType().Name}: {e.Reason}"));
        Note($"region={session.Setup.Layout.Id}; gate={session.Options.Barrowworks}; seed={session.Simulation!.World.WorldSeed}");
    }

    private void Add(string name, double x, double z, string? interact = null, Vector2? lookAt = null) =>
        _stops.Enqueue(new Stop(name, (long)(x*1000), (long)(z*1000), interact, lookAt));

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
        if (++_frames > 500_000 || _captureSeconds > 420 || sim.Combat.Defeated)
        {
            Note("FAILED: frame budget or defeat");
            return "failed";
        }
        if (!_entered)
        {
            _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);
            _camera.Yaw=0;_camera.Pitch=-.08f;_camera.TargetDistance=5;
            if (!_thresholdShot)
            {
                if ((_exteriorSettle+=_frameSeconds)<1) return null;
                _thresholdShot=true;
                for (int i = 0; i < PipelineMonitors.Length; i++) _pipelines[i] = (long)Performance.GetMonitor(PipelineMonitors[i]);
                for (int i = 0; i < _collections.Length; i++) _collections[i] = GC.CollectionCount(i);
                return "entrance_threshold";
            }
            _camera.TargetDistance=3.5f;
            _entered=true;
            _stats.Mark("portal entry submitted");
            _session.Submit(new EnterSpaceCommand(sim.PlayerId,"portal.barrowworks.threshold"));
            _lastProgress=sim.WorldTick;
            return null;
        }
        if (sim.PlayerInExterior)
        {
            if (!_leaving) return "failed";
            _controller.SteerWorld(Vector3.Zero,Gait.Walk,_camera);
            _camera.Yaw=0;_camera.Pitch=-.08f;_camera.TargetDistance=6;
            if ((_exteriorSettle+=_realtime ? _frameSeconds : _session.TickSeconds)<(_returnBaseline ? 10 : 5)) return null;
            if (_exteriorSettle<100) {_exteriorSettle=100;return "entrance_drain";}
            if (!_reentered && OS.GetEnvironment("BARROWWORKS_REVIEW_REENTER") == "1")
            {
                _reentered = true;
                _leaving = false;
                Add("reentered_drain", 8, 38);
                Note("reenter drain after interior resource release");
                _session.Submit(new EnterSpaceCommand(sim.PlayerId, "portal.barrowworks.drain"));
                return null;
            }
            return "done";
        }
        if (sim.Creatures.FirstOrDefault(c => c.DefId == "creature.construct.ring_custodian") is
            { Phase: CombatPhase.Windup, AttackSource: { } source } telling && telling.PhaseTicksLeft < 10 && _attackShots.Add(source))
            return "attack_" + source.Split('.').Last();
        if (!_stops.TryPeek(out var stop))
            return "done";
        if (_arrived)
        {
            _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
            _settleSeconds -= _frameSeconds;
            if (--_settle > 0 || _settleSeconds > 0)
                return null;
            _arrived = false;
            _stops.Dequeue();
            _stats.Mark("room/interaction " + stop.Name);
            Note("reached " + stop.Name);
            if (stop.Name=="drain_return")
            {
                _session.Submit(new LeaveSpaceCommand(sim.PlayerId));
                _leaving=true;_exteriorSettle=0;
            }
            _lastProgress = sim.WorldTick;
            if (stop.Name.StartsWith("R", StringComparison.Ordinal) && ++_roomsDrawn >= _roomLimit && _roomLimit<13)
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
            _settleSeconds = stop.Name == "custodian" ? 4 : .35; // Include the whole death clip before the corpse portrait.
            var look = stop.LookAt;
            if (stop.Interact is { } key)
            {
                var switchSite = sim.ActiveLayout.Switches.FirstOrDefault(s => s.Key == key);
                var door = sim.ActiveLayout.Doors.FirstOrDefault(d => d.Key == key);
                if ((switchSite?.Body ?? door?.ClosedFootprint) is BoxBlocker target)
                    look = new Vector2(target.CenterXMm/1000f, target.CenterZMm/1000f);
                _session.Submit(new InteractCommand(sim.PlayerId, key));
            }
            _camera.Yaw = look is { } aim ? Mathf.Atan2(-(aim.X-body.XMm/1000f), -(aim.Y-body.ZMm/1000f)) : Mathf.Pi;
            _camera.Pitch = -.06f;
            _camera.TargetDistance = stop.Name == "custodian" ? 6.5f : 3.5f;
            if (stop.Name == "custodian") _camera.Pitch = -.22f;
            return null;
        }
        if (_path.Count == 0)
        {
            ulong began = Time.GetTicksUsec();
            bool planned = Plan(stop);
            double ms = (Time.GetTicksUsec() - began) / 1000.0;
            _stats.Mark($"review path search {stop.Name}: {ms:0.000} ms");
            Note($"path search {stop.Name}: {ms:0.000} ms");
            if (!planned)
            {
                Note("FAILED: no collision-clear route to " + stop.Name);
                return "failed";
            }
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

    private long _fightInputTick = -1;
    private void Fight(CreatureView boss)
    {
        var sim = _session.Simulation!;
        if (_fightInputTick == sim.WorldTick) return;
        _fightInputTick = sim.WorldTick;
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
                        sim.Setup.Movement.BodyRadiusMm, layout.Space, closed, 1800)) continue;
                parents[next] = at;
                queue.Enqueue(next);
            }
        }
        return false;
    }

    private static double Distance(long x, long z, long tx, long tz) => Math.Sqrt(Math.Pow(tx-x, 2)+Math.Pow(tz-z, 2));
}
