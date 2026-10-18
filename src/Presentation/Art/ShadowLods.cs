using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>Point-shadow culling ignores visibility ranges. Mirror the authored bands
/// in CastShadow, leaving node visibility and static GI geometry alone.</summary>
public sealed class ShadowLods
{
    private sealed record Band(GeometryInstance3D Node, Vector3 Centre, float BeginSquared, float EndSquared)
    {
        public ulong Id = Node.GetInstanceId();
        public GeometryInstance3D.ShadowCastingSetting Original = Node.CastShadow;
        public GeometryInstance3D.ShadowCastingSetting Applied = Node.CastShadow;
    }
    private readonly List<Band> _bands = new();
    private readonly List<GeometryInstance3D> _pending = new();
    private readonly HashSet<ulong> _known = new();
    private Node? _root;
    private SceneTree? _tree;

    public void Watch(Node root)
    {
        Unwatch();
        if (System.Environment.GetEnvironmentVariable("UNNAMED_SHADOW_LODS") == "0") return;
        _root = root; _tree = root.GetTree();
        _tree.NodeAdded += Added;
        foreach (var node in root.FindChildren("*", "GeometryInstance3D", true, false)) Added(node);
    }

    public void Unwatch()
    {
        if (_tree is not null) _tree.NodeAdded -= Added;
        _tree = null; _root = null;
        _bands.Clear(); _pending.Clear(); _known.Clear();
    }

    private void Added(Node node)
    {
        if (node is GeometryInstance3D geometry) _pending.Add(geometry);
    }

    public void Update(Vector3 eye)
    {
        // Ready/construction must finish first. Includes duplicated pieces, rigged LODs and later spawns.
        foreach (var node in _pending)
        {
            if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree() || _root is null
                || node.GetViewport() != _root.GetViewport() || !_root.IsAncestorOf(node)
                || node.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off
                || node.VisibilityRangeBegin == 0 && node.VisibilityRangeEnd == 0
                || !_known.Add(node.GetInstanceId())) continue;
            var bounds = node.CustomAabb.Size == Vector3.Zero ? node.GetAabb() : node.CustomAabb;
            // Keep both levels throughout the complete authored fade/hysteresis interval.
            float begin = Math.Max(0, node.VisibilityRangeBegin - node.VisibilityRangeBeginMargin);
            float end = node.VisibilityRangeEnd > 0 ? node.VisibilityRangeEnd + node.VisibilityRangeEndMargin : 0;
            _bands.Add(new Band(node, bounds.GetCenter(), begin * begin, end * end));
            node.SetMeta("shadow_lod_original", node.CastShadow.ToString());
        }
        _pending.Clear();
        for (int i = _bands.Count - 1; i >= 0; i--)
        {
            var band = _bands[i];
            if (!GodotObject.IsInstanceValid(band.Node)) { _known.Remove(band.Id); _bands.RemoveAt(i); continue; }
            if (!band.Node.IsInsideTree()) continue;
            var current = band.Node.CastShadow;
            // First-person/FoldEcho may change the authored mode. Do not restore a visible body.
            if (current != band.Applied)
            {
                band.Original = current;
                band.Node.SetMeta("shadow_lod_original", current.ToString());
            }
            float distanceSquared = eye.DistanceSquaredTo(band.Node.GlobalTransform * band.Centre);
            bool casting = distanceSquared >= band.BeginSquared
                && (band.EndSquared == 0 || distanceSquared < band.EndSquared);
            var next = casting ? band.Original : GeometryInstance3D.ShadowCastingSetting.Off;
            if (next != current) band.Node.CastShadow = next;
            band.Applied = next;
        }
    }
}
