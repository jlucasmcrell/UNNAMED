// Presentation only: peoples' surfaces over unchanged approved MetaHuman anatomy.
using Godot;

namespace UNNAMED.Presentation.Art;

public static class PeopleAppearance
{
    public static void EnsureSex(Node model, string? sex)
    {
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
            for (Node? at = mesh; at is not null && at != model; at = at.GetParent())
                if (!Ui.Models.CharacterSex.AllowsGroom(sex, at.Name.ToString())) { mesh.Visible = false; break; }
    }

    public static void Apply(Node model, string? people, ArtLibrary art, string modelId)
    {
        // The approved body master also carries the player's beard. Native NPC identities use their own grooms.
        if (modelId.StartsWith("mh_npc_", StringComparison.Ordinal) || modelId.StartsWith("mh_resident_", StringComparison.Ordinal))
            foreach (var mesh in model.FindChildren("mh_veth_m_beard*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
                mesh.Visible = false;
        ApplyBodyColour(model, art, modelId);
        if (people is null or "people.veth" or "people.kal")
            return;
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        {
            if (mesh.Mesh is null)
                continue;
            if (people == "people.mor")
                mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            if (mesh.Name.ToString().EndsWith("_shadow", StringComparison.Ordinal))
            {
                if (people == "people.mor")
                    mesh.Visible = false;
                continue;
            }
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.Mesh.SurfaceGetMaterial(surface) is not BaseMaterial3D source)
                    continue;
                string name = source.ResourceName;
                bool skin = name.EndsWith("_skin", StringComparison.Ordinal);
                bool eye = name.EndsWith("_eye", StringComparison.Ordinal);
                if ((people is "people.constructed" or "people.ondrek") && name.EndsWith("_hair", StringComparison.Ordinal))
                {
                    mesh.SetSurfaceOverrideMaterial(surface, new StandardMaterial3D
                    {
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(0, 0, 0, 0),
                    });
                }
                else if (people == "people.vaskaal" && eye)
                {
                    mesh.SetSurfaceOverrideMaterial(surface, Reflective(new Color(.18f, .23f, .25f), .65f, .07f));
                }
                else if (people == "people.constructed" && (skin || eye))
                {
                    var material = Reflective(eye ? new Color(.004f, .004f, .006f) : new Color(.67f, .7f, .73f),
                        eye ? 0f : 1f, eye ? .08f : .12f);
                    KeepBaseLayer(material, source);
                    mesh.SetSurfaceOverrideMaterial(surface, material);
                }
                else if (people == "people.ondrek" && (skin || name.EndsWith("_stone", StringComparison.Ordinal)))
                {
                    if (GD.Load<Shader>("res://Art/people_stone.gdshader") is not { } shader)
                        continue;
                    var material = new ShaderMaterial { Shader = shader, ResourceName = name + "_geology" };
                    if (art?.WorldMaps("ter_ph_lichen_rock") is { Normal: not null, Orm: not null } stone)
                    {
                        material.SetShaderParameter("stone_colour", stone.Albedo);
                        material.SetShaderParameter("stone_normal", stone.Normal);
                        material.SetShaderParameter("stone_orm", stone.Orm);
                        material.SetShaderParameter("use_scanned_stone", true);
                    }
                    material.SetShaderParameter("stone_tiles", name.EndsWith("_head_skin", StringComparison.Ordinal) ? 1.2f : 5.5f);
                    KeepBaseLayer(material, source);
                    mesh.SetSurfaceOverrideMaterial(surface, material);
                }
                else if (people == "people.ondrek" && eye)
                {
                    mesh.SetSurfaceOverrideMaterial(surface, Reflective(new Color(.21f, .30f, .32f), .3f, .2f));
                }
                else if ((people == "people.siann" && skin) || people is "people.orenth" or "people.mor")
                {
                    if (GD.Load<Shader>("res://Art/people_phase.gdshader") is not { } shader)
                        continue;
                    var material = new ShaderMaterial { Shader = shader, ResourceName = name + "_" + people };
                    material.SetShaderParameter("mode", people == "people.siann" ? 0 : people == "people.orenth" ? 1 : 2);
                    material.SetShaderParameter("head_surface", name.EndsWith("_head_skin", StringComparison.Ordinal));
                    material.SetShaderParameter("base_map", source.AlbedoTexture);
                    material.SetShaderParameter("base_colour", source.AlbedoColor);
                    if (people == "people.mor")
                        mesh.SetSurfaceOverrideMaterial(surface, material);
                    else
                    {
                        var baseMaterial = mesh.GetSurfaceOverrideMaterial(surface) ?? source;
                        // Duplicate per figure: changing a shared imported material would affect unrelated people.
                        var local = (Material)baseMaterial.Duplicate();
                        local.NextPass = material;
                        mesh.SetSurfaceOverrideMaterial(surface, local);
                    }
                }
            }
        }
    }

    // Optional identity/people colour layer over the unchanged approved body and its normal maps.
    private static void ApplyBodyColour(Node model, ArtLibrary art, string modelId)
    {
        if (art.RiggedFile(modelId, "body_skin_colour.png") is not { } path
            || art.Texture(path, "albedo") is not { } colour)
            return;
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        {
            if (mesh.Mesh is null) continue;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.Mesh.SurfaceGetMaterial(surface)?.ResourceName.EndsWith("_body_skin", StringComparison.Ordinal) != true)
                    continue;
                var material = mesh.GetSurfaceOverrideMaterial(surface) ?? mesh.Mesh.SurfaceGetMaterial(surface);
                if (material is ShaderMaterial skin)
                {
                    var local = (ShaderMaterial)skin.Duplicate();
                    local.SetShaderParameter("texture_albedo", colour);
                    mesh.SetSurfaceOverrideMaterial(surface, local);
                }
                else if (material is BaseMaterial3D source)
                {
                    var local = (BaseMaterial3D)source.Duplicate();
                    local.AlbedoTexture = colour;
                    mesh.SetSurfaceOverrideMaterial(surface, local);
                }
            }
        }
    }

    private static ShaderMaterial Reflective(Color colour, float metal, float rough)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Art/people_reflective.gdshader") };
        material.SetShaderParameter("colour", colour);
        material.SetShaderParameter("metal", metal);
        material.SetShaderParameter("rough", rough);
        return material;
    }

    private static void KeepBaseLayer(ShaderMaterial material, BaseMaterial3D source)
    {
        if (!source.ResourceName.EndsWith("_body_skin", StringComparison.Ordinal))
            return;
        material.SetShaderParameter("base_map", source.AlbedoTexture);
        material.SetShaderParameter("base_colour", source.AlbedoColor);
        material.SetShaderParameter("preserve_base_layer", true);
    }
}
