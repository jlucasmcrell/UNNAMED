using System.Text.Json;
using Godot;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

/// <summary>
/// D23's stealth proof route (STEALTH.md §2.7, §7): one real-time pass down the waystation's lane at night that shows the three reads the
/// player actually uses - the light gem, the noise ring and an observer's awareness with the direction it is looking - and then a shot at
/// something that has not noticed the character.
/// </summary>
/// <remarks>
/// Booted with the fixture's own kit (<c>--start-items</c>) and driven only through the same public commands the player's keys submit. Every
/// beat writes a still and appends a line to <c>phases.jsonl</c>, and the whole pass writes a numbered frame sequence beside them for
/// stitching, because what moves here (a body crouching, an awareness ring filling, an arrow flying) is what has to be watched as motion.
/// </remarks>
public partial class Main
{
    /// <summary>A frame every this many rendered frames: 20 fps at a 60 Hz loop is enough for a route, and a third of the disk.</summary>
    private const int StealthEveryNthFrame = 3;

    private void InstallStealthReview()
    {
        // --stealth-probe: boot, prove the scene builds, and quit. It exists because a run of the whole route costs a GPU lease, and the last
        // two attempts died in BuildScene before the route began; this answers "does this worktree boot at all" in a few seconds of one.
        if (_options.GetValueOrDefault("--stealth-probe") is { Length: > 0 } probe)
        {
            _ = StealthProbe(Path.GetFullPath(probe));
            return;
        }
        if (_options.GetValueOrDefault("--stealth-review") is not { Length: > 0 } directory)
            return;
        _rcd3Driving = _innFreeCamera = true;
        _ = StealthReview(Path.GetFullPath(directory), _options.GetValueOrDefault("--stealth-beats") ?? "all");
    }

