using Godot;
using System.Text.Json;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    // The Spill's real-time gate (docs/FIELD_RUINS.md): one leg per run, recorded from the game window. Normal controller input only -
    // walking, the attack, Interact - over the region's own spawns and switches; the start place (--stand) and level (--start-level)
    // are the run's fixtures, and nothing else is set.
    private async Task RunFieldRuinsVideo(string directory)
    {
        _rcd3Driving = true;
        var sim = _session.Simulation ?? throw new InvalidDataException("No world for the field-ruins gate");
        string leg = OS.GetEnvironment("OTHERREACH_FIELD_RUINS_LEG");
        _npcs.SuppressPresentationForBenchmark = false;
        _camera.Camera.MakeCurrent();
        _camera.TargetDistance = 5.5f; _camera.Pitch = -.16f;
        var events = new List<object>();
        int startHealth = sim.Combat.Health;
        void Mark(string phase, object? detail = null)
        {
            var row = new { phase, detail, utc = DateTimeOffset.UtcNow, uptime_ms = Time.GetTicksMsec(), world_tick = sim.WorldTick,
                body = _controller.Authoritative, health = sim.Combat.Health, level = sim.Player.Progression.Level,
                // The game's own allocations: nvidia-smi on a 24 GB card shows the driver filling what is free, not what the game needs.
                video_mb = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576,
                textures_mb = Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) / 1048576,
                buffers_mb = Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed) / 1048576,
                fps = Engine.GetFramesPerSecond(), creatures_within_60m = sim.Creatures.Count(c => c.Alive && Distance(_controller.Authoritative, c.Body) < 60_000),
                nearest_hostile = Threat(60) is { } near ? $"{near.DefId} {Distance(_controller.Authoritative, near.Body) / 1000:0} m {near.Mind}" : null };
            events.Add(row); File.AppendAllText(Path.Combine(directory, "phases.jsonl"), JsonSerializer.Serialize(row) + "\n");
        }
        CreatureView? Threat(double within) => sim.Creatures
            .Where(c => c.Alive && c.Hostile && Distance(_controller.Authoritative, c.Body) < within * 1000)
            .OrderBy(c => Distance(_controller.Authoritative, c.Body)).FirstOrDefault();
        // Close to the sword's reach of the creature's own body and swing whenever ready; behind a slow turner, circle to its back first.
        async Task Fight(CreatureView first, double budget)
        {
            Mark("fight_start", new { first.DefId, first.Key });
            ulong deadline = Time.GetTicksMsec() + (ulong)(budget * 1000);
            var target = first;
            while (true)
            {
                var current = sim.Creatures.Single(c => c.Key == target.Key);
                if (!current.Alive)
                {
                    Mark("kill", new { current.DefId, current.Key });
                    if (Threat(14) is not { } next)
                        break;
                    target = next;
                    continue;
                }
                if (sim.Combat.Health <= 0 || sim.Combat.Downed)
                    throw new InvalidDataException("The character died in the field-ruins gate");
                if (Time.GetTicksMsec() > deadline)
                    throw new TimeoutException("Field-ruins fight ran past its budget against " + current.DefId);
                var body = _controller.Authoritative;
                long radius = _session.Setup.Combat.Creatures[current.DefId].RadiusMm;
                float reach = (sim.Combat.Weapon.ReachMm + radius - 150) / 1000f;
                var to = new Vector3((float)((current.Body.XMm - body.XMm) / 1000.0), 0, (float)((current.Body.ZMm - body.ZMm) / 1000.0));
                var goal = to;
                // The slow-turning constructs are fought from behind, where their plate is open (their lore's own advice).
                if (current.DefId is "creature.construct.gate_warden" or "creature.construct.animated_armour")
                {
                    float facing = current.Body.FacingMdeg / 1000f * Mathf.Pi / 180f;
                    var forward = new Vector3(Mathf.Sin(facing), 0, Mathf.Cos(facing));
                    var behind = to - forward * (reach - .5f);
                    if (behind.Length() > .7f)
                        goal = behind;
                }
                _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
                bool inReach = to.Length() <= reach;
                bool settled = goal == to ? inReach : goal.Length() <= .7f;
                _controller.SteerWorld(settled ? Vector3.Zero : goal.Normalized(), Gait.Run, _camera, faceCamera: true);
                if (inReach && sim.Combat.Phase == CombatPhase.Idle)
                    _controller.Attack();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
            Mark("fight_end");
        }
        ulong sampled = 0;
        // A careful body gets its breath back before walking on: out of a fight below half health, it waits (up to 25 s) to regain some.
        async Task Rest()
        {
            if (sim.Combat.Health * 2 >= sim.Combat.MaxHealth)
                return;
            Mark("rest");
            for (int i = 0; i < 50 && sim.Combat.Health * 5 < sim.Combat.MaxHealth * 4 && Threat(12) is null; i++)
                await Rcd3Wait(.5);
        }
        async Task Go(IEnumerable<(double X, double Z)> points, double budget, Gait gait, double fightWithin = 9)
        {
            ulong deadline = Time.GetTicksMsec() + (ulong)(budget * 1000);
            foreach (var point in points)
            {
                var goal = new Body((long)Math.Round(point.X * 1000), 0, (long)Math.Round(point.Z * 1000), 0);
                foreach (var corner in Playthrough.WayTo(sim, _controller.Authoritative, goal, 0))
                {
                    var last = _avatar.GlobalPosition; ulong moved = Time.GetTicksMsec();
                    while (true)
                    {
                        if (Threat(fightWithin) is { } threat)
                        {
                            await Fight(threat, 150);
                            await Rest();
                            moved = Time.GetTicksMsec();
                        }
                        var body = _controller.Authoritative;
                        var toward = new Vector3((float)(corner.X - body.XMm / 1000.0), 0, (float)(corner.Z - body.ZMm / 1000.0));
                        if (toward.Length() < .3f)
                            break;
                        if (Time.GetTicksMsec() > deadline)
                            throw new TimeoutException("Field-ruins route timed out at " + body);
                        if (_avatar.GlobalPosition.DistanceTo(last) > .1f) { last = _avatar.GlobalPosition; moved = Time.GetTicksMsec(); }
                        if (Time.GetTicksMsec() - moved > 8000)
                            throw new InvalidDataException("Field-ruins route blocked at " + body + " toward " + corner);
                        if (Time.GetTicksMsec() - sampled > 3000) { sampled = Time.GetTicksMsec(); Mark("sample"); }
                        _camera.Yaw = Mathf.Atan2(-toward.X, -toward.Z);
                        _controller.SteerWorld(toward.Normalized(), gait, _camera);
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    }
                }
            }
            _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera); await Rcd3Wait(.2);
        }
        async Task Still(string key, (double X, double Z) spot)
        {
            await Go(new[] { spot }, 40, Gait.Run, 0);
            var view = sim.Switches.Single(s => s.Site.Key == key);
            var (x, z) = Footprints.Center(view.Site.Body);
            var body = _controller.Authoritative;
            _camera.Yaw = Mathf.Atan2(-(float)((x - body.XMm) / 1000.0), -(float)((z - body.ZMm) / 1000.0));
            await Rcd3Wait(.6);
            for (int i = 0; i < 20 && !sim.Switches.Single(s => s.Site.Key == key).Set; i++)
            {
                _controller.Interact(key); await Rcd3Wait(.5);
            }
            if (!sim.Switches.Single(s => s.Site.Key == key).Set)
                throw new InvalidDataException("The switch would not set: " + key);
            Mark("switch_set", key); await Rcd3Wait(2.5);
        }
        async Task Look((double X, double Z) at, double seconds)
        {
            var body = _controller.Authoritative;
            _camera.Yaw = Mathf.Atan2(-(float)(at.X - body.XMm / 1000.0), -(float)(at.Z - body.ZMm / 1000.0));
            await Rcd3Wait(seconds);
        }
        Mark("harness_start", new { leg });
        // Lazy model loads span several frames: a stable renderer first, then the recorder.
        ulong previous = Time.GetTicksMsec(), reported = previous; int stable = 0; ulong worst = 0;
        while (stable < 60)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ulong now = Time.GetTicksMsec(); worst = Math.Max(worst, now - previous);
            stable = now - previous < 250 ? stable + 1 : 0; previous = now;
            if (now - reported > 10_000)
            {
                Mark("warming", new { worst_frame_ms = worst, stable });
                reported = now; worst = 0;
            }
        }
        Mark("world_warm", new { leg });
        DisplayServer.WindowSetTitle("Otherreach - " + Path.GetFileName(directory));
        File.WriteAllText(Path.Combine(directory, "record_ready.json"), "{}");
        ulong recordingDeadline = Time.GetTicksMsec() + 30000;
        while (!File.Exists(Path.Combine(directory, "recording.started")))
        {
            if (Time.GetTicksMsec() > recordingDeadline)
                throw new TimeoutException("Field-ruins WGC recorder did not start");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        Mark("recording");
        switch (leg)
        {
            case "west":
                // From Old Late's east side: the Needle ahead over the open ground, then the Spill's west lane and its tenders.
                await Look((890, 752), 4);
                await Go(new[] { (700.0, 768.0), (760.0, 766.0) }, 120, Gait.Walk);
                Mark("spill_in_view"); await Look((890, 752), 3);
                await Go(new[] { (790.0, 760.0), (806.0, 758.0) }, 120, Gait.Run, 16);
                Mark("west_lane"); await Look((850, 758), 5);
                break;
            case "east":
                // From Cairnwold's green: the Needle over the trees, up the scarp by the east lane between the north rib and the Needle.
                await Look((890, 752), 4);
                await Go(new[] { (935.0, 728.0), (915.0, 729.0) }, 90, Gait.Walk);
                Mark("scarp_foot"); await Look((890, 740), 3);
                // The lane ends short of the north-east breach: its armour is the court's (level 5), not the approach's.
                await Go(new[] { (898.0, 729.0), (884.0, 728.0) }, 120, Gait.Run, 16);
                Mark("east_lane"); await Look((850, 758), 5);
                break;
            case "core":
                // The court, from its north rim: the warden from behind (and the breach's armour, if it comes), the three anchors and the Needle.
                _camera.TargetDistance = 7f; _camera.Pitch = -.2f;
                await Look((866, 762), 4);
                Mark("in_court");
                // A sentinel holds its post until it notices a body: walk to the warden and take it on from behind.
                if (sim.Creatures.FirstOrDefault(c => c.DefId == "creature.construct.gate_warden" && c.Alive) is { } warden)
                    await Fight(warden, 240);
                // Each spot is within reach (1.6 m from the structure's edge) of the switch it stills.
                await Still("switch.field.anchor_west", (840.2, 759.4));
                await Still("switch.field.anchor_northeast", (856.3, 748.6));
                await Still("switch.field.anchor_south", (852.1, 767.4));
                await Still("switch.field.needle", (873.9, 752.0));
                if (!sim.Switches.Single(s => s.Site.Key == "switch.field.needle").Set)
                    throw new InvalidDataException("The gate did not settle");
                Mark("gate_stilled"); await Look((890, 752), 6);
                break;
            case "route":
                // The new player's way east (the population pass): from the Ashen Waystone along the Old East Road, past the Lantern Shrine
                // and the overturned wain, up by the Rim Stone and into the Spill's east lane - fighting whatever comes, resting when hurt.
                await Look((600, 656), 3);
                // A body that keeps running outpaces most of it to the leash; a player turns on what comes, so the route does.
                await Go(new[] { (464.0, 748.0), (522.0, 690.0), (564.0, 690.0), (600.0, 656.0) }, 300, Gait.Run, 25);
                Mark("foldscar_turn");
                await Go(new[] { (680.0, 657.0), (760.0, 658.0), (840.0, 659.0) }, 360, Gait.Run, 25);
                Mark("road_east");
                await Go(new[] { (880.0, 660.0), (881.3, 701.8), (884.0, 728.0) }, 240, Gait.Run, 25);
                Mark("east_lane"); await Look((850, 758), 5);
                break;
            case "busy":
                // The spot the population pass crowds most (the court's north side): thirty seconds of frame rate, fighting what comes.
                await Look((866, 762), 2);
                for (ulong until = Time.GetTicksMsec() + 30_000; Time.GetTicksMsec() < until;)
                {
                    if (Threat(10) is { } threat)
                        await Fight(threat, 120);
                    Mark("sample"); await Rcd3Wait(1);
                }
                break;
            default:
                throw new InvalidDataException("OTHERREACH_FIELD_RUINS_LEG must be west, east, core, route or busy");
        }
        File.WriteAllText(Path.Combine(directory, "recording.finished"), "done");
        File.WriteAllText(Path.Combine(directory, "complete.json"), JsonSerializer.Serialize(new
        {
            status = "REAL_GAME_FIELD_RUINS_VIDEO_REQUIRES_CLAUDE_REVIEW", leg, events, start_health = startHealth,
            route = "Normal controller walking, attacks and Interact; start place and level are the run's only fixtures",
            engine_max_fps = Engine.MaxFps, people = VisualOptions.People,
        }, new JsonSerializerOptions { WriteIndented = true }));
        ulong stoppedDeadline = Time.GetTicksMsec() + 15000;
        while (!File.Exists(Path.Combine(directory, "recording.stopped")) && Time.GetTicksMsec() < stoppedDeadline)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
