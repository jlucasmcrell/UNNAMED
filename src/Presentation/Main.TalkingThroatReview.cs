using Godot;
using System.Text.Json;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Opt-in packaged evidence: ordinary walk/talk commands and the production face driver. No geometry or pose substitution.
    private async Task RunTalkingThroatGate(string directory, Camera3D camera)
    {
        Directory.CreateDirectory(directory);
        _inventory.Close();
        _rcd3Driving = true;
        const string npcId = "npc.ashen_hollow.sel_arien"; // Her survey table is outdoors; the gate must not stare at an inn wall.
        var simulation = _session.Simulation ?? throw new InvalidOperationException("Talking gate has no world");
        double reach = (_session.Setup.Items.Inventory.ReachMm + _session.Setup.Movement.BodyRadiusMm) / 1000.0;
        if (!await Approach(npcId, Math.Max(.8, reach - .25), 90)) throw new InvalidOperationException("Talking gate could not walk into Sel's talk reach");
        var figure = _npcs.FigureOf(npcId) as SkinnedFigure ?? throw new InvalidOperationException("Talking gate requires the shipped HumGen NPC");
        var driver = figure.FindChildren("*", "", true, false).OfType<FaceDriver>().SingleOrDefault()
            ?? throw new InvalidOperationException("Talking NPC has no production face driver");
        if (!driver.NativeJawBound) throw new InvalidOperationException("Talking NPC has no native FACS jaw binding");
        var rows = new List<object>();
        bool hudWasVisible = _hud.Visible;
        bool freeCamera = _innFreeCamera;
        uint canvasMask = GetViewport().CanvasCullMask;
        float fov = camera.Fov;
        try
        {
            _hud.Visible = false;
            _innFreeCamera = true;
            GetViewport().CanvasCullMask = 0; // Framing only; UI/gameplay state keeps running.
            camera.MakeCurrent();
            camera.Fov = 32;
            foreach (string view in new[] { "front", "three_quarter" })
            {
                // Re-open through gameplay for each angle, so both sample a fresh real utterance rather than a held-open mouth.
                if (simulation.Conversation is not null) { _dialogue.Leave(); await Rcd3Wait(.15); }
                _controller.Talk(npcId);
                await Rcd3Wait(.7); // Let the normal conversation turn finish before choosing its face angle.
                var talk = simulation.Conversation;
                if (talk?.NpcId != npcId || !simulation.Npcs.Single(n => n.Id == npcId).Talking)
                    throw new InvalidOperationException("Talking gate did not open a normal NPC conversation");
                _dialogue.Visible = false; // Framing only; the authoritative conversation and normal speech driver keep running.
                var aim = (figure.Head ?? throw new InvalidOperationException("Talking NPC has no head marker")) - Vector3.Up * .07f;
                var basis = figure.GlobalBasis.Orthonormalized();
                camera.GlobalPosition = aim + basis.Z * .85f + Vector3.Up * .02f
                    + (view == "front" ? Vector3.Zero : basis.X * .38f);
                camera.LookAt(aim);
                for (int sample = 0; sample < 3; sample++)
                {
                    ulong deadline = Time.GetTicksMsec() + 2500;
                    while (driver.JawWeight < .045f && Time.GetTicksMsec() < deadline)
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (driver.JawWeight < .045f) throw new InvalidOperationException("Normal dialogue produced no measurable speaking jaw");
                    string name = view + "_" + sample;
                    if (GetViewport().GetCamera3D() != camera) throw new InvalidDataException("Talking close-up camera is not rendering");
                    await Rcd3Shot(directory, name);
                    rows.Add(new { frame = name + ".png", npc = npcId, model = figure.ModelId, talk.DialogueId, talk.NodeId,
                        tick = simulation.WorldTick, jaw_weight = driver.JawWeight, native_jaw_bound = driver.NativeJawBound,
                        native_jaw_angle_rad = figure.FindChildren("*", "", true, false).OfType<HumGenJaw>().Single().AppliedAngle,
                        camera = camera.GlobalPosition.ToString() });
                    await Rcd3Wait(.12);
                }
            }
            File.WriteAllText(Path.Combine(directory, "evidence.json"), JsonSerializer.Serialize(new {
                status = "CAPTURED_REQUIRES_CLAUDE_FACE_AND_THROAT_REVIEW", mode = "Normal walking, conversation and native visemes; no static-GLB anatomy inference", frames = rows
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            _dialogue.Leave();
            _hud.Visible = hudWasVisible;
            _innFreeCamera = freeCamera;
            GetViewport().CanvasCullMask = canvasMask;
            camera.Fov = fov;
            _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
        }
    }
}
