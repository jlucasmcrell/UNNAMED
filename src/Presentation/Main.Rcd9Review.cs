using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation;

public partial class Main
{
    /// <summary>Opt-in native wardrobe inspection, using the normal NPC bindings, figures, materials and motion loader.</summary>
    private async Task RunRcd9WardrobeReview(string directory, Camera3D camera)
    {
        if (OS.GetEnvironment("OTHERREACH_HUMGEN_LOD_REVIEW") == "1")
        {
            await RunHumGenLodReview(directory, camera);
            return;
        }
        var looks = _bindings.People.Where(p => p.Key != "player" && p.Value.Model.StartsWith("humgen_", StringComparison.Ordinal))
            .GroupBy(p => p.Value.Model)
            .Select(g => g.OrderBy(p => p.Value.PeopleId is null or "people.veth" ? 0 : 1).ThenBy(p => p.Key, StringComparer.Ordinal).First())
            .OrderBy(p => p.Value.Model, StringComparer.Ordinal).ToArray();
        if (looks.Length != 14 || looks.Any(p => p.Value.Model.StartsWith("humgen_player_", StringComparison.Ordinal)))
            throw new InvalidDataException("Wardrobe review requires all fourteen NPC bodies and no player body assigned to an NPC");
        _hud.Visible = false;
        _avatar.Visible = false;
        _npcs.Visible = false;
        camera.Fov = 32;
        var feet = _avatar.GlobalPosition + Vector3.Up * 2.5f;
        var stage = new Node3D { Name = "WardrobeInspection" };
        AddChild(stage);
        stage.AddChild(new MeshInstance3D
        {
            Position = feet - Vector3.Up * .04f,
            Mesh = new BoxMesh { Size = new Vector3(3, .08f, 3) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.18f, .2f, .2f), Roughness = 1 },
        });
        var captionLayer = new CanvasLayer();
        AddChild(captionLayer);
        var caption = new Label { Position = new Vector2(32, 32) };
        caption.AddThemeFontSizeOverride("font_size", 26);
        caption.AddThemeColorOverride("font_shadow_color", Colors.Black);
        caption.AddThemeConstantOverride("shadow_offset_x", 2);
        caption.AddThemeConstantOverride("shadow_offset_y", 2);
        captionLayer.AddChild(caption);
        var receipt = new List<object>();
        foreach (var look in looks)
        {
            var figure = SkinnedFigure.Create(_art, _bindings, look.Key)
                ?? throw new InvalidDataException("Missing native NPC: " + look.Key);
            stage.AddChild(figure);
            figure.SetStance(CombatStance.AtRest);
            figure.SetMovementDirection(Vector3.Back);
            figure.Pose(feet, 0, 0, 0);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            foreach (bool walking in new[] { false, true })
            {
                string pose = walking ? "walk_threequarter" : "idle_front";
                caption.Text = look.Value.Model + " | " + pose + "\n" + look.Key + " | " + (look.Value.PeopleId ?? "people.veth")
                    + "\nStaged wardrobe inspection: normal game figure, native fitted outfit";
                var aim = feet + Vector3.Up * .92f;
                camera.GlobalPosition = aim + (walking ? new Vector3(2.3f, .15f, 3) : new Vector3(0, .15f, 3.8f));
                camera.LookAt(aim);
                var footStart = figure.Foot("L") ?? throw new InvalidDataException("NPC has no native left foot");
                float footMotion = 0;
                ulong until = Time.GetTicksMsec() + 1100;
                while (Time.GetTicksMsec() < until)
                {
                    figure.Pose(feet, 0, walking ? 1.6f : 0, GetProcessDeltaTime());
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    if (figure.Foot("L") is { } foot) footMotion = Math.Max(footMotion, foot.DistanceTo(footStart));
                }
                if (figure.ClipState is null || (walking && (figure.ClipState is not ("walk" or "locomotion_2d") || footMotion < .02f)))
                    throw new InvalidDataException("NPC did not play its native wardrobe test motion: " + look.Value.Model);
                await Rcd3Shot(directory, "wardrobe_" + look.Value.Model + "_" + pose);
                receipt.Add(new { model = look.Value.Model, person = look.Key, people = look.Value.PeopleId, pose, clip = figure.ClipState, foot_motion_m = footMotion, joints = figure.Skeleton.GetBoneCount() });
            }
            if (look.Value.Model == "humgen_named_tavar_orr")
            {
                if (!figure.SetRestPose("captive_kneel")) throw new InvalidDataException("Tavar has no accepted captive kneel");
                var aim = feet + Vector3.Up * .55f;
                camera.GlobalPosition = aim + new Vector3(2.3f, .3f, 3);
                camera.LookAt(aim);
                caption.Text = look.Value.Model + " | captive_kneel\n" + look.Key + " | " + look.Value.PeopleId
                    + "\nWinter coat inspection: normal captive animation";
                ulong until = Time.GetTicksMsec() + 1100;
                while (Time.GetTicksMsec() < until)
                {
                    figure.Pose(feet, 0, 0, GetProcessDeltaTime());
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
                if (figure.ClipState != "captive_kneel") throw new InvalidDataException("Tavar did not play his captive kneel");
                await Rcd3Shot(directory, "wardrobe_" + look.Value.Model + "_captive_kneel");
                receipt.Add(new { model = look.Value.Model, person = look.Key, people = look.Value.PeopleId, pose = "captive_kneel", clip = figure.ClipState, joints = figure.Skeleton.GetBoneCount() });
            }
            figure.Free();
        }
        File.WriteAllText(Path.Combine(directory, "wardrobe_review.json"), JsonSerializer.Serialize(new
        {
            status = "NATIVE_FRAMES_REQUIRE_CLAUDE_REVIEW", npc_bodies = looks.Length, views = receipt,
            staging = "Raised inspection platform in the running world; walk in place; normal NPC bindings, no player bodies",
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
