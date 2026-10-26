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

    /// <summary>
    /// The creator's skin tone and hair colour on a built figure (<see cref="LookSwatches"/>): the skin's colour scaled, the hair cards and cap
    /// dyed like the garments, each on this figure's own copy of the material. The material each surface had before is kept on the mesh, so choosing
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
                    var dyed = Dyed(original, garment switch { "shirt" => shirt, "trousers" => trousers, _ => LookSwatches.Boots });
                    // The rubber's gloss gives the boots away even in leather brown.
                    if (garment == "boots" && dyed is BaseMaterial3D leather)
                    {
                        leather.RoughnessTexture = null;
                        leather.Roughness = .75f;
                    }
                    mesh.SetSurfaceOverrideMaterial(surface, dyed);
                    continue;
                }
                var swatch = isSkin ? skin : hair;
                var drawn = swatch is null ? original : isSkin ? Tinted(original, swatch) : Dyed(original, swatch, lift: .4f);
                // The body's skin texture carries the baked hair's colour painted under the cap; blended, a recoloured cap lets it show
                // through (dark sideburns under blond). Cut out instead, the cap covers it wherever it has hair.
                if (swatch is not null && name.EndsWith("_Haircap", StringComparison.OrdinalIgnoreCase) && drawn is BaseMaterial3D cap)
                {
                    cap.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                    cap.AlphaScissorThreshold = .2f;
                    cap.AlphaAntialiasingMode = BaseMaterial3D.AlphaAntiAliasing.AlphaToCoverage;
                }
                mesh.SetSurfaceOverrideMaterial(surface, drawn);
            }
        }
    }

    /// <summary>Whether <paramref name="model"/> wears a garment the creator's shirt and trouser colours dye.</summary>
    public static bool WearsDyedCloth(Node model) => Materials(model).Any(name => LookSwatches.GarmentKind(name) is "shirt" or "trousers");

    /// <summary>Whether <paramref name="model"/> wears the frontier outfit (HumGen's Pirate set).</summary>
    public static bool WearsFrontier(Node model) => Materials(model).Any(name => name.Contains("_Pirate_", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Materials(Node model) => model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
        .Where(mesh => mesh.Mesh is not null)
        .SelectMany(mesh => Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).Select(s => mesh.Mesh.SurfaceGetMaterial(s)?.ResourceName ?? ""));

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
        var body = model as SkinnedModel ?? model.FindChildren("*", "", true, false).OfType<SkinnedModel>().FirstOrDefault();
        if (body is null)
            return;
        var skeleton = body.Skeleton;
        var worn = skeleton.GetChildren().OfType<MeshInstance3D>().Where(m => m.HasMeta("look_style")).ToList();
        if (worn.FirstOrDefault()?.GetMeta("look_style").AsString() == asset && (asset is not null || worn.Count == 0))
            return;
        foreach (var mesh in worn)
            skeleton.RemoveChild(mesh);
        var style = asset is null ? null : art?.Rigged(asset);
        // The body's own hair is hidden through its level-of-detail choice, which would otherwise show it again at the next band.
        foreach (var own in body.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().Where(IsHead))
            body.OutfitVisible(own, style is null);
        var added = new List<MeshInstance3D>();
        if (style is not null)
        {
            // The hair zone's look (character_materials.json), as the body's own hair had it when the body was built.
            CharacterMaterials.Upgrade(style);
            foreach (var mesh in style.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().ToList())
            {
                mesh.GetParent().RemoveChild(mesh);
                skeleton.AddChild(mesh);
                mesh.Skeleton = mesh.GetPathTo(skeleton);
                mesh.SetMeta("look_style", asset);
                added.Add(mesh);
            }
            style.QueueFree();
        }
        body.SwapParts(worn, added);
        foreach (var mesh in worn)
            mesh.QueueFree();
        // The style's cap and cards carry the face's shapes (brows, jaw): the face drives them with the body's own.
        foreach (var face in model.FindChildren("*", "", true, false).OfType<FaceDriver>())
            face.Rebind(body);
    }

    /// <summary>
    /// The garment in <paramref name="cloth"/>'s colour, its weave kept: the albedo scaled (in linear light) by what takes the texture's mean
    /// colour to the cloth's, the mean read from the texture's smallest mip so nothing large is decompressed.
    /// </summary>
    private static Material Dyed(Material original, Swatch cloth, float lift = .2f)
    {
        if (original.Duplicate() is not BaseMaterial3D copy)
            return original;
        var mean = (copy.AlbedoTexture is { } texture ? MeanColour(texture) : null) ?? Colors.White;
        var tint = copy.AlbedoColor.SrgbToLinear() * mean.SrgbToLinear();
        // Part of the colour (a fifth for cloth) is added under the dye, so parts baked black (the sweater's collar band, the rubber boots,
        // the roots and shadow in hair cards) come out as a darker shade of the same colour instead of staying black.
        var target = new Color(cloth.R, cloth.G, cloth.B).SrgbToLinear() * (1 - lift);
        var dye = new Color(target.R / Math.Max(tint.R, .002f), target.G / Math.Max(tint.G, .002f), target.B / Math.Max(tint.B, .002f)).LinearToSrgb();
        copy.AlbedoColor = new Color(dye.R, dye.G, dye.B, copy.AlbedoColor.A);
        var floor = (new Color(cloth.R, cloth.G, cloth.B).SrgbToLinear() * lift).LinearToSrgb();
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(new Color(floor.R, floor.G, floor.B));
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
}