    private async Task StealthProbe(string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            // One drawn frame is the whole proof: the scene built, the world is ticked, and a screenshot came back non-empty.
            await Rcd3Wait(2);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var image = GetViewport().GetTexture().GetImage();
            image.SavePng(Path.Combine(directory, "boot.png"));
            var sim = _session.Simulation!;
            File.WriteAllText(Path.Combine(directory, "boot_ok.json"), JsonSerializer.Serialize(new
            {
                ok = image.GetWidth() > 0 && image.GetHeight() > 0,
                width = image.GetWidth(),
                height = image.GetHeight(),
                tick = sim.WorldTick,
                region = sim.ActiveLayout.Id,
                creatures = sim.Creatures.Length,
                band = sim.PlayerLightBand.ToString(),
                noise_mm = sim.PlayerNoiseMm,
            }));
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(directory, "boot_failed.txt"), e.ToString());
            throw;
        }
        finally
        {
            CaptureQuit.At(this, 3);
        }
    }

    private async Task StealthReview(string directory, string beats)
    {
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "frames"));
        DisplayServer.WindowSetTitle("Otherreach_D23_Stealth_Proof");
        bool wanted(string beat) => beats is "all" || beats.Split(',').Contains(beat, StringComparer.OrdinalIgnoreCase);
        var rejected = new List<string>();
        _session.Subscribe<CommandRejected>(e => rejected.Add(e.Reason));

        // The shot, recorded from the simulation's own event rather than sampled at a beat: four captures reported a silent "loose" because the
        // beat happened to fall outside the two ticks the shot was in the air, and the fix is to hear the event, not to look harder.
        var shots = new List<UNNAMED.World.Runtime.ShotLoosed>();
        var misses = new List<UNNAMED.World.Runtime.AttackMissed>();
        _session.Subscribe<UNNAMED.World.Runtime.AttackMissed>(e => misses.Add(e));
        _session.Subscribe<UNNAMED.World.Runtime.ShotLoosed>(e => shots.Add(e));
        long ShotPeakNoise() => _session.Simulation!.PlayerNoiseMm;
        var captions = new List<object>();
        int frame = 0;

        void Mark(string phase, string caption, long tick) =>
            File.AppendAllText(Path.Combine(directory, "phases.jsonl"), JsonSerializer.Serialize(new
            {
                phase, caption, tick, elapsed_s = Math.Round(Time.GetTicksMsec() / 1000.0, 2),
                lit_permille = _session.Simulation!.PlayerLitPermille,
                band = _session.Simulation!.PlayerLightBand.ToString(),
                noise_m = _session.Simulation!.PlayerNoiseMm / 1000.0,
                stance = _session.Simulation!.Posture.Stance.ToString(),
                creatures = _session.Simulation!.Creatures.Select(c => new { c.DefId, mind = c.Mind.ToString(), c.Awareness, c.Knows }),
            }) + System.Environment.NewLine);

        // The route's own camera caption strip: what a reviewer reads in the video without a title card beside it, low on the left where the HUD
        // does not sit (the sheet's own top-left corner is the character's name and the light gem).
        var layer = new CanvasLayer { Layer = 100 };
        var caption = new Label { Position = new Vector2(26, 700) };
        caption.AddThemeFontSizeOverride("font_size", 22);
        caption.AddThemeConstantOverride("outline_size", 8);
        layer.AddChild(caption);
        AddChild(layer);

        // A camera of the route's own, behind and over the character's shoulder: the game's camera is free while a harness drives, and a route shot
        // of the sky proves nothing about what the player can see. It takes the game camera's environment and cull mask so the scene looks the same.
        var source = _camera.Camera;
        var routeCamera = new Camera3D
        {
            Fov = 70,
            Near = source.Near,
            Far = source.Far,
            CullMask = source.CullMask,
            Environment = source.Environment,
        };
        AddChild(routeCamera);
        routeCamera.MakeCurrent();

        /// Put the camera behind the character, looking where they face, at eye height plus a little.
        void Follow()
        {
            var body = _session.Simulation!.Player.Body;
            double facing = body.FacingMdeg / 1000.0 * Math.PI / 180;
            var eye = new Vector3(body.XMm / 1000f, body.YMm / 1000f + 1.7f, body.ZMm / 1000f);
            var back = new Vector3((float)Math.Sin(facing), 0, (float)Math.Cos(facing));
            routeCamera.GlobalPosition = eye - back * 3.6f + new Vector3(0, 0.9f, 0);
            routeCamera.LookAt(eye + back * 6f, Vector3.Up);
        }
        Follow();

        async Task Wait(double seconds) => await Rcd3Wait(seconds);

        // The camera follows the body each captured frame, so the route never films the sky.
        async Task Shot(string? stillName)
        {
            Follow();
            await SavedFrame(directory, frame++, stillName);
        }

        /// One beat: hold the shot, say what it is, and keep the frame ticker running for the whole time it lasts.
        async Task Beat(string phase, string words, double seconds, bool still = true)
        {
            var sim = _session.Simulation!;
            caption.Text = words;
            Captions();
            Mark(phase, words, sim.WorldTick);
            if (still)
                await Shot($"{frame:0000}_{phase}");
            ulong until = Time.GetTicksMsec() + (ulong)Math.Ceiling(seconds * 1000);
            while (Time.GetTicksMsec() < until)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (Engine.GetFramesDrawn() % StealthEveryNthFrame == 0)
                    await Shot(null);
            }
        }

        void Captions() => captions.Add(new { text = caption.Text, frame = Engine.GetFramesDrawn() });

        /// Walk (or run, when far) toward a region point until the body is inside <paramref name="within"/> millimetres of it, or the time is up.
        async Task Approach((long XMm, long ZMm) to, long within, double seconds)
        {
            ulong until = Time.GetTicksMsec() + (ulong)Math.Ceiling(seconds * 1000);
            while (Time.GetTicksMsec() < until)
            {
                var body = _session.Simulation!.Player.Body;
                long dx = to.XMm - body.XMm, dz = to.ZMm - body.ZMm;
                double gap = Math.Sqrt((double)dx * dx + (double)dz * dz);
                if (gap <= within)
                    break;
                var toward = new Vector3(dx / 1000f, 0, dz / 1000f);
                _controller.SteerWorld(toward, gap > 13_000 ? Gait.Run : Gait.Walk, _camera);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (Engine.GetFramesDrawn() % StealthEveryNthFrame == 0)
                    await Shot(null);
            }
            _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
        }

        /// A short walk, captured, at a gait and stance the beat asks for.
        async Task Walk(Vector3 toward, Gait gait, double seconds)
        {
            ulong until = Time.GetTicksMsec() + (ulong)Math.Ceiling(seconds * 1000);
            while (Time.GetTicksMsec() < until)
            {
                _controller.SteerWorld(toward.Length() > .1f ? toward.Normalized() : Vector3.Zero, gait, _camera);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (Engine.GetFramesDrawn() % StealthEveryNthFrame == 0)
                    await Shot(null);
            }
            _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
        }

        try
        {
            // A body with nothing shiny in the off hand: the gem must read what is there, not what the HUD hopes.
            var sim = _session.Simulation!;

            // Night: from the 07:30 the world starts at, run the clock to the small hours so the lane really is dark (config, §2.4).
            if (sim.WorldTick < 33_000)
                sim.Advance(33_000 - sim.WorldTick);
            await Rcd3Wait(1.2);

            // The fixture's own kit out of the pack and into the hands, through the command a player's click submits.
            // The bow and the blade only: the torch stays in the pack until the beat that lights it, because a lit torch is a light standing where
            // the body is and it would flatten every reading before that (STEALTH §2.2).
                        // The blade first and the bow last: each equip replaces the main hand, so the last one listed is the one the route actually holds, and a
            // route that means to shoot has to end with the bow in its hands (this is why three captures reported a silent "loose").
            foreach (string wantedItem in new[] { "item.weapon.iron_dagger", "item.weapon.hunting_bow" })
                if (sim.Player.Inventory.FirstOrDefault(e => e.DefId == wantedItem) is { } entry)
                {
                    rejected.Clear();
                    _session.Submit(new EquipCommand(sim.PlayerId, entry.ItemId));
                    await Rcd3Wait(0.3);
                    if (rejected.Count > 0)
                        File.AppendAllText(Path.Combine(directory, "equip_refused.txt"), $"{wantedItem}: {string.Join("; ", rejected)}{System.Environment.NewLine}");
                }

            // What the character actually left the pack with and what is actually in their hands. The stills read UNARMED for three captures and
            // nothing in the logs explained it, so the route writes the truth beside the frames instead of leaving me to read a corner of the HUD.
            File.WriteAllText(Path.Combine(directory, "kit.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                inventory = sim.Player.Inventory.Select(e => new { e.DefId, e.Count }),
                // The definition, not the item id: "which weapon is in the hand" has cost four captures already.
                equipment = sim.Player.Equipment.Select(kv => new
                {
                    slot = kv.Key.ToString(),
                    item = sim.Player.Inventory.FirstOrDefault(e => e.ItemId == kv.Value)?.DefId ?? "unknown",
                }),
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            // 1. The dark lane: nothing lit, nothing moving, the gem at its lowest and the ring flat.
            _controller.Crouch(true);
            if (wanted("dark"))
                await Beat("dark_lane", "Night, down the lane\nShrouded - nothing to be seen by", 2.5);

            // 2. Crouch-walking: a small ring, and the gem unchanged because the light has not changed.
            if (wanted("crouch"))
            {
                await Walk(Vector3.Back, Gait.Walk, 1.6);
                await Beat("crouch_walk", "Crouched, walking\nA quieter ring, and no lighter", 1.8);
            }

            // 3. Under the yard lamp: the same body in better light, which is the whole of S1's read.
            if (wanted("lamp"))
            {
                var yard = _session.Setup.LayoutFor(sim.ActiveLayout.SpaceId).Lights.FirstOrDefault(l => l.Key == "light.waystation_yard");
                if (yard is not null)
                {
                    // Run the lane, then the last few metres at a walk, so the approach reads as a body arriving rather than a slide.
                    await Approach((yard.XMm, yard.ZMm), within: 2_000, seconds: 14);
                    await Walk(Vector3.Zero, Gait.Walk, 0.6);
                    _controller.Crouch(false);
                    await Beat("under_the_lamp", "Past the yard lamp\nIts circle is lit; this lane is not, and the gem says so", 2.5);
                }
            }

            // 4. Standing tall in the open: the ring at a walk, and the body plainly visible.
            if (wanted("open"))
            {
                await Walk(Vector3.Forward, Gait.Walk, 1.4);
                await Beat("open_ground", "Standing in the open\nThe same body, worth more to a pair of eyes", 1.8);
            }

            // 4b. The torch out of the pack: the same body, the same lane, and the gem climbing because the player chose to carry a light.
            if (wanted("torch") && sim.Player.Inventory.FirstOrDefault(e => e.DefId == "item.tool.torch") is { } torch)
            {
                rejected.Clear();
                _session.Submit(new EquipCommand(sim.PlayerId, torch.ItemId));
                await Rcd3Wait(0.4);
                await Beat("torch_lit", "The torch out of the pack\nCarrying a light is the loudest thing a thief does", 2.2);
                // The torch took the hand. Put the bow back before the shot, or the shot is a swing at nothing - which is what four captures of a
                // silent "loose" were: a torch held up to the light, and an arrow never loosed.
                if (sim.Player.Inventory.FirstOrDefault(e => e.DefId == "item.weapon.hunting_bow") is { } shotBow)
                {
                    _session.Submit(new EquipCommand(sim.PlayerId, shotBow.ItemId));
                    await Rcd3Wait(0.4);
                }
            }

            // 5. The shot: whatever is nearest that can be shot, from behind, at the range a bow has.
            if (wanted("shot"))
            {
                var quarry = sim.Creatures
                    .Where(c => c.Alive)
                    .OrderBy(c => Math.Pow(c.Body.XMm - sim.Player.Body.XMm, 2) + Math.Pow(c.Body.ZMm - sim.Player.Body.ZMm, 2))
                    .FirstOrDefault();
                if (quarry is not null)
                {
                    caption.Text = $"A {quarry.DefId[(quarry.DefId.LastIndexOf('.') + 1)..]}, {Distance(sim.Player.Body, quarry.Body):0} m";
                    await Beat("quarry_found", caption.Text + "\nThe bow, and a body that has not noticed", 1.2);
                    // Close to the range a hunting bow is good at, then put the arrow through it.
                    await Approach((quarry.Body.XMm, quarry.Body.ZMm), within: 22_000, seconds: 16);
                    var body = sim.Player.Body;
                    var toward = new Vector3((quarry.Body.XMm - body.XMm) / 1000f, 0, (quarry.Body.ZMm - body.ZMm) / 1000f);
                    _controller.SteerWorld(toward, Gait.Walk, _camera);
                    await Wait(0.25);
                    _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
                    rejected.Clear();
                    rejected.Clear();
                    shots.Clear();
                    misses.Clear();
                    _session.Submit(new AttackCommand(sim.PlayerId));
                    // Wait for the string, and keep the loudest ring reading across the flight: the shot is heard at the bow own radius and it has to
                    // be caught while it is there, not sampled once and reported as silence (STEALTH 2.7, 7.8).
                    long peak = 0;
                    for (int i = 0; i < 90 && shots.Count == 0; i++)
                    {
                        await Rcd3Wait(0.033);
                        peak = Math.Max(peak, ShotPeakNoise());
                    }
                    peak = Math.Max(peak, ShotPeakNoise());
                    File.WriteAllText(Path.Combine(directory, "shot.json"), System.Text.Json.JsonSerializer.Serialize(new
                    {
                        loosed = shots.Count > 0,
                        shots = shots.Select(s => new { s.Source, from_x = s.FromXMm / 1000.0, from_z = s.FromZMm / 1000.0, to_x = s.ToXMm / 1000.0, to_z = s.ToZMm / 1000.0 }),
                        peak_noise_m = peak / 1000.0,
                        refused = rejected,
                        missed = misses.Count,
                        ammo = sim.Player.Inventory.Where(e => e.DefId.StartsWith("item.ammo.", StringComparison.Ordinal)).Select(e => new { e.DefId, e.Count }),
                    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    string shotWords = shots.Count == 0
                        ? "The loose\nNo shot: " + (rejected.Count > 0 ? string.Join("; ", rejected.Take(2)) : "nothing loosed")
                        : $"The loose\n{peak / 1000.0:0.0} m of bow - heard at the bow, and again where it lands";
                    await Beat("loose", shotWords, 1.6, still: false);
                    await Beat("after_the_shot", $"The bolt of it\n{rejected.Count} refused: {string.Join("; ", rejected.Take(2))}", 1.6);
                }
            }

            // 6. Awareness: what the body that is still standing has made of all this.
            if (wanted("aware"))
            {
                var nearest = sim.Creatures
                    .Where(c => c.Alive)
                    .OrderBy(c => Math.Pow(c.Body.XMm - sim.Player.Body.XMm, 2) + Math.Pow(c.Body.ZMm - sim.Player.Body.ZMm, 2))
                    .FirstOrDefault();
                var words = nearest is null
                    ? "Nothing left to notice"
                    : $"{nearest.DefId[(nearest.DefId.LastIndexOf('.') + 1)..]}: {nearest.Mind}, awareness {nearest.Awareness}";
                await Beat("awareness", words, 2.2);
            }

            // 7. S2's distraction: a lure stone thrown away from the body, and the nearest animal's attention going with it.
            if (wanted("lure") && sim.Player.Inventory.FirstOrDefault(e => e.DefId == "item.misc.lure_stone") is { } stone)
            {
                // A sleeping animal is deaf to a stone (its hearing is the role's own share), so the beat picks something awake to be distracted, and says
                // so when the nearest awake body is too far to hear the landing.
                var watched = sim.Creatures
                    .Where(c => c.Alive && !c.Asleep)
                    .OrderBy(c => Math.Pow(c.Body.XMm - sim.Player.Body.XMm, 2) + Math.Pow(c.Body.ZMm - sim.Player.Body.ZMm, 2))
                    .FirstOrDefault();
                int before = watched?.Awareness ?? 0;
                rejected.Clear();
                _session.Submit(new ThrowCommand(sim.PlayerId, stone.ItemId, -700, 700));
                await Rcd3Wait(1.6);
                var after = watched is null ? null : sim.Creatures.FirstOrDefault(c => c.Id == watched.Id);
                var words = watched is null
                    ? "The stone lands\nNothing awake here to be distracted - the birds are roosting"
                    : $"The Lure Stone, thrown off the lane\n{after?.Mind}: {before} -> {after?.Awareness ?? 0} - it looks where the noise is, not at the body";
                await Beat("thrown_lure", words, 2.2);

                // S3's own half of the same event: a noise gives a place to look, and the animal goes to look at it. The beat names the animal's mind and how
                // far off it is while it hunts the noise - a search is a widening around a remembered point, not a chase.
                if (wanted("search") && watched is not null)
                {
                    await Rcd3Wait(4.0);
                    var seeker = sim.Creatures.FirstOrDefault(c => c.Id == watched.Id);
                    if (seeker is not null)
                    {
                        double gap = Math.Sqrt(Math.Pow(seeker.Body.XMm - sim.Player.Body.XMm, 2)
                            + Math.Pow(seeker.Body.ZMm - sim.Player.Body.ZMm, 2)) / 1000.0;
                        await Beat("searching", $"{seeker.DefId[(seeker.DefId.LastIndexOf('.') + 1)..]}: {seeker.Mind}\n"
                            + $"It hunts the noise, {gap:0.0} m out - and nothing here has seen the body", 2.2);
                    }
                }
            }

            // 8. S2's surfaces: walk from the ground the character is on to the nearest patch of a different one, and name both with their rings. The
            // comparison is the point - a viewer should see the number change when the boots do.
            if (wanted("surface"))
            {
                // The ring is only worth something while the boots are moving, so the "before" reading is taken mid-step rather than from a standing body.
                await Walk(Vector3.Forward, Gait.Walk, 0.5);
                string before = sim.PlayerSurface;
                long ringBefore = sim.PlayerNoiseMm;
                var here = sim.Player.Body;
                var patch = sim.Setup.LayoutFor(sim.ActiveLayout.SpaceId).Surfaces
                    .Where(p => p.Kind != before)
                    .OrderBy(p => Math.Pow((p.MinXMm + p.MaxXMm) / 2.0 - here.XMm, 2) + Math.Pow((p.MinZMm + p.MaxZMm) / 2.0 - here.ZMm, 2))
                    .FirstOrDefault();
                if (patch is not null)
                {
                    await Approach(((patch.MinXMm + patch.MaxXMm) / 2, (patch.MinZMm + patch.MaxZMm) / 2), within: 1_500, seconds: 12);
                    await Walk(Vector3.Forward, Gait.Walk, 0.8);
                    await Beat("the_ground", $"From {before} to {sim.PlayerSurface}\nThe same walk: {ringBefore} mm of ring, then {sim.PlayerNoiseMm} mm", 2.0);
                }
                else
                {
                    await Beat("the_ground", $"The ground here: {before}\nNo other surface within reach to walk onto", 1.8);
                }
            }
            File.WriteAllText(Path.Combine(directory, "captions.json"), JsonSerializer.Serialize(captions));
            File.WriteAllText(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(new
            {
                frames = frame,
                beats = captions.Count,
                rejected,
                band = sim.PlayerLightBand.ToString(),
                lit_permille = sim.PlayerLitPermille,
                note = "D23 stealth proof route: light gem, noise ring, awareness, one shot.",
            }));
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(directory, "failure.txt"), e.ToString());
            throw;
        }
        finally
        {
            _controller.Crouch(false);
            _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
            CaptureQuit.At(this, 3);
        }

        static double Distance(Body a, Body b) =>
            Math.Sqrt(Math.Pow(a.XMm - b.XMm, 2) + Math.Pow(a.ZMm - b.ZMm, 2)) / 1000.0;
    }

    /// <summary>One captured frame: a numbered frame for the sequence, and (when asked) a named still for the contact sheet.</summary>
    private async Task<string> SavedFrame(string directory, int frame, string? stillName)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        // The sequence is a downscaled JPEG and the stills are full-size PNGs: saving 1.4 MB of lossless PNG per frame made the first pass a
        // three-second timelapse of a hundred-second route, which is not the real-time video the phase asks for. Timing is recorded beside the
        // frames so the stitcher can play them at the speed they happened.
        var small = image.Duplicate() as Image ?? image;
        small.Resize(Math.Min(1280, image.GetWidth()), Math.Min(720, image.GetHeight()), Image.Interpolation.Bilinear);
        small.SaveJpg(Path.Combine(directory, "frames", $"f{frame:00000}.jpg"), 0.82f);
        if (stillName is not null)
            image.SavePng(Path.Combine(directory, "still_" + stillName + ".png"));
        File.AppendAllText(Path.Combine(directory, "frames.log"),
            $"{frame}\t{Time.GetTicksMsec() / 1000.0:0.000}{System.Environment.NewLine}");
        return stillName ?? string.Empty;
    }
}
