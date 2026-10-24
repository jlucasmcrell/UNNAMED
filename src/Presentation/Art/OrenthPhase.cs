// Presentation only: reality never quite registers an Orenth (RACES.md 4). No gameplay state lives here.
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// Drives an Orenth figure's registration failures, each a twin of the figure's own skinned meshes on the same skeleton:
/// the shadow is cast by a shadow-only twin that trails the body by an instant (the real meshes stop casting); edges
/// briefly double in short bursts, rarer and fainter at rest; and fast movement leaves a faint secondary silhouette.
/// Phase scars and the missing iris segment are surface changes, set once by <see cref="Scar"/>.
/// </summary>
public partial class OrenthPhase : Node
{
    private const double ShadowLag = .09, TrailLag = .15, History = .4;
    private const float LastLevelDistance = 18f;

    private readonly List<(MeshInstance3D Source, MeshInstance3D Shadow, MeshInstance3D Edge, MeshInstance3D Trail)> _twins = new();
    private readonly Queue<(double Time, Vector3 Position)> _path = new();
    private Node3D _body = null!;
    private RandomNumberGenerator _random = new();
    private double _clock, _nextBurst, _burstEnd;
    private Vector3 _burstOffset;

    /// <summary>Which failures are drawn (the review shows them one at a time); all by default.</summary>
    public bool EdgeDoubling { get; set; } = true;
    public bool LaggingShadow { get; set; } = true;
    public bool Afterimage { get; set; } = true;

    public static OrenthPhase Attach(Node3D model, string seed)
    {
        var phase = new OrenthPhase { Name = "OrenthPhase", _body = model };
        phase._random.Seed = (ulong)seed.GetHashCode();
        var ghost = GD.Load<Shader>("res://Art/people_orenth_ghost.gdshader");
        var shadow = new ShaderMaterial { Shader = GD.Load<Shader>("res://Art/people_orenth_shadow.gdshader") };
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().ToList())
        {
            if (mesh.Mesh is null || mesh.Skin is null || !mesh.Visible || mesh.VisibilityRangeBegin >= LastLevelDistance)
                continue;
            MeshInstance3D Twin(string suffix)
            {
                var twin = new MeshInstance3D
                {
                    Name = mesh.Name + suffix, Mesh = mesh.Mesh, Skin = mesh.Skin, Transform = mesh.Transform,
                    VisibilityRangeBegin = mesh.VisibilityRangeBegin, VisibilityRangeEnd = mesh.VisibilityRangeEnd, Layers = mesh.Layers,
                    CustomAabb = mesh.CustomAabb, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                mesh.GetParent().AddChild(twin);
                twin.Skeleton = twin.GetPathTo(mesh.GetNode(mesh.Skeleton));
                return twin;
            }
            var lagging = Twin("_phase_shadow");
            lagging.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
            lagging.MaterialOverride = shadow;
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            var edge = Twin("_phase_edge");
            var trail = Twin("_phase_trail");
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                var source = mesh.GetSurfaceOverrideMaterial(surface) ?? mesh.Mesh.SurfaceGetMaterial(surface);
                var material = new ShaderMaterial { Shader = ghost };
                if (source is BaseMaterial3D standard)
                {
                    material.SetShaderParameter("base_map", standard.AlbedoTexture);
                    material.SetShaderParameter("base_colour", standard.AlbedoColor);
                }
                else if (source is ShaderMaterial shaded && shaded.GetShaderParameter("texture_albedo").AsGodotObject() is Texture2D albedo)
                {
                    material.SetShaderParameter("base_map", albedo);
                }
                edge.SetSurfaceOverrideMaterial(surface, material);
                trail.SetSurfaceOverrideMaterial(surface, material);
            }
            phase._twins.Add((mesh, lagging, edge, trail));
        }
        model.AddChild(phase);
        return phase;
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        var at = _body.GlobalPosition;
        _path.Enqueue((_clock, at));
        while (_path.Count > 2 && _path.Peek().Time < _clock - History)
            _path.Dequeue();
        Vector3 Behind(double lag)
        {
            var target = _clock - lag;
            var previous = _path.Peek();
            foreach (var sample in _path)
            {
                if (sample.Time >= target)
                    return previous.Time >= target ? sample.Position : previous.Position.Lerp(sample.Position, (float)((target - previous.Time) / Math.Max(sample.Time - previous.Time, 1e-6)));
                previous = sample;
            }
            return at;
        }
        var oldest = _path.Peek();
        float speed = _clock - oldest.Time > .05 ? (at - oldest.Position).Length() / (float)(_clock - oldest.Time) : 0f;

