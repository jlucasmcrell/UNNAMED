using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Diagnostics;

/// <summary>Headless: attach the game's FaceDriver to each paid GLB and report where the eyeballs point after the eye modifier runs.</summary>
public partial class EyeGazeProbe : Node
{
    public override async void _Ready()
    {
        foreach (string pair in OS.GetCmdlineUserArgs())
        {
            string path = pair.Split('|')[0];
            string? library = pair.Contains('|') ? pair.Split('|')[1] : null;
            string clip = pair.Split('|').Length > 2 ? pair.Split('|')[2] : "idle";
            var document = new GltfDocument();
            var state = new GltfState();
            if (document.AppendFromFile(path, state) != Error.Ok) { GD.PushError("load " + path); continue; }
            var model = (Node3D)document.GenerateScene(state);
            AddChild(model);
            var rig = ArtLibrary.Find<Skeleton3D>(model)!;
            var face = FaceDriver.Attach(model, "eye-probe");
            if (face is null) { GD.Print("EYE_PROBE no face ", path); continue; }
            AddChild(face);
            if (library is not null)
            {
                foreach (var imported in model.FindChildren("*", nameof(AnimationPlayer), true, false)) imported.Free();
                var player = new AnimationPlayer();
                model.AddChild(player);
                player.RootNode = new NodePath("..");
                var motion = GD.Load<AnimationLibrary>(library);
                var lib = new AnimationLibrary();
                string skeletonPath = model.GetPathTo(rig);
                foreach (string name in motion.GetAnimationList())
                {
                    var animation = (Animation)motion.GetAnimation(name).Duplicate();
                    for (int t = 0; t < animation.GetTrackCount(); t++)
                        animation.TrackSetPath(t, new NodePath($"{skeletonPath}:{animation.TrackGetPath(t).GetSubName(0)}"));
                    lib.AddAnimation(name, animation);
                }
                player.AddAnimationLibrary("", lib);
                player.Play(clip);
            }
            var forward = rig.GlobalBasis.Inverse() * model.GlobalBasis.Z;
            var rows = new List<string>();
            void Measure()
            {
                foreach (string name in new[] { "LeftEye", "RightEye" })
                {
                    int bone = rig.FindBone(name);
                    var axis = (rig.GetBoneGlobalRest(bone).Basis.Inverse() * forward).Normalized();
                    var pose = rig.GetBoneGlobalPose(bone);
                    var look = (pose.Basis * axis).Normalized();
                    int headBone = rig.FindBone("Head");
                    var headAxis = (rig.GetBoneGlobalRest(headBone).Basis.Inverse() * forward).Normalized();
                    var headPose = rig.GetBoneGlobalPose(headBone).Basis;
                    var inHead = (headPose.Inverse() * look).Normalized();
                    var headFwd = (headPose.Inverse() * (headPose * headAxis)).Normalized();
                    float relPitch = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(inHead.Y, -1, 1)) - Mathf.Asin(Mathf.Clamp(headFwd.Y, -1, 1)));
                    var headWorld = (headPose * headAxis).Normalized();
                    rows.Add($"{name} in-head pitch {relPitch:F1} head-world pitch {Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(headWorld.Y, -1, 1))):F1} eye-world pitch {Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(look.Y, -1, 1))):F1}");
                    var rest = (rig.GetBoneGlobalRest(bone).Basis * axis).Normalized();
                    rows.Add($"{name}: pitch {Mathf.RadToDeg(Mathf.Asin(look.Y)):F1} deg yaw {Mathf.RadToDeg(Mathf.Atan2(look.X, look.Z)):F1} deg (rest pitch {Mathf.RadToDeg(Mathf.Asin(rest.Y)):F1})");
                }
            }
            for (int frame = 0; frame < 90; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            rig.SkeletonUpdated += Measure;
            for (int frame = 0; frame < 3; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            rig.SkeletonUpdated -= Measure;
            var target = (Node3D?)model.FindChild("FacialEyeTarget", true, false);
            int left = rig.FindBone("LeftEye");
            var eyeWorld = rig.GlobalTransform * rig.GetBoneGlobalPose(left).Origin;
            string shapes = "";
            foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
                if (mesh.Name.ToString().Contains("Eyes_LOD0") && mesh.Mesh is ArrayMesh array)
                    for (int s = 0; s < array.GetBlendShapeCount(); s++)
                        if (Math.Abs(mesh.GetBlendShapeValue(s)) > .001f) shapes += $" {array.GetBlendShapeName(s)}={mesh.GetBlendShapeValue(s):F2}";
            GD.Print("EYE_PROBE ", Path.GetFileName(path), " ", clip, " | ", string.Join(" | ", rows.Distinct()),
                " | target-eye ", target is null ? "none" : (target.GlobalPosition - eyeWorld).ToString(), " | eye-mesh shapes:", shapes);
            face.QueueFree();
            model.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        GetTree().Quit();
    }
}
