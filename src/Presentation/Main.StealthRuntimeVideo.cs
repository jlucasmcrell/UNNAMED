// UNNAMED Presentation - the stealth world, live: the watch on her round, and the bell the town answers (STEALTH.md 8.2)
// Godot presentation only: it drives the game through the same commands a player uses, and writes what it saw.

using System.Text.Json;
using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    /// <summary>The flag a runtime video run is started with, and the world it needs: no flag, no run.</summary>
    private void InstallStealthRuntimeVideo()
    {
        if (_options.GetValueOrDefault("--stealth-runtime-video") is not { Length: > 0 } directory)
            return;
        // One run, every area: `--stealth-areas "waystation=npc.frontier.waystation_watch,cairn=...,road=..."`, each area's own
        // clip under the run's directory. A single area can still be named with --stealth-area/--stealth-guard.
        var areas = _options.GetValueOrDefault("--stealth-areas") is { Length: > 0 } list
            ? list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .Where(pair => pair.Length == 2)
                .Select(pair => (Area: pair[0], Parts: pair[1].Split('@', 2)))
                .Where(entry => entry.Parts.Length >= 1 && entry.Parts[0].Length > 0)
                .Select(entry => (entry.Area, Guard: entry.Parts[0], Portal: entry.Parts.Length > 1 ? entry.Parts[1] : (string?)null))
                .ToArray()
            : new[]
            {
                (_options.GetValueOrDefault("--stealth-area") ?? "waystation",
                    _options.GetValueOrDefault("--stealth-guard") ?? "npc.frontier.waystation_watch",
                    (string?)null),
            };
        _ = Run();
        async Task Run()
        {
            foreach (var (area, guard, portal) in areas)
            {
                try
                {
                    await StealthRuntimeVideo(Path.Combine(Path.GetFullPath(directory), area), guard, area, portal);
                }
                catch (Exception error)
                {
                    GD.PushError($"STEALTH_RUNTIME_VIDEO failed for {area}: {error}");
                }
            }
            CaptureQuit.At(this, 0);
        }
    }

    /// <summary>
    /// One area's runtime proof: the watch walks her authored round (walking where the round says walk, standing where it says stand), then the
    /// bell at the post is rung and she answers it. Booted with <c>--stealth-runtime-video &lt;dir&gt; --stealth-guard &lt;npc id&gt;</c>; the page
    /// writes numbered frames the way the route proof does, so the run is watched rather than asserted.
    /// </summary>
    private async Task StealthRuntimeVideo(string directory, string guardId, string label, string? entryPortal = null)
    {
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "frames"));
        DisplayServer.WindowSetTitle("Otherreach_D23_Runtime_" + label);
        for (int wait = 0; wait < 900 && _session.Simulation is null; wait++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var simulation = _session.Simulation ?? throw new System.IO.InvalidDataException("No world for the runtime video");
        if (entryPortal is { Length: > 0 })
        {
            // Walk to the threshold on the same steering a player uses, take it, and wait for the space to change: the interior's watch
            // lives in the interior, so nothing is filmed until the character is through.
            var portal = simulation.ActiveLayout.Portals.Portals.FirstOrDefault(p => p.Key == entryPortal);
            if (portal is not null)
            {
                long px = portal.ExteriorXMm, pz = portal.ExteriorZMm;
                for (int i = 0; i < 2400; i++)
                {
                    var body = simulation.Player.Body;
                    double dx = px - body.XMm, dz = pz - body.ZMm;
                    if (dx * dx + dz * dz < 1_500_000)
                        break;
                    _controller.SteerWorld(new Vector3((float)dx, 0, (float)dz).Normalized(), Domain.Spatial.Gait.Run, _camera);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
            }
            _session.Submit(new EnterSpaceCommand(simulation.PlayerId, entryPortal));
            var before = simulation.ActiveLayout.SpaceId;
            for (int i = 0; i < 600 && simulation.ActiveLayout.SpaceId == before; i++)
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        _rcd3Driving = true;
        _npcs.SuppressPresentationForBenchmark = false;
        bool hudWasVisible = _hud.Visible;
        _hud.Visible = false;
        var rejected = new List<string>();
        _session.Subscribe<CommandRejected>(e => rejected.Add(e.Reason));
        int frame = 0;

        void Mark(string phase, string caption) =>
            System.IO.File.AppendAllText(Path.Combine(directory, "phases.jsonl"), JsonSerializer.Serialize(new
            {
                phase, caption, label, tick = simulation.WorldTick, elapsed_s = Math.Round(Time.GetTicksMsec() / 1000.0, 2),
                guard = simulation.Npcs.Where(n => n.Id == guardId).Select(n => new { n.Body.XMm, n.Body.ZMm }).FirstOrDefault(),
                patrols = simulation.ActiveLayout.Patrols.Select(p => p.Key).ToArray(),
                bells = simulation.ActiveLayout.Switches.Where(s => s.Key.StartsWith("switch.bell.")).Select(s => s.Key).ToArray(),
                nps = simulation.Npcs.Select(n => n.Id).ToArray(),
                rejected = rejected.TakeLast(4).ToArray(),
            }) + System.Environment.NewLine);

        var layer = new CanvasLayer { Layer = 100 };
        var caption = new Label { Position = new Vector2(26, 470) };
        caption.AddThemeFontSizeOverride("font_size", 22);
        caption.AddThemeConstantOverride("outline_size", 8);
        layer.AddChild(caption);
        AddChild(layer);

        var source = _camera.Camera;
        var film = new Camera3D
        {
            Fov = 62,
            Near = source.Near,
            Far = source.Far,
            CullMask = source.CullMask,
            Environment = source.Environment,
        };
        AddChild(film);
        film.MakeCurrent();

        /// The camera behind the watch, looking the way she looks: what a body following her would see.
        void FollowGuard()
        {
            var guard = simulation.Npcs.Where(n => n.Id == guardId && n.Space == simulation.ActiveLayout.SpaceId)
                .Select(n => n.Body).FirstOrDefault();
            double facing = guard.FacingMdeg / 1000.0 * Math.PI / 180;
            var eye = new Vector3(guard.XMm / 1000f, guard.YMm / 1000f + 1.6f, guard.ZMm / 1000f);
            var ahead = new Vector3((float)Math.Sin(facing), 0, (float)Math.Cos(facing));
            film.GlobalPosition = eye - ahead * 4.2f + new Vector3(0, 1.4f, 0);
            film.LookAt(eye + ahead * 2f, Vector3.Up);
        }

        async Task Shot(string? still) => await SavedFrame(directory, frame++, still);

        /// Walk the character to the watch, on the same steering a player uses: near enough to see her, and near enough that her cell is
        /// simulated while the camera is on her.
        async Task ApproachGuard(double metres)
        {
            for (int i = 0; i < 3600; i++)
            {
                var guard = simulation.Npcs.Where(n => n.Id == guardId && n.Space == simulation.ActiveLayout.SpaceId)
                    .Select(n => n.Body).FirstOrDefault();
                if (guard.XMm == 0 && guard.ZMm == 0)
                    break;
                var body = simulation.Player.Body;
                double dx = guard.XMm - body.XMm, dz = guard.ZMm - body.ZMm;
                double away = Math.Sqrt(dx * dx + dz * dz);
                if (away <= metres * 1000)
                    break;
                _controller.SteerWorld(new Vector3((float)dx, 0, (float)dz).Normalized(), Domain.Spatial.Gait.Walk, _camera);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            _controller.SteerWorld(Vector3.Zero, Domain.Spatial.Gait.Walk, _camera);
        }

        async Task Hold(string phase, string words, double seconds)
        {
            caption.Text = words;
            Mark(phase, words);
            double until = Time.GetTicksMsec() / 1000.0 + seconds;
            while (Time.GetTicksMsec() / 1000.0 < until)
            {
                FollowGuard();
                await Shot(null);
            }
        }

        // 0. Stand beside her first: what a character would do to watch a guard on her round.
        await ApproachGuard(8);

        // 1. The round: she walks to the next stop and stands on it, and the feet agree with the round.
        await Hold("round", $"{label}: the watch walks her round", 16);

        // 2. The bell: the character rings it, from where the post is reachable.
        var bell = simulation.ActiveLayout.Switches.FirstOrDefault(s => s.Key.StartsWith("switch.bell."));
        if (bell is not null)
        {
            var (bx, bz) = Footprints.Center(bell.Body);
            var target = new Vector3(bx / 1000f + 1.2f, 0, bz / 1000f);
            var body = simulation.Player.Body;
            double toward = Math.Atan2(target.X - body.XMm / 1000.0, target.Z - body.ZMm / 1000.0);
            for (int i = 0; i < 400 && Math.Abs(target.X - simulation.Player.Body.XMm / 1000.0) > 1.4; i++)
            {
                _controller.SteerWorld(new Vector3((float)Math.Sin(toward), 0, (float)Math.Cos(toward)),
                    Domain.Spatial.Gait.Walk, _camera);
                FollowGuard();
                await Shot(null);
            }
            caption.Text = $"{label}: the bell";
            Mark("ring", caption.Text);
            _session.Submit(new InteractCommand(simulation.PlayerId, bell.Key));
            for (int i = 0; i < 30; i++)
            {
                FollowGuard();
                await Shot(null);
            }

            // 3. The answer: the bell has rung, and the town comes to it.
            await Hold("answer", $"{label}: the alarm, and the watch answers it", 14);
        }

        Mark("end", $"{label}: done, {rejected.Count} refused commands");
        await SavedFrame(directory, frame, "final");
        _hud.Visible = hudWasVisible;
        caption.QueueFree();
        layer.QueueFree();
        film.QueueFree();
    }
}
