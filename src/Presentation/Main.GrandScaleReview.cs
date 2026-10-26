using Godot;
using System.Text.Json;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Grand-scale landmark video gates (OTHERREACH_GRAND_REVIEW=foldscar|oldlate), recorded in real time (WGC) by the harness once
    // record_ready.json appears. foldscar: a fresh traveller walks from the Ashen Waystone to the Foldscar with the normal controller, turns
    // the three Quiet Stones and steadies the heart, and walks back out to look. oldlate: the same traveller walks from the Waystone past the
    // great tree to the stone giant, stands at its knee looking up at the crown, walks round it, and looks back from the east. No state
    // injection and no teleport: every switch is worked by InteractCommand.
    private async Task RunGrandReview(string directory, string landmark)
    {
        _rcd3Driving = true;
        var sim = _session.Simulation ?? throw new InvalidDataException("Foldscar gate has no world");
        _camera.Camera.MakeCurrent(); _camera.TargetDistance = 4; _camera.Pitch = -.12f;
        var events = new List<object>();
        void Mark(string phase)
        {
            var row = new { phase, utc = DateTimeOffset.UtcNow, uptime_ms = Time.GetTicksMsec(), world_tick = sim.WorldTick, body = _controller.Authoritative };
            events.Add(row); File.AppendAllText(Path.Combine(directory, "phases.jsonl"), JsonSerializer.Serialize(row) + "\n");
        }
        void Face(double x, double z, float pitch)
        {
            var body = _controller.Authoritative;
            _camera.Yaw = Mathf.Atan2(-(float)(x - body.XMm / 1000.0), -(float)(z - body.ZMm / 1000.0)); _camera.Pitch = pitch;
        }
        async Task Go(IEnumerable<(double X, double Z)> points, double budget, Gait gait)
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
                        var body = _controller.Authoritative;
                        var toward = new Vector3((float)(corner.X - body.XMm / 1000.0), 0, (float)(corner.Z - body.ZMm / 1000.0));
                        if (toward.Length() < .25f) break;
                        if (Time.GetTicksMsec() > deadline) throw new TimeoutException("Walk timed out at " + body);
                        if (_avatar.GlobalPosition.DistanceTo(last) > .1f) { last = _avatar.GlobalPosition; moved = Time.GetTicksMsec(); }
                        if (Time.GetTicksMsec() - moved > 8000) throw new InvalidDataException("Walk blocked at " + body + " toward " + corner);
                        _camera.Yaw = Mathf.Atan2(-toward.X, -toward.Z);
                        _controller.SteerWorld(toward.Normalized(), gait, _camera);
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    }
                }
            }
            _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera); await Rcd3Wait(.2);
        }
        async Task Work(string key, (double X, double Z) stand, double lookX, double lookZ, float pitch, double hold)
        {
            await Go(new[] { stand }, 60, Gait.Run);
            Face(lookX, lookZ, pitch);
            await Rcd3Wait(.5);
            _controller.Interact(key);
            ulong deadline = Time.GetTicksMsec() + 5000;
            while (!sim.Switches.Single(s => s.Site.Key == key).Set)
            {
                if (Time.GetTicksMsec() > deadline) throw new InvalidDataException("Switch refused: " + key);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Mark(key + "_set"); CloseScreens(); Face(lookX, lookZ, pitch);
            await Rcd3Wait(hold);
        }
        ulong previous = Time.GetTicksMsec(); int stableFrames = 0;
        while (stableFrames < 60)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ulong now = Time.GetTicksMsec(); stableFrames = now - previous < 250 ? stableFrames + 1 : 0; previous = now;
        }
        Mark("world_warm");
        await Go(new[] { (438.0, 754.0) }, 20, Gait.Walk);
        if (landmark == "oldlate") Face(647.5, 766.4, .1f); else if (landmark == "road") Face(488, 745, 0f); else Face(553, 648, .06f);
        await Rcd3Wait(1);
        DisplayServer.WindowSetTitle("Otherreach - " + Path.GetFileName(directory));
        File.WriteAllText(Path.Combine(directory, "record_ready.json"), "{}");
        ulong recordingDeadline = Time.GetTicksMsec() + 30000;
        while (!File.Exists(Path.Combine(directory, "recording.started")))
        {
            if (Time.GetTicksMsec() > recordingDeadline) throw new TimeoutException("Foldscar WGC recorder did not start");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        Mark("approach_start"); await Rcd3Wait(4);
        if (landmark == "road")
        {
            // Tree popping gate (owner: no tree may blink in and out): the Old East Road from the waystation to Cairnwold at a run, with a
            // slow full turn at three stations, so every tree's fade to its impostor is crossed walking and turning, at the tier's own lines.
            async Task Turn(string station)
            {
                Mark(station); _camera.Pitch = -.04f;
                float start = _camera.Yaw; ulong from = Time.GetTicksMsec();
                while (Time.GetTicksMsec() - from < 12000)
                {
                    _camera.Yaw = start + Mathf.Tau * (Time.GetTicksMsec() - from) / 12000f;
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                _camera.Pitch = -.12f;
            }
            await Go(new[] { (440.0, 740.0), (447.0, 739.0), (451.8, 742.0), (451.8, 736.0), (465.0, 736.0), (488.0, 745.0), (488.0, 720.0) }, 90, Gait.Run);
            await Turn("hollow_edge");
            await Go(new[] { (522.0, 690.0), (564.0, 690.0), (600.0, 656.0), (680.0, 657.0) }, 120, Gait.Run);
            await Turn("mid_road");
            await Go(new[] { (760.0, 658.0), (840.0, 659.0), (920.0, 660.0) }, 120, Gait.Run);
            await Turn("cairnwold_edge");
            await Go(new[] { (978.0, 653.0) }, 40, Gait.Run);
            Mark("done");
            File.WriteAllText(Path.Combine(directory, "recording.finished"), "done");
            File.WriteAllText(Path.Combine(directory, "complete.json"), JsonSerializer.Serialize(new
            {
                status = "REAL_GAME_TREE_ROAD_VIDEO_REQUIRES_CLAUDE_REVIEW", events, tier = VisualOptions.Tier,
                tree_far = System.Environment.GetEnvironmentVariable("UNNAMED_TREE_FAR") != "0",
                route = "Normal controller running the Old East Road, three full camera turns; no teleport",
                engine_max_fps = Engine.MaxFps,
            }, new JsonSerializerOptions { WriteIndented = true }));
            ulong stoppedRoad = Time.GetTicksMsec() + 15000;
            while (!File.Exists(Path.Combine(directory, "recording.stopped")) && Time.GetTicksMsec() < stoppedRoad)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            return;
        }
        if (landmark == "oldlate")
        {
            // Each walk leg goes back to the walking pitch; a held upward look carried into a walk drops the camera into the grass.
            void Walking() { _camera.Pitch = -.12f; _camera.TargetDistance = 4; }
            await Go(new[] { (470.0, 756.0), (520.0, 752.0) }, 120, Gait.Walk);
            Mark("great_tree"); Face(578, 748, .12f); await Rcd3Wait(4);
            Walking(); await Go(new[] { (560.0, 768.0), (600.0, 768.0) }, 120, Gait.Walk);
            Mark("giant_west"); Face(647.5, 766.4, .12f); await Rcd3Wait(6);
            Walking(); await Go(new[] { (614.0, 758.0) }, 60, Gait.Walk);
            _camera.TargetDistance = 3;
            Mark("at_the_knee"); Face(645, 766.4, .25f); await Rcd3Wait(8);
            Walking(); await Go(new[] { (610.0, 735.0), (647.0, 722.0) }, 120, Gait.Walk);
            Mark("from_the_south"); Face(647.5, 766.4, .15f); await Rcd3Wait(6);
            Walking(); await Go(new[] { (690.0, 740.0), (712.0, 768.0) }, 120, Gait.Walk);
            Face(647.5, 766.4, .1f);
            await Go(new[] { (705.0, 768.0) }, 20, Gait.Walk);
            Mark("from_the_east"); await Rcd3Wait(6);
            Mark("done");
            File.WriteAllText(Path.Combine(directory, "recording.finished"), "done");
            File.WriteAllText(Path.Combine(directory, "complete.json"), JsonSerializer.Serialize(new
            {
                status = "REAL_GAME_OLD_LATE_VIDEO_REQUIRES_CLAUDE_REVIEW", events,
                route = "Normal controller walking from the Ashen Waystone past the great tree, round the stone giant and out east; no state injection or teleport",
                engine_max_fps = Engine.MaxFps, people = VisualOptions.People,
            }, new JsonSerializerOptions { WriteIndented = true }));
            ulong stopped = Time.GetTicksMsec() + 15000;
            while (!File.Exists(Path.Combine(directory, "recording.stopped")) && Time.GetTicksMsec() < stopped)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            return;
        }
        await Go(new[] { (465.0, 736.0), (488.0, 720.0), (514.0, 694.0) }, 150, Gait.Walk);
        Mark("ring_edge"); Face(553, 648, .08f); await Rcd3Wait(5);
        await Work("switch.stone_north", (547.2, 678.0), 550, 678, .06f, 3);
        await Work("switch.stone_southwest", (524.8, 638.0), 522, 638, .06f, 3);
        await Work("switch.stone_southeast", (583.8, 631.0), 581, 631, .06f, 3);
        _camera.TargetDistance = 10;
        await Work("switch.foldscar_heart", (553.0, 652.3), 553, 648, .1f, 9);
        Mark("released");
        _camera.TargetDistance = 4;
        await Go(new[] { (505.0, 706.0) }, 60, Gait.Walk);
        // Turned back to face it by walking towards it: the walk leaves the camera behind the character, looking at the heart.
        _camera.Pitch = .06f;
        await Go(new[] { (518.0, 692.0) }, 20, Gait.Walk); await Rcd3Wait(6);
        Mark("done");
        File.WriteAllText(Path.Combine(directory, "recording.finished"), "done");
        File.WriteAllText(Path.Combine(directory, "complete.json"), JsonSerializer.Serialize(new
        {
            status = "REAL_GAME_FOLDSCAR_VIDEO_REQUIRES_CLAUDE_REVIEW", events,
            route = "Normal controller walking from the Ashen Waystone, InteractCommand on the three Quiet Stones and the heart; no state injection or teleport",
            engine_max_fps = Engine.MaxFps, people = VisualOptions.People,
        }, new JsonSerializerOptions { WriteIndented = true }));
        ulong stoppedDeadline = Time.GetTicksMsec() + 15000;
        while (!File.Exists(Path.Combine(directory, "recording.stopped")) && Time.GetTicksMsec() < stoppedDeadline)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
