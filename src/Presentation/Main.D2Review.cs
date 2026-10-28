using Godot;
using System.Text.Json;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Presentation;

public partial class Main
{
    /// <summary>
    /// The D2 evidence pass (owner notes 16 and 18), opt-in with <c>--d2-review &lt;dir&gt;</c> and a route: <c>waystone</c> walks the character up
    /// to the Ashen Waystone and lays a hand on it, so the attune moment - the runes, the ring along the ground, the surge and the chime -
    /// plays in a recorded clip; <c>rain</c> walks a short lap of the waystation yard and stands in it, so the reworked rain, its splashes and
    /// the wet ground are recorded. Nothing is injected: the character walks on the normal stick and touches the stone with Interact.
    /// The clip itself is the movie writer's, so the master audio is in it.
    /// </summary>
    private async Task RunD2Review(string directory, string route)
    {
        _rcd3Driving = true;
        var sim = _session.Simulation ?? throw new InvalidDataException("D2 review has no world");
        _npcs.SuppressPresentationForBenchmark = false;
        _camera.Camera.MakeCurrent();
        _camera.TargetDistance = 4; _camera.Pitch = -.16f;
        var events = new List<object>();
        void Mark(string phase, object? detail = null)
        {
            var row = new { phase, detail, utc = DateTimeOffset.UtcNow, uptime_ms = Time.GetTicksMsec(), world_tick = sim.WorldTick,
                body = _controller.Authoritative };
            events.Add(row); File.AppendAllText(Path.Combine(directory, "phases.jsonl"), JsonSerializer.Serialize(row) + "\n");
        }
        // Lazy loads span frames; nothing is timed until the renderer has been steady for a second.
        ulong previous = Time.GetTicksMsec(); int stable = 0;
        while (stable < 60)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ulong now = Time.GetTicksMsec(); stable = now - previous < 250 ? stable + 1 : 0; previous = now;
        }
        Mark("world_warm");
        DisplayServer.WindowSetTitle("Otherreach - d2 review " + route);
        CloseScreens();

