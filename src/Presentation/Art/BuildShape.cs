// UNNAMED Presentation - a body's build drawn as girth: the trunk and limbs broader or narrower across their length (WP2, the creator)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// The creator's build on a body that has no model of its own for it: after the clips have posed the skeleton, the trunk (hips to neck)
/// and each upper arm, forearm, thigh and shin are scaled across their own length - never along it, so no limb grows longer and the
/// sockets, the IK and the feet stay where the clips put them - and every bone hanging off them (the neck and head, the collarbones,
/// the hands, the feet) is put back exactly as posed. A girth of 1 does nothing. Bounded by PJ04 §5.2: discrete presets, small ranges.
/// </summary>
public sealed partial class BuildShape : SkeletonModifier3D
{
    /// <summary>One stretch of the body: the bones scaled together, across the line from <see cref="From"/> to <see cref="To"/>.</summary>
    private sealed record Segment(int From, int To, int[] Bones);

    private Segment[] _segments = Array.Empty<Segment>();

    /// <summary>The bones put back as posed, and every bone this touches, parents first.</summary>
    private int[] _kept = Array.Empty<int>();

    private int[] _order = Array.Empty<int>();
    private readonly Dictionary<int, Transform3D> _posed = new();

    /// <summary>How broad: 1 as modelled, above broader, below narrower.</summary>
    public float Girth { get; set; } = 1f;

    /// <summary>Find the trunk and the limbs on this rig (either vocabulary, <see cref="BoneNames"/>); false, and no effect, when it lacks them.</summary>
    public bool Bind(Skeleton3D skeleton)
    {
        var stops = new HashSet<int>();
        int Bone(string canonical) => BoneNames.Find(skeleton, canonical);
        var spans = new List<(int From, int To)> { (Bone("hips"), Bone("neck")) };
        foreach (string side in new[] { "L", "R" })
        {
            spans.Add((Bone($"upper_arm.{side}"), Bone($"forearm.{side}")));
            spans.Add((Bone($"forearm.{side}"), Bone($"hand.{side}")));
            spans.Add((Bone($"thigh.{side}"), Bone($"shin.{side}")));
            spans.Add((Bone($"shin.{side}"), Bone($"foot.{side}")));
        }
        if (spans.Any(s => s.From < 0 || s.To < 0))
            return false;
        // A stretch stops at the next stretch's first bone and wherever the body hangs something of its own: the neck, the collarbones.
        foreach (var (from, to) in spans)
            stops.Add(to);
        foreach (var (from, _) in spans.Skip(1))
            stops.Add(from);
        for (int b = 0; b < skeleton.GetBoneCount(); b++)
        {
            string name = skeleton.GetBoneName(b).ToLowerInvariant();
            if (name.Contains("clavicle") || name.StartsWith("shoulder", StringComparison.Ordinal))
                stops.Add(b);
        }
        var touched = new HashSet<int>();
        var segments = new List<Segment>();
        foreach (var (from, to) in spans)
        {
            var bones = new List<int>();
            var open = new Stack<int>();
            open.Push(from);
            while (open.Count > 0)
            {
                int bone = open.Pop();
                bones.Add(bone);
                foreach (int child in skeleton.GetBoneChildren(bone))
                {
                    if (!stops.Contains(child))
                        open.Push(child);
                }
            }
            segments.Add(new Segment(from, to, bones.ToArray()));
            touched.UnionWith(bones);
        }
        var kept = new HashSet<int>();
        foreach (int bone in touched)
        {
            foreach (int child in skeleton.GetBoneChildren(bone))
            {
                if (!touched.Contains(child))
                    kept.Add(child);
            }
        }
        _segments = segments.ToArray();
        _kept = kept.ToArray();
        int Depth(int bone)
        {
            int depth = 0;
            for (int p = skeleton.GetBoneParent(bone); p >= 0; p = skeleton.GetBoneParent(p))
                depth++;
            return depth;
        }
        _order = touched.Concat(kept).OrderBy(Depth).ToArray();
        return true;
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        if (GetSkeleton() is not { } skeleton || _segments.Length == 0 || Mathf.Abs(Girth - 1f) < 0.001f)
            return;
        _posed.Clear();
        foreach (int bone in _order)
            _posed[bone] = skeleton.GetBoneGlobalPose(bone);
        var wanted = new Dictionary<int, Transform3D>(_posed);
        foreach (var segment in _segments)
        {
            var along = _posed[segment.To].Origin - _posed[segment.From].Origin;
            if (along.LengthSquared() < 1e-8f)
                continue;
            var across = Across(along.Normalized(), Girth);
            foreach (int bone in segment.Bones)
            {
                var pose = _posed[bone];
                wanted[bone] = new Transform3D(across * pose.Basis, pose.Origin);
            }
        }
        // Parents first, so every bone ends exactly where it is wanted whatever its parent became.
        foreach (int bone in _order)
            skeleton.SetBoneGlobalPose(bone, wanted[bone]);
    }

    /// <summary>A scale of <paramref name="girth"/> across the unit direction <paramref name="d"/> and 1 along it: g I + (1 - g) d dᵀ.</summary>
    private static Basis Across(Vector3 d, float girth)
    {
        float k = 1f - girth;
        return new Basis(
            new Vector3(girth + k * d.X * d.X, k * d.Y * d.X, k * d.Z * d.X),
            new Vector3(k * d.X * d.Y, girth + k * d.Y * d.Y, k * d.Z * d.Y),
            new Vector3(k * d.X * d.Z, k * d.Y * d.Z, girth + k * d.Z * d.Z));
    }
}
