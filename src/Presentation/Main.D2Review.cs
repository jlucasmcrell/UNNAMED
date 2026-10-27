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
            if (route == "rain")
            {
                // A lap of the yard and then standing in it: enough rain in frame to judge the streaks, the splashes and the wet ground.
                Mark("rain_walk");
                await Go(new[] { (34.0, 150.0), (40.0, 146.0), (46.0, 150.0) }, 90);
                _camera.Pitch = -.05f;
                Mark("rain_stand");
                await Rcd3Wait(6);
                Mark("rain_close");
                await Rcd3Wait(3);
            }
            else
            {
                Mark("walk_to_stone");
                await Go(new[] { (428.9, 758.0) }, 60);
                var body = _controller.Authoritative;
                var stone = sim.Waystones.Single(w => w.Site.Key == "waystone.ashen").Site;
                _camera.Yaw = Mathf.Atan2(-(float)(stone.ArrivalXMm / 1000.0 - body.XMm / 1000.0),
                    -(float)(stone.ArrivalZMm / 1000.0 - body.ZMm / 1000.0));
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
