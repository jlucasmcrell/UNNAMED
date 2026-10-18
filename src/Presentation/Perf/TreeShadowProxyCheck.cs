using Godot;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Greybox;

namespace UNNAMED.Presentation.Perf;

public partial class TreeShadowProxyCheck : Node3D
{
    public override void _Ready()
    {
        try
        {
            WindField.Register();
            using var box = new BoxMesh();
            using var arrays = box.SurfaceGetArrays(0);
            using var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            using var shader = new Shader { Code = WindField.TreeShader };
            using var material = new ShaderMaterial { Shader = shader };
            mesh.SurfaceSetMaterial(0, material);
            var source = new MeshInstance3D { Mesh = mesh, Position = new Vector3(4, 0, 3), LodBias = 1,
                CustomAabb = new Aabb(new Vector3(-1,0,-1), new Vector3(2,2,2)), Layers = 4,
                VisibilityRangeBegin = 5, VisibilityRangeEnd = 25 };
            AddChild(source);
            source.SetInstanceShaderParameter("tree_tint", new Color(.8f,1.1f,.7f));
            source.SetInstanceShaderParameter("tree_height", 13f);
            source.SetInstanceShaderParameter("stiffness", 1.3f);
            source.SetInstanceShaderParameter("ow_late", 7f);
            var bands = new ShadowLods(); bands.Watch(this);
            var near = source.GlobalPosition + Vector3.Back * 10;
            bands.Update(near);
            var proxy = TreeShadowProxy.Add(source, 8) ?? throw new Exception("Tree proxy was not created");
            void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
            Require(proxy.Mesh == source.Mesh && proxy.GetActiveMaterial(0) == material, "Geometry or wind/alpha material changed");
            Require(proxy.Transform == Transform3D.Identity && proxy.GetParent() == source && proxy.CustomAabb == source.CustomAabb
                && proxy.Layers == source.Layers && proxy.GIMode == GeometryInstance3D.GIModeEnum.Disabled, "Transform, bounds, layers or GI changed");
            Require(source.LodBias == 1 && proxy.LodBias == .125f, "Visible geometry LOD was changed");
            foreach (string parameter in new[] { "tree_tint", "tree_height", "stiffness", "ow_late" })
                Require(proxy.GetInstanceShaderParameter(parameter).Equals(source.GetInstanceShaderParameter(parameter)), "Wind/tint parameter lost: " + parameter);
            bands.Update(near);
            Require(source.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off
                && proxy.CastShadow == GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, "Duplicate or missing near shadow");
            bands.Update(source.GlobalPosition + Vector3.Back * 40);
            Require(source.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off
                && proxy.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off, "Proxy ignored the authored far band");
            bands.Update(near);
            Require(source.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off
                && proxy.CastShadow == GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, "Watcher restored the expensive original caster");
            Require(TreeShadowProxy.Add(source, 8) is null, "A second proxy was created");
            bands.Unwatch(); source.Free();
            GD.Print("TREE SHADOW PROXY CHECK PASS: same visible mesh/LOD, alpha/wind/tint, identity transform, bounds/layers, native shadow-only mode and watched range transitions");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PrintErr(e); GetTree().Quit(1); }
    }
}
