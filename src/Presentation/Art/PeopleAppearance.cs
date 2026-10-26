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
        if (people is null or "people.veth")
            return;
        if (people == PeopleLooks.Kal)
        {
            // The body's own slate maps and its age (kal_stone.json, written with the maps from the body's export).
            if (art.RiggedFile(modelId, "kal_stone_rgbh.png") is { } colourPath && art.RiggedFile(modelId, "kal_stone_rm.png") is { } maskPath
                && art.RiggedFile(modelId, "kal_stone.json") is { } agePath
                && art.Texture(colourPath, "albedo") is { } colour && art.Texture(maskPath, "data") is { } mask)
            {
                using var age = System.Text.Json.JsonDocument.Parse(File.ReadAllText(agePath));
                KalStone(model, colour, mask, PeopleLooks.StoneCoverage(age.RootElement.GetProperty("age").GetInt32()));
            }
            if (art.RiggedFile(modelId, "kal_wings.glb") is { } wings
                && model.FindChildren("*", nameof(Skeleton3D), true, false).OfType<Skeleton3D>().FirstOrDefault() is { } skeleton)
                KalWings.Attach(skeleton, wings);
            return;
        }
        if (people == PeopleLooks.Siann)
        {
            // Only bodies made for the look carry its mask: a Siann on any other body (Sel Arien's voiced head) keeps her own look.
            if (art.RiggedFile(modelId, "siann_skin.png") is { } siannPath && art.Texture(siannPath, "data") is { } mask)
            {
                SiannSkin(model, mask);
                SiannEyes(model);
            }
            return;
        }
        if (people == PeopleLooks.Orenth)
        {
            if (model is Node3D body)
                OrenthPhase.Attach(body);
            return;
        }
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
                else if (people == "people.mor")
                {
                    if (GD.Load<Shader>("res://Art/people_phase.gdshader") is not { } shader)
                        continue;
                    var material = new ShaderMaterial { Shader = shader, ResourceName = name + "_" + people };
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

    /// <summary>
    /// The Kal calcification layer on every HumanShaders skin surface: the body's own baked slate (<paramref name="colour"/>: rgb + height)
    /// and its joint/age mask (<paramref name="mask"/>: r roughness, g mask, b bare skin). Clothing, hair, eyes and teeth are other materials.
    /// </summary>
    public static void KalStone(Node model, Texture2D colour, Texture2D mask, float coverage)
    {
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        {
            if (mesh.Mesh is null) continue;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if ((mesh.GetSurfaceOverrideMaterial(surface) ?? mesh.Mesh.SurfaceGetMaterial(surface)) is not ShaderMaterial skin
                    || !skin.ResourceName.EndsWith("_HumanShaders", StringComparison.Ordinal))
                    continue;
                var local = (ShaderMaterial)skin.Duplicate();
                local.SetShaderParameter("use_stone", true);
                local.SetShaderParameter("stone_rgbh", colour);
                local.SetShaderParameter("stone_rm", mask);
                local.SetShaderParameter("stone_coverage", coverage);
                mesh.SetSurfaceOverrideMaterial(surface, local);
            }
        }
    }

    /// <summary>
    /// The Siann opal skin on every HumanShaders skin surface: pearl pallor, mother-of-pearl sheen and silver-lavender traces
    /// from the body's own mask (<paramref name="mask"/>: r sheen, g thin areas), with stronger, softer subsurface light.
    /// </summary>
    public static void SiannSkin(Node model, Texture2D mask)
    {
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        {
            if (mesh.Mesh is null) continue;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if ((mesh.GetSurfaceOverrideMaterial(surface) ?? mesh.Mesh.SurfaceGetMaterial(surface)) is not ShaderMaterial skin
                    || !skin.ResourceName.EndsWith("_HumanShaders", StringComparison.Ordinal))
                    continue;
                var local = (ShaderMaterial)skin.Duplicate();
                local.SetShaderParameter("use_opal", true);
                local.SetShaderParameter("opal_mask", mask);
                local.SetShaderParameter("subsurface_scattering_strength", .6f);
                mesh.SetSurfaceOverrideMaterial(surface, local);
            }
        }
    }

    private static readonly Dictionary<ulong, Texture2D> SiannIrises = new(), SiannIrisGlow = new();

    /// <summary>The HumGen eye surfaces with their iris recoloured pale lavender around a brighter inner ring, a cool pearl sclera and a soft glow.</summary>
    public static void SiannEyes(Node model)
    {
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        {
            if (mesh.Mesh is null) continue;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if ((mesh.GetSurfaceOverrideMaterial(surface) ?? mesh.Mesh.SurfaceGetMaterial(surface)) is not BaseMaterial3D eye
                    || !eye.ResourceName.Contains("_Eyes", StringComparison.Ordinal) || eye.AlbedoTexture is not { } source)
                    continue;
                if (!SiannIrises.TryGetValue(source.GetRid().Id, out var iris) || !SiannIrisGlow.TryGetValue(source.GetRid().Id, out var glow))
                {
                    var image = source.GetImage();
                    if (image.IsCompressed())
                        image.Decompress();
                    image.ClearMipmaps();
                    image.Convert(Image.Format.Rgba8);
                    int w = image.GetWidth(), h = image.GetHeight();
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            float u = (x + .5f) / w - .5f, v = (y + .5f) / h - .5f;
                            var c = image.GetPixel(x, y);
                            var (r, g, b, blend) = PeopleLooks.SiannIris(MathF.Sqrt(u * u + v * v), c.Luminance);
                            if (blend > 0)
                                image.SetPixel(x, y, c.Lerp(new Color(r, g, b, c.A), blend));
                        }
                    // the glow is the iris alone: a lit sclera turns the whole eye blank white
                    var lit = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            float u = (x + .5f) / w - .5f, v = (y + .5f) / h - .5f, radius = MathF.Sqrt(u * u + v * v);
                            lit.SetPixel(x, y, radius is > .05f and < .105f ? image.GetPixel(x, y) : Colors.Black);
                        }
                    image.GenerateMipmaps();
                    lit.GenerateMipmaps();
                    iris = SiannIrises[source.GetRid().Id] = ImageTexture.CreateFromImage(image);
                    glow = SiannIrisGlow[source.GetRid().Id] = ImageTexture.CreateFromImage(lit);
                }
                var local = (BaseMaterial3D)eye.Duplicate();
                local.AlbedoTexture = iris;
                // the soft glow of a Siann's eyes
                local.EmissionEnabled = true;
                local.Emission = new Color(.72f, .68f, .95f);
                local.EmissionTexture = glow;
                local.EmissionEnergyMultiplier = .25f;
                // a softer cornea: the eye kind's mirror clearcoat reflects the sky across the whole pale iris
                local.Clearcoat = .3f;
                local.ClearcoatRoughness = .15f;
                mesh.SetSurfaceOverrideMaterial(surface, local);
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
