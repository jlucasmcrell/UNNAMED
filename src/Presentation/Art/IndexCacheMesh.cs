using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>Complete the runtime import's vertex-cache optimization without changing any triangle or vertex attribute.</summary>
public static class IndexCacheMesh
{
    public static ImporterMesh Optimize(ImporterMesh source, bool alphaTestedShader = false)
    {
        if (source.GetBlendShapeCount() != 0) return source;
        for (int s = 0; s < source.GetSurfaceCount(); s++)
        {
            using var arrays = source.GetSurfaceArrays(s);
            if (arrays[(int)Mesh.ArrayType.Bones].VariantType != Variant.Type.Nil) return source;
        }
        var result = new ImporterMesh { ResourceName = source.ResourceName };
        for (int s = 0; s < source.GetSurfaceCount(); s++)
        {
            using var original = source.GetSurfaceArrays(s);
            using var arrays = original.Duplicate();
            var primitive = source.GetSurfacePrimitiveType(s);
            // Blended surfaces can depend on triangle submission order. Keep their authored order.
            bool orderIndependent = alphaTestedShader || source.GetSurfaceMaterial(s) is null
                || source.GetSurfaceMaterial(s) is BaseMaterial3D { Transparency:
                    BaseMaterial3D.TransparencyEnum.Disabled or BaseMaterial3D.TransparencyEnum.AlphaScissor };
            using var lods = new Godot.Collections.Dictionary();
            var points = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            int[] Order(int[] indices) => orderIndependent && primitive == Mesh.PrimitiveType.Triangles && indices.Length >= 3
                ? OrderForCache(points, indices) : indices;
            for (int lod = 0; lod < source.GetSurfaceLodCount(s); lod++)
                lods[source.GetSurfaceLodSize(s, lod)] = Order(source.GetSurfaceLodIndices(s, lod));
            if (arrays[(int)Mesh.ArrayType.Index].VariantType != Variant.Type.Nil)
                arrays[(int)Mesh.ArrayType.Index] = Order(arrays[(int)Mesh.ArrayType.Index].AsInt32Array());
            result.AddSurface(primitive, arrays, lods: lods, material: source.GetSurfaceMaterial(s),
                name: source.GetSurfaceName(s), flags: source.GetSurfaceFormat(s));
        }
        return result;
    }

    private static int[] OrderForCache(Vector3[] points, int[] indices)
    {
        // SurfaceTool exposes the engine's meshoptimizer cache pass; ImporterMesh's
        // editor-only optimize_indices() is not exposed to runtime scripts.
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = points;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        using var tool = new SurfaceTool();
        tool.CreateFromArrays(arrays);
        tool.OptimizeIndicesForCache();
        using var ordered = tool.CommitToArrays();
        return ordered[(int)Mesh.ArrayType.Index].AsInt32Array();
    }
}
