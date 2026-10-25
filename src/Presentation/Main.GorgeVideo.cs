using Godot;
using System.Text.Json;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    // The gorge's real-time gate (spec 28), recorded by WGC from the game window: the walk to the lip past the den, the vista, Halda's rope
    // giving way, the hollow cleared and its store taken, and the climb back out by the cleft. Normal controller input only - walking,
    // the attack, Interact - over the world's own spawns, portals and containers; the start place and level are the run's fixtures.
    private async Task RunGorgeVideo(string directory)
    {
        _rcd3Driving = true;
        var sim = _session.Simulation ?? throw new InvalidDataException("No world for the gorge gate");
        _npcs.SuppressPresentationForBenchmark = false;
        _camera.Camera.MakeCurrent();
        _camera.TargetDistance = 5f; _camera.Pitch = -.18f;
        var events = new List<object>();
        void Mark(string phase, object? detail = null)
        {
            var row = new { phase, detail, utc = DateTimeOffset.UtcNow, uptime_ms = Time.GetTicksMsec(), world_tick = sim.WorldTick,
                space = sim.ActiveLayout.SpaceId.ToString(), body = _controller.Authoritative, health = sim.Combat.Health };
            events.Add(row); File.AppendAllText(Path.Combine(directory, "phases.jsonl"), JsonSerializer.Serialize(row) + "\n");
        }
        double Away(Body a, Body b) => Math.Sqrt(Math.Pow(a.XMm - b.XMm, 2) + Math.Pow(a.ZMm - b.ZMm, 2));
        CreatureView? Threat(double within) => sim.Creatures
            .Where(c => c.Alive && c.Hostile && Away(_controller.Authoritative, c.Body) < within * 1000)
            .OrderBy(c => Away(_controller.Authoritative, c.Body)).FirstOrDefault();
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
                    if (Threat(14) is not { } next) break;
                    target = next;
                    continue;
                }
                if (sim.Combat.Health <= 0 || sim.Combat.Downed)
                    throw new InvalidDataException("The character fell in the gorge gate to " + current.DefId);
                if (Time.GetTicksMsec() > deadline)
                    throw new TimeoutException("Gorge fight ran past its budget against " + current.DefId);
                var body = _controller.Authoritative;
                long radius = _session.Setup.Combat.Creatures[current.DefId].RadiusMm;
                float reach = (sim.Combat.Weapon.ReachMm + radius - 150) / 1000f;
                var to = new Vector3((float)((current.Body.XMm - body.XMm) / 1000.0), 0, (float)((current.Body.ZMm - body.ZMm) / 1000.0));
                _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
                bool inReach = to.Length() <= reach;
                _controller.SteerWorld(inReach ? Vector3.Zero : to.Normalized(), Gait.Run, _camera, faceCamera: true);
                if (inReach && sim.Combat.Phase == CombatPhase.Idle)
                    _controller.Attack();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
            Mark("fight_end");
        }
        // Outside, the frontier's own way-finding; in the hollow, the given points are its passages' middle line.
        async Task Go(IEnumerable<(double X, double Z)> points, double budget, Gait gait, double fightWithin = 9)
        {
            ulong deadline = Time.GetTicksMsec() + (ulong)(budget * 1000);
            foreach (var point in points)
            {
                var goal = new Body((long)Math.Round(point.X * 1000), 0, (long)Math.Round(point.Z * 1000), 0);
                var corners = sim.PlayerInExterior ? Playthrough.WayTo(sim, _controller.Authoritative, goal, 0) : new[] { (X: point.X, Z: point.Z) };
                foreach (var (cx, cz) in corners)
                {
                    var last = _avatar.GlobalPosition; ulong moved = Time.GetTicksMsec();
                    while (true)
                    {
                        if (fightWithin > 0 && Threat(fightWithin) is { } threat)
                        {
                            await Fight(threat, 240);
                            moved = Time.GetTicksMsec();
                        }
                        var body = _controller.Authoritative;
                        var toward = new Vector3((float)(cx - body.XMm / 1000.0), 0, (float)(cz - body.ZMm / 1000.0));
                        if (toward.Length() < .3f) break;
                        if (Time.GetTicksMsec() > deadline) throw new TimeoutException("Gorge route timed out at " + body);
                        if (_avatar.GlobalPosition.DistanceTo(last) > .1f) { last = _avatar.GlobalPosition; moved = Time.GetTicksMsec(); }
                        if (Time.GetTicksMsec() - moved > 8000) throw new InvalidDataException($"Gorge route blocked at {body} toward ({cx}, {cz})");
                        _camera.Yaw = Mathf.Atan2(-toward.X, -toward.Z);
                        _controller.SteerWorld(toward.Normalized(), gait, _camera);
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    }
                }
            }
            _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera); await Rcd3Wait(.2);
        }
        async Task Look((double X, double Z) at, float pitch, double seconds)
        {
            var body = _controller.Authoritative;
            _camera.Yaw = Mathf.Atan2(-(float)(at.X - body.XMm / 1000.0), -(float)(at.Z - body.ZMm / 1000.0));
            _camera.Pitch = pitch;
            await Rcd3Wait(seconds);
        }
        async Task Use(string portal, bool inside)
        {
            for (int i = 0; i < 10 && sim.PlayerInExterior == inside; i++) { _controller.Interact(portal); await Rcd3Wait(.4); }
            if (sim.PlayerInExterior == inside) throw new InvalidDataException("The way would not take the character: " + portal);
            Mark(inside ? "went_down" : "came_up", portal);
            while (_rope is not null) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Rcd3Wait(.5);
        }
        // OTHERREACH_GORGE_DIAG: the same view inside the hollow with one layer hidden at a time - a labelled diagnostic, never the look.
        async Task Diagnose(string where)
        {
            if (OS.GetEnvironment("OTHERREACH_GORGE_DIAG") != "1" || _interior is not { } cave)
                return;
            var roof = cave.FindChild("roof", true, false) as Node3D;
            var terrain = _hollow.FindChild("Terrain3D", true, false) as Node3D;
            foreach (var world in GetTree().Root.FindChildren("*", nameof(WorldEnvironment), true, false).OfType<WorldEnvironment>())
                File.AppendAllText(Path.Combine(directory, "diag_air.txt"),
                    $"{where} {world.GetPath()} environment={(world.Environment is null ? "none" : "set")} compositor={(world.Compositor is null ? "none" : "set")}" + "\n");
            _camera.Pitch = .55f; await Rcd3Wait(1);
            await Rcd3Shot(directory, $"diag_{where}_as_is");
            foreach (var (name, node) in new[] { ("no_roof", roof), ("no_frontier", (Node3D)_hollow), ("no_terrain3d", terrain) })
            {
                if (node is null) continue;
                node.Visible = false; await Rcd3Wait(.6);
                await Rcd3Shot(directory, $"diag_{where}_{name}");
                node.Visible = true;
            }
            _camera.Pitch = -.18f;
        }
        ulong previous = Time.GetTicksMsec(); int stable = 0;
        while (stable < 60)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ulong now = Time.GetTicksMsec(); stable = now - previous < 250 ? stable + 1 : 0; previous = now;
        }
        Mark("world_warm");
        DisplayServer.WindowSetTitle("Otherreach - " + Path.GetFileName(directory));
        File.WriteAllText(Path.Combine(directory, "record_ready.json"), "{}");
        ulong recordingDeadline = Time.GetTicksMsec() + 30000;
        while (!File.Exists(Path.Combine(directory, "recording.started")))
        {
            if (Time.GetTicksMsec() > recordingDeadline) throw new TimeoutException("Gorge WGC recorder did not start");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        Mark("recording");
        // The approach: up past the den's east rock to the lip, the pack on the way if it is home.
        await Look((540, 810), -.1f, 3);
        await Go(new[] { (548.0, 790.0), (547.0, 798.6) }, 150, Gait.Walk, 16);
        Mark("lip");
        _camera.TargetDistance = 3.2f;
        await Look((540, 870), -.18f, 5);
        await Look((497, 850), -.12f, 8);
        await Look((640, 827), -.28f, 5);
        await Look((540, 812), -.55f, 4);
        // Halda's rope.
        await Go(new[] { (547.5, 798.9) }, 30, Gait.Walk, 0);
        Mark("at_rope"); await Look((547.5, 806), -.5f, 2.5);
        await Use("portal.den_gorge.rope", inside: true);
        Mark("on_ledge"); _camera.TargetDistance = 4f; await Diagnose("ledge");
        await Look((520, 840), -.1f, 5);
        await Look((539, 790), -.05f, 2.5);
        // The hollow: the mouth, the young in the antechamber, the squeeze, the matriarch's chamber, Halda's store.
        await Go(new[] { (539.0, 799.0), (539.5, 791.0), (540.0, 786.0) }, 180, Gait.Walk, 9);
        Mark("antechamber"); await Diagnose("antechamber");
        await Go(new[] { (537.0, 781.5), (532.5, 777.0), (528.0, 772.5), (524.0, 768.0) }, 180, Gait.Walk, 9);
        Mark("great_chamber"); await Diagnose("great_chamber"); await Look((511.5, 773.5), .25f, 3);
        await Go(new[] { (518.0, 765.0) }, 240, Gait.Walk, 22);
        Mark("chamber_clear");
        await Look((503, 754), .05f, 3);
        await Go(new[] { (531.0, 760.0), (537.5, 756.5), (540.8, 755.0) }, 120, Gait.Walk, 9);
        await Look((541.5, 754.0), -.2f, 1.5);
        _session.Submit(new TakeAllCommand(sim.PlayerId, "container.den_gorge.haldas_store"));
        await Rcd3Wait(2);
        bool knife = sim.Player.Inventory.Any(e => e.DefId == "item.weapon.haldas_knife");
        Mark("store_taken", new { knife });
        // And out: back to the ledge and up the cleft.
        await Go(new[] { (537.5, 756.5), (531.0, 760.0), (524.0, 768.0), (528.0, 772.5), (532.5, 777.0), (537.0, 781.5), (540.0, 786.0),
            (539.5, 791.0), (539.0, 799.0), (548.0, 802.5), (557.0, 804.4) }, 240, Gait.Walk, 9);
        Mark("cleft_foot"); await Look((557, 799), .2f, 2);
        await Use("portal.den_gorge.cleft", inside: false);
        _camera.TargetDistance = 4f;
        await Look((547.5, 806), -.45f, 5);
        // The fall again at dusk, from where the cleft comes up.
        VisualOptions.Pin("hour", "18.5");
        await Rcd3Wait(2);
        Mark("dusk");
        _camera.TargetDistance = 3.2f;
        await Look((497, 850), -.12f, 8);
        await Look((560, 870), -.2f, 4);
        Mark("done");
        File.WriteAllText(Path.Combine(directory, "recording.finished"), "done");
        File.WriteAllText(Path.Combine(directory, "complete.json"), JsonSerializer.Serialize(new
        {
            status = "REAL_GAME_GORGE_VIDEO_REQUIRES_CLAUDE_REVIEW", knife, events,
            route = "Normal controller: walk past the den to the lip, Interact on Halda's rope, fight the hollow's spiders, TakeAll at Halda's store, Interact on the cleft; no state injection or teleport",
        }, new JsonSerializerOptions { WriteIndented = true }));
        ulong stoppedDeadline = Time.GetTicksMsec() + 15000;
        while (!File.Exists(Path.Combine(directory, "recording.stopped")) && Time.GetTicksMsec() < stoppedDeadline)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
