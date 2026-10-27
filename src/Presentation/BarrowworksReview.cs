// Acceptance driver: ordinary commands, the shipped frontier, real portal and collision, gameplay camera/environment.
using Godot;
using System.Collections.Concurrent;
using System.Text.Json;
using UNNAMED.Application;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Perf;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public sealed class BarrowworksReview
{
    private sealed record Stop(string Name, long X, long Z, string? Interact = null, Vector2? LookAt = null)
    {
        /// <summary>Stand anywhere within reach of (X, Z) rather than on it: a station or container can stand inside a wall's footprint.</summary>
        public bool Near { get; init; }
        public string? Take { get; init; }
        public string? Craft { get; init; }
        public string? Enter { get; init; }
        public bool Leave { get; init; }
        /// <summary>Hold here, out of the court, until the Custodian's attempt has reset (owner ruling OQ-17).</summary>
        public bool Retreat { get; init; }
        /// <summary>Wait out of the fight until health has come back, as a player does before the court.</summary>
        public bool Rest { get; init; }
    }
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
    private readonly bool _captureGpuStages;
    private readonly ConcurrentQueue<object> _gpuCaptures = new();
    private int _lastGpuCapture = -100, _gpuCaptureCount;
    private readonly Performance.Monitor[] PipelineMonitors =
    {
        Performance.Monitor.PipelineCompilationsCanvas, Performance.Monitor.PipelineCompilationsMesh,
        Performance.Monitor.PipelineCompilationsSurface, Performance.Monitor.PipelineCompilationsDraw,
        Performance.Monitor.PipelineCompilationsSpecialization,
    };
    private readonly long[] _pipelines;
    private readonly int[] _collections = new int[3];
    private bool _entered, _arrived, _thresholdShot, _leaving, _memoryReported, _reentered;
    private int? _woundedHp;
    private bool _equipped;
    private string? _fighting;
    private long? _restFrom;
    private long _leaveAsked;
    private Task<Search>? _search;
    private Stop? _searched;
    private (long X, long Z) _goal;
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
            int previousCount = _stats.Count;
            _stats.Record(measured);
            if (_captureGpuStages && _stats.Count > previousCount && measured > .033 && _gpuCaptureCount < 256
                && (measured > .075 || _stats.Count - _lastGpuCapture > 60))
            {
                int observed = _stats.Count - 1;
                _lastGpuCapture = _stats.Count;
                _gpuCaptureCount++;
                // Read the engine's timestamp buffers on their owning thread. GPU results lag observation;
                // preserve both IDs/raw timestamps rather than claiming they are the same frame.
                RenderingServer.CallOnRenderThread(Callable.From(() =>
                {
                    var device = RenderingServer.GetRenderingDevice();
                    if (device is null) return;
                    var steps = new List<object>();
                    for (uint i = 0; i < device.GetCapturedTimestampsCount(); i++)
                        steps.Add(new { name = device.GetCapturedTimestampName(i), cpu_raw = device.GetCapturedTimestampCpuTime(i),
                            gpu_raw = device.GetCapturedTimestampGpuTime(i) });
                    _gpuCaptures.Enqueue(new { observed_frame = observed, render_frame = device.GetCapturedTimestampsFrame(), steps });
                }));
            }
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
    public void Phase(string name, ulong started)
    {
        double ms = (Time.GetTicksUsec() - started) / 1000.0;
        if (ms > 5) _stats.Mark($"review timing {name}: {ms:0.000} ms");
    }
    public void WriteTiming()
    {
        if (_captureGpuStages)
        {
            RenderingServer.ForceSync();
            File.WriteAllText(Path.Combine(Directory, "gpu_frames.json"), JsonSerializer.Serialize(_gpuCaptures.ToArray(),
                new JsonSerializerOptions { WriteIndented = true }));
        }
        _stats.Write(Directory, new Dictionary<string, string>
        {
            ["route"] = _returnBaseline ? "Fresh exterior drain launch; no portal used; ten-second same-view control"
                : "Frontier threshold -> all thirteen Barrowworks rooms -> solo Custodian -> headworks -> drain return",
            ["clock"] = _returnBaseline ? "Fresh-launch control; initial boot/first-draw warmup retained in raw data; compare the steady tail separately"
                : _realtime ? "Normal fixed 20 Hz through GameSession.Frame(delta); monotonic frame timing; boot/screenshot readback excluded, portal entry/return included"
                : "One ordinary simulation tick per rendered frame; monotonic frame timing; boot/screenshot readback excluded, portal entry/return included",
            ["tier"] = VisualOptions.Tier,
        });
    }
    public string Directory { get; }

    public BarrowworksReview(GameSession session, PlayerController controller, CameraRig camera, string directory, bool realtime = false)
    {
        _session = session;
        _controller = controller;
        _camera = camera;
        _realtime = realtime;
        _returnBaseline = OS.GetEnvironment("BARROWWORKS_REVIEW_RETURN_BASELINE") == "1";
        _captureGpuStages = DisplayServer.GetName() != "headless" && OS.GetCmdlineUserArgs().Contains("--capture-gpu-stages");
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
        _stops.Enqueue(new Stop("archive_supplies", 74_500, 62_500, LookAt: new Vector2(74, 64)) { Near = true, Take = "container.barrowworks.archive_crate" });
        Add("R08_return_gallery", 72, 38, lookAt: new Vector2(76, 41));
        Add("gallery_latch", 68, 38, "door.barrowworks.gallery");
        // The wedge is ordinary crafting at a public anvil outside (M8 05): out by the threshold through the opened
        // gallery shortcut, make it at the Ashen Hollow forge anvil from the archive's haft, and back in.
        _stops.Enqueue(new Stop("threshold_out", 50_000, 10_000) { Leave = true });
        _stops.Enqueue(new Stop("forge_door", 452_000, 742_000, "door.forge_shed") { Near = true });
        _stops.Enqueue(new Stop("forge_anvil", 457_500, 740_500, LookAt: new Vector2(457.5f, 740.5f)) { Near = true, Craft = "recipe.smithing.fitting_wedge" });
        _stops.Enqueue(new Stop("threshold_in", 440_000, 621_000) { Near = true, Enter = "portal.barrowworks.threshold" });
        Add("archive_cover", 78, 62, "door.barrowworks.archive");
        Add("R09_blind_store", 92, 60, lookAt: new Vector2(95, 61));
        Add("blind_ledger", 95, 60, "switch.barrowworks.blind_ledger");
        Add("R13_inspection_bay", 72, 84, lookAt: new Vector2(76.5f, 86.5f));
        _stops.Enqueue(new Stop("R10_service_vestibule", 50_000, 84_000, LookAt: new Vector2(54, 88.5f)) { Rest = true });
        Add("R11_measuring_court", 50, 100);
        // Combat and the earned interlock remain ordinary gameplay; no flag or position is written by this driver.
        // A first attempt is broken off once the Custodian is wounded, to show the retreat resetting it to full health.
        Add("custodian_wound", 50, 106);
        _stops.Enqueue(new Stop("retreat_reset", 50_000, 84_000, LookAt: new Vector2(50, 100)) { Retreat = true, Rest = true });
        Add("court_return", 50, 100, lookAt: new Vector2(50, 106));
        Add("custodian", 50, 106);
        Add("court_interlock", 50, 117, "door.barrowworks.headworks");
        Add("R12_headworks", 50, 131, lookAt: new Vector2(42.6f, 131));
        _stops.Enqueue(new Stop("retaining_saddle", 45_700, 127_300, "switch.barrowworks.headworks_preserve") { Near = true });
        Add("drain_return",8,38);
        // A transition diagnostic: the same threshold entry and drain return without the long route between them.
        if (OS.GetEnvironment("BARROWWORKS_REVIEW_SHORT") == "1")
        {
            var keep = _stops.Where(s => s.Name is "R01_threshold" or "R02_intake_hall" or "notice" or "R03_wash_passage" or "R04_drain_mouth" or "drain_return").ToList();
            _stops.Clear();
            foreach (var s in keep) _stops.Enqueue(s);
        }
        _session.Subscribe<CommandRejected>(e => Note($"REFUSED {e.Command.GetType().Name}: {e.Reason}" + (e.Command is LeaveSpaceCommand
            ? " - engaged: " + string.Join(", ", _session.Simulation!.Creatures.Where(c => c.Alive && c.Hostile).Select(c => $"{c.Key} at {c.Body.XMm},{c.Body.ZMm}"))
            : "")));
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
        if (++_frames > 2_000_000 || _captureSeconds > 1500 || sim.Combat.Defeated)
        {
            Note("FAILED: frame budget or defeat");
            return "failed";
        }
        if (_frames < 3 && DisplayServer.GetName() != "headless")
            // The recorder finds the game window by this title, never the desktop. Set from the running loop: the engine names the
            // window after the project once it opens, which overwrote a title set during boot.
            DisplayServer.WindowSetTitle("Otherreach - " + Path.GetFileName(Directory));
        if (!_equipped)
        {
            // The fixture's reference kit (--start-items) is put on as a player would, before the threshold photograph.
            _equipped = true;
            foreach (var item in sim.Player.Inventory.Where(e => e.DefId.StartsWith("item.armor.", StringComparison.Ordinal)))
                _session.Submit(new EquipCommand(sim.PlayerId, item.ItemId));
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
        if (sim.PlayerInExterior && _leaving)
        {
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
        // Leaving is refused while something still fights the character: deal with it, then walk back to the drain and try again.
        if (_leaving && !sim.PlayerInExterior && _stops.Count == 0 && sim.WorldTick - _leaveAsked > 40)
        {
            Note("still inside after asking to leave; back to the drain");
            _leaving = false;
            Add("drain_return", 8, 38);
        }
        if (!_stops.TryPeek(out var stop))
            return _leaving ? null : "done";
        // The ring's ordinary creatures (M8 09 E1-E4) are fought where they come at the character, then the route is searched again.
        var at = sim.Player.Body;
        if (sim.Creatures.Where(c => c.Alive && c.Hostile && c.DefId != "creature.construct.ring_custodian")
                .OrderBy(c => Distance(at.XMm, at.ZMm, c.Body.XMm, c.Body.ZMm)).FirstOrDefault() is { } threat
            && Distance(at.XMm, at.ZMm, threat.Body.XMm, threat.Body.ZMm) < 30_000 && _search is null)
        {
            if (_fighting != threat.Key) Note($"engaged by {threat.DefId} ({threat.Key})");
            _fighting = threat.Key;
            _path.Clear();
            _searched = null;
            Fight(threat);
            return null;
        }
        if (_fighting is { } fought && sim.Creatures.FirstOrDefault(c => c.Key == fought) is { Alive: false })
        {
            Note($"defeated {fought}");
            _fighting = null;
            _restFrom = sim.WorldTick;
        }
        // After a fight, rest back to health before going on, as a player would.
        if (_restFrom is { } rested)
        {
            if (sim.Combat.Health * 20 < sim.Combat.MaxHealth * 19 && sim.WorldTick - rested < 3_000)
            {
                _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
                _lastProgress = sim.WorldTick;
                return null;
            }
            Note($"rested to {sim.Combat.Health}/{sim.Combat.MaxHealth}");
            _restFrom = null;
        }
        if (_arrived)
        {
            _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
            if (stop.Retreat && Custodian(sim) is { } waiting && (waiting.Hostile || waiting.Health < waiting.MaxHealth))
            {
                if (sim.WorldTick - _lastProgress > 1200)
                {
                    Note($"FAILED: Custodian never reset after the retreat (hp {waiting.Health}/{waiting.MaxHealth})");
                    return "failed";
                }
                return null;
            }
            if (stop.Retreat && Custodian(sim) is { } reset && _woundedHp is { } wounded)
            {
                Note($"retreat reset: Custodian {wounded}/{reset.MaxHealth} when the attempt was broken off, {reset.Health}/{reset.MaxHealth} after");
                _woundedHp = null;
            }
            if (stop.Rest && sim.Combat.Health * 20 < sim.Combat.MaxHealth * 19 && sim.WorldTick - _lastProgress < 3_000)
                return null;
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
                _leaving=true;_exteriorSettle=0;_leaveAsked=sim.WorldTick;
            }
            _lastProgress = sim.WorldTick;
            if (stop.Name.StartsWith("R", StringComparison.Ordinal) && ++_roomsDrawn >= _roomLimit && _roomLimit<13)
                _stops.Clear();
            return stop.Name;
        }
        if (stop.Name == "custodian_wound" && Custodian(sim) is { Alive: true } quarry)
        {
            if (quarry.Health > quarry.MaxHealth * 4 / 5)
            {
                Fight(quarry);
                return null;
            }
            _woundedHp = quarry.Health;
            _stops.Dequeue();
            _path.Clear();
            _stats.Mark("room/interaction " + stop.Name);
            Note($"reached {stop.Name}: Custodian at {quarry.Health}/{quarry.MaxHealth}; breaking off");
            _lastProgress = sim.WorldTick;
            return stop.Name;
        }
        if (stop.Name == "custodian" && Custodian(sim) is { Alive: true } boss)
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
        if (_search is { IsCompleted: true } && _searched == stop)
        {
            var found = _search.Result;
            _search = null;
            _stats.Mark($"review path search {stop.Name}: {found.Ms:0.000} ms (worker thread)");
            Note($"path search {stop.Name}: {found.Ms:0.000} ms");
            if (found.Path is null)
            {
                Note("FAILED: no collision-clear route to " + stop.Name);
                return "failed";
            }
            foreach (var step in found.Path) _path.Enqueue(step);
            // Already in the goal's cell but not within arrival distance of it: an empty route would be searched again every tick.
            if (_path.Count == 0) _path.Enqueue(found.Goal);
            _goal = found.Goal;
            _lastProgress = sim.WorldTick;
        }
        if (_search is not null)
        {
            // Standing still while the route is searched on a worker, so a long search is not a rendered stall.
            _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
            _lastProgress = sim.WorldTick;
            return null;
        }
        long reach = Math.Min(sim.Setup.Movement.InteractReachMm, sim.Setup.Items.Inventory.ReachMm) - 300;
        if (_searched == stop && (_path.Count == 0 && Distance(body.XMm, body.ZMm, _goal.X, _goal.Z) < 300
                                  || stop.Near && Distance(body.XMm, body.ZMm, stop.X, stop.Z) <= reach))
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
                // A door someone already opened is walked through, not shut again.
                if (door is null || !sim.Doors.Single(d => d.Site == door).Open)
                    _session.Submit(new InteractCommand(sim.PlayerId, key));
            }
            if (stop.Take is { } container)
                _session.Submit(new TakeAllCommand(sim.PlayerId, container));
            if (stop.Craft is { } recipe)
                _session.Submit(new CraftCommand(sim.PlayerId, recipe));
            if (stop.Leave)
                _session.Submit(new LeaveSpaceCommand(sim.PlayerId));
            if (stop.Enter is { } portal)
                _session.Submit(new EnterSpaceCommand(sim.PlayerId, portal));
            _camera.Yaw = look is { } aim ? Mathf.Atan2(-(aim.X-body.XMm/1000f), -(aim.Y-body.ZMm/1000f)) : Mathf.Pi;
            _camera.Pitch = -.06f;
            _camera.TargetDistance = stop.Name == "custodian" ? 6.5f : 3.5f;
            if (stop.Name == "custodian") _camera.Pitch = -.22f;
            return null;
        }
        if (_path.Count == 0)
        {
            Plan(stop);
            return null;
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
        if (sim.Combat.Health * 2 < sim.Combat.MaxHealth && sim.WorldTick % 20 == 0)
            _controller.UseConsumable();
        // Circle the slow housing toward its exposed rear; its planted windup is the warning to clear the arm.
        var tangent = new Vector3(towards.Z, 0, -towards.X).Normalized();
        var movement = distance > 1600 ? towards.Normalized() : tangent;
        _controller.SteerWorld(movement, Gait.Run, _camera, faceCamera: true);
        if (boss.Phase == CombatPhase.Windup && boss.PhaseTicksLeft < 7 && sim.Combat.Phase == CombatPhase.Idle
            && sim.Player.Progression.Pools.Stamina is null or > 20)
            _controller.Dodge(tangent);
        else if (distance < 1900 && sim.Combat.Phase == CombatPhase.Idle && sim.Player.Progression.Pools.Stamina is null or > 15)
            _controller.Attack();
        _lastProgress = sim.WorldTick;
    }

    private CreatureView? Custodian(Simulation sim) => sim.Creatures.FirstOrDefault(c => c.DefId == "creature.construct.ring_custodian");

    private sealed record Search(List<(long X, long Z)>? Path, (long X, long Z) Goal, double Ms);

    /// <summary>
    /// A collision-clear 1 m route to the stop, searched on a worker over the layout and door state taken now (both immutable).
    /// A Near stop ends at the first cell within interaction reach; the search stays in a box around both ends.
    /// </summary>
    private void Plan(Stop stop)
    {
        var sim = _session.Simulation!;
        var layout = sim.ActiveLayout;
        var closed = layout.ClosedDoors(d => sim.Doors.Single(v => v.Site == d).Open,
            b => !sim.Barriers.Single(v => v.Site == b).Standing).ToArray();
        var body = sim.Player.Body;
        var start = ((int)Math.Round(body.XMm/1000.0), (int)Math.Round(body.ZMm/1000.0));
        var end = ((int)Math.Round(stop.X/1000.0), (int)Math.Round(stop.Z/1000.0));
        long near = Math.Min(sim.Setup.Movement.InteractReachMm, sim.Setup.Items.Inventory.ReachMm) - 300;
        long radius = sim.Setup.Movement.BodyRadiusMm, height = layout.SpaceId.IsExterior ? 0 : 1800;
        var space = layout.Space;
        _searched = stop;
        if (layout.SpaceId.IsExterior)
        {
            // Outside, the frontier's own navigation lattice, as the playthrough harnesses walk it.
            ulong began = Time.GetTicksUsec();
            var corners = Playthrough.WayTo(sim, body, new Body(stop.X, 0, stop.Z, 0), standOff: stop.Near ? near / 1000.0 - 0.3 : 0)
                .Select(c => ((long)Math.Round(c.X * 1000), (long)Math.Round(c.Z * 1000))).ToList();
            _search = Task.FromResult(new Search(corners, corners.Count > 0 ? corners[^1] : (body.XMm, body.ZMm), (Time.GetTicksUsec() - began) / 1000.0));
            return;
        }
        _search = Task.Run(() =>
        {
            ulong began = Time.GetTicksUsec();
            bool Goal((int X, int Z) at) => stop.Near ? Distance(at.X * 1000L, at.Z * 1000L, stop.X, stop.Z) <= near : at == end;
            int minX = Math.Min(start.Item1, end.Item1) - 40, maxX = Math.Max(start.Item1, end.Item1) + 40;
            int minZ = Math.Min(start.Item2, end.Item2) - 40, maxZ = Math.Max(start.Item2, end.Item2) + 40;
            var parents = new Dictionary<(int X, int Z), (int X, int Z)?> { [start] = null };
            var queue = new Queue<(int X, int Z)>();
            queue.Enqueue(start);
            while (queue.TryDequeue(out var at))
            {
                if (Goal(at))
                {
                    var path = new List<(int X, int Z)>();
                    for (var current = ((int X, int Z)?)at; current is { } p; current = parents[p])
                        path.Add(p);
                    path.Reverse();
                    return new Search(path.Skip(1).Select(p => (p.X * 1000L, p.Z * 1000L)).ToList(), (at.X * 1000L, at.Z * 1000L),
                        (Time.GetTicksUsec() - began) / 1000.0);
                }
                foreach (var (dx, dz) in new[] { (1,0), (-1,0), (0,1), (0,-1) })
                {
                    var next = (at.X+dx, at.Z+dz);
                    if (next.Item1 < minX || next.Item1 > maxX || next.Item2 < minZ || next.Item2 > maxZ) continue;
                    if (parents.ContainsKey(next) || !Kinematics.IsClear(next.Item1*1000L, next.Item2*1000L, radius, space, closed, height)) continue;
                    parents[next] = at;
                    queue.Enqueue(next);
                }
            }
            return new Search(null, default, (Time.GetTicksUsec() - began) / 1000.0);
        });
    }

    private static double Distance(long x, long z, long tx, long tz) => Math.Sqrt(Math.Pow(tx-x, 2)+Math.Pow(tz-z, 2));
}
