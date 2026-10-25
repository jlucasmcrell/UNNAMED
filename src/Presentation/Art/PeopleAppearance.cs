// Presentation only: peoples' surfaces over unchanged approved MetaHuman anatomy.
using Godot;
using UNNAMED.Domain.Character;

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

    /// <summary>
    /// The creator's skin tone and hair colour on a built figure (<see cref="LookSwatches"/>): the skin's colour scaled, a colour added over
    /// the hair cards, each on this figure's own copy of the material. The material each surface had before is kept on the mesh, so choosing
    /// again starts from it rather than from the last tint, and "as made" puts it back.
    /// </summary>
    public static void ApplyLook(Node model, string? people, CharacterAppearance? look, ArtLibrary? art = null, string? bodyModel = null)
    {
        int? Index(string slider) => look is not null && look.Sliders.TryGetValue(slider, out int value) ? value : null;
        WearHairStyle(model, art, LookSwatches.HairStyles(bodyModel).ElementAtOrDefault(Index(LookSwatches.StyleSlider) ?? 0).Asset);
        var skin = LookSwatches.Chosen(LookSwatches.Skin(people), Index(LookSwatches.SkinSlider));
        var hair = LookSwatches.Chosen(LookSwatches.Hair(people), Index(LookSwatches.HairSlider));
        var shirt = LookSwatches.Cloth(LookSwatches.Shirts, Index(LookSwatches.ShirtSlider));
        var trousers = LookSwatches.Cloth(LookSwatches.Trousers, Index(LookSwatches.TrouserSlider));
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        {
            if (mesh.Mesh is null)
                continue;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                string name = WithoutLod(mesh.Mesh.SurfaceGetMaterial(surface)?.ResourceName ?? string.Empty);
                bool isSkin = name.EndsWith("_Skin", StringComparison.OrdinalIgnoreCase);
                bool isHair = HairSurfaces.Any(h => name.EndsWith(h, StringComparison.OrdinalIgnoreCase));
                string? garment = isSkin || isHair ? null : LookSwatches.GarmentKind(name);
                if (!isSkin && !isHair && garment is null)
                    continue;
                string key = $"look_base_{surface}";
                if (!mesh.HasMeta(key))
                    mesh.SetMeta(key, (GodotObject?)mesh.GetSurfaceOverrideMaterial(surface) ?? mesh.Mesh.SurfaceGetMaterial(surface));
                var original = mesh.GetMeta(key).As<Material>();
                if (garment is not null)
                {
                    mesh.SetSurfaceOverrideMaterial(surface, Dyed(original, garment switch { "shirt" => shirt, "trousers" => trousers, _ => LookSwatches.Boots }));
                    continue;
                }
                var swatch = isSkin ? skin : hair;
                mesh.SetSurfaceOverrideMaterial(surface, swatch is null ? original : isSkin ? Tinted(original, swatch) : Coloured(original, swatch));
            }
        }
    }

    private static readonly string[] HairSurfaces = { "_HairCards", "_FaceHairCards", "_Haircap" };

    private static string WithoutLod(string name) =>
        name.LastIndexOf("_LOD", StringComparison.Ordinal) is int at and >= 0 && at + 4 < name.Length && name[(at + 4)..].All(char.IsAsciiDigit) ? name[..at] : name;

    private static bool IsHead(MeshInstance3D mesh) => mesh.Mesh is { } m && m.GetSurfaceCount() > 0
        && HairSurfaces.Any(h => WithoutLod(m.SurfaceGetMaterial(0)?.ResourceName ?? "").EndsWith(h, StringComparison.OrdinalIgnoreCase)) && mesh.Name.ToString().Contains("Hair", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A hairstyle other than the body's own: the style's rigged model loaded once, its hair meshes moved onto this body's skeleton (the same
    /// HumGen rig, bound by bone name) and the body's own hair hidden; none puts the body's own back. A style already worn is left alone.
    /// </summary>
    private static void WearHairStyle(Node model, ArtLibrary? art, string? asset)
    {
        if (model.FindChildren("*", nameof(Skeleton3D), true, false).OfType<Skeleton3D>().FirstOrDefault() is not { } skeleton)
            return;
        var worn = skeleton.GetChildren().OfType<MeshInstance3D>().Where(m => m.HasMeta("look_style")).ToList();
        if (worn.FirstOrDefault()?.GetMeta("look_style").AsString() == asset && (asset is not null || worn.Count == 0))
            return;
        foreach (var mesh in worn)
        {
            skeleton.RemoveChild(mesh);
            mesh.QueueFree();
        }
        var style = asset is null ? null : art?.Rigged(asset);
        foreach (var own in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().Where(IsHead))
            own.Visible = style is null;
        if (style is null)
            return;
        CharacterMaterials.ApplyLods(style);
        foreach (var mesh in style.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().ToList())
        {
            mesh.GetParent().RemoveChild(mesh);
            skeleton.AddChild(mesh);
            mesh.Skeleton = mesh.GetPathTo(skeleton);
            mesh.SetMeta("look_style", asset);
        }
        style.QueueFree();
    }

    /// <summary>
    /// The garment in <paramref name="cloth"/>'s colour, its weave kept: the albedo scaled (in linear light) by what takes the texture's mean
    /// colour to the cloth's, the mean read from the texture's smallest mip so nothing large is decompressed.
    /// </summary>
    private static Material Dyed(Material original, Swatch cloth)
    {
        if (original.Duplicate() is not BaseMaterial3D copy)
            return original;
        var mean = (copy.AlbedoTexture is { } texture ? MeanColour(texture) : null) ?? Colors.White;
        var tint = copy.AlbedoColor.SrgbToLinear() * mean.SrgbToLinear();
        // A fifth of the cloth's colour is added under the dye, so parts baked black (the sweater's collar band, the rubber boots) come out
        // as a darker shade of the same cloth instead of staying black.
        var target = new Color(cloth.R, cloth.G, cloth.B).SrgbToLinear() * .8f;
        var dye = new Color(target.R / Math.Max(tint.R, .002f), target.G / Math.Max(tint.G, .002f), target.B / Math.Max(tint.B, .002f)).LinearToSrgb();
        copy.AlbedoColor = new Color(dye.R, dye.G, dye.B, copy.AlbedoColor.A);
        var lift = (new Color(cloth.R, cloth.G, cloth.B).SrgbToLinear() * .2f).LinearToSrgb();
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(new Color(lift.R, lift.G, lift.B));
        copy.DetailEnabled = true;
        copy.DetailBlendMode = BaseMaterial3D.BlendModeEnum.Add;
        copy.DetailAlbedo = ImageTexture.CreateFromImage(image);
        return copy;
    }

    /// <summary>
    /// A cloth texture's mean colour from its last mip of at least 64 pixels, the near-black padding between its UV islands left out (it
    /// would darken the mean and over-brighten the dye), or null when it holds no image.
    /// </summary>
    private static Color? MeanColour(Texture2D texture)
    {
        if (texture.GetImage() is not { } image || image.IsEmpty())
            return null;
        int level = 0;
        while (level < image.GetMipmapCount() && Math.Min(image.GetWidth(), image.GetHeight()) >> (level + 1) >= 64)
            level++;
        var data = image.GetData();
        int end = level < image.GetMipmapCount() ? (int)image.GetMipmapOffset(level + 1) : data.Length;
        var small = Image.CreateFromData(Math.Max(1, image.GetWidth() >> level), Math.Max(1, image.GetHeight() >> level), false, image.GetFormat(),
            data[(int)image.GetMipmapOffset(level)..end]);
        if (small.IsCompressed())
            small.Decompress();
        small.Convert(Image.Format.Rgba8);
        if (small.GetWidth() > 64)
            small.Resize(64, 64 * small.GetHeight() / small.GetWidth());
        float r = 0, g = 0, b = 0;
        int n = 0;
        for (int y = 0; y < small.GetHeight(); y++)
            for (int x = 0; x < small.GetWidth(); x++)
            {
                var c = small.GetPixel(x, y);
                if (c.A < .5f || c.R + c.G + c.B < .06f)
                    continue;
                r += c.R; g += c.G; b += c.B; n++;
            }
        return n == 0 ? null : new Color(r / n, g / n, b / n);
    }

    private static Material Tinted(Material original, Swatch tone)
    {
        var copy = (Material)original.Duplicate();
        if (copy is ShaderMaterial shader)
        {
            var albedo = original is ShaderMaterial source ? source.GetShaderParameter("albedo").AsColor() : Colors.White;
            shader.SetShaderParameter("albedo", new Color(albedo.R * tone.R, albedo.G * tone.G, albedo.B * tone.B, albedo.A));
        }
        else if (copy is BaseMaterial3D material)
            material.AlbedoColor = new Color(material.AlbedoColor.R * tone.R, material.AlbedoColor.G * tone.G, material.AlbedoColor.B * tone.B, material.AlbedoColor.A);
        return copy;
    }

    private static Material Coloured(Material original, Swatch colour)
    {
        if (original.Duplicate() is not BaseMaterial3D copy)
            return original;
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(new Color(colour.R, colour.G, colour.B));
        copy.DetailEnabled = true;
        copy.DetailBlendMode = BaseMaterial3D.BlendModeEnum.Add;
        copy.DetailAlbedo = ImageTexture.CreateFromImage(image);
        return copy;
    }
}