        async Task Go((double X, double Z)[] points, double budget)
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
                        if (Time.GetTicksMsec() > deadline) throw new TimeoutException($"D2 review walk timed out at {body}");
                        if (_avatar.GlobalPosition.DistanceTo(last) > .1f) { last = _avatar.GlobalPosition; moved = Time.GetTicksMsec(); }
                        if (Time.GetTicksMsec() - moved > 8000) throw new InvalidDataException($"D2 review walk blocked at {body} toward {corner}");
                        _camera.Yaw = Mathf.Atan2(-toward.X, -toward.Z);
                        _controller.SteerWorld(toward.Normalized(), Gait.Run, _camera);
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    }
                }
            }
            _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera); await Rcd3Wait(.3);
        }

        try
        {
            if (route == "traversal")
            {
                // BRIEF D6 evidence pass: a running jump, a vault over the fallen timber, a climb onto the smithy fence and a climb up the
                // rim boulder, all driven through the normal stick and the normal jump key - nothing is injected into the simulation.
                _camera.Pitch = -.10f;
                Mark("traversal_start");

                // 1. The running jump, in the open yard, so the arc and the airtime are what the frame shows.
                await Go(new[] { (30.0, 150.0) }, 25);
                Mark("running_jump");
                _controller.SteerWorld(new Vector3(0, 0, -1), Gait.Run, _camera);
                await Rcd3Wait(.35);
                _controller.Jump();
                await Rcd3Wait(.55);
                _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
                await Rcd3Wait(.6);
                Mark("jump_done");

                // 2. The vault: run at the fallen timber (0.8 m, inside the 1.2 m window) with no jump at all.
                await Go(new[] { (167.0, 143.0) }, 40);
                Mark("vault_approach");
                _controller.SteerWorld(new Vector3(0, 0, -1), Gait.Run, _camera);
                await Rcd3Wait(1.8);
                _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
                await Rcd3Wait(.6);
                Mark("vault_done", _controller.Authoritative);

                // 3. The climb onto the smithy fence (1.2 m) - walk into it and the body mounts the top.
                await Go(new[] { (47.5, 149.0), (47.5, 147.6) }, 60);
                Mark("fence_approach");
                _controller.SteerWorld(new Vector3(0, 0, -1), Gait.Run, _camera);
                await Rcd3Wait(1.4);
                _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
                await Rcd3Wait(.6);
                Mark("fence_climbed", _controller.Authoritative);

                // 4. The climb up the rim boulder (2.57 m, inside the 3 m mantle reach): a wall climb, not a vault.
                await Go(new[] { (12.5, 88.0), (11.0, 84.6) }, 70);
                Mark("rock_approach");
                _controller.SteerWorld(new Vector3(-1, 0, -0.35f), Gait.Run, _camera);
                await Rcd3Wait(1.6);
                _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
                await Rcd3Wait(.8);
                Mark("rock_climbed", _controller.Authoritative);

                // 5. The ladder (D6): the owned wooden ladder at (49.6, 147.2), 3.2 m, climbable at any height by its prefix.
                await Go(new[] { (51.6, 147.2), (50.6, 147.2) }, 70);
                Mark("ladder_approach");
                _controller.SteerWorld(new Vector3(-1, 0, 0), Gait.Run, _camera);
                await Rcd3Wait(1.6);
                _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
                await Rcd3Wait(.6);
                Mark("ladder_climbed", _controller.Authoritative);
                // 6. The scramble: the waystation's own slope, walked up at a pace rather than a creep.
                await Go(new[] { (30.0, 140.0) }, 60);
                _controller.SteerWorld(new Vector3(0, 0, -1), Gait.Run, _camera);
                await Rcd3Wait(1.4);
                _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
                await Rcd3Wait(.5);
                Mark("slope_scrambled", _controller.Authoritative);
                _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
                await Rcd3Wait(.4);
            }
            else if (route == "rain")
            {
                // A lap of the yard and then standing in it: enough rain in frame to judge the streaks, the splashes and the wet ground.
                // The spawn is already out in the open, looking east down the waystation: stand in it and turn slowly, which shows the
                // streaks against the sky, then against the buildings, and the wet ground under both.
                Mark("rain_stand");
                _camera.Pitch = -.04f;
                // The rain is off in the weather weights the owner asked for, and the world's first front is fair: pin the drawn sky to rain
                // for the length of this clip so the reworked streaks, splashes and the wet ground are what is being filmed.
                var sky = GetNodeOrNull<WorldEnvironment>("Sky") ?? FindChild("*", true, false) as WorldEnvironment;
                for (int i = 0; i < 120 && sky is not null; i++)
                {
                    Greybox.WeatherStates.Apply(sky, "rain");
                    if (i % 20 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                await Rcd3Wait(3);
                Mark("rain_turn");
                float startYaw = _camera.Yaw;
                for (int i = 0; i < 60; i++)
                {
                    _camera.Yaw = startYaw + Mathf.Tau * i / 120f;
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                Mark("rain_ground");
                _camera.Pitch = -.45f;
                await Rcd3Wait(4);
            }
            else
            {
                Mark("walk_to_stone");
                await Go(new[] { (428.9, 758.0) }, 60);
                var stone = sim.Waystones.Single(w => w.Site.Key == "waystone.ashen").Site;
                _camera.TargetDistance = 5f; _camera.Pitch = -.08f;
                // One step toward the stone with the stick, so the character turns to it and the camera with them: the answer plays
                // in front of the camera instead of off its shoulder.
                for (int i = 0; i < 30; i++)
                {
                    var body = _controller.Authoritative;
                    var toward = new Vector3((float)(stone.ArrivalXMm / 1000.0 - body.XMm / 1000.0), 0,
                        (float)(stone.ArrivalZMm / 1000.0 - body.ZMm / 1000.0));
                    if (toward.Length() < 1.4f) break;
                    _camera.Yaw = Mathf.Atan2(-toward.X, -toward.Z);
                    _controller.SteerWorld(toward.Normalized(), Gait.Walk, _camera);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
                await Rcd3Wait(.6);
                Mark("attune");
                _controller.TouchStone("waystone.ashen");
                // The stone opens its own screen on a touch; close it at once so the answer itself is what the clip shows.
                CloseScreens();
                await Rcd3Wait(.4);
                CloseScreens();
                // The answer: runes, the ring, the surge and the chime, then the toast.
                await Rcd3Wait(3.5);
                bool known = sim.Waystones.Single(w => w.Site.Key == "waystone.ashen").Known;
                Mark("attuned", new { known });
                if (!known) throw new InvalidDataException("The stone refused the touch");
                // Departure and arrival, through the same travel the panel asks for: the Ashen stone back to itself is a real journey of no
                // distance, so the departure and arrival effects and their sounds play without a second stone being attuned first.
                Mark("travel");
                TravelWithEffects("waystone.ashen", "waystone.ashen");
                await Rcd3Wait(6);
            }
            File.WriteAllText(Path.Combine(directory, "complete.json"), JsonSerializer.Serialize(new
            {
                status = "D2_REVIEW_COMPLETE", route, events, world_tick = sim.WorldTick,
                body = _controller.Authoritative, engine_max_fps = Engine.MaxFps, weather = Greybox.WeatherStates.Now,
            }, new JsonSerializerOptions { WriteIndented = true }));
            Mark("complete");
        }
        catch (Exception e)
        {
            Mark("failed", e.Message);
            File.WriteAllText(Path.Combine(directory, "failed.json"), JsonSerializer.Serialize(new { route, error = e.Message, events }));
            throw;
        }
        await Rcd3Wait(.5);
        GetTree().Quit();
    }
}
