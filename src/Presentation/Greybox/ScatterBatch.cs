using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>Optional 2x2 batching of distant, non-shadowing chunks. Original chunk centres still choose their LOD bands.</summary>
public static class ScatterBatch
{
    public const string GridKey = "scatter_batch_grid";

    public static int Build(Node3D parent)
    {
        // The current world scatter uses world-space instance transforms below an identity root.
        if (parent.GlobalTransform != Transform3D.Identity) return 0;
        var shaders = new Dictionary<Shader, Shader>();
        var eligible = parent.GetChildren().OfType<MultiMeshInstance3D>().Where(n => n.Visible && n.HasMeta(GridKey)
            && n.GetMeta(GridKey).AsVector3I().Z >= 2 && n.Transform == Transform3D.Identity
            && n.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off && n.Multimesh is { UseCustomData: true, VisibleInstanceCount: -1 }
            && n.Multimesh.Mesh is { } mesh && mesh.GetSurfaceCount() == 1
            && mesh.SurfaceGetMaterial(0) is ShaderMaterial { Shader: { } shader }
            && shader.Code.Contains("uniform float thin_from", StringComparison.Ordinal)).ToArray();
        var groups = eligible.GroupBy(n => (Mesh: n.Multimesh.Mesh.GetInstanceId(),
            X: n.GetMeta(GridKey).AsVector3I().X / 2, Z: n.GetMeta(GridKey).AsVector3I().Y / 2,
            Level: n.GetMeta(GridKey).AsVector3I().Z));
        int saved = 0;
        foreach (var group in groups)
        {
            var chunks = group.ToArray();
            if (chunks.Length is < 2 or > 4) continue;
            var first = chunks[0];
            var source = (ShaderMaterial)first.Multimesh.Mesh.SurfaceGetMaterial(0);
            if (!shaders.TryGetValue(source.Shader, out var shader))
            {
                string code = source.Shader.Code.Replace("shader_type spatial;", "shader_type spatial;\n#define SCATTER_BATCH");
                shaders.Add(source.Shader, shader = new Shader { Code = code });
            }
            var material = (ShaderMaterial)source.Duplicate();
            material.Shader = shader;
            var boxes = chunks.Select(n => n.CustomAabb.Size == Vector3.Zero ? n.GetAabb() : n.CustomAabb).ToArray();
            var bounds = boxes.Skip(1).Aggregate(boxes[0], (box, next) => box.Merge(next));
            var centres = boxes.Select(b => b.GetCenter()).ToArray();
            var metadata = new Vector3[4]; centres.CopyTo(metadata, 0);
            material.SetShaderParameter("scatter_centres", metadata);
            material.SetShaderParameter("scatter_band", new Vector2(first.VisibilityRangeBegin, first.VisibilityRangeEnd));
            var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true,
                Mesh = first.Multimesh.Mesh, InstanceCount = chunks.Sum(n => n.Multimesh.InstanceCount) };
            int at = 0;
            for (int c = 0; c < chunks.Length; c++)
            for (int i = 0; i < chunks[c].Multimesh.InstanceCount; i++, at++)
            {
                multi.SetInstanceTransform(at, chunks[c].Multimesh.GetInstanceTransform(i));
                var tint = chunks[c].Multimesh.GetInstanceCustomData(i);
                // Four chunk IDs fit without sacrificing RGB. Decoding the field share loses at most 5e-7.
                tint.A = c + tint.A * .5f;
                multi.SetInstanceCustomData(at, tint);
            }
            float pad = centres.Max(c => c.DistanceTo(bounds.GetCenter()));
            parent.AddChild(new MultiMeshInstance3D { Name = $"batch_{first.Name}", Multimesh = multi, MaterialOverride = material,
                CustomAabb = bounds, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = first.GIMode, LodBias = first.LodBias,
                VisibilityRangeBegin = Math.Max(0, first.VisibilityRangeBegin - pad),
                VisibilityRangeEnd = first.VisibilityRangeEnd == 0 ? 0 : first.VisibilityRangeEnd + pad,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled });
            foreach (var old in chunks) { parent.RemoveChild(old); old.QueueFree(); }
            saved += chunks.Length - 1;
        }
        GD.Print($"UNNAMED scatter batch pilot: {saved} distant draw nodes saved; original placements, chunk bands and near geometry retained");
        return saved;
    }
}
