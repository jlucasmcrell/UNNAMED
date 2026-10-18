using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Perf;

public partial class StaticShadowMeshCheck : Node
{
    public override void _Ready()
    {
        try
        {
            using var mesh = new ImporterMesh();
            using var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            Vector3[] points = [Vector3.Zero, Vector3.Right, Vector3.Up, Vector3.Zero, Vector3.Up, Vector3.Left];
            arrays[(int)Mesh.ArrayType.Vertex] = points;
            arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[points.Length];
            using var lods = new Godot.Collections.Dictionary { [0.25f] = new[] { 3, 4, 5 } };
            mesh.AddSurface(Mesh.PrimitiveType.Triangles, arrays, lods: lods);
            StaticShadowMesh.Build(mesh);
            var visible = mesh.GetMesh();
            if (visible.ShadowMesh is not { } shadow) throw new Exception("No shadow mesh");
            using var depth = shadow.SurfaceGetArrays(0);
            var vertices = depth[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var indices = depth[(int)Mesh.ArrayType.Index].AsInt32Array();
            if (vertices.Length != 4 || indices.Length != 6) throw new Exception("Vertices not welded");
            for (int i = 0; i < points.Length; i++)
                if (vertices[indices[i]] != points[i]) throw new Exception("Triangle topology changed");
            using var original = visible.SurfaceGetArrays(0);
            if (!original[(int)Mesh.ArrayType.Vertex].AsVector3Array().SequenceEqual(points)
                || original[(int)Mesh.ArrayType.TexUV].AsVector2Array().Length != 6
                || mesh.GetSurfaceLodCount(0) != 1) throw new Exception("Visible mesh changed");
            using var surfaces = RenderingServer.MeshGetSurface(shadow.GetRid(), 0);
            using var shadowLods = surfaces["lods"].AsGodotArray();
            if (shadowLods.Count != 1) throw new Exception("Shadow LOD lost");
            using var lod = shadowLods[0].AsGodotDictionary();
            byte[] bytes = lod["index_data"].AsByteArray();
            if (bytes.Length != 6) throw new Exception("Shadow LOD indices lost");
            int[] expected = [indices[3], indices[4], indices[5]];
            for (int i = 0; i < 3; i++)
                if (BitConverter.ToUInt16(bytes, i * 2) != expected[i]) throw new Exception("Shadow LOD topology changed");
            StaticShadowMesh.Build(mesh);
            if (visible.ShadowMesh != shadow) throw new Exception("Existing shadow replaced");
            using var skinned = new ImporterMesh();
            arrays[(int)Mesh.ArrayType.Bones] = new int[points.Length * 4];
            arrays[(int)Mesh.ArrayType.Weights] = Enumerable.Range(0, points.Length * 4).Select(i => i % 4 == 0 ? 1f : 0f).ToArray();
            skinned.AddSurface(Mesh.PrimitiveType.Triangles, arrays);
            StaticShadowMesh.Build(skinned);
            if (skinned.GetMesh().ShadowMesh is not null) throw new Exception("Skinned import accepted");
            if (OS.GetEnvironment("UNNAMED_SHADOW_CHECK_MODEL") is { Length: > 0 } path)
            {
                using var document = new GltfDocument();
                using var state = new GltfState();
                if (document.AppendFromFile(path, state) != Error.Ok) throw new Exception("Real asset failed to load");
                var imports = state.GetMeshes();
                foreach (var entry in imports)
                {
                    var asset = entry.Mesh;
                    StaticShadowMesh.Build(asset);
                    var full = asset.GetMesh();
                    if (full.ShadowMesh is not { } welded) throw new Exception("Real asset has no shadow mesh");
                    for (int s = 0; s < full.GetSurfaceCount(); s++)
                    {
                        using var a = full.SurfaceGetArrays(s);
                        using var b = welded.SurfaceGetArrays(s);
                        var av = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                        var bv = b[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                        var ai = a[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil
                            ? Enumerable.Range(0, av.Length).ToArray() : a[(int)Mesh.ArrayType.Index].AsInt32Array();
                        var bi = b[(int)Mesh.ArrayType.Index].AsInt32Array();
                        if (ai.Length != bi.Length) throw new Exception("Real asset lost triangles");
                        for (int i = 0; i < ai.Length; i++)
                            if (av[ai[i]] != bv[bi[i]]) throw new Exception("Real asset triangle changed");
                        GD.Print($"STATIC SHADOW REAL ASSET: surface {s}, vertices {av.Length} -> {bv.Length}, exact {ai.Length / 3} triangles");
                    }
                }
                ((Godot.Collections.Array)imports).Dispose();
            }
            GD.Print("STATIC SHADOW MESH CHECK PASS: welded positions, exact triangles, visible UVs, shadow LODs, idempotence");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
}
