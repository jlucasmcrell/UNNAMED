// UNNAMED Presentation - opt-in marketing film driver, ported from claude/marketing-capture 8803c818.
// Each shot is a camera path authored as data and rendered one frame per engine frame under Godot's --fixed-fps, so the
// simulation, the animation and the camera all advance by the same fixed step however slowly the machine renders. Every
// rendered frame is written to disk; a shot whose frames already exist is replayed without rewriting them (resume by index).

using System.Text.Json;
using Godot;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Capture;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    private MarketingFilm? _film;

    /// <summary>OTHERREACH_MKT_CHARACTER="Name,people.x,body.x_f.average,She": the created character a film's new game makes.</summary>
    private static CharacterSpec FilmCharacter()
    {
        if (OS.GetEnvironment("OTHERREACH_MKT_CHARACTER").Split(',') is not [var name, var people, var body, var pronouns, var face, var hair, var stature])
            return DefaultCharacter;
        return new CharacterSpec(name, Enum.Parse<Pronouns>(pronouns), people, body, null)
        {
            Appearance = new CharacterAppearance(int.Parse(stature), face, hair, null, null, null),
        };
    }

    private sealed class MarketingFilm : IDisposable
    {
        private readonly Main _main;
        private readonly JsonElement[] _shots;
        private readonly string _out;
        private readonly Camera3D _camera = new() { Name = "FilmCamera", Near = 0.05f, Far = 4000 };
        private readonly double _warmSeconds;
        private readonly ulong _bootMs = Time.GetTicksMsec();
        private readonly System.Text.StringBuilder _log = new();
        private int _shot = -1;
        private int _frame;
        private int _preroll;
        private int _frames;
        private float _fps;
        private ulong _settleUntil;
        private int _recordIndex;
        private float _progress;
        private bool _drawDue;
        private StreamWriter? _poses;
        private readonly int _renderFps;
        private Camera3D? _trackedCamera;
        private string _format = "jpg";
        private readonly HashSet<string> _fired = new();
        private Vector3 _orbitCentre;
        private Vector3 _aim;
        private int _settleFrames;
        private float? _trackYaw;
        private bool _advance;

        public MarketingFilm(Main main, string planPath, string outDir)
        {
            _main = main;
            // Godot filters processed engine options out of OS.GetCmdlineArgs; use the native process arguments.
            _renderFps = FilmTiming.RenderFps(System.Environment.GetCommandLineArgs());
            _out = Path.GetFullPath(outDir);
            Directory.CreateDirectory(_out);
            using var plan = JsonDocument.Parse(File.ReadAllText(planPath));
            _shots = plan.RootElement.GetProperty("shots").EnumerateArray().Select(s => s.Clone()).ToArray();
            _warmSeconds = Num(plan.RootElement, "warm_seconds", 30);
            main.AddChild(_camera);
            RenderingServer.FramePreDraw += BeforeDraw;
            RenderingServer.FramePostDraw += Saved;
        }

        private static double Num(JsonElement e, string key, double fallback) => e.TryGetProperty(key, out var v) ? v.GetDouble() : fallback;
        private static string Text(JsonElement e, string key, string fallback = "") => e.TryGetProperty(key, out var v) ? v.GetString() ?? fallback : fallback;
        private static Vector3 V(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());

        private void Note(string line)
        {
            string stamped = $"{(Time.GetTicksMsec() - _bootMs) / 1000.0:0.0}s {line}";
            GD.Print("MKT " + stamped);
            _log.AppendLine(stamped);
            File.WriteAllText(Path.Combine(_out, "film_log.txt"), _log.ToString());
        }

        private void Saved()
        {
            try { SaveFrame(); }
            catch (Exception e) { Failed(e); }
        }

        private void SaveFrame()
        {
            if (!_drawDue)
                return;
            _drawDue = false;
            var shot = _shots[_shot];
            if (_recordIndex >= 0)
            {
                string path = Path.Combine(_out, Text(shot, "name"), $"f_{_recordIndex:00000}.{_format}");
                bool complete = false;
                if (File.Exists(path))
                {
                    using var cached = Image.LoadFromFile(path);
                    complete = cached is not null && !cached.IsEmpty();
                }
                if (!complete)
                {
                    using var image = _main.GetViewport().GetTexture().GetImage();
                    var error = _format == "png" ? image.SavePng(path) : image.SaveJpg(path, 0.95f);
                    if (error != Error.Ok) throw new IOException($"Film frame could not be saved: {path} ({error})");
                }
                var view = _main.GetViewport().GetCamera3D();
                var rendered = view.GetCameraTransform();
                var p = rendered.Origin;
                var q = rendered.Basis.GetRotationQuaternion();
                _poses!.WriteLine(FormattableString.Invariant($"{_recordIndex},{Engine.GetFramesDrawn()},{_main._session.Simulation!.WorldTick},{_recordIndex / (double)_renderFps:R},{p.X:R},{p.Y:R},{p.Z:R},{q.X:R},{q.Y:R},{q.Z:R},{q.W:R},{view.Fov:R},{_aim.X:R},{_aim.Y:R},{_aim.Z:R}"));
                _poses.Flush();
            }
            _frame++;
            if (_recordIndex + 1 < _frames) return;
            _poses?.Dispose(); _poses = null;
            File.WriteAllText(Path.Combine(_out, Text(shot, "name"), "shot_done.json"), JsonSerializer.Serialize(new
            {
                frames = _frames, fps = _renderFps, tick = _main._session.Simulation!.WorldTick, hour = Greybox.SkyView.Hour,
                player = Feet.ToString(), seconds = (Time.GetTicksMsec() - _bootMs) / 1000.0,
            }));
            Note($"shot {Text(shot, "name")} done ({_frames} native {_renderFps} fps frames)");
            _main._controller.SteerWorld(Vector3.Zero, Gait.Walk, _main._camera);
            _main._controller.Guard(false);
            _advance = true;
        }

        // After every node's interpolated presentation update, immediately before each viewport is rendered.
        private void BeforeDraw()
        {
            try { Present(); }
            catch (Exception e) { Failed(e); }
        }

        public void Present()
        {
            if (_shot < 0 || _shot >= _shots.Length) return;
            if (Text(_shots[_shot].GetProperty("camera"), "mode") == "gameplay")
                _main._camera.Camera.MakeCurrent();
            else
                Pose(_shots[_shot], _progress);
            var view = _main.GetViewport().GetCamera3D();
            // FramePreDraw runs after the tree's usual transform flush: submit this pose now, not next frame.
            view.ForceUpdateTransform();
            if (view != _trackedCamera)
            {
                Greybox.StartCamera.Track(_main, view);
                _trackedCamera = view;
            }
            _main._shadowLods.Update(view.GlobalPosition);
        }

        public void Dispose()
        {
            RenderingServer.FramePreDraw -= BeforeDraw;
            RenderingServer.FramePostDraw -= Saved;
            try { _poses?.Dispose(); }
            catch (IOException e) { GD.PushWarning($"Film telemetry could not finish: {e.Message}"); }
            _poses = null;
        }

        private void Failed(Exception e)
        {
            _drawDue = false;
            Dispose();
            _main.CannotStart("The film capture failed. See the capture log.", e);
        }

        private Vector3 Feet => _main._avatar.GlobalPosition;

        /// <summary>A point of a camera key: [x, y, z] with y above the ground there, from the player's feet when the key is anchored to the player.</summary>
        private Vector3 Point(JsonElement camera, JsonElement p)
        {
            var v = V(p);
            if (Text(camera, "anchor") == "player")
                return Feet + v;
            if (camera.TryGetProperty("abs", out var abs) && abs.GetBoolean())
                return v;
            return new Vector3(v.X, _main._ground.Height(v.X, v.Z) + v.Y, v.Z);
        }

        /// <summary>Null while running; the exit code once every shot is written.</summary>
        public int? Update(double delta)
        {
            if (Math.Abs(delta - 1.0 / _renderFps) > .000001)
                throw new InvalidOperationException($"Film delta {delta:R} does not match --fixed-fps {_renderFps}; capture refused");
            if (_shot < 0)
            {
                if ((Time.GetTicksMsec() - _bootMs) / 1000.0 < _warmSeconds)
                {
                    _main.ReadInput();
                    return null;
                }
                if (OS.GetEnvironment("OTHERREACH_MKT_KEEP_ORDER") is { Length: > 0 } order)
                    _main.KeepLoadOrder(order);
                var sim = _main._session.Simulation!;
                Note($"character {sim.Player.Identity} {sim.Player.Identity.PeopleId} {sim.Player.Identity.BodyPresetId} level {sim.Player.Progression.Level} at {Feet}");
                Note("formulas: " + string.Join(", ", _main._controller.Formulas()));
                Note("carried: " + string.Join(", ", sim.Player.Inventory.Select(e => e.DefId)));
                Begin(0);
            }
            // The next shot begins a frame after the last one was drawn, so its hour and sky cannot reach the last saved frame.
            if (_advance)
            {
                _advance = false;
                Begin(_shot + 1);
            }
            if (_shot >= _shots.Length)
                return 0;
            var shot = _shots[_shot];
            if (Time.GetTicksMsec() < _settleUntil || _settleFrames-- > 0)
            {
                _progress = 0;
                if (Text(shot.GetProperty("camera"), "mode") == "gameplay") Pose(shot, _progress);
                Drive(shot, -_preroll / _fps);
                return null;
            }
            int index = _frame - _preroll;
            _progress = FilmTiming.Progress(index, _frames);
            if (Text(shot.GetProperty("camera"), "mode") == "gameplay") Pose(shot, _progress);
            Drive(shot, index / _fps);
            _recordIndex = index;
            _drawDue = true;
            return null;
        }

        private void Begin(int index)
        {
            _shot = index;
            if (_shot >= _shots.Length)
            {
                Note("all shots done");
                File.WriteAllText(Path.Combine(_out, "complete.json"), "{\"status\":\"FILM_COMPLETE\"}");
                return;
            }
            var shot = _shots[_shot];
            string name = Text(shot, "name");
            if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || Path.GetFileName(name) != name
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("Each film shot needs a simple folder name");
            Directory.CreateDirectory(Path.Combine(_out, name));
            _fps = _renderFps;
            _frames = FilmTiming.Frames(Num(shot, "seconds", 5), _renderFps);
            // Legacy plans express preroll at their old 30 fps rate. Preserve its duration, not its cadence.
            double planFps = Num(shot, "fps", 60);
            if (planFps <= 0) throw new InvalidDataException("Film plan fps must be positive");
            if (Num(shot, "preroll", 30) < 0) throw new InvalidDataException("Film preroll cannot be negative");
            _preroll = checked((int)Math.Ceiling(Num(shot, "preroll", 30) / planFps * _renderFps));
            string folder = Path.Combine(_out, name);
            var texture = _main.GetViewport().GetTexture();
            string signature = JsonSerializer.Serialize(new { shot, fps = _renderFps, content = _main._session.Content.Hash,
                width = texture.GetWidth(), height = texture.GetHeight(), options = _main._options.OrderBy(o => o.Key).ToArray(),
                driver = typeof(Main).Module.ModuleVersionId });
            string identity = Path.Combine(folder, "shot_config.json");
            if (Directory.EnumerateFiles(folder, "f_*").Any() && (!File.Exists(identity) || File.ReadAllText(identity) != signature))
                throw new InvalidDataException($"{folder}: existing frames belong to a different plan/build/cadence; choose a fresh output folder");
            File.WriteAllText(identity, signature);
            _poses = new StreamWriter(Path.Combine(folder, "camera_frames.csv"));
            _poses.WriteLine("frame,render_frame,sim_tick,seconds,x,y,z,qx,qy,qz,qw,fov,aim_x,aim_y,aim_z");
            _frame = 0;
            _fired.Clear();
            _format = Text(shot, "format", "jpg");
            if (_format is not ("png" or "jpg" or "jpeg")) throw new InvalidDataException("Film format must be png or jpg");
            if (shot.TryGetProperty("hour", out var hour))
                VisualOptions.Pin("hour", hour.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (shot.TryGetProperty("unarmed", out var unarmed) && unarmed.GetBoolean())
                _main._session.Submit(new UnequipCommand(_main._session.Simulation!.PlayerId, UNNAMED.Domain.Items.EquipSlot.MainHand));
            if (shot.TryGetProperty("celestial", out var celestial))
                VisualOptions.Pin("celestial", celestial.GetString()!);
            bool hud = shot.TryGetProperty("hud", out var h) && h.GetBoolean();
            _main._hud.Visible = hud;
            if (_main._hintLine is not null) _main._hintLine.Visible = false;
            var camera = shot.GetProperty("camera");
            if (Text(camera, "mode") == "orbit")
                _orbitCentre = Point(camera, camera.GetProperty("centre"));
            _settleUntil = Time.GetTicksMsec() + (ulong)(Num(shot, "settle_seconds", 4) * 1000);
            _settleFrames = (int)Num(shot, "settle_frames", 0);
            _trackYaw = null;
            Note($"shot {name} begins: {_frames} frames at {_fps} fps, preroll {_preroll}, hud {hud}");
        }

        private void Pose(JsonElement shot, float t)
        {
            var camera = shot.GetProperty("camera");
            string mode = Text(camera, "mode");
            if (mode == "gameplay")
            {
                if (camera.TryGetProperty("track", out var track) && track.GetBoolean() && _trackYaw is { } goal)
                    _main._camera.Yaw = Mathf.LerpAngle(_main._camera.Yaw, goal, 0.05f);
                else if (camera.TryGetProperty("yaw", out var yaw))
                    _main._camera.Yaw = Mathf.DegToRad(yaw.GetSingle() + ((float)Num(camera, "yaw_to", yaw.GetSingle()) - yaw.GetSingle()) * t);
                if (camera.TryGetProperty("pitch", out var pitch)) _main._camera.Pitch = pitch.GetSingle();
                if (camera.TryGetProperty("distance", out var distance)) _main._camera.TargetDistance = distance.GetSingle();
                _main._camera.Camera.MakeCurrent();
                return;
            }
            float fov = (float)Num(camera, "fov", 50);
            _camera.Fov = fov + ((float)Num(camera, "fov_to", fov) - fov) * t;
            Vector3 eye, aim;
            if (mode == "orbit")
            {
                float a = Mathf.DegToRad((float)(Num(camera, "from_deg", 0) + (Num(camera, "to_deg", 10) - Num(camera, "from_deg", 0)) * t));
                float radius = (float)(Num(camera, "radius", 10) + (Num(camera, "radius_to", Num(camera, "radius", 10)) - Num(camera, "radius", 10)) * t);
                float height = (float)(Num(camera, "height", 3) + (Num(camera, "height_to", Num(camera, "height", 3)) - Num(camera, "height", 3)) * t);
                var centre = Text(camera, "anchor") == "player" ? Feet + V(camera.GetProperty("centre")) : _orbitCentre;
                eye = centre + new Vector3(Mathf.Sin(a) * radius, height, Mathf.Cos(a) * radius);
                aim = centre + Vector3.Up * (float)Num(camera, "aim_lift", 0);
            }
            else
            {
                var from = camera.GetProperty("from");
                var to = camera.TryGetProperty("to", out var end) ? end : from;
                eye = Point(camera, from.GetProperty("pos")).Lerp(Point(camera, to.GetProperty("pos")), t);
                aim = Point(camera, from.GetProperty("aim")).Lerp(Point(camera, to.GetProperty("aim")), t);
            }
            _camera.GlobalPosition = eye;
            _aim = aim;
            _camera.LookAt(aim);
            _camera.MakeCurrent();
        }

        /// <summary>The shot's inputs at time <paramref name="at"/> seconds into its recording: the player's own commands, nothing else.</summary>
        private void Drive(JsonElement shot, float at)
        {
            if (!shot.TryGetProperty("inputs", out var inputs))
            {
                _main.ReadInput();
                return;
            }
            var controller = _main._controller;
            var rig = _main._camera;
            var sim = _main._session.Simulation!;
            int i = 0;
            foreach (var input in inputs.EnumerateArray())
            {
                string key = $"{i++}";
                double start = Num(input, "at", 0), end = Num(input, "until", start);
                string action = Text(input, "do");
                bool held = at >= start && at < end;
                if (end > start)
                {
                    if (!held)
                    {
                        if (at >= end && _fired.Add(key + "end"))
                        {
                            if (action is "steer" or "toward" or "fight") controller.SteerWorld(Vector3.Zero, Gait.Walk, rig);
                            if (action == "guard") controller.Guard(false);
                        }
                        continue;
                    }
                    switch (action)
                    {
                        case "steer":
                            var d = V(input.GetProperty("dir"));
                            controller.SteerWorld(new Vector3(d.X, 0, d.Z).Normalized(), Enum.Parse<Gait>(Text(input, "gait", "Walk")), rig);
                            break;
                        case "toward":
                            var goal = V(input.GetProperty("point")) - Feet; goal.Y = 0;
                            controller.SteerWorld(goal.Length() > 0.6f ? goal.Normalized() : Vector3.Zero, Enum.Parse<Gait>(Text(input, "gait", "Walk")), rig);
                            break;
                        case "guard":
                            if (_fired.Add(key)) controller.Guard(true);
                            break;
                        case "fight":
                            Fight(input, sim, controller, rig);
                            break;
                        case "approach_talk":
                            // Walk to where the NPC actually is now (routines move them), and speak once in reach.
                            var npc = sim.Npcs.Single(n => n.Id == Text(input, "id"));
                            var toNpc = new Vector3(npc.Body.XMm / 1000f, 0, npc.Body.ZMm / 1000f) - (Feet with { Y = 0 });
                            _trackYaw = Mathf.Atan2(-toNpc.X, -toNpc.Z);
                            if (toNpc.Length() > (float)Num(input, "reach", 1.8))
                                controller.SteerWorld(toNpc.Normalized(), Gait.Walk, rig);
                            else
                            {
                                controller.SteerWorld(Vector3.Zero, Gait.Walk, rig);
                                if (_fired.Add(key + "talk")) { Note($"talk {npc.Id} at {at:0.00}s"); controller.Talk(npc.Id); }
                            }
                            break;
                    }
                    continue;
                }
                if (at < start || !_fired.Add(key))
                    continue;
                Note($"input {action} at {at:0.00}s");
                switch (action)
                {
                    case "attack": controller.Attack(); break;
                    case "dodge": var dd = V(input.GetProperty("dir")); controller.Dodge(new Vector3(dd.X, 0, dd.Z).Normalized()); break;
                    case "jump": controller.Jump(); break;
                    case "crouch": controller.Crouch(input.GetProperty("on").GetBoolean()); break;
                    case "cast": controller.Cast(Text(input, "id")); break;
                    case "technique": controller.Technique(Text(input, "id")); break;
                    case "talk": controller.Talk(Text(input, "id")); break;
                    case "gather": controller.Gather(Text(input, "id")); break;
                    case "interact": controller.Interact(Text(input, "id")); break;
                    case "craft": controller.Craft(Text(input, "id")); break;
                    case "use_item": controller.UseItem(Text(input, "id")); break;
                    case "takedown": controller.Takedown(); break;
                    case "face":
                        var f = V(input.GetProperty("point")) - Feet;
                        controller.SteerWorld(new Vector3(f.X, 0, f.Z).Normalized() * 0.05f, Gait.Walk, rig, true);
                        break;
                    case "close": _main.CloseScreens(); break;
                    case "equip":
                        var entry = sim.Player.Inventory.FirstOrDefault(e => e.DefId == Text(input, "id")) ?? throw new InvalidDataException("Not carried: " + Text(input, "id"));
                        _main._session.Submit(new EquipCommand(sim.PlayerId, entry.ItemId));
                        break;
                    default: throw new InvalidDataException("Unknown film input: " + action);
                }
            }
            if (!inputs.EnumerateArray().Any())
                _main.ReadInput();
        }

        private void Fight(JsonElement input, UNNAMED.World.Runtime.Simulation sim, PlayerController controller, CameraRig rig)
        {
            string selector = Text(input, "target");
            var target = sim.Creatures.Where(c => c.Alive && (c.DefId.Contains(selector, StringComparison.Ordinal) || c.Key.Contains(selector, StringComparison.Ordinal)))
                .OrderBy(c => new Vector3(c.Body.XMm / 1000f, 0, c.Body.ZMm / 1000f).DistanceSquaredTo(Feet with { Y = 0 })).FirstOrDefault();
            if (target is null)
            {
                controller.SteerWorld(Vector3.Zero, Gait.Walk, rig);
                return;
            }
            var to = new Vector3(target.Body.XMm / 1000f, 0, target.Body.ZMm / 1000f) - (Feet with { Y = 0 });
            _trackYaw = Mathf.Atan2(-to.X, -to.Z);
            float reach = (sim.Combat.Weapon.ReachMm + _main._session.Setup.Combat.Creatures[target.DefId].RadiusMm - 100) / 1000f;
            controller.SteerWorld(to.Length() > reach ? to.Normalized() : Vector3.Zero, Gait.Run, rig, true);
            if (to.Length() <= reach && sim.Combat.Phase == CombatPhase.Idle)
                controller.Attack();
        }
    }
}
