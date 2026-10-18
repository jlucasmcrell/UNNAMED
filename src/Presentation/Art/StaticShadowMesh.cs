using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>Position-only depth geometry for runtime imports, like the editor importer builds.
/// The renderer uses it only for materials whose depth pass needs no other vertex attributes.</summary>
public static class StaticShadowMesh
{
    public static void Build(ImporterMesh source)
    {
        if (source.GetBlendShapeCount() != 0 || source.GetMesh().ShadowMesh is not null) return;
        var visible = source.GetMesh();
        var shadow = new ArrayMesh();
        for (int surface = 0; surface < source.GetSurfaceCount(); surface++)
        {
            if (source.GetSurfacePrimitiveType(surface) != Mesh.PrimitiveType.Triangles) return;
            // Match the uploaded mesh, including any importer position quantization.
            using var arrays = visible.SurfaceGetArrays(surface);
            if (arrays[(int)Mesh.ArrayType.Bones].VariantType != Variant.Type.Nil) return;
            var positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var unique = new Dictionary<Vector3, int>();
            var vertices = new List<Vector3>();
            var remap = new int[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                if (!unique.TryGetValue(positions[i], out int index))
                {
                    index = vertices.Count;
                    unique.Add(positions[i], index);
                    vertices.Add(positions[i]);
                }
                remap[i] = index;
            }
            int[] Map(int[] indices) => Array.ConvertAll(indices, i => remap[i]);
            var indices = arrays[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil
                ? Enumerable.Range(0, positions.Length).ToArray() : arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            using var depth = new Godot.Collections.Array();
            depth.Resize((int)Mesh.ArrayType.Max);
            depth[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
            depth[(int)Mesh.ArrayType.Index] = Map(indices);
            using var lods = new Godot.Collections.Dictionary();
            for (int lod = 0; lod < source.GetSurfaceLodCount(surface); lod++)
                lods[source.GetSurfaceLodSize(surface, lod)] = Map(source.GetSurfaceLodIndices(surface, lod));
            shadow.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, depth, lods: lods);
        }
        if (shadow.GetSurfaceCount() > 0) source.GetMesh().ShadowMesh = shadow;
    }
}
