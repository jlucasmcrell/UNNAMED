// Presentation only: reality never quite registers an Orenth (RACES.md 4). No gameplay state lives here.
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// An Orenth figure's registration lag, drawn by twins of the figure's own skinned meshes on the same skeleton: the shadow
/// is cast by a shadow-only twin that trails the body by an instant (the real meshes stop casting), and running leaves a
/// faint secondary silhouette that trails a little further (<see cref="PeopleLooks.AfterimageStrength"/>).
/// </summary>
public partial class OrenthPhase : Node
{
    private const double ShadowLag = .09, TrailLag = .15, History = .4;
    private const float LastLevelDistance = 18f;

    private readonly List<(MeshInstance3D Source, MeshInstance3D Shadow, MeshInstance3D Trail)> _twins = new();
    private readonly Queue<(double Time, Vector3 Position)> _path = new();
    private Node3D _body = null!;
    private double _clock;

    public static OrenthPhase Attach(Node3D model)
    {
        var phase = new OrenthPhase { Name = "OrenthPhase", _body = model };
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
            var trail = Twin("_phase_trail");
            trail.Visible = false;
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
                trail.SetSurfaceOverrideMaterial(surface, material);
            }
            phase._twins.Add((mesh, lagging, trail));
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
        float trail = PeopleLooks.AfterimageStrength(speed);
        var shadowLag = Behind(ShadowLag) - at;
        var trailLag = Behind(TrailLag) - at;
        foreach (var (source, lagging, trailTwin) in _twins)
        {
            var toLocal = source.GlobalBasis.Inverse();
            lagging.Visible = source.IsVisibleInTree();
            lagging.SetInstanceShaderParameter("offset", toLocal * shadowLag);
            trailTwin.Visible = trail > .005f && source.IsVisibleInTree();
            trailTwin.SetInstanceShaderParameter("offset", toLocal * trailLag);
            trailTwin.SetInstanceShaderParameter("strength", trail);
        }
    }
}
