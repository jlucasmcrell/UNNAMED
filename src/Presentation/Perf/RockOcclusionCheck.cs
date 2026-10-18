using System.Reflection;
using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Perf;

public partial class RockOcclusionCheck : Node
{
    public override void _Ready()
    {
        try
        {
            System.Environment.SetEnvironmentVariable("UNNAMED_ROCK_OCCLUSION", "1");
            var build = typeof(ArtLibrary).GetMethod("AddOpaqueOccluders", BindingFlags.NonPublic | BindingFlags.Static)!;
            using var mesh = new ArrayMesh();
            Vector3[] points = [new(-2, 0, 0), new(-2, 3, 0), new(-.5f, 0, 0), new(.5f, 0, 0), new(2, 3, 0), new(2, 0, 0)];
            using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = points;
            arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 3, 4, 5 }; // An intentional doorway-shaped gap.
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            using var solid = new StandardMaterial3D { CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            mesh.SurfaceSetMaterial(0, solid);
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            using var cutout = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor };
            mesh.SurfaceSetMaterial(1, cutout);
            Node3D Root()
            {
                var root = new Node3D();
                var child = new MeshInstance3D { Mesh = mesh, Position = new Vector3(5, 2, 7), RotationDegrees = new Vector3(0, 31, 0) };
                root.AddChild(child); child.Owner = root;
                return root;
            }
            using var root = Root();
            build.Invoke(null, [root, "env_ph_coastal_cliff_01"]);
            var occluder = root.FindChildren("*", "OccluderInstance3D", true, false).OfType<OccluderInstance3D>().Single();
            var shape = (ArrayOccluder3D)occluder.Occluder;
            using var uploaded = mesh.SurfaceGetArrays(0);
            if (!shape.GetVertices().SequenceEqual(uploaded[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                || !shape.GetIndices().SequenceEqual(new[] { 0, 1, 2, 3, 4, 5 }))
                throw new Exception("Opaque triangles changed, or cutout surface/hole was filled");
            if (occluder.Transform != Transform3D.Identity || occluder.Owner != root) throw new Exception("Occluder transform/ownership changed");
            using var packed = new PackedScene();
            if (packed.Pack(root) != Error.Ok) throw new Exception("Could not pack occluder");
            using var clone = packed.Instantiate<Node3D>();
            if (clone.FindChildren("*", "OccluderInstance3D", true, false).Count != 1) throw new Exception("Occluder lost on instancing");
            solid.CullMode = BaseMaterial3D.CullModeEnum.Back;
            using var oneSided = Root(); build.Invoke(null, [oneSided, "env_ph_coastal_cliff_01"]);
            if (oneSided.FindChildren("*", "OccluderInstance3D", true, false).Count != 0) throw new Exception("One-sided rock became a two-sided occluder");
            solid.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            using var unrelated = Root(); build.Invoke(null, [unrelated, "unknown_open_mesh"]);
            if (unrelated.FindChildren("*", "OccluderInstance3D", true, false).Count != 0) throw new Exception("Unreviewed geometry was included");
            root.Free(); clone.Free(); oneSided.Free(); unrelated.Free();
            GD.Print("ROCK OCCLUSION CHECK PASS: exact opaque triangles, retained hole, alpha excluded, two-sided guard, transform, packing, narrow asset scope");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PrintErr(e); GetTree().Quit(1); }
    }
}
