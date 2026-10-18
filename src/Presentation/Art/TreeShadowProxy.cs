using Godot;

namespace UNNAMED.Presentation.Art;

// Optional pilot: retain full visible geometry and use native index LODs only for shadows.
internal static class TreeShadowProxy
{
    internal static MeshInstance3D? Add(MeshInstance3D source, float pixels)
    {
        if (source.Mesh is not ArrayMesh mesh || source.Skin is not null || mesh.GetBlendShapeCount() != 0
            || source.CastShadow != GeometryInstance3D.ShadowCastingSetting.On) return null;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            if (source.GetActiveMaterial(s) is not ShaderMaterial { Shader: { } shader }
                || !shader.Code.Contains("instance uniform float tree_height", StringComparison.Ordinal)) return null;
        var proxy = new MeshInstance3D
        {
            Name = "TreeShadowLOD", Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled, LodBias = 1 / pixels,
            CustomAabb = source.CustomAabb, ExtraCullMargin = source.ExtraCullMargin, Layers = source.Layers,
            VisibilityRangeBegin = source.VisibilityRangeBegin, VisibilityRangeEnd = source.VisibilityRangeEnd,
            VisibilityRangeBeginMargin = source.VisibilityRangeBeginMargin, VisibilityRangeEndMargin = source.VisibilityRangeEndMargin,
            VisibilityRangeFadeMode = source.VisibilityRangeFadeMode,
        };
        for (int s = 0; s < mesh.GetSurfaceCount(); s++) proxy.SetSurfaceOverrideMaterial(s, source.GetActiveMaterial(s));
        foreach (string parameter in new[] { "tree_tint", "tree_height", "stiffness", "ow_late" })
            proxy.SetInstanceShaderParameter(parameter, source.GetInstanceShaderParameter(parameter));
        proxy.SetMeta("tree_shadow_lod_pixels", pixels);
        source.AddChild(proxy); // Identity local transform retains the exact wind origin and parent visibility.
        ShadowLods.SetMode(source, GeometryInstance3D.ShadowCastingSetting.Off);
        return proxy;
    }
}
