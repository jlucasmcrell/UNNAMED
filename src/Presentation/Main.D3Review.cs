using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Opt-in two-still fixture only. Quests start through normal on_new_game content and real events.
    private async void RunD3Review(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var sim = _session.Simulation!;
            var source = _camera.Camera;
            var camera = new Camera3D { Fov = 60, Near = source.Near, Far = source.Far, CullMask = source.CullMask,
                Attributes = source.Attributes, Environment = source.Environment };
            AddChild(camera);
            source.GetNode<OmniLight3D>("CharacterFill").Reparent(camera, false);
            var p = sim.Player.Body;
            var aim = new Vector3(p.XMm / 1000f, p.YMm / 1000f + 1.2f, p.ZMm / 1000f);
            camera.Position = aim + new Vector3(-5, 4, 6);
            camera.LookAt(aim);
            camera.MakeCurrent();
            // Prime the existing lazy actor views before the proof; no simulated walk or hidden world components.
            SetProcess(false);
            for (int i = 0; i < 8; i++) { Draw(1, 1.0 / 60); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            SetProcess(true);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var card = _questCards.Cards.Current;
            if (card is not { Started: true }) throw new InvalidDataException("No real quest-accept card visible");
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, "quest_accept.png"));
            // The same dismiss operation as the key, repeated for the fixture's simultaneous starts.
            _questCards.Cards.Clear();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var tracker = QuestPresentation.Tracker(sim.Quests, sim.TrackedQuest(_journal.Tracked));
            if (tracker.Rows.Count < 3) throw new InvalidDataException("Need three actual active quests");
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, "quest_tracker.png"));
            File.WriteAllText(Path.Combine(directory, "evidence.json"), JsonSerializer.Serialize(new
            {
                player = sim.Player.Name, card, tracker, quests = sim.Quests,
                tick = sim.WorldTick, resolution = GetViewport().GetVisibleRect().Size.ToString(),
                fixture = "Private content marks three existing side quests on_new_game; production text/objectives/events, no direct authoritative writes.",
                scope = "Two native viewport stills; setup primes lazy views frozen, then normal unpaused gameplay. No FPS or full-suite claim."
            }, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(directory, "gate.exit"), "0");
            GetTree().Quit();
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(directory, "failure.txt"), e.ToString());
            File.WriteAllText(Path.Combine(directory, "gate.exit"), "1");
            GD.PushError(e.ToString());
            CaptureQuit.At(this, 1);
        }
    }
}
