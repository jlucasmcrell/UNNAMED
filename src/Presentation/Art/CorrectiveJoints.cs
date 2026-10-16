// UNNAMED Presentation - a MetaHuman body's corrective helper joints, driven as its own rig drives them (character pipeline 4.2)
// Godot presentation only: no gameplay state lives here (D-11)

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// The knee, elbow, glute, deltoid and wrist helper joints of a MetaHuman body, moved as Epic's body rig moves them: the character
/// pipeline sweeps each limb joint on the built MetaHuman and records the helpers (asset_work/metahuman/tools/ue_sample_helpers.py,
/// fit_helpers.py), shipped beside the rigged model as <c>&lt;id&gt;_correctives.json</c>. Each helper follows one driver joint: per
/// driver axis, a table over the driver's swing about it (degrees) of the helper's offset from rest; the axes' entries add. Garments
/// take their weights from the body, helpers included, so every garment inherits the correction (CHARACTER_ANIMATION_STRATEGY.md 4.2).
/// Runs after the clips, IK and look-at and before the garment springs (SkinnedFigure keeps it there).
/// </summary>
public sealed partial class CorrectiveJoints : SkeletonModifier3D
{
    private sealed record Axis(int Component, Vector3[] Move, Vector3[] Turn);

    private sealed record Helper(int Bone, int Driver, Axis[] Axes);

    private Helper[] _helpers = Array.Empty<Helper>();
    private float _first, _step;

    /// <summary>The model's corrective joints on <paramref name="skeleton"/>, or null when it ships no tables.</summary>
    public static CorrectiveJoints? Load(ArtLibrary art, string assetId, Skeleton3D skeleton)
    {
        if (art.RiggedFile(assetId, assetId + "_correctives.json") is not { } path)
            return null;
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var angles = json.RootElement.GetProperty("angles_deg").EnumerateArray().Select(a => (float)a.GetDouble()).ToArray();
        static Vector3[] Rows(JsonElement e) => e.EnumerateArray()
            .Select(v => new Vector3((float)v[0].GetDouble(), (float)v[1].GetDouble(), (float)v[2].GetDouble())).ToArray();
        var helpers = new List<Helper>();
        foreach (var h in json.RootElement.GetProperty("helpers").EnumerateObject())
        {
            int bone = skeleton.FindBone(h.Name), driver = skeleton.FindBone(h.Value.GetProperty("driver").GetString()!);
            if (bone < 0 || driver < 0)
            {
                art.Report(assetId, $"corrective joint {h.Name} or its driver is not in the skeleton; left at rest", assetId);
                continue;
            }
            var axes = h.Value.GetProperty("axes").EnumerateObject()
                .Select(a => new Axis("xyz".IndexOf(a.Name[0]), Rows(a.Value.GetProperty("t")), Rows(a.Value.GetProperty("r")))).ToArray();
            helpers.Add(new Helper(bone, driver, axes));
        }
        var joints = new CorrectiveJoints { Name = "CorrectiveJoints", _helpers = helpers.ToArray(), _first = angles[0], _step = angles[1] - angles[0] };
        skeleton.AddChild(joints);
        return joints;
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        if (GetSkeleton() is not { } skeleton)
            return;
        foreach (var h in _helpers)
        {
            // The driver's rotation away from its rest, as a rotation vector in its own frame: one component per swept axis.
            var away = skeleton.GetBoneRest(h.Driver).Basis.GetRotationQuaternion().Inverse() * skeleton.GetBonePoseRotation(h.Driver);
            if (away.W < 0)
                away = -away;
            float half = Mathf.Acos(Mathf.Clamp(away.W, -1f, 1f));
            float sin = Mathf.Sin(half);
            var degrees = sin < 1e-6f ? Vector3.Zero : new Vector3(away.X, away.Y, away.Z) / sin * Mathf.RadToDeg(2 * half);
            Vector3 move = Vector3.Zero, turn = Vector3.Zero;
            foreach (var axis in h.Axes)
            {
                float at = Mathf.Clamp((degrees[axis.Component] - _first) / _step, 0, axis.Move.Length - 1);
                int i = Math.Min((int)at, axis.Move.Length - 2);
                float f = at - i;
                move += axis.Move[i].Lerp(axis.Move[i + 1], f);
                turn += axis.Turn[i].Lerp(axis.Turn[i + 1], f);
            }
            var rest = skeleton.GetBoneRest(h.Bone);
            skeleton.SetBonePosePosition(h.Bone, rest.Origin + move);
            var restTurn = rest.Basis.GetRotationQuaternion();
            skeleton.SetBonePoseRotation(h.Bone, turn.LengthSquared() < 1e-12f ? restTurn : new Quaternion(turn.Normalized(), turn.Length()) * restTurn);
        }
    }
}
