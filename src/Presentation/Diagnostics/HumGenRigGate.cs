using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Diagnostics;

/// <summary>Headless gate on the actual paid exports; does not load or exercise either retired cast.</summary>
public partial class HumGenRigGate : Node
{
    public override void _Ready()
    {
        try
        {
            var rows = new List<object>();
            foreach (string path in OS.GetCmdlineUserArgs())
            {
                var document = new GltfDocument();
                var state = new GltfState();
                if (document.AppendFromFile(path, state) != Error.Ok) throw new InvalidDataException(path);
                var model = document.GenerateScene(state);
                AddChild(model);
                var skeleton = ArtLibrary.Find<Skeleton3D>(model) ?? throw new InvalidDataException("Missing skeleton");
                int upgraded = CharacterMaterials.Upgrade(model, out int zones);
                if (zones < 3 || upgraded < 3) throw new InvalidDataException("Paid skin/eye/hair materials did not resolve");
                string[] roles = ["hips", "pelvis", "head", "neck", "chest", "thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R", "hand_r", "hand_l", "eye.L", "eye.R"];
                foreach (string role in roles)
                    if (BoneNames.Find(skeleton, role) < 0) throw new InvalidDataException("Missing role: " + role);
                var face = FaceDriver.Attach(model, "humgen-rig-gate") ?? throw new InvalidDataException("FACS driver did not bind");
                AddChild(face);
                face.Say("Ada, we should go to the inn. Who are you?");
                float blinkPeak = 0, jawPeak = 0;
                for (int frame = 0; frame < 600; frame++)
                {
                    face._Process(1.0 / 120);
                    blinkPeak = Mathf.Max(blinkPeak, face.BlinkWeight);
                    jawPeak = Mathf.Max(jawPeak, face.JawWeight);
                }
                if (blinkPeak < .5f || jawPeak < .1f) throw new InvalidDataException("FACS blink/speech did not advance");
                var grip = BodyModifiers.Grip(skeleton, "RightHand") ?? throw new InvalidDataException("Hand grip did not bind");
                var rotations = grip.ClosedRotations(skeleton);
                if (rotations.Count != 15) throw new InvalidDataException("Expected fifteen native finger segments");
                var socket = HeldWeapon.ClosedGrip(skeleton, "RightHand", rotations);
                if (!socket.Origin.IsFinite() || socket.Origin.Length() > .2f || Mathf.Abs(socket.Basis.Determinant()) < .99f)
                    throw new InvalidDataException("Invalid hand socket: " + socket);
                rows.Add(new { body = path, sha256 = Godot.FileAccess.GetSha256(path), bones = skeleton.GetBoneCount(),
                    roles = roles.Length, finger_segments = rotations.Count, grip_offset_m = socket.Origin.Length(),
                    face_bound = true, blink_peak = blinkPeak, jaw_peak = jawPeak, material_zones = zones, materials_upgraded = upgraded });
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
            GetTree().Quit(1);
        }
    }
}
