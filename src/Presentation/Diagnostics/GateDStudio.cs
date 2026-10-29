using Godot;
using System.Text.Json;
using UNNAMED.Application;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Audio;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Diagnostics;

/// <summary>Focused real-art evidence when the farm cannot boot the complete town.
/// Live simulation, normal NPC drawing/schedules/voice and creator; studio lighting only.</summary>
public partial class GateDStudio(IReadOnlyDictionary<string, string> options, bool inspectOnly = false) : Node3D
{
    private GameSession? _session;
    private NpcsView? _npcs;
    private bool _running;

    public override void _Ready() => Run();

    public override void _Process(double delta)
    {
        if (!_running) return;
        _session!.Frame(delta);
        _npcs!.Draw(_session.Simulation!, delta, _session.TickSeconds);
    }

    private async void Run()
    {
        try
        {
            string part = options["--gate-d"], directory = options["--gate-d-out"];
            if (part is not ("face" or "animation") || !Path.IsPathFullyQualified(directory))
                throw new ArgumentException("Gate D studio requires face|animation and an absolute output path");
            Directory.CreateDirectory(directory);
            string npc = part == "face" ? "npc.ashen_hollow.sel_arien" : "npc.ashen_hollow.kera_voss";
            _session = GameSession.Boot(new GameOptions(options["--content-root"], Path.Combine(directory, "profile"))
            {
                LockProfile = true, StartXMm = part == "face" ? 472000 : 490000,
                StartZMm = part == "face" ? 723000 : 740000,
            });
            var simulation = _session.NewGame(CharacterSpec.Quick("Eris", null), 42);
            var catalog = AssetCatalog.Load(options["--asset-root"], Path.GetDirectoryName(options["--content-root"])!);
            ArtLibrary.FallbackRoot = options["--asset-fallback"];
            var art = new ArtLibrary(catalog.Root);
            var bindings = ArtBindings.LoadWithOverlay(ArtBindings.ResourcePath, options["--bindings-overlay"]);
            art.Withhold(bindings.Withheld);
            AddChild(new WorldEnvironment { Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black,
                Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
                ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(.72f, .77f, .85f), AmbientLightEnergy = .55f,
                TonemapMode = Godot.Environment.ToneMapper.Agx, SsaoEnabled = true,
            } });
            AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, -25, 0), LightEnergy = 1.2f, ShadowEnabled = true });
            var camera = new Camera3D { Current = true, Fov = 36, Near = .05f };
            AddChild(camera);
            var voice = new VoicePlayer(); voice.Load(catalog.Root); AddChild(voice);
            _npcs = new NpcsView { ReviewOnlyNpc = npc, Voice = voice };
            _npcs.Bind(art, bindings); AddChild(_npcs);
            _npcs.Draw(simulation, .05, _session.TickSeconds);
            var figure = _npcs.FigureOf(npc) as SkinnedFigure ?? throw new InvalidDataException("Native NPC missing");
            var face = figure.FindChildren("*", "", true, false).OfType<FaceDriver>().Single();
            if (part == "animation")
            {
                var station = simulation.ActiveLayout.Stations.Where(s => s.Kind == "anvil")
                    .OrderBy(s => Math.Pow(s.XMm / 1000f - figure.Position.X, 2) + Math.Pow(s.ZMm / 1000f - figure.Position.Z, 2)).First();
                var look = bindings.Stations[station.Kind];
                var at = new Vector3(station.XMm / 1000f,
                    simulation.ActiveLayout.Space.Terrain.HeightAtMm(station.XMm, station.ZMm) / 1000f, station.ZMm / 1000f);
                AddChild(Fitting.Site(art, look, at) ?? throw new InvalidDataException("Owned anvil missing"));
            }
            _running = true;
            await Wait(2);
            if (part == "face")
            {
                _session.Submit(new TalkCommand(simulation.PlayerId, npc));
                await Wait(.1);
                var talk = simulation.Conversation ?? throw new InvalidOperationException(
                    $"Normal talk command did not open a line; player={simulation.Player.Body}, npc={simulation.Npcs.Single(n => n.Id == npc).Body}");
                if (!voice.Say(talk.LineId)) throw new InvalidDataException("Owned voiced line missing: " + talk.LineId);
            }
            else
            {
                ulong deadline = Time.GetTicksMsec() + 15000;
                while ((simulation.Npcs.Single(n => n.Id == npc).Activity != "work" || figure.ClipState != "work_smith")
                    && Time.GetTicksMsec() < deadline) await Wait(.25);
                if (figure.ClipState != "work_smith") throw new InvalidDataException(
                    $"Actual smith schedule did not select work: {simulation.Npcs.Single(n => n.Id == npc).Activity}, {figure.ClipState}, {simulation.Npcs.Single(n => n.Id == npc).Body}");
            }
            void Aim()
            {
                var target = (figure.Head ?? figure.GlobalPosition + Vector3.Up * 1.6f) - Vector3.Up * (part == "face" ? .07f : .45f);
                camera.GlobalPosition = target + figure.GlobalBasis * new Vector3(.35f, .02f, part == "face" ? 1.2f : 2.8f);
                camera.LookAt(target);
            }
            var frames = new List<object>();
            async Task Capture(string segment, double seconds, bool npcFace)
            {
                ulong start = Time.GetTicksMsec(); int frame = 0;
                using var slots = new SemaphoreSlim(4);
                var writes = new List<Task>();
                while (Time.GetTicksMsec() - start < seconds * 1000)
                {
                    await slots.WaitAsync();
                    if (npcFace) Aim();
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    var recording = npcFace ? face.RecordedSpeech?.Invoke() : null;
                    string file = $"{segment}_{frame++:0000}.png";
                    double at = (Time.GetTicksMsec() - start) / 1000.0;
                    // GPU readback stays on the main thread. Each worker owns
                    // its CPU image; PNG compression must not stall lip motion.
                    var image = GetViewport().GetTexture().GetImage();
                    writes.Add(Task.Run(() =>
                    {
                        try { image.SavePng(Path.Combine(directory, file)); }
                        finally { image.Dispose(); slots.Release(); }
                    }));
                    frames.Add(new { file, segment, seconds = at, clip = npcFace ? figure.ClipState : null,
                        jaw = npcFace ? (float?)face.JawWeight : null, blink = npcFace ? (float?)face.BlinkWeight : null,
                        mood = npcFace ? face.Mood : segment, voice_position = recording?.Position, voice_duration = recording?.Duration });
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                await Task.WhenAll(writes);
            }
            CheckArt(); Aim(); await Wait(.5);
            File.WriteAllText(Path.Combine(directory, "native_visibility.json"), JsonSerializer.Serialize(new {
                hide_body = OS.GetEnvironment("OTHERREACH_HIDE_BODY"),
                player = simulation.Player.Body.ToString(), clip = figure.ClipState,
                figure_basis = figure.GlobalBasis.ToString(), rig_basis = figure.Skeleton.GlobalBasis.ToString(),
                head_pose = figure.Skeleton.GetBoneGlobalPose(figure.Skeleton.FindBone("Head")).ToString(),
                meshes = figure.FindChildren("*", "", true, false).OfType<MeshInstance3D>().Select(mesh => new {
                    name = mesh.Name.ToString(), mesh.Visible, in_tree = mesh.IsVisibleInTree(), mesh.Layers,
                    shadow = mesh.CastShadow.ToString(), mesh.Transparency, mesh.VisibilityRangeBegin, mesh.VisibilityRangeEnd,
                    regions = mesh.GetInstanceShaderParameter("region_hidden_mask").ToString(),
                    at = mesh.GlobalPosition.ToString(), bounds = mesh.GetAabb().ToString(),
                    materials = Enumerable.Range(0, mesh.Mesh?.GetSurfaceCount() ?? 0).Select(surface => {
                        var mat = mesh.GetSurfaceOverrideMaterial(surface) ?? mesh.Mesh!.SurfaceGetMaterial(surface);
                        return new { name = mat?.ResourceName, shader = (mat as ShaderMaterial)?.Shader.ResourcePath,
                            conceal = (mat as ShaderMaterial)?.GetShaderParameter("conceal").ToString(),
                            mask = (mat as ShaderMaterial)?.GetShaderParameter("conceal_map").ToString() };
                    }).ToArray()
                }).ToArray()
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (inspectOnly)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using (var image = GetViewport().GetTexture().GetImage()) image.SavePng(Path.Combine(directory, "front.png"));
                var target = figure.Head!.Value - Vector3.Up * .07f;
                camera.GlobalPosition = target + figure.GlobalBasis * new Vector3(-.35f, .02f, -1.2f); camera.LookAt(target);
                await Wait(.3);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using (var image = GetViewport().GetTexture().GetImage()) image.SavePng(Path.Combine(directory, "back.png"));
                CaptureQuit.At(this); return;
            }
            var spoken = simulation.Conversation;
            await Capture(part, 8, true);
            if (part == "face")
            {
                voice.Hush();
                var creator = new CreatorScreen(); creator.Bind(_session, art, bindings, HudIcons.None); AddChild(creator);
                creator.Open(); creator.UsePeople(PeopleLooks.Siann); creator.Go(CreatorStep.Face); creator.Frame(1);
                await Wait(1);
                foreach (string mood in new[] { "neutral", "warm", "worried", "curious" })
                {
                    creator.PreviewExpression(mood); await Capture("creator_" + mood, 1.4, false);
                }
            }
            CheckArt();
            File.WriteAllText(Path.Combine(directory, "performance.json"), JsonSerializer.Serialize(new {
                status = "CAPTURED_PENDING_VISUAL_REVIEW", context = "Focused studio; full-town GPU device loss remains unresolved",
                npc, model = figure.ModelId, line = spoken?.LineId, text = spoken?.Text,
                video_mib = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576, frames,
            }, new JsonSerializerOptions { WriteIndented = true }));
            CaptureQuit.At(this);
            void CheckArt()
            {
                GD.Print($"UNNAMED art: {art.Used.Count} assets drawn from the library; {art.Problems.Count} withheld or unavailable");
                if (art.Problems.Count > 0) throw new InvalidDataException("Focused proof refuses withheld art");
            }
        }
        catch (Exception error) { GD.PushError(error.ToString()); CaptureQuit.At(this, 2); }
    }

    private async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    public override void _ExitTree() => _session?.Dispose();
}
