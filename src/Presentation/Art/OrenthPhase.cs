// Presentation only: reality never quite registers an Orenth (RACES.md 4). No gameplay state lives here.
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// An Orenth figure's registration lag, drawn by twins of the figure's own skinned meshes on the same skeleton: the shadow
/// is cast by a shadow-only twin that trails the body by an instant (the real meshes stop casting), and running leaves a
/// trail of pale spirit ghosts, each further behind and fainter (<see cref="PeopleLooks.AfterimageLags"/>).
/// </summary>
public partial class OrenthPhase : Node
{
    private const double ShadowLag = .09, History = .4;
    private const float LastLevelDistance = 18f;

    private readonly List<(MeshInstance3D Source, MeshInstance3D Shadow, MeshInstance3D[] Trail, ShaderMaterial[] Spirit)> _twins = new();
    private readonly Queue<(double Time, Vector3 Position)> _path = new();
    private Node3D _body = null!;
    private double _clock;

    public static OrenthPhase Attach(Node3D model)
    {
        var phase = new OrenthPhase { Name = "OrenthPhase", _body = model };
        var spirit = GD.Load<Shader>("res://Art/people_orenth_ghost.gdshader");
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
            var trail = PeopleLooks.AfterimageLags.Select((_, ghost) => Twin("_phase_trail" + ghost)).ToArray();
            var materials = new ShaderMaterial[mesh.Mesh.GetSurfaceCount()];
            for (int surface = 0; surface < materials.Length; surface++)
            {
                var source = mesh.GetSurfaceOverrideMaterial(surface) ?? mesh.Mesh.SurfaceGetMaterial(surface);
                var material = new ShaderMaterial { Shader = spirit };
                if (source is BaseMaterial3D standard)
                {
                    material.SetShaderParameter("base_map", standard.AlbedoTexture);
                    material.SetShaderParameter("base_colour", standard.AlbedoColor);
                }
                else if (source is ShaderMaterial shaded && shaded.GetShaderParameter("texture_albedo").AsGodotObject() is Texture2D albedo)
                {
                    material.SetShaderParameter("base_map", albedo);
                }
                materials[surface] = material;
                foreach (var copy in trail)
                    copy.SetSurfaceOverrideMaterial(surface, material);
            }
            foreach (var copy in trail)
                copy.Visible = false;
            phase._twins.Add((mesh, lagging, trail, materials));
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
        var shadowLag = Behind(ShadowLag) - at;
        var lags = PeopleLooks.AfterimageLags.Select(lag => Behind(lag) - at).ToArray();
        foreach (var (source, lagging, trail, _) in _twins)
        {
            var toLocal = source.GlobalBasis.Inverse();
            lagging.SetInstanceShaderParameter("offset", toLocal * shadowLag);
            for (int ghost = 0; ghost < trail.Length; ghost++)
            {
                float strength = PeopleLooks.AfterimageStrength(speed, ghost);
                trail[ghost].Visible = strength > .005f && source.IsVisibleInTree();
                trail[ghost].SetInstanceShaderParameter("offset", toLocal * lags[ghost]);
                trail[ghost].SetInstanceShaderParameter("strength", strength);
            }
        }
    }

    /// <summary>
    /// A decoy: the figure's pose at this moment, frozen as a spirit ghost for <paramref name="seconds"/>, standing where a
    /// figure at <paramref name="at"/> (the figure's own transform) would stand. The caller decides what it absorbs;
    /// <see cref="OrenthDecoy.Shatter"/> draws the hit. No optional arguments: other branches reach this through Call.
    /// </summary>
    public OrenthDecoy Linger(double seconds, Transform3D at)
    {
        var decoy = new OrenthDecoy { Seconds = seconds };
        foreach (var (source, _, _, spirit) in _twins)
        {
            if (!source.IsVisibleInTree())
                continue;
            var frozen = new MeshInstance3D
            {
                Mesh = source.BakeMeshFromCurrentSkeletonPose(), Transform = ArtLibrary.Relative(_body, source),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            for (int surface = 0; surface < spirit.Length; surface++)
                frozen.SetSurfaceOverrideMaterial(surface, spirit[surface]);
            decoy.AddChild(frozen);
        }
        GetTree().CurrentScene.AddChild(decoy);
        var figure = _body.GetParent() as Node3D ?? _body;
        decoy.GlobalTransform = at * (figure.GlobalTransform.AffineInverse() * _body.GlobalTransform);
        return decoy;
    }
}
