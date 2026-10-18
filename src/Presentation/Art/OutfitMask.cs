using Godot;

namespace UNNAMED.Presentation.Art;

public static partial class Outfit
{
    private const string UnmaskedMesh = "outfit_unmasked_mesh";

    // StandardMaterial3D (hair, underwear, engine skin) cannot read the shader's
    // instance masks. Filter topology once when dressing; Clear restores the
    // shared resource. Godot's native mesh serialization retains packed vertex,
    // skin and morph buffers exactly, without re-quantizing decoded normals.
    internal static void MaskMesh(MeshInstance3D instance, int regions, int sources)
    {
        if (instance.Mesh is not ArrayMesh original || (regions | sources) == 0)
            return;
        var surfaces = original.Get("_surfaces").AsGodotArray().Duplicate(true);
        bool changed = false;
        for (int surface = 0; surface < original.GetSurfaceCount(); surface++)
        {
            var arrays = original.SurfaceGetArrays(surface);
            var uv = arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
            var hidden = uv.Select(v =>
            {
                int region = Math.Clamp((int)(v.X + 0.5f), 0, 30);
                int flags = v.Y < 0 ? (int)(-v.Y - 1 + 0.5f) : 0;
                return (regions & (1 << region)) != 0 || (sources & flags) != 0;
            }).ToArray();
            if (original.SurfaceGetPrimitiveType(surface) != Mesh.PrimitiveType.Triangles || !hidden.Any(h => h))
                continue;
            var raw = surfaces[surface].AsGodotDictionary();
            int count = raw["vertex_count"].AsInt32();
            int stride = count <= 65536 ? 2 : 4;
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            if (indices.Length == 0)
                indices = Enumerable.Range(0, count).ToArray();
            indices = KeepTriangles(indices, hidden);
            raw["index_data"] = PackIndices(indices, stride);
            raw["index_count"] = indices.Length;
            raw["format"] = (ulong)raw["format"].AsInt64() | (ulong)Mesh.ArrayFormat.FormatIndex;
            if (raw.TryGetValue("lods", out var levels))
            {
                var lods = levels.AsGodotArray();
                for (int level = 1; level < lods.Count; level += 2)
                {
                    var bytes = lods[level].AsByteArray();
                    var at = Enumerable.Range(0, bytes.Length / stride).Select(i => stride == 2
                        ? (int)BitConverter.ToUInt16(bytes, i * stride) : (int)BitConverter.ToUInt32(bytes, i * stride)).ToArray();
                    lods[level] = PackIndices(KeepTriangles(at, hidden), stride);
                }
            }
            changed = true;
        }
        if (!changed)
            return;
        var masked = (ArrayMesh)original.Duplicate();
        masked.ClearSurfaces();
        masked.Set("_surfaces", surfaces);
        instance.SetMeta(UnmaskedMesh, original);
        var morphs = Enumerable.Range(0, instance.GetBlendShapeCount()).Select(instance.GetBlendShapeValue).ToArray();
        instance.Mesh = masked;
        for (int shape = 0; shape < morphs.Length; shape++)
            instance.SetBlendShapeValue(shape, morphs[shape]);
    }

    private static byte[] PackIndices(int[] indices, int stride)
    {
        var bytes = new byte[indices.Length * stride];
        for (int i = 0; i < indices.Length; i++)
        {
            if (stride == 2)
                BitConverter.TryWriteBytes(bytes.AsSpan(i * stride), checked((ushort)indices[i]));
            else
                BitConverter.TryWriteBytes(bytes.AsSpan(i * stride), indices[i]);
        }
        return bytes;
    }

    private static int[] KeepTriangles(int[] indices, bool[] hidden)
    {
        var kept = new List<int>(indices.Length);
        for (int i = 0; i < indices.Length; i += 3)
            if (!hidden[indices[i]] && !hidden[indices[i + 1]] && !hidden[indices[i + 2]])
                kept.AddRange(indices.AsSpan(i, 3));
        // Empty indices mean UNINDEXED to Godot. Retain surface/material
        // numbering with a degenerate triangle when a whole surface is hidden.
        return kept.Count == 0 ? new[] { 0, 0, 0 } : kept.ToArray();
    }
}
