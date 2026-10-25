using Godot;
using System.Text.Json;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Art;
using UNNAMED.World.Runtime;
namespace UNNAMED.Presentation;

/// <summary>
/// Opt-in real-time evidence for the race abilities (owner, 2026-10-24; RACE_ABILITIES_PROPOSAL.md): after the normal creator's Begin, a Kal
/// sprint-jumps into a gap leap, then glides down falling ground, flares and lands; an Orenth blinks, wraith-steps, sets an anchor, walks
/// away and crosses back. It acts through the same commands the player's keys send, while an external window capture records the run.
/// <c>OTHERREACH_RACE_ABILITIES_REVIEW</c> names the people's route (kal, orenth); <c>OTHERREACH_REVIEW_FIGURE</c>, "person|body", draws
/// the player as that person's figure on that HumGen body - review only, until the RC binds the new peoples' player bodies.
/// </summary>
public partial class Main
{
    private async Task RunRaceAbilitiesReview(string directory, string route)
    {
        _rcd3Driving = true;
        var sim = _session.Simulation!;
        if (OS.GetEnvironment("OTHERREACH_REVIEW_FIGURE").Split('|') is [{ Length: > 0 } person, { Length: > 0 } model]
            && SkinnedFigure.Create(_art, _bindings, person, body: model) is { } figure)
        {
            var feet = _avatar.Position;
            _avatar.QueueFree();
            _avatar = figure;
            _avatar.Name = "Player";
            AddChild(_avatar);
            _avatar.Position = feet;
            _avatarModel = model;
            _avatarPose = null;
            _wingsDrawn = null;
        }
        var log = new List<object>();
        void Mark(string phase, object? detail = null)
        {
            var row = new { phase, uptime_ms = Time.GetTicksMsec(), world_tick = sim.WorldTick, body = sim.Player.Body.ToString(),
                posture = sim.Posture.ToString(), detail };
            log.Add(row);
            File.AppendAllText(Path.Combine(directory, "phases.jsonl"), JsonSerializer.Serialize(row) + "\n");
        }
        _session.Subscribe<WingsChanged>(e => Mark("wings", e.Wing.ToString()));
        _session.Subscribe<Blinked>(e => Mark("blinked", new { from = e.From.ToString(), to = e.To.ToString() }));
        _session.Subscribe<WraithStepped>(e => Mark("wraith_step", e.UntilTick));
        _session.Subscribe<AnchorPlaced>(e => Mark("anchor", e.At.ToString()));
        _session.Subscribe<Recalled>(e => Mark("recalled", new { from = e.From.ToString(), to = e.To.ToString() }));
        _session.Subscribe<CommandRejected>(e => Mark("refused", $"{e.Command.GetType().Name}: {e.Reason}"));

        // Lazy model loads span several frames: wait for a steady renderer before anything is timed.
        ulong previous = Time.GetTicksMsec();
        for (int steady = 0; steady < 60;)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ulong now = Time.GetTicksMsec();
            steady = now - previous < 250 ? steady + 1 : 0;
            previous = now;
        }
        if (route == "kal")
        {
            await KalSlope();
            Mark("slope", new { top = _glideFrom.Top.ToString(), drop_mm = _glideFrom.Drop, dir = $"{_glideFrom.DirX},{_glideFrom.DirZ}" });
        }
        else
        {
            await OrenthField();
            Mark("field", sim.Player.Body.ToString());
        }
        DisplayServer.WindowSetTitle("Otherreach - " + Path.GetFileName(directory));
        File.WriteAllText(Path.Combine(directory, "record_ready.json"), JsonSerializer.Serialize(new { window_title = "^Otherreach - " + Path.GetFileName(directory) + "$" }));
        ulong deadline = Time.GetTicksMsec() + 90000;
        while (!File.Exists(Path.Combine(directory, "recording.started")))
        {
            if (Time.GetTicksMsec() > deadline)
                throw new TimeoutException("The window recording never started");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        await Rcd3Wait(2);
        Mark("recording");
        if (route == "kal")
            await KalRoute(Mark);
        else
            await OrenthRoute(Mark);
        await Rcd3Wait(2);
        File.WriteAllText(Path.Combine(directory, "recording.finished"), DateTime.Now.ToString("o"));
        File.WriteAllText(Path.Combine(directory, "complete.json"), JsonSerializer.Serialize(new
        {
            status = "RACE_ABILITIES_REALTIME_REQUIRES_CLAUDE_REVIEW", route, people = sim.Player.Identity.PeopleId, model = _avatarModel,
            wings_rig = _avatar is SkinnedFigure f && f.Skeleton.GetNodeOrNull("KalWings") is not null,
            phases = log.Count, engine_max_fps = Engine.MaxFps,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Where the Kal glides from: the walkable point within 400 m of the start with the greatest fall of the ground ahead of it over 40 m,
    /// at least 70 m from any spawn or living creature (a fight is not the evidence), and the way down. The body walks there first,
    /// unrecorded; the recording opens on the slope.
    /// </summary>
    private (Body Top, int DirX, int DirZ, long Drop) _glideFrom;

    private async Task KalSlope()
    {
        var sim = _session.Simulation!;
        var space = sim.ActiveLayout.Space;
        var start = sim.Player.Body;
        var danger = sim.Setup.Combat.Spawns.Select(s => (s.XMm, s.ZMm)).Concat(sim.Creatures.Where(c => c.Alive).Select(c => (c.Body.XMm, c.Body.ZMm))).ToList();
        bool Quiet(long x, long z) => danger.All(d => Math.Pow(d.XMm - x, 2) + Math.Pow(d.ZMm - z, 2) > 70_000.0 * 70_000.0);
        (long Drop, Body Top, int DirX, int DirZ) best = (0, start, 0, 1000);
        for (long dx = -400_000; dx <= 400_000; dx += 6_000)
        for (long dz = -400_000; dz <= 400_000; dz += 6_000)
        {
            long x = start.XMm + dx, z = start.ZMm + dz;
            if (x < space.MinXMm || z < space.MinZMm || x > space.MaxXMm || z > space.MaxZMm || !Quiet(x, z)
                || !Kinematics.IsClear(x, z, sim.PlayerMovement.BodyRadiusMm, space, sim.DynamicBlockers))
                continue;
            for (int a = 0; a < 16; a++)
            {
                double angle = a * Math.PI / 8;
                int ux = (int)Math.Round(Math.Sin(angle) * 1000), uz = (int)Math.Round(Math.Cos(angle) * 1000);
                long ex = x + ux * 40, ez = z + uz * 40;
                if (ex < space.MinXMm || ez < space.MinZMm || ex > space.MaxXMm || ez > space.MaxZMm || !Quiet(ex, ez)
                    || space.Blockers.Any(b => b.HeightMm > 0 && b.Crosses(x, z, ex, ez)))
                    continue;
                long drop = space.HeightAtMm(x, z) - space.HeightAtMm(ex, ez);
                if (drop > best.Drop)
                    best = (drop, new Body(x, space.HeightAtMm(x, z), z, 0), ux, uz);
            }
        }
        _glideFrom = (best.Top, best.DirX, best.DirZ, best.Drop);
        foreach (var (cx, cz) in Playthrough.WayTo(sim, sim.Player.Body, best.Top, standOff: 0))
            await SteerTo(cx, cz, Gait.Run, 120);
        _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
        SideOn();
    }

    /// <summary>A close follow view side-on to the way down, held every frame: the wings are the evidence, so they must be in frame.</summary>
    private void SideOn()
    {
        var (_, dx, dz, _) = _glideFrom;
        _camera.Yaw = Mathf.Atan2(-dz, dx);
        _camera.TargetDistance = 4.5f;
        _camera.Pitch = -.08f;
    }

    /// <summary>
    /// Where the Orenth is filmed: open ground at least 30 m from anyone (gate04's still camera stood inside a villager), with the 10 m
    /// strip ahead and the camera spot 6 m to its side both clear. The body walks there and ends facing down the strip, unrecorded.
    /// </summary>
    private async Task OrenthField()
    {
        var sim = _session.Simulation!;
        var space = sim.ActiveLayout.Space;
        var start = sim.Player.Body;
        long radius = sim.PlayerMovement.BodyRadiusMm;
        var others = sim.Setup.Combat.Spawns.Select(s => (s.XMm, s.ZMm)).Concat(sim.Creatures.Where(c => c.Alive).Select(c => (c.Body.XMm, c.Body.ZMm)))
            .Concat(sim.Npcs.Select(n => (n.Body.XMm, n.Body.ZMm))).ToList();
        bool Open(long x, long z) => x > space.MinXMm && z > space.MinZMm && x < space.MaxXMm && z < space.MaxZMm
            && others.All(o => Math.Pow(o.XMm - x, 2) + Math.Pow(o.ZMm - z, 2) > 30_000.0 * 30_000.0)
            && Kinematics.IsClear(x, z, radius, space, sim.DynamicBlockers);
        for (long ring = 0; ring <= 200_000; ring += 6_000)
        for (int a = 0; a < 16; a++)
        {
            double angle = a * Math.PI / 8;
            int ux = (int)Math.Round(Math.Sin(angle) * 1000), uz = (int)Math.Round(Math.Cos(angle) * 1000);
            long x = start.XMm + ux * ring / 1000, z = start.ZMm + uz * ring / 1000;
            if (!Enumerable.Range(-1, 13).All(m => Open(x + ux * m, z + uz * m)) || !Open(x + ux * 4 + uz * 6, z + uz * 4 - ux * 6)
                || space.Blockers.Any(b => b.HeightMm > 0 && b.Crosses(x, z, x + ux * 10, z + uz * 10)))
                continue;
            foreach (var (cx, cz) in Playthrough.WayTo(sim, sim.Player.Body, new Body(x - ux * 2, space.HeightAtMm(x, z), z - uz * 2, 0), standOff: 0))
                await SteerTo(cx, cz, Gait.Run, 120);
            await SteerTo(x / 1000.0, z / 1000.0, Gait.Walk, 10);
            _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
            return;
        }
        throw new InvalidOperationException("No open ground for the Orenth route within 200 m");
    }

    private async Task SteerTo(double x, double z, Gait gait, double budgetSeconds)
    {
        ulong deadline = Time.GetTicksMsec() + (ulong)(budgetSeconds * 1000);
        while (true)
        {
            var body = _controller.Authoritative;
            var toward = new Vector3((float)(x - body.XMm / 1000.0), 0, (float)(z - body.ZMm / 1000.0));
            if (toward.Length() < .3f || Time.GetTicksMsec() > deadline)
                break;
            _controller.SteerWorld(toward.Normalized(), gait, _camera);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
    }

    /// <summary>A gap leap away from the slope, then two glides down it: one held to the ground, one flared to brake on the way.</summary>
    private async Task KalRoute(Action<string, object?> mark)
    {
        var (top, dx, dz, _) = _glideFrom;
        var down = new Vector3(dx / 1000f, 0, dz / 1000f);
        // 1. Gap leap: sprint, then jump.
        mark("leap", null);
        for (int i = 0; i < 40; i++)
        {
            SideOn();
            _controller.SteerWorld(-down, Gait.Sprint, _camera);
            if (i == 20)
                _controller.Jump();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
        await Rcd3Wait(2);
        for (int glide = 0; glide < 2; glide++)
        {
            await SteerTo(top.XMm / 1000.0, top.ZMm / 1000.0, Gait.Run, 30);
            SideOn();
            await Rcd3Wait(1.5);
            mark(glide == 0 ? "glide" : "glide_flare", null);
            _controller.Jump();
            await Rcd3Wait(.35);
            _controller.Glide(true);
            ulong flareAt = Time.GetTicksMsec() + 1400, ends = Time.GetTicksMsec() + 15000;
            bool flared = false;
            while (Time.GetTicksMsec() < ends && _session.Simulation!.Posture.Airborne)
            {
                SideOn();
                _controller.SteerWorld(down, Gait.Run, _camera);
                if (glide == 1 && !flared && Time.GetTicksMsec() > flareAt)
                {
                    _controller.Crouch(true);
                    flared = true;
                }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            _controller.Glide(false);
            _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
            mark("landed", null);
            await Rcd3Wait(2.5);
        }
    }

    /// <summary>
    /// At game camera distance (coordinator, 2026-10-24: the lagging afterimage must be visible): a still camera 6 m side-on to the middle
    /// of one blink, so both ends are in frame; the blink, a wraith-step where it lands, and a sprint back across the frame.
    /// </summary>
    private async Task OrenthRoute(Action<string, object?> mark)
    {
        var sim = _session.Simulation!;
        var start = sim.Player.Body;
        var facing = start.FacingMdeg / 1000.0 * Math.PI / 180;
        var ahead = new Vector3((float)Math.Sin(facing), 0, (float)Math.Cos(facing));
        var side = new Vector3(ahead.Z, 0, -ahead.X);
        var middle = new Vector3(start.XMm / 1000f, start.YMm / 1000f + 1.0f, start.ZMm / 1000f) + ahead * 4;
        var still = new Camera3D { Fov = 60, Near = .05f, Far = 2000, Environment = _camera.Camera.Environment, Attributes = _camera.Camera.Attributes };
        AddChild(still);
        still.GlobalPosition = middle + side * 6 + Vector3.Up * .4f;
        still.LookAt(middle);
        still.MakeCurrent();
        await Rcd3Wait(2);
        _controller.PeopleAbility("ability.people.orenth.blink");
        await Rcd3Wait(4);
        _controller.PeopleAbility("ability.people.orenth.wraith_step");
        await Rcd3Wait(4);
        mark("sprint", null);
        for (int i = 0; i < 70; i++)
        {
            _controller.SteerWorld(-ahead, Gait.Sprint, _camera);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
        await Rcd3Wait(3);
        _camera.Camera.MakeCurrent();
        still.QueueFree();
    }
}
