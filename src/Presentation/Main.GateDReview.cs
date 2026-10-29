using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation;

public partial class Main
{
    // One short opt-in proof per part. Normal NPC routines, normal conversation,
    // normal creator. Frame timestamps preserve the real playback clock for muxing.
    private async Task RunGateDReview(string directory, Camera3D camera, string part)
    {
        Directory.CreateDirectory(directory);
        _hud.Visible = false;
        _inventory.Close();
        _rcd3Driving = true;
        var simulation = _session.Simulation ?? throw new InvalidOperationException("Gate D requires a live world");
        string npc = part == "animation" ? "npc.ashen_hollow.kera_voss" : "npc.ashen_hollow.sel_arien";
        if (!await Approach(npc, 3, 90)) throw new InvalidOperationException("Gate D could not reach " + npc);
        var figure = _npcs.FigureOf(npc) as SkinnedFigure ?? throw new InvalidOperationException("Gate D requires native HumGen art");
        var face = figure.FindChildren("*", "", true, false).OfType<FaceDriver>().Single();
        if (part == "face")
        {
            _controller.Talk(npc);
            await Rcd3Wait(.15);
            if (simulation.Conversation?.NpcId != npc) throw new InvalidOperationException("Normal conversation did not open");
            _dialogue.Visible = false;
        }
        else
        {
            if (simulation.Npcs.Single(n => n.Id == npc).Activity != "work")
                throw new InvalidOperationException("Smith is not working on the current schedule");
            await Rcd3Wait(.5);
            if (figure.ClipState != "work_smith") throw new InvalidOperationException("Native smith take is not bound");
        }
        var frames = new List<object>();
        async Task Capture(string segment, double seconds, Action? aim = null)
        {
            ulong start = Time.GetTicksMsec();
            int frame = 0;
            while (Time.GetTicksMsec() - start < seconds * 1000)
            {
                aim?.Invoke();
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                string name = $"{segment}_{frame++:0000}.png";
                double at = (Time.GetTicksMsec() - start) / 1000.0;
                var recording = face.RecordedSpeech?.Invoke();
                using var image = GetViewport().GetTexture().GetImage();
                image.SavePng(Path.Combine(directory, name));
                frames.Add(new { file = name, segment, seconds = at, clip = figure.ClipState,
                    jaw = face.JawWeight, blink = face.BlinkWeight, mood = face.Mood,
                    voice_position = recording?.Position, voice_duration = recording?.Duration });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        void Aim()
        {
            var target = (figure.Head ?? figure.GlobalPosition + Vector3.Up * 1.6f) - Vector3.Up * (part == "face" ? .07f : .4f);
            camera.Fov = 36;
            camera.GlobalPosition = target + figure.GlobalBasis * new Vector3(.35f, .02f, part == "face" ? 1.2f : 2.8f);
            camera.LookAt(target);
        }
        var spoken = simulation.Conversation;
        await Capture(part, 8, Aim);
        if (part == "face")
        {
            _dialogue.Leave();
            _creator.Open();
            _creator.UsePeople(PeopleLooks.Siann);
            _creator.Go(CreatorStep.Face);
            _creator.Frame(1);
            await Rcd3Wait(1);
            foreach (string mood in new[] { "neutral", "warm", "worried", "curious" })
            {
                _creator.PreviewExpression(mood);
                await Capture("creator_" + mood, 1.4);
            }
            _creator.Close();
        }
        File.WriteAllText(Path.Combine(directory, "performance.json"), JsonSerializer.Serialize(new {
            status = "CAPTURED_PENDING_VISUAL_REVIEW", npc, model = figure.ModelId,
            line = spoken?.LineId, text = spoken?.Text, frames
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