        // Edge doubling: a short stutter of two or three flickers, every few seconds in motion and rarely at rest.
        bool moving = speed > .4f;
        if (_clock >= _nextBurst)
        {
            _burstEnd = _clock + _random.RandfRange(.12f, .2f);
            _nextBurst = _burstEnd + (moving ? _random.RandfRange(1.2f, 3.2f) : _random.RandfRange(4.5f, 9f));
            float turn = _random.RandfRange(0, Mathf.Tau);
            _burstOffset = new Vector3(Mathf.Cos(turn), _random.RandfRange(-.15f, .25f), Mathf.Sin(turn)) * _random.RandfRange(.025f, .045f);
        }
        float edge = EdgeDoubling && _clock < _burstEnd && Math.Sin((_burstEnd - _clock) * 90.0) > -.3 ? (moving ? .55f : .3f) : 0f;
        float trail = Afterimage ? Mathf.Clamp((speed - 2.6f) / 2.4f, 0f, 1f) * .18f : 0f;
        var shadowLag = LaggingShadow ? Behind(ShadowLag) - at : Vector3.Zero;
        var trailLag = Behind(TrailLag) - at;
        foreach (var (source, lagging, edgeTwin, trailTwin) in _twins)
        {
            var toLocal = source.GlobalBasis.Inverse();
            lagging.SetInstanceShaderParameter("offset", toLocal * shadowLag);
            edgeTwin.Visible = edge > 0 && source.IsVisibleInTree();
            edgeTwin.SetInstanceShaderParameter("offset", toLocal * _burstOffset);
            edgeTwin.SetInstanceShaderParameter("strength", edge);
            trailTwin.Visible = trail > .005f && source.IsVisibleInTree();
            trailTwin.SetInstanceShaderParameter("offset", toLocal * trailLag);
            trailTwin.SetInstanceShaderParameter("strength", trail);
        }
    }

    /// <summary>
    /// Phase scars: textureless skin from the body's scar mask, and for an older walker the extra scars and one iris that is
    /// missing a segment. Only skin drawn by HumanShaders and the HumGen eye surface are touched.
    /// </summary>
    public static void Scar(Node model, Texture2D mask, bool older)
    {
        // Drawn after the eye's own (transparent) surface, which would otherwise cover it.
        var iris = older ? new ShaderMaterial { Shader = GD.Load<Shader>("res://Art/people_orenth_iris.gdshader"), RenderPriority = 1 } : null;
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        {
            if (mesh.Mesh is null)
                continue;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                var material = mesh.GetSurfaceOverrideMaterial(surface) ?? mesh.Mesh.SurfaceGetMaterial(surface);
                if (material is ShaderMaterial skin && skin.ResourceName.EndsWith("_HumanShaders", StringComparison.Ordinal))
                {
                    var local = (ShaderMaterial)skin.Duplicate();
                    local.SetShaderParameter("use_phase_scars", true);
                    local.SetShaderParameter("scar_mask", mask);
                    local.SetShaderParameter("scar_age", older ? 1f : 0f);
                    mesh.SetSurfaceOverrideMaterial(surface, local);
                }
                else if (iris is not null && material is not null && material.ResourceName.Contains("_Eyes", StringComparison.Ordinal))
                {
                    var local = (Material)material.Duplicate();
                    local.NextPass = iris;
                    mesh.SetSurfaceOverrideMaterial(surface, local);
                }
            }
        }
    }
}
