using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Perf;

public partial class IndexCacheCheck : Node
{
    public override void _Ready()
    {
        try
        {
            using var synthetic = new ImporterMesh();
            using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
            const int side = 40;
            var points = Enumerable.Range(0, side * side).Select(i => new Vector3(i % side, 0, i / side)).ToArray();
            var triangles = new List<(int, int, int)>();
            for (int z = 0; z < side - 1; z++) for (int x = 0; x < side - 1; x++)
            { int a = z * side + x; triangles.Add((a, a + side, a + 1)); triangles.Add((a + 1, a + side, a + side + 1)); }
            var rng = new Random(9421);
            var indices = triangles.OrderBy(_ => rng.Next()).SelectMany(t => new[] { t.Item1, t.Item2, t.Item3 }).ToArray();
            arrays[(int)Mesh.ArrayType.Vertex] = points;
            arrays[(int)Mesh.ArrayType.Normal] = Enumerable.Repeat(Vector3.Up, points.Length).ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = points.Select(p => new Vector2(p.X, p.Z) / side).ToArray();
            arrays[(int)Mesh.ArrayType.Index] = indices;
            using var lods = new Godot.Collections.Dictionary { [.5f] = indices.Take(indices.Length / 6 * 3).ToArray() };
            synthetic.AddSurface(Mesh.PrimitiveType.Triangles, arrays, lods: lods, material: new StandardMaterial3D(), name: "grid");
            Check(synthetic, "shuffled grid", requireImprovement: true);
            using (var unchanged = synthetic.GetSurfaceArrays(0))
                if (!unchanged[(int)Mesh.ArrayType.Index].AsInt32Array().SequenceEqual(indices))
                    throw new Exception("Optimization mutated its source arrays");
            synthetic.SetSurfaceMaterial(0, new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha });
            using (var blended = IndexCacheMesh.Optimize(synthetic))
            using (var unchanged = blended.GetSurfaceArrays(0))
                if (!unchanged[(int)Mesh.ArrayType.Index].AsInt32Array().SequenceEqual(indices))
                    throw new Exception("Blended triangle order changed");
            if (OS.GetEnvironment("UNNAMED_INDEX_CHECK_MODEL") is { Length: > 0 } path)
            {
                using var doc = new GltfDocument(); using var state = new GltfState();
                if (doc.AppendFromFile(path, state) != Error.Ok) throw new Exception("Could not import real mesh");
                var meshes = state.GetMeshes();
                foreach (var entry in meshes)
                {
                    if (entry.Mesh is not { } mesh) continue;
                    using var bones = new Godot.Collections.Array(); mesh.GenerateLods(60, 0, bones);
                    Check(mesh, System.IO.Path.GetFileName(path));
                }
                ((Godot.Collections.Array)meshes).Dispose();
            }
            arrays[(int)Mesh.ArrayType.Bones] = new int[points.Length * 4];
            arrays[(int)Mesh.ArrayType.Weights] = Enumerable.Repeat(.25f, points.Length * 4).ToArray();
            using var skinned = new ImporterMesh(); skinned.AddSurface(Mesh.PrimitiveType.Triangles, arrays);
            if (IndexCacheMesh.Optimize(skinned) != skinned) throw new Exception("Skinned mesh was changed");
            GD.Print("IndexCacheCheck PASS"); GetTree().Quit();
        }
        catch (Exception e) { GD.PrintErr(e); GetTree().Quit(1); }
    }

    private static void Check(ImporterMesh source, string label, bool requireImprovement = false)
    {
        using var optimized = IndexCacheMesh.Optimize(source);
        if (optimized.GetSurfaceCount() != source.GetSurfaceCount()) throw new Exception("Surface count changed");
        for (int s = 0; s < source.GetSurfaceCount(); s++)
        {
            using var before = source.GetSurfaceArrays(s); using var after = optimized.GetSurfaceArrays(s);
            for (int a = 0; a < (int)Mesh.ArrayType.Max; a++)
                if (a != (int)Mesh.ArrayType.Index && !GD.VarToBytes(before[a]).SequenceEqual(GD.VarToBytes(after[a])))
                    throw new Exception($"Vertex attribute {a} changed");
            if (source.GetSurfaceMaterial(s) != optimized.GetSurfaceMaterial(s)
                || source.GetSurfaceName(s) != optimized.GetSurfaceName(s)
                || source.GetSurfaceFormat(s) != optimized.GetSurfaceFormat(s)
                || source.GetSurfaceLodCount(s) != optimized.GetSurfaceLodCount(s)) throw new Exception("Surface metadata changed");
            for (int lod = -1; lod < source.GetSurfaceLodCount(s); lod++)
            {
                var p = lod < 0 ? before[(int)Mesh.ArrayType.Index].AsInt32Array() : source.GetSurfaceLodIndices(s, lod);
                var q = lod < 0 ? after[(int)Mesh.ArrayType.Index].AsInt32Array() : optimized.GetSurfaceLodIndices(s, lod);
                static string[] Faces(int[] ix) => Enumerable.Range(0, ix.Length / 3)
                    .Select(i => $"{ix[3*i]},{ix[3*i+1]},{ix[3*i+2]}").Order(StringComparer.Ordinal).ToArray();
                if (!Faces(p).SequenceEqual(Faces(q))) throw new Exception("Triangle or winding changed");
                if (lod >= 0 && source.GetSurfaceLodSize(s, lod) != optimized.GetSurfaceLodSize(s, lod))
                    throw new Exception("LOD error threshold changed");
                int Misses(int[] ix)
                {
                    var cache = new LinkedList<int>(); int misses = 0;
                    foreach (int v in ix) { if (!cache.Remove(v)) misses++; cache.AddFirst(v); if (cache.Count > 32) cache.RemoveLast(); }
                    return misses;
                }
                int oldMiss = Misses(p), newMiss = Misses(q);
                GD.Print($"IndexCacheCheck {label} surface={s} lod={lod}: {p.Length/3} identical triangles; simulated LRU32 misses {oldMiss}->{newMiss}");
                if (requireImprovement && newMiss >= oldMiss * .6) throw new Exception("Synthetic cache locality did not improve");
            }
        }
    }
}
