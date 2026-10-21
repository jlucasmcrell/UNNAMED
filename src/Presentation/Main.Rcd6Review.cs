using Godot;
using UNNAMED.Presentation.Player;
using System.Text.Json;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Opt-in pose evidence on the normal world's actual player and NPCs; presentation only.
    private async Task RunRcd6MpfbMotionReview(string directory, Camera3D camera)
    {
        if (_avatar is not SkinnedFigure player) throw new InvalidOperationException("MPFB player required");
        var candidates = _npcs.GetChildren().OfType<SkinnedFigure>()
            .Where(f => f.IsInsideTree()).OrderBy(f => f.GlobalPosition.DistanceSquaredTo(player.GlobalPosition))
            .GroupBy(f => f.ModelId).Select(g => g.First()).Where(f => f.ModelId != player.ModelId).Take(2).ToArray();
        if (candidates.Length != 2) throw new InvalidOperationException("Two different MPFB NPC rigs required");
        var actors = new[] { player }.Concat(candidates).ToArray();
        SetProcess(false);
        _scatter.Visible = false; // Explicit diagnostic: clear grass so ground contact is visible.
        camera.Fov = 50;
        var rows = new List<object>();
        var stage = player.GlobalPosition;
        foreach (var figure in actors)
        {
            foreach (var other in _npcs.GetChildren().OfType<Figure>().Append(_avatar)) other.Visible = other == figure;
            figure.GlobalPosition = stage; // Pose-only review staging on the player's clear ground, no world-state changes.
            figure.SetFirstPerson(false);
            var model = figure.GetChildren().OfType<SkinnedModel>().Single();
            var animator = model.GetNode<AnimationPlayer>("Clips");
            model.SetProcess(false);
            animator.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
            figure.Skeleton.ModifierCallbackModeProcess = Skeleton3D.ModifierCallbackModeProcessEnum.Manual;
            foreach (string state in new[] { "idle", "walk", "run", "turn", "sword_attack_1" })
            {
                string clip = state == "turn" ? "walk" : state;
                if (!model.Has(clip)) throw new InvalidOperationException("Missing motion " + clip);
                foreach (float phase in new[] { .15f, .45f, .75f })
                {
                    figure.Rotation = new Vector3(0, state == "turn" ? phase * Mathf.Pi : 0, 0);
                    animator.Play(clip, 0);
                    animator.Seek(animator.GetAnimation(clip).Length * phase, true);
                    animator.Advance(0);
                    figure.Skeleton.Advance(0);
                    foreach (var view in new[] { (Name: "side", Yaw: Mathf.Pi / 2), (Name: "threequarter", Yaw: Mathf.Pi / 4) })
                    {
                        var aim = figure.GlobalPosition + Vector3.Up * .95f;
                        camera.GlobalPosition = aim + figure.GlobalBasis.Z.Rotated(Vector3.Up, view.Yaw) * 3.5f + Vector3.Up * .2f;
                        camera.LookAt(aim);
                        await Rcd3Wait(.08);
                        string name = $"{figure.ModelId}_{state}_{phase:0.00}_{view.Name}";
                        await Rcd3Shot(directory, name);
                        Vector3 Bone(string bone) => figure.Skeleton.GlobalTransform * figure.Skeleton.GetBoneGlobalPose(figure.Skeleton.FindBone(bone)).Origin;
                        rows.Add(new { name, person = figure.Name.ToString(), model = figure.ModelId, state, clip, phase,
                            feet = figure.GlobalPosition.ToString(), left = Bone("ball_l").ToString(), right = Bone("ball_r").ToString(),
                            pelvis = Bone("pelvis").ToString(), head = Bone("head").ToString(), stature = figure.StatureScale,
                            camera = camera.GlobalPosition.ToString(), fixture = "Normal-world actual figure and shipped assets; pose sampling; staged on player clear ground; other NPCs and grass hidden for sole visibility" });
                    }
                }
            }
        }
        File.WriteAllText(Path.Combine(directory, "motion.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(directory, "complete.json"), JsonSerializer.Serialize(new { status = "CAPTURED_REQUIRES_CLAUDE_REVIEW", frames = rows.Count }));
    }
}
