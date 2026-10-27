using Godot;
using System.Text.Json;
namespace UNNAMED.Presentation.Art;

/// <summary>Inputs glTF cannot carry from HumGen's native skin and layered eyes.</summary>
internal static class HumGenMaterials
{
    public static void Apply(Node model, ArtLibrary art, string id)
    {
        if (art.RiggedFile(id, "native_materials.json") is not { } path) return;
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var data = json.RootElement;
        Texture2D Map(string name, string role) => art.Texture(art.RiggedFile(id, data.GetProperty(name).GetString()!)
            ?? throw new InvalidDataException("Native HumGen map missing: " + name), role)
            ?? throw new InvalidDataException("Native HumGen map failed: " + name);
        // Modular native grooms/wardrobe carry their own inputs, without unused
        // copies of the body's skin and eye maps.
        var pbr = data.TryGetProperty("skin_pbr", out _) ? Map("skin_pbr", "data") : null;
        var outerNormal = data.TryGetProperty("cornea_normal", out _) ? Map("cornea_normal", "normal") : null;
        var eyeScatter = data.TryGetProperty("eye_scatter", out _) ? Map("eye_scatter", "data") : null;
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        for (int surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
        {
            var source = mesh.Mesh!.SurfaceGetMaterial(surface) as BaseMaterial3D;
            var current = mesh.GetSurfaceOverrideMaterial(surface);
            if (source is null) continue;
            if (current is ShaderMaterial skin && source.ResourceName.EndsWith("_Skin", StringComparison.OrdinalIgnoreCase))
            {
                skin.SetShaderParameter("native_humgen", true);
                skin.SetShaderParameter("native_specular_map", true);
                skin.SetShaderParameter("native_rough_spec_cavity", pbr ?? throw new InvalidDataException("Native skin PBR map missing"));
                skin.SetShaderParameter("roughness", source.Roughness);
                skin.SetShaderParameter("double_specularity", false);
                // Native cavity is already in the baked colour. ORM red is explicitly constant 1 in HumGen's exporter.
                skin.SetShaderParameter("use_ambient_occlusion", source.AOEnabled && source.AOTexture is not null);
                skin.SetShaderParameter("subsurface_scattering_strength", data.GetProperty("skin_sss_weight").GetSingle() * .5f);
            }
            else if (mesh.Name.ToString().Contains("_Cornea_LOD0", StringComparison.Ordinal))
            {
                var cornea = new ShaderMaterial { Shader = GD.Load<Shader>("res://Art/humgen_cornea.gdshader"), ResourceName = "HumGen_native_cornea" };
                cornea.SetShaderParameter("normal_map", outerNormal ?? throw new InvalidDataException("Native cornea normal missing"));
                mesh.SetSurfaceOverrideMaterial(surface, cornea);
                mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            }
            else if (mesh.Name.ToString().EndsWith("_Eyes_LOD0", StringComparison.Ordinal))
            {
                var eye = new ShaderMaterial { Shader = GD.Load<Shader>("res://Art/humgen_inner_eye.gdshader"), ResourceName = "HumGen_native_inner_eye" };
                eye.SetShaderParameter("color_map", source.AlbedoTexture);
                eye.SetShaderParameter("normal_map", source.NormalTexture);
                eye.SetShaderParameter("scatter_map", eyeScatter ?? throw new InvalidDataException("Native eye scatter missing"));
                eye.SetShaderParameter("roughness", data.GetProperty("eye_roughness").GetSingle());
                eye.SetShaderParameter("metallic", data.GetProperty("eye_metallic").GetSingle());
                mesh.SetSurfaceOverrideMaterial(surface, eye);
            }
            else if (source.ResourceName.Contains("HairCards", StringComparison.OrdinalIgnoreCase)
                || source.ResourceName.Contains("Haircap", StringComparison.OrdinalIgnoreCase))
            {
                var hair = (BaseMaterial3D)source.Duplicate();
                // Keep the native atlas and strand colour; recover fine brow coverage
                // after the native alpha bake rather than painting a replacement brow.
                if (mesh.Name.ToString().Contains("Eyebrow", StringComparison.OrdinalIgnoreCase))
                {
                    var colour = hair.AlbedoColor;
                    hair.AlbedoColor = new Color(colour.R, colour.G, colour.B, colour.A * 1.15f);
                }
                if (hair.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor)
                    hair.Transparency = BaseMaterial3D.TransparencyEnum.AlphaHash;
                string suffix = System.Text.RegularExpressions.Regex.Match(mesh.Name, "_LOD([0-9]+)$") is { Success: true } level && level.Groups[1].Value != "0"
                    ? "_lod" + level.Groups[1].Value : "";
                string flow = "hair_flow" + suffix;
                if (data.TryGetProperty(flow, out _) && source.ResourceName.Contains("_HairCards", StringComparison.OrdinalIgnoreCase))
                {
                    hair.AnisotropyEnabled = true; hair.Anisotropy = .75f;
                    hair.AnisotropyFlowmap = Map(flow, "data");
                }
                mesh.SetSurfaceOverrideMaterial(surface, hair);
            }
        }
    }
}
