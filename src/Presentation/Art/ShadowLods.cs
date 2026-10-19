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
        public uint Layers = Node.Layers;
        public Vector3 WorldCentre;
        public bool InTree;
        public ShadowLodBounds? Bounds;
    }
    private readonly List<Band> _bands = new();
    private readonly List<GeometryInstance3D> _pending = new();
    private readonly HashSet<ulong> _known = new();
    private Node? _root;
    private SceneTree? _tree;
    private Vector3 _eye;

    // Callers changing a body's authored shadow mode must preserve the distance-band mask.
    public static void SetMode(GeometryInstance3D node, GeometryInstance3D.ShadowCastingSetting mode)
    {
        if (node.HasMeta("shadow_lod_bounds") && node.GetMeta("shadow_lod_bounds").AsGodotObject() is ShadowLodBounds bounds
            && GodotObject.IsInstanceValid(bounds)) bounds.ChangeMode?.Invoke(mode);
        else node.CastShadow = mode;
    }

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
        foreach (var band in _bands)
        {
            if (GodotObject.IsInstanceValid(band.Node))
            {
                band.Node.CastShadow = band.Original;
                band.Node.Layers = band.Layers;
                band.Node.RemoveMeta("shadow_lod_bounds");
                band.Node.RemoveMeta("shadow_lod_original");
            }
            if (GodotObject.IsInstanceValid(band.Bounds))
            {
                band.Bounds!.Changed = null; band.Bounds.InTree = null; band.Bounds.ChangeMode = null;
                band.Bounds.Free();
            }
        }
        _bands.Clear(); _pending.Clear(); _known.Clear();
    }

    private void Added(Node node)
    {
        if (node is GeometryInstance3D geometry) _pending.Add(geometry);
    }

    public void Update(Vector3 eye)
    {
        _eye = eye;
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
            var band = new Band(node, bounds.GetCenter(), begin * begin, end * end);
            var watcher = new ShadowLodBounds { Name = "ShadowLodBounds" };
            band.Bounds = watcher;
            watcher.InTree = active => band.InTree = active;
            watcher.Changed = transform => { band.WorldCentre = transform * band.Centre; Apply(band); };
            watcher.ChangeMode = mode =>
            {
                if (band.Original == mode) return;
                band.Original = mode;
                node.SetMeta("shadow_lod_original", mode.ToString());
                Apply(band);
            };
            _bands.Add(band);
            node.SetMeta("shadow_lod_original", node.CastShadow.ToString());
            node.SetMeta("shadow_lod_bounds", watcher);
            node.AddChild(watcher);
        }
        _pending.Clear();
        for (int i = _bands.Count - 1; i >= 0; i--)
        {
            var band = _bands[i];
            // The watcher's ExitTree runs before an in-tree node can be freed. Active bands
            // need only managed distance arithmetic; reserve native lifetime polls for detached bands.
            if (!band.InTree && !GodotObject.IsInstanceValid(band.Node)) { _known.Remove(band.Id); _bands.RemoveAt(i); continue; }
            Apply(band);
        }
    }

    private void Apply(Band band)
    {
        if (!band.InTree) return;
        float distanceSquared = _eye.DistanceSquaredTo(band.WorldCentre);
        bool casting = distanceSquared >= band.BeginSquared && (band.EndSquared == 0 || distanceSquared < band.EndSquared);
        // Off makes a ShadowsOnly proxy visible in the colour pass. Keep its mode
        // and exclude it from every camera/light layer while outside its band.
        bool proxy = band.Original == GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
        uint layers = proxy && !casting ? 0u : band.Layers;
        if (band.Node.Layers != layers) band.Node.Layers = layers;
        var next = proxy || casting ? band.Original : GeometryInstance3D.ShadowCastingSetting.Off;
        if (next == band.Applied) return;
        band.Node.CastShadow = next;
        band.Applied = next;
    }
}
