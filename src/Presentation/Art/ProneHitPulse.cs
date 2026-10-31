using Godot;
namespace UNNAMED.Presentation.Art;

/// <summary>Recorded upper-body recoil over the current prone or rising pose, leaving its footing clock and legs alone.</summary>
internal sealed partial class ProneHitPulse : SkeletonModifier3D
{
    private readonly List<(int Bone,int Hit,int Reference)> _tracks=new();
    private Animation _hit=null!,_reference=null!;
    public float Age {get;set;}=float.PositiveInfinity;

    public void Bind(Skeleton3D rig,Animation hit,Animation reference,IReadOnlySet<string> upperBody)
    {
        _hit=hit;_reference=reference;
        var resting=Enumerable.Range(0,reference.GetTrackCount())
            .Where(t=>reference.TrackGetType(t)==Animation.TrackType.Rotation3D)
            .ToDictionary(t=>reference.TrackGetPath(t).GetSubName(0).ToString(),t=>t,StringComparer.Ordinal);
        for(int track=0;track<hit.GetTrackCount();track++)
        {
            string bone=hit.TrackGetPath(track).GetSubName(0);
            int joint=rig.FindBone(bone);
            if(hit.TrackGetType(track)==Animation.TrackType.Rotation3D && upperBody.Contains(bone)
                && resting.TryGetValue(bone,out int at) && joint>=0)
                _tracks.Add((joint,track,at));
        }
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        if(!float.IsFinite(Age)||Age<0||Age>=_hit.Length||GetSkeleton() is not {} rig)return;
        foreach(var (bone,hit,reference) in _tracks)
        {
            var basis=_reference.RotationTrackInterpolate(reference,Math.Min(Age,_reference.Length));
            var recoil=(basis.Inverse()*_hit.RotationTrackInterpolate(hit,Age)).Normalized();
            float angle=Quaternion.Identity.AngleTo(recoil);
            recoil=Quaternion.Identity.Slerp(recoil,Math.Min(1,Mathf.DegToRad(20)/Math.Max(angle,.000001f)));
            rig.SetBonePoseRotation(bone,(rig.GetBonePoseRotation(bone)*recoil).Normalized());
        }
    }
}
