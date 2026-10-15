// UNNAMED Presentation - garments worn on a MetaHuman body: layered, refitted meshes on the body's own skeleton (character pipeline 4.1)
// Godot presentation only: no gameplay state lives here (D-11)

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// An outfit: garments refitted onto a body by the character pipeline (asset_work/metahuman/tools/bl_garment.py), each a rigged model on
/// the same game skeleton with a sidecar <c>&lt;id&gt;_garment.json</c> giving its layer (CHARACTER_ANIMATION_STRATEGY.md 4.3: 1 under-layer
/// to 6 cloak) and <c>hides_mask</c>, the body regions it covers as bits of the region IDs. Its meshes join the body's skeleton; the body,
/// and every garment on a lower layer, then drop the triangles of the regions an outer garment covers (region_mask.gdshaderinc), so
/// neither can poke through it whatever the pose.
/// </summary>
public static class Outfit
{
    private const string ClothShaderPath = "res://Art/cloth.gdshader";
    private static Shader? _cloth;

    /// <summary>Dress <paramref name="figure"/> in <paramref name="garments"/> (rigged asset IDs); returns how many were put on.</summary>
    public static int Dress(ArtLibrary art, SkinnedModel figure, IReadOnlyList<string> garments)
    {
        if (garments.Count == 0)
            return 0;
        var body = figure.Skeleton.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>().ToList();
        var worn = new List<(List<MeshInstance3D> Meshes, int Layer, int Hides)>();
        foreach (string id in garments)
        {
            if (art.RiggedFile(id, id + "_garment.json") is not { } spec)
            {
                art.Report(id, $"garment without {id}_garment.json (its layer and the regions it covers); not worn", id);
                continue;
            }
            if (art.Rigged(id) is not { } model)
                continue;
            using var json = JsonDocument.Parse(File.ReadAllText(spec));
            int layer = json.RootElement.GetProperty("layer").GetInt32();
            int hides = json.RootElement.GetProperty("hides_mask").GetInt32();
            CharacterMaterials.ApplyLods(model);
            var meshes = model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>().ToList();
            foreach (var mesh in meshes)
            {
                mesh.Owner = null;
                mesh.GetParent().RemoveChild(mesh);
                figure.Skeleton.AddChild(mesh);
                mesh.Transform = Transform3D.Identity;
                mesh.Skeleton = new NodePath("..");
                for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
                {
                    if (Cloth(mesh.Mesh.SurfaceGetMaterial(s)) is { } cloth)
                        mesh.SetSurfaceOverrideMaterial(s, cloth);
                }
            }
            model.Free();
            worn.Add((meshes, layer, hides));
        }
        int all = worn.Aggregate(0, (mask, g) => mask | g.Hides);
        foreach (var mesh in body)
            mesh.SetInstanceShaderParameter("region_hidden_mask", all);
        foreach (var (meshes, layer, _) in worn)
        {
            int over = worn.Where(g => g.Layer > layer).Aggregate(0, (mask, g) => mask | g.Hides);
            foreach (var mesh in meshes)
                mesh.SetInstanceShaderParameter("region_hidden_mask", over);
        }
        return worn.Count;
    }

    /// <summary>A garment surface's imported glTF material as the cloth shader (it carries the region mask); null for anything else.</summary>
    private static ShaderMaterial? Cloth(Material? source)
    {
        if (source is not BaseMaterial3D imported)
            return null;
        _cloth ??= GD.Load<Shader>(ClothShaderPath);
        var m = new ShaderMaterial { ResourceName = imported.ResourceName, Shader = _cloth };
        m.SetShaderParameter("albedo", imported.AlbedoColor);
        m.SetShaderParameter("roughness", imported.Roughness);
        m.SetShaderParameter("metallic", imported.Metallic);
        if (imported.AlbedoTexture is { } albedo)
            m.SetShaderParameter("texture_albedo", albedo);
        if (imported.RoughnessTexture is { } orm)
            m.SetShaderParameter("texture_orm", orm);
        if (imported.NormalEnabled && imported.NormalTexture is { } normal)
        {
            m.SetShaderParameter("texture_normal", normal);
            m.SetShaderParameter("normal_strength", imported.NormalScale);
        }
        if (imported.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor)
            m.SetShaderParameter("alpha_scissor", imported.AlphaScissorThreshold);
        return m;
    }
}
