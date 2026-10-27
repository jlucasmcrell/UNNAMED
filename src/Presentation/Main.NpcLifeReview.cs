using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Opt-in private content fixture: three real NPCs have adjacent routine anchors.
    // No authoritative writes, synthetic mouth overrides or animation timing override.
    private async void RunNpcLifeReview(string directory)
    {
        Directory.CreateDirectory(directory);
        bool talkOnly = File.Exists(Path.Combine(directory, "content", "NPC_LIFE_TALK_ONLY.txt"));
        string[] ids = talkOnly ? ["npc.ashen_hollow.renn_vale"]
            : ["npc.ashen_hollow.renn_vale", "npc.ashen_hollow.kera_voss", "npc.cairnwold.berrin_garth"];
        var rows = new List<object>();
        var source = _camera.Camera;
        var camera = new Camera3D { Name = "NpcReviewCamera", Fov = 55, Near = source.Near, Far = source.Far,
            CullMask = source.CullMask, Attributes = source.Attributes, Environment = source.Environment };
        AddChild(camera); _innFreeCamera = true;
        source.GetNode<OmniLight3D>("CharacterFill").Reparent(camera, false);
        AudioEffectRecord? audio = null;
        int bus = AudioServer.GetBusIndex("Master"), slot = AudioServer.GetBusEffectCount(bus);
        try
        {
            SetProcess(false);
            for (int i = 0; i < 8; i++) { Draw(1, 1.0 / 60); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            var figures = ids.Select(id => _npcs.FigureOf(id) as SkinnedFigure
                ?? throw new InvalidDataException("Expected real HumGen NPC " + id)).ToArray();
            foreach (var f in figures)
                foreach (var mesh in f.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
                    if (mesh.Name.ToString().Contains("_Teeth", StringComparison.Ordinal) && mesh.Mesh is not null)
                        for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
                            if (mesh.GetActiveMaterial(s) is BaseMaterial3D m)
                                rows.Add(new { phase = "mouth_material", f.ModelId, mesh = mesh.Name.ToString(),
                                    m.Roughness, m.Metallic, m.MetallicSpecular,
                                    albedo = m.AlbedoTexture?.GetWidth(), normal = m.NormalTexture?.GetWidth(),
                                    roughness_texture = m.RoughnessTexture is not null,
                                    metallic_texture = m.MetallicTexture is not null });
            _voice.Preload("dialogue.ashen_hollow.renn_vale/greet");
            var center = figures.Select(f => f.GlobalPosition).Aggregate(Vector3.Zero, (a, b) => a + b) / figures.Length;
            if (figures.Any(f => f.GlobalPosition.DistanceTo(center) > 4)) throw new InvalidDataException("Private adjacent anchors missing");
            camera.GlobalPosition = center + new Vector3(0, 2, 6.5f);
            camera.LookAt(center + Vector3.Up); camera.MakeCurrent();
            _avatar.Visible = false; // Only the foreground player, to keep the close review unobstructed.
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            File.WriteAllText(Path.Combine(directory, "record_ready.json"), "{}");
            ulong deadline = Time.GetTicksMsec() + 60000;
            while (!File.Exists(Path.Combine(directory, "recording.started")))
            {
                if (Time.GetTicksMsec() > deadline) throw new TimeoutException("Owned WGC recorder not ready");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            audio = new AudioEffectRecord(); AudioServer.AddBusEffect(bus, audio); audio.SetRecordingActive(true);
            SetProcess(true);
            ulong start = Time.GetTicksMsec(); int sample = -1;
            while (Time.GetTicksMsec() - start < (talkOnly ? 0UL : 20000UL))
            {
                if (File.Exists(@"C:\Otherreach_Recovery\OWNER_PLAYING.flag")) throw new OperationCanceledException("Owner playing");
                int at = (int)((Time.GetTicksMsec() - start) / 250);
                if (at != sample)
                {
                    sample = at;
                    rows.Add(new { phase = "three_real_npcs_idle", utc = DateTimeOffset.UtcNow,
                        elapsed_ms = Time.GetTicksMsec() - start, npcs = figures.Select((f, i) => new {
                            id = ids[i], f.ModelId, clip = f.ClipState, feet = f.GlobalPosition.ToString(),
                            hands = f.FindChildren("*", "", true, false).OfType<HandGrip>().Select(h => new {
                                h.Closed, bindings = h.ClosedRotations(f.Skeleton).Count }).ToArray() }).ToArray() });
                }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            if (!talkOnly) await Rcd3Shot(directory, "three_npcs");
            var figure = figures[0];
            var face = figure.FindChildren("*", "", true, false).OfType<FaceDriver>().Single();
            var sim = _session.Simulation!;
            var npc = sim.Npcs.Single(n => n.Id == ids[0]);
            double reach = (_session.Setup.Items.Inventory.ReachMm + _session.Setup.Movement.BodyRadiusMm) / 1000.0;
            var body = sim.Player.Body;
            if (new Vector2((body.XMm - npc.Body.XMm) / 1000f, (body.ZMm - npc.Body.ZMm) / 1000f).Length() > reach)
                throw new InvalidDataException($"Player {body} not within {reach:0.000}m of greeting NPC {npc.Body}; no approach fallback");
            if (talkOnly)
            {
                int hand = figure.Skeleton.FindBone("RightHand");
                var hands = figure.Skeleton.GlobalTransform * figure.Skeleton.GetBoneGlobalPose(hand).Origin;
                camera.Fov = 20; camera.GlobalPosition = hands + new Vector3(.1f, .1f, 1).Normalized() * 2;
                camera.LookAt(hands); camera.MakeCurrent();
                rows.Add(new { phase = "hands", utc = DateTimeOffset.UtcNow, player = body, npc = npc.Body,
                    camera_distance_m = camera.GlobalPosition.DistanceTo(hands), camera_fov = camera.Fov });
                await Rcd3Wait(4); await Rcd3Shot(directory, "relaxed_hand");
            }
            void CloseCamera()
            {
                var aim = figure.Head!.Value - Vector3.Up * .07f;
                var toward = (_avatar.GlobalPosition - aim); toward.Y = 0; toward = toward.Normalized();
                camera.Fov = 32; camera.GlobalPosition = aim + toward * .85f + Vector3.Up * .02f; camera.LookAt(aim); camera.MakeCurrent();
            }
            CloseCamera();
            string expected = "dialogue.ashen_hollow.renn_vale/greet";
            _voice.Preload(expected); _controller.Talk(ids[0]);
            deadline = Time.GetTicksMsec() + 5000;
            while (sim.Conversation?.NpcId != ids[0])
            {
                if (Time.GetTicksMsec() > deadline) throw new InvalidDataException("Normal conversation refused");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            ulong speechStart = Time.GetTicksMsec(); float peak = 0;
            while (_voice.Speaking || Time.GetTicksMsec() - speechStart < 150)
            {
                if (Time.GetTicksMsec() - speechStart > 45000) throw new TimeoutException("Full greeting did not finish");
                if (File.Exists(@"C:\Otherreach_Recovery\OWNER_PLAYING.flag")) throw new OperationCanceledException("Owner playing");
                CloseCamera();
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (GetViewport().GetCamera3D() != camera) throw new InvalidDataException("Review close camera was replaced before draw");
                var speech = _voice.Speech(expected);
                if (speech is null || sim.Conversation?.LineId != expected || !_dialogue.Visible)
                    throw new InvalidDataException("Normal voiced greeting interrupted or not visible");
                peak = Math.Max(peak, face.JawWeight);
                rows.Add(new { phase = "normal_greeting", utc = DateTimeOffset.UtcNow, playback = speech.Value.Position,
                    duration = speech.Value.Duration, envelope = speech.Value.Jaw, jaw = face.JawWeight,
                    native_jaw_angle = figure.FindChildren("*", "", true, false).OfType<HumGenJaw>().Single().AppliedAngle,
                    face.NativeJawBound, dialogue_visible = _dialogue.Visible, view_camera = camera.Name.ToString(),
                    view_position = camera.GlobalPosition.ToString(), view_fov = camera.Fov });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            var ended = _voice.Speech(expected);
            if (peak < .03f || ended is null || ended.Value.Position < ended.Value.Duration - .15)
                throw new InvalidDataException("Greeting native jaw/full utterance incomplete");
            rows.Add(new { phase = "greeting_finished", utc = DateTimeOffset.UtcNow, full_utterance_finished = true,
                duration = ended.Value.Duration, playback = ended.Value.Position, peak_jaw = peak });
            await Rcd3Shot(directory, "after_greeting");
            File.WriteAllText(Path.Combine(directory, "evidence.json"), JsonSerializer.Serialize(new {
                player = sim.Player.Name, talkOnly, subjects = ids, scope = "Private anchors; production NPC draw/idle/hand/face/normal conversation and actual Master. Render filter draws only review subjects; simulation population retained. Foreground player hidden. Not a world/performance gate.",
                resolution = GetViewport().GetVisibleRect().Size.ToString(), rows }, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(directory, "gate.exit"), "0");
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(directory, "failure.txt"), e.ToString());
            File.WriteAllText(Path.Combine(directory, "partial_evidence.json"), JsonSerializer.Serialize(rows));
            File.WriteAllText(Path.Combine(directory, "gate.exit"), "1"); GD.PushError(e.ToString());
        }
        finally
        {
            SetProcess(true);
            if (audio is not null) { audio.SetRecordingActive(false); audio.GetRecording()?.SaveToWav(Path.Combine(directory, "game_master.wav")); AudioServer.RemoveBusEffect(bus, slot); audio.Dispose(); }
            File.WriteAllText(Path.Combine(directory, "recording.finished"), "done");
            ulong stop = Time.GetTicksMsec() + 15000;
            while (!File.Exists(Path.Combine(directory, "recording.stopped")) && Time.GetTicksMsec() < stop)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetTree().Quit(File.ReadAllText(Path.Combine(directory, "gate.exit")) == "0" ? 0 : 1);
        }
    }
}
