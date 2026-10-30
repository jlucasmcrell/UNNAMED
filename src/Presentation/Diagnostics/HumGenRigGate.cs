using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Diagnostics;

/// <summary>Headless gate on the actual paid exports; does not load or exercise either retired cast.</summary>
public partial class HumGenRigGate : Node
{
    public override async void _Ready()
    {
        try
        {
            var rows = new List<object>();
            foreach (string path in OS.GetCmdlineUserArgs())
            {
                string runtimeRoot = OS.GetEnvironment("OTHERREACH_HUMGEN_GATE_RUNTIME_ROOT");
                SkinnedModel? runtime = null;
                Node3D model;
                if (!string.IsNullOrEmpty(runtimeRoot))
                {
                    string id = Path.GetFileNameWithoutExtension(path);
                    if (id.EndsWith("_rigged", StringComparison.Ordinal)) id = id[..^7];
                    runtime = SkinnedModel.Create(new ArtLibrary(Path.Combine(runtimeRoot, "primary")), id,
                        new Dictionary<string, string>(), "humgen-payload-gate") ?? throw new InvalidDataException("Runtime model failed: " + id);
                    model = runtime;
                }
                else
                {
                    var document = new GltfDocument();
                    var state = new GltfState();
                    if (document.AppendFromFile(path, state) != Error.Ok) throw new InvalidDataException(path);
                    model = (Node3D)document.GenerateScene(state);
                }
                AddChild(model);
                var player = runtime?.GetNode<AnimationPlayer>("Clips");
                if (runtime is not null)
                {
                    runtime.SetProcess(false);
                    if (!runtime.HasFootPlantEvents) throw new InvalidDataException("Runtime foot contacts did not configure");
                    player!.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
                    runtime.Play("idle", 0);
                }
                var skeleton = ArtLibrary.Find<Skeleton3D>(model) ?? throw new InvalidDataException("Missing skeleton");
                int upgraded = CharacterMaterials.Upgrade(model, out int zones);
                if (zones < 3 || upgraded < 3) throw new InvalidDataException("Paid skin/eye/hair materials did not resolve");
                string[] roles = ["hips", "pelvis", "head", "neck", "chest", "thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R", "hand_r", "hand_l", "eye.L", "eye.R"];
                foreach (string role in roles)
                    if (BoneNames.Find(skeleton, role) < 0) throw new InvalidDataException("Missing role: " + role);
                var face = FaceDriver.Attach(model, "humgen-rig-gate") ?? throw new InvalidDataException("FACS driver did not bind");
                AddChild(face);
                face.SetProcess(false);
                face.Say("Ada, we should go to the inn. Who are you?");
                float blinkPeak = 0, jawPeak = 0;
                int jaw = skeleton.FindBone("Jaw");
                var jawRest = skeleton.GetBoneRest(jaw).Basis.GetRotationQuaternion();
                int eye = skeleton.FindBone("LeftEye");
                var eyeRest = skeleton.GetBoneRest(eye).Basis.GetRotationQuaternion();
                var body = (Node3D)model;
                var opticalAxis = skeleton.GetBoneGlobalRest(eye).Basis.Inverse() * (skeleton.GlobalBasis.Inverse() * body.GlobalBasis.Z);
                float teethJawPeak = 0, eyePeak = 0, eyeForwardPeak = 0;
                void SampleFace()
                {
                    teethJawPeak = Mathf.Max(teethJawPeak, Mathf.RadToDeg(jawRest.AngleTo(skeleton.GetBonePoseRotation(jaw))));
                    eyePeak = Mathf.Max(eyePeak, Mathf.RadToDeg(eyeRest.AngleTo(skeleton.GetBonePoseRotation(eye))));
                    var forward = (skeleton.GlobalBasis * skeleton.GetBoneGlobalPose(eye).Basis * opticalAxis).Normalized();
                    eyeForwardPeak = Mathf.Max(eyeForwardPeak, Mathf.RadToDeg(forward.AngleTo(body.GlobalBasis.Z.Normalized())));
                }
                skeleton.ModifierCallbackModeProcess = Skeleton3D.ModifierCallbackModeProcessEnum.Manual;
                skeleton.SkeletonUpdated += SampleFace;
                for (int frame = 0; frame < 600; frame++)
                {
                    face._Process(1.0 / 120);
                    player?.Advance(1.0 / 120);
                    blinkPeak = Mathf.Max(blinkPeak, face.BlinkWeight);
                    jawPeak = Mathf.Max(jawPeak, face.JawWeight);
                    skeleton.Advance(1.0 / 120);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                skeleton.SkeletonUpdated -= SampleFace;
                if (blinkPeak < .5f || jawPeak < .1f) throw new InvalidDataException("FACS blink/speech did not advance");
                if (!face.NativeJawBound || face.NativeEyeBindings != 2 || teethJawPeak < .5f || eyeForwardPeak < .1f || eyeForwardPeak > 30)
                    throw new InvalidDataException($"Native facial channels: jaw={face.NativeJawBound}, eyes={face.NativeEyeBindings}, teeth={teethJawPeak}, eye={eyePeak}, optical={eyeForwardPeak} degrees");
                int runtimePlants = 0;
                if (runtime is not null)
                {
                    if (runtime.Has("bow_draw") && (runtime.MotionActionPhases is not { } marks
                        || !marks.TryGetValue("bow_draw", out var bow) || bow.Length != 3
                        || bow[0] <= 0 || bow[0] >= bow[1] || bow[1] >= bow[2] || bow[2] != 1))
                        throw new InvalidDataException("Native bow release phase is missing or invalid");
                    runtime.FootPlanted += (_, _, _) => runtimePlants++;
                    for (int frame = 0; frame < 240; frame++)
                    {
                        if (!runtime.MoveBlend(new Vector2(0, 1.5f), 1.0 / 120, runtime.MotionPaces!, 1))
                            throw new InvalidDataException("Runtime locomotion blend did not configure");
                        ArtLibrary.Find<AnimationTree>(runtime)!.Advance(1.0 / 120);
                        skeleton.Advance(1.0 / 120);
                    }
                    if (runtimePlants < 2) throw new InvalidDataException("Runtime blended gait emitted no foot contacts");
                }
                var grip = BodyModifiers.Grip(skeleton, "RightHand") ?? throw new InvalidDataException("Hand grip did not bind");
                var rotations = grip.ClosedRotations(skeleton);
                if (rotations.Count != 15) throw new InvalidDataException("Expected fifteen native finger segments");
                var socket = HeldWeapon.ClosedGrip(skeleton, "RightHand", rotations);
                if (!socket.Origin.IsFinite() || socket.Origin.Length() > .2f || Mathf.Abs(socket.Basis.Determinant()) < .99f)
                    throw new InvalidDataException("Invalid hand socket: " + socket);
                rows.Add(new { body = path, sha256 = Godot.FileAccess.GetSha256(path), bones = skeleton.GetBoneCount(),
                    roles = roles.Length, finger_segments = rotations.Count, grip_offset_m = socket.Origin.Length(),
                    face_bound = true, blink_peak = blinkPeak, jaw_peak = jawPeak, material_zones = zones, materials_upgraded = upgraded,
                    native_eye_bindings = face.NativeEyeBindings, teeth_jaw_peak_degrees = teethJawPeak, eye_peak_degrees = eyePeak,
                    eye_forward_peak_degrees = eyeForwardPeak, eye_rest_scale = skeleton.GetBoneRest(eye).Basis.Scale.ToString(),
                    runtime_clips = player?.GetAnimationList().Length, runtime_foot_contacts = runtime?.HasFootPlantEvents,
                    runtime_blended_plant_events = runtimePlants });
                face.Free();
                model.Free();
            }
            if (rows.Count == 0) throw new InvalidDataException("Pass paid HumGen GLB paths after --");
            string report = OS.GetEnvironment("OTHERREACH_HUMGEN_RIG_GATE_REPORT");
            if (string.IsNullOrEmpty(report)) throw new InvalidDataException("Set OTHERREACH_HUMGEN_RIG_GATE_REPORT");
            System.IO.File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(rows, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            GD.Print("HUMGEN_RIG_GATE_PASS ", rows.Count);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            CaptureQuit.At(this, 1);
        }
    }
}
