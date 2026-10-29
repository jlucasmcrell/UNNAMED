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
        // A still at every mark, written beside the phase log: the review runs on a farm where the desktop is not recorded, and a frame on
        // disk is what lets anyone check the body is standing on the fence rather than beside it.
        void Shot(string name) => Main.SaveScreenshot(GetViewport(), directory, $"traversal_{name}");

        // The climb camera is DERIVED, not tuned: stand it out along the normal of the wall the climber is against, a little to one side, at
        // the height of the ladder's middle, and look at the climber. Every hand-placed offset tonight landed in a doorway or behind a roof;
        // a normal cannot point into the building it came from.
        // The outward normal of the nearest structure the body is standing against: from the structure's own centre towards the body.
        Vector3 WallNormal(Vector3 at)
        {
            Vector3 best = Vector3.Back;
            float nearest = float.MaxValue;
            foreach (var blocker in _session.Setup.Layout.Space.Blockers)
            {
                if (blocker.HeightMm < 1_000)
                    continue;
                var (cx, cz) = UNNAMED.Domain.Spatial.Footprints.Center(blocker);
                var c = new Vector3((float)(cx / 1000.0), at.Y, (float)(cz / 1000.0));
                float distance = c.DistanceTo(at);
                if (distance < nearest && distance < 4f)
                {
                    nearest = distance;
                    var dir = (at - c) with { Y = 0 };
                    if (dir.LengthSquared() > .0001f)
                        best = dir.Normalized();
                }
            }
            return best;
        }

        Camera3D? side = null;
        async Task ShotSide(string name)
        {
            var body = _controller.Authoritative;
            var at = new Vector3(body.XMm / 1000f, body.YMm / 1000f, body.ZMm / 1000f);
            side ??= new Camera3D { Fov = 55 };
            if (side.GetParent() is null)
                AddChild(side);
            // From the OPEN side: the yard is south and west of the forge, so an offset into it keeps the wall and the roof out of the shot.
            Vector3 outward = WallNormal(at);
            Vector3 across = outward.Cross(Vector3.Up).Normalized();
            side.GlobalPosition = at + outward * 3.6f + across * 1.3f + new Vector3(0, 1.15f, 0);
            side.LookAt(at + new Vector3(0, .55f, 0), Vector3.Up);
            side.MakeCurrent();
            await Rcd3Wait(.12);
            Main.SaveScreenshot(GetViewport(), directory, $"traversal_{name}");
            _camera.Camera.MakeCurrent();
            await Rcd3Wait(.05);
        }

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
            void PressReport(string what, float dirX = 0, float dirZ = 0)
        {
            var body = _controller.Authoritative;
            long ground = (long)(_ground.Height((float)(body.XMm / 1000.0), (float)(body.ZMm / 1000.0)) * 1000);
            var space = _session.Setup.Layout.Space;
            var touching = space.Blockers.Where(b => b.DistanceTo(body.XMm, body.ZMm) < 1_500)
                .Select(b => $"{b.Id}(h={b.HeightMm},climb={b.Climbable})");
            var rules = _session.Setup.Movement;
            // Mantle's own arithmetic, recomputed here from the play data so the failing condition names itself.
            double radius = rules.BodyRadiusMm, len = Math.Sqrt((double)dirX * dirX + (double)dirZ * dirZ);
            var probe = new System.Text.StringBuilder();
            if (len > 0)
            {
                double px = body.XMm + dirX / len * radius, pz = body.ZMm + dirZ / len * radius;
                foreach (var b in space.Blockers.Where(b => b.DistanceTo(px, pz) < 2_000))
                {
                    var (cx, cz) = UNNAMED.Domain.Spatial.Footprints.Center(b);
                    long structureGround = space.HeightAtMm(cx, cz);
                    long top = structureGround + b.HeightMm;
                    long feet = ground + (body.YMm - ground);
                    long reach = rules.ClimbPrefixes.Any(x => b.Id.StartsWith(x, StringComparison.Ordinal)) ? long.MaxValue : rules.MantleMm;
                    probe.Append($"{b.Id}: probeDist={b.DistanceTo(px, pz):F1} top={top} feet={feet} reach={reach} ")
                         .Append($"climbable={b.Climbable} h={b.HeightMm} clearance={b.ClearanceMm} ")
                         .Append($"skip={(!b.Climbable ? "notClimbable" : b.HeightMm <= 0 ? "noHeight" : b.ClearanceMm > 0 ? "overhang" : top <= feet ? "topBelowFeet" : top > ground + (body.YMm - ground) + reach ? "beyondReach" : "none")}; ");
                }
            }
            GD.Print($"UNNAMED D6 press {what}: body y={body.YMm} ground={ground} raised={body.YMm - ground}; " +
                     $"stepUp={rules.StepUpMm} vault={rules.VaultMm} mantle={rules.MantleMm} prefixes=[{string.Join(',', rules.ClimbPrefixes)}] " +
                     $"posture={_session.Simulation?.Posture}; touching {string.Join(' ', touching)} " +
                     $"blockerTypes {string.Join(' ', space.Blockers.Where(b => b.DistanceTo(body.XMm, body.ZMm) < 1_500).Select(b => b.GetType().Name))}; {probe}");
        }

        if (route == "traversal")
            {
                // D6 traversal proof, all of it within twenty metres of where the character starts, so the walk cannot lose its way across the
                // frontier: a running jump, a run at the smithy fence, the owned wooden ladder at (49.6, 147.2) region = (449.6, 755.2) world,
                // and the waystation slope. Region coordinates are world (x + 400, z + 608).
                Mark("traversal_start");
                _controller.SteerWorld(new Vector3(0, 0, -1), Gait.Sprint, _camera);
                await Rcd3Wait(.8);
                _controller.Jump();
                Mark("running_jump");
                await Rcd3Wait(1.4);
                _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
                await Rcd3Wait(.4);
                Shot("jump_done");
                Mark("jump_done");

                // The fence: stand in front of the smithy fence (its box is world 444..451.4 x 746..746.4) and run north into it, shooting the
                // run-up, the contact, the rise and the top so a reviewer sees the climb and not just where it ended.
                await Go(new[] { (447.0, 743.5) }, 70);
                Shot("fence_runup");
                _controller.SteerWorld(new Vector3(0, 0, 1), Gait.Run, _camera);
                await Rcd3Wait(.35);
                await ShotSide("fence_contact");
                await Rcd3Wait(.25);
                await ShotSide("fence_rising");
                _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
                await Rcd3Wait(1.1);
                await ShotSide("fence_top");
                PressReport("fence", 0, 1);
                Mark("fence_vaulted", _controller.Authoritative);

                // The ladder (D6) leans on the forge's north wall (box 453..463 x 745.6..746, 3.0 m), so the climb goes up the wall. Stand in
                // the open yard south of it and walk north into its long face.
                await Go(new[] { (461.8, 748.6) }, 90);
                Shot("ladder_runup");
                _controller.SteerWorld(new Vector3(0, 0, -1), Gait.Walk, _camera);
                await Rcd3Wait(.75);
                await ShotSide("ladder_contact");
                await Rcd3Wait(.25);
                await ShotSide("ladder_on_rung");
                await Rcd3Wait(.35);
                await ShotSide("ladder_rising");
                await Rcd3Wait(.8);
                await ShotSide("ladder_top");
                _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
                await Rcd3Wait(.5);
                PressReport("ladder", 0, -1);
                Mark("ladder_climbed", _controller.Authoritative);

                // The slope: the waystation's own rise.
                await Go(new[] { (434.0, 752.0) }, 60);
                _controller.SteerWorld(new Vector3(0, 0, -1), Gait.Run, _camera);
                await Rcd3Wait(1.6);
                _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
                await Rcd3Wait(.5);
                Shot("slope_scrambled");
                Mark("slope_scrambled", _controller.Authoritative);
                Mark("traversal_done");
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
