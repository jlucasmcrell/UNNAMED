using Godot;
using UNNAMED.Presentation.Greybox;

namespace UNNAMED.Presentation.Perf;

public partial class TreeMaterialsCheck : Node3D
{
    private static void Require(bool ok, string why)
    {
        if (!ok) throw new InvalidOperationException(why);
    }

    public override void _Ready()
    {
        try
        {
            var source = new StandardMaterial3D { AlbedoColor = Colors.White };
            var mesh = new BoxMesh { Material = source };
            (Node3D Root, MeshInstance3D Mesh) Tree()
            {
                var root = new Node3D();
                var instance = new MeshInstance3D { Mesh = mesh };
                AddChild(root); root.AddChild(instance);
                return (root, instance);
            }
            var a = Tree(); var b = Tree();
            Color tintA = new(.8f, 1.1f, .9f), tintB = new(1.15f, .9f, .85f);
            Palette.Windblown(a.Root, 1.3f, tintA);
            Palette.Windblown(b.Root, 2f, tintB);
            Require(a.Mesh.GetSurfaceOverrideMaterial(0).GetInstanceId() == b.Mesh.GetSurfaceOverrideMaterial(0).GetInstanceId(), "Identical source materials are not shared");
            Require(a.Mesh.GetInstanceShaderParameter("tree_tint").AsColor() == tintA && b.Mesh.GetInstanceShaderParameter("tree_tint").AsColor() == tintB, "Per-tree tints lost");
            Require(a.Mesh.GetInstanceShaderParameter("stiffness").AsSingle() == 1.3f && b.Mesh.GetInstanceShaderParameter("stiffness").AsSingle() == 2f, "Per-tree stiffness lost");
            Palette.Late(a.Root, 7f);
            Require(a.Mesh.GetInstanceShaderParameter("ow_late").AsSingle() == 7f && b.Mesh.GetInstanceShaderParameter("ow_late").AsSingle() == 0f, "Old Late changed a neighbouring tree");
            Require(source.AlbedoColor == Colors.White, "Source material was tinted");

            // Non-white factors retain their original pre-conversion colour product.
            mesh = new BoxMesh { Material = new StandardMaterial3D { AlbedoColor = new Color(.4f, .6f, .8f) } };
            var c = Tree(); var d = Tree();
            Palette.Windblown(c.Root, 1f, tintA); Palette.Windblown(d.Root, 1f, tintB);
            Require(c.Mesh.GetSurfaceOverrideMaterial(0).GetInstanceId() != d.Mesh.GetSurfaceOverrideMaterial(0).GetInstanceId(), "Non-white colour fallback unexpectedly shares a tint");
            Require(((ShaderMaterial)c.Mesh.GetSurfaceOverrideMaterial(0)).GetShaderParameter("albedo_colour").AsColor() == new Color(.4f, .6f, .8f) * tintA, "Non-white colour product changed");
            GD.Print("TREE MATERIAL CHECK PASS: shared resources, independent tint/stiffness/Late, source and non-white colours preserved");
            GetTree().Quit(0);
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
}
