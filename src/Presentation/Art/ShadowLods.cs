using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>Point-shadow culling ignores visibility ranges. Mirror the authored hard bands
/// in CastShadow, leaving visibility and static GI geometry alone.</summary>
public sealed class ShadowLods
{
    private sealed record Band(GeometryInstance3D Node, float BeginSquared, float EndSquared,
        GeometryInstance3D.ShadowCastingSetting Original)
    {
        public bool Casting = true;
    }
    private sealed record Group(GeometryInstance3D Reference, Vector3 Centre, Band[] Bands);
    private readonly List<Group> _groups = new();

    public void Clear() => _groups.Clear();

    public void Add(IEnumerable<GeometryInstance3D> nodes)
    {
        if (System.Environment.GetEnvironmentVariable("UNNAMED_SHADOW_LODS") == "0") return;
        var bands = nodes.Where(n => n.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off
            && (n.VisibilityRangeBegin > 0 || n.VisibilityRangeEnd > 0)
            && n.VisibilityRangeFadeMode == GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled)
            .Select(n => new Band(n, n.VisibilityRangeBegin * n.VisibilityRangeBegin,
                n.VisibilityRangeEnd * n.VisibilityRangeEnd, n.CastShadow)).ToArray();
        if (bands.Length == 0) return;
        foreach (var band in bands) band.Node.SetMeta("shadow_lod_original", band.Original.ToString());
        var reference = bands[0].Node;
        var bounds = reference.CustomAabb.Size == Vector3.Zero ? reference.GetAabb() : reference.CustomAabb;
        _groups.Add(new Group(reference, bounds.GetCenter(), bands));
    }

    public void Update(Vector3 eye)
    {
        for (int g = _groups.Count - 1; g >= 0; g--)
        {
            var group = _groups[g];
            if (!GodotObject.IsInstanceValid(group.Reference)) { _groups.RemoveAt(g); continue; }
            if (!group.Reference.IsInsideTree()) continue;
            float distanceSquared = eye.DistanceSquaredTo(group.Reference.GlobalTransform * group.Centre);
            foreach (var band in group.Bands)
            {
                bool casting = distanceSquared >= band.BeginSquared
                    && (band.EndSquared == 0 || distanceSquared < band.EndSquared);
                if (casting == band.Casting) continue;
                band.Node.CastShadow = casting ? band.Original : GeometryInstance3D.ShadowCastingSetting.Off;
                band.Casting = casting;
            }
        }
    }
}
