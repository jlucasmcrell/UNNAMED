using Godot;
using System.Text.Json;
using UNNAMED.Domain.Character;
using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Art;

/// <summary>Native kept-key appearance on the same meshes in preview and gameplay.</summary>
public sealed partial class HumGenCreatorAppearance : Node
{
    private HumGenCreatorData _data = null!;
    private CreatorCatalogue _catalogue = null!;
    private readonly List<(MeshInstance3D Mesh, Dictionary<string, int> Keys)> _meshes = new();
    private string? _face;
    private HumGenEyeFitData? _eyeFit;
    private readonly Dictionary<CreatorChoice, string?> _parts = new();

    public static bool Apply(Node model, string? people, CharacterAppearance? look, ArtLibrary art, string? id)
    {
        if (id?.StartsWith("humgen_creator_", StringComparison.Ordinal) != true) return false;
        var controller = model.FindChildren("*", "", true, false).OfType<HumGenCreatorAppearance>().FirstOrDefault();
        if (controller is null)
        {
            string json = Godot.FileAccess.GetFileAsString("res://Art/humgen_creator.json");
            controller = new HumGenCreatorAppearance { Name = "NativeCreatorAppearance", _data = new(json), _catalogue = CreatorCatalogue.Read(json) };
            if (art.RiggedFile(id, "eye_fit.json") is { } eyeFit) controller._eyeFit = new(File.ReadAllText(eyeFit));
            foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
            {
                if (mesh.Mesh is not ArrayMesh array) continue;
                var keys = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < array.GetBlendShapeCount(); i++)
                {
                    string name = array.GetBlendShapeName(i);
                    if (name.StartsWith("f_", StringComparison.Ordinal) || name.StartsWith("b_", StringComparison.Ordinal) || name.StartsWith("s_", StringComparison.Ordinal)) keys[name] = i;
                }
                controller._meshes.Add((mesh, keys));
            }
            model.AddChild(controller);
        }
        string sex = id.EndsWith("female", StringComparison.Ordinal) ? "f" : "m";
        people ??= "people.veth";
        look ??= new CharacterAppearance(controller._catalogue.Race(people)?.MidStatureMm ?? 1750, controller._data.FirstFace(sex), null, null, null, null);
        controller.Parts(model, art, id, look, people, sex);
        var weights = controller._data.Weights(look, controller._catalogue, people, sex);
        foreach (var (mesh, keys) in controller._meshes)
        foreach (var (key, index) in keys)
            mesh.SetBlendShapeValue(index, weights.GetValueOrDefault(key));
        if (controller._eyeFit is { } nativeEyes)
        {
            var fit = nativeEyes.Fit(weights);
            foreach (var (mesh, keys) in controller._meshes)
                if (keys.TryGetValue("s_eye_radius", out int radius)) mesh.SetBlendShapeValue(radius, fit.Radius);
            static Vector3 Vector(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);
            if (ArtLibrary.Find<Skeleton3D>(model) is { } eyeRig)
            {
                eyeRig.SetMeta("creator_eye_offset_LeftEye", Vector(fit.Left));
                eyeRig.SetMeta("creator_eye_offset_RightEye", Vector(fit.Right));
            }
        }
        string face = look.Face ?? controller._data.FirstFace(sex);
        // The face chooses HumGen's authored texture set.  The creator's own colours are
        // re-applied with it so a live slider cannot leave a stale shared material on a
        // different preview.
        controller.Materials(art, face, look, people);
        controller._face = face;
        controller.People(model, people, look, art, sex);
        var nativeBase = controller._data.Preset(controller._catalogue.Options(CreatorChoice.Face, sex)
            .First(f => f.Label == "Caucasian 2").Id);
        if (model is SkinnedFigure figure) figure.SetCreatorStature(look.StatureMm, nativeBase.GetProperty("height").GetProperty("set").GetSingle() * 10);
        return true;
    }

    /// <summary>
    /// The peoples' own treatments over the native preset set: their iris, a Kal's calcification and
    /// folding wings, the Orenth registration lag.  Hair, brows, lashes and skin colour are the
    /// creator's material pass.  Attached once: this runs again on every look change.
    /// </summary>
    private void People(Node model, string people, CharacterAppearance look, ArtLibrary art, string sex)
    {
        if (people == PeopleLooks.Siann) PeopleAppearance.SiannEyes(model);
        else if (people == PeopleLooks.Kal)
        {
            PeopleAppearance.KalEyes(model);
            // The Kal's wings are exported beside the race body, on the same HumGen rig vocabulary.
            if (ArtLibrary.Find<Skeleton3D>(model) is { } skeleton
                && !skeleton.GetChildren().OfType<KalWings>().Any()
                && art.RiggedFile("humgen_race_kal_" + sex + "_compact", "kal_wings.glb") is { } wings)
                KalWings.Attach(skeleton, wings);
        }
        else if (people == PeopleLooks.Orenth && model is Node3D body && !model.FindChildren("*", "", true, false).OfType<OrenthPhase>().Any())
            OrenthPhase.Attach(body);
        // The creator keeps HumGen's own baked iris per preset: the Siann and Kal irises replace it in the
        // close-up eye's shader, and the others may lighten or darken it.
        float lightness = Slider(look, "eyes.lightness", 1f);
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
            for (int surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
            {
                if (!Exported(mesh, surface).Contains("_Eyes", StringComparison.Ordinal)) continue;
                var drawn = mesh.GetSurfaceOverrideMaterial(surface);
                if (drawn is ShaderMaterial inner)
                {
                    inner.SetShaderParameter("iris_lightness", people is PeopleLooks.Siann or PeopleLooks.Kal ? 1f : lightness);
                    inner.SetShaderParameter("iris_recolour", people is PeopleLooks.Siann or PeopleLooks.Kal);
                    inner.SetShaderParameter("sclera_pale", people == PeopleLooks.Siann ? .75f : 0f);
                    if (people == PeopleLooks.Siann)
                    {
                        inner.SetShaderParameter("iris_base", new Color(.44f, .43f, .48f));
                        inner.SetShaderParameter("iris_ring", new Color(.12f, .12f, .13f));
                        inner.SetShaderParameter("iris_shade", new Vector2(.65f, .5f));
                        inner.SetShaderParameter("pupil_radius", .038f);
                        inner.SetShaderParameter("pupil_colour", new Color(.02f, .02f, .03f));
                    }
                    else if (people == PeopleLooks.Kal)
                    {
                        inner.SetShaderParameter("iris_base", new Color(.82f, .5f, .16f));
                        inner.SetShaderParameter("iris_ring", new Color(.14f, .1f, .04f));
                        inner.SetShaderParameter("iris_shade", new Vector2(.55f, .7f));
                        inner.SetShaderParameter("pupil_radius", .05f);
                        inner.SetShaderParameter("pupil_colour", new Color(.02f, .015f, .01f));
                    }
                }
                else if (drawn is BaseMaterial3D eye && people is not (PeopleLooks.Siann or PeopleLooks.Kal) && !Mathf.IsEqualApprox(lightness, 1f))
                {
                    var local = (BaseMaterial3D)eye.Duplicate();
                    local.AlbedoColor = new Color(lightness, lightness, lightness, local.AlbedoColor.A);
                    mesh.SetSurfaceOverrideMaterial(surface, local);
                }
            }
    }

    private void Materials(ArtLibrary art, string face, CharacterAppearance look, string people)
    {
        string manifest = art.RiggedFile("humgen_creator_materials", face + "/materials.json")
            ?? throw new InvalidDataException("Native preset materials missing: " + face);
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var parts = document.RootElement.GetProperty("parts");
        var preset = _data.Preset(face);
        foreach (var (mesh, _) in _meshes)
        for (int surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
        {
            // A modular groom or garment carries its own atlases; the preset set is the body's.
            if (mesh.HasMeta("creator_part_kind")) continue;
            string materialName = Exported(mesh, surface);
            string part = parts.EnumerateObject().Select(p => p.Name).FirstOrDefault(p =>
                materialName.EndsWith("_" + p, StringComparison.OrdinalIgnoreCase)) ?? "";
            if (!parts.TryGetProperty(part, out var entry)) continue;
            var maps = entry.GetProperty("maps");
            Texture2D? Map(string name, string role) => maps.TryGetProperty(name, out var relative)
                ? art.Texture(art.RiggedFile("humgen_creator_materials", face + "/" + relative.GetString())
                    ?? throw new InvalidDataException("Native material map missing"), role) : null;
            var material = (Material)Origin(mesh, surface).Duplicate();
            if (material is ShaderMaterial shader)
            {
                bool skin = part == "Skin";
                if (Map("BaseColor", "color") is { } color) shader.SetShaderParameter(skin ? "texture_albedo" : "color_map", color);
                if (Map("Normal", "normal") is { } normal) shader.SetShaderParameter(skin ? "texture_normal" : "normal_map", normal);
                if (skin && Map("MetallicRoughness", "data") is { } roughness)
                {
                    shader.SetShaderParameter("texture_roughness", roughness);
                    shader.SetShaderParameter("roughness", 1f);
                }
            }
            else if (material is BaseMaterial3D native)
            {
                if (Map("BaseColor", "color") is { } color) native.AlbedoTexture = color;
                if (Map("Normal", "normal") is { } normal) { native.NormalEnabled = true; native.NormalTexture = normal; }
                if (Map("MetallicRoughness", "data") is { } roughness) { native.RoughnessTexture = roughness; native.Roughness = 1f; }
            }
            mesh.SetSurfaceOverrideMaterial(surface, material);
        }
        foreach (var (mesh, _) in _meshes)
        for (int surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
            Colour(mesh, surface, preset, look, people);
    }

    /// <summary>
    /// The creator's live colours over the native texture set a surface already carries: skin tone,
    /// redness and saturation, the hair and the brows/lashes apart, a Siann's chalk-white opal skin and
    /// white hair, a Kal's calcification.  A colour correction, never a replacement texture.
    /// </summary>
    private static void Colour(MeshInstance3D mesh, int surface, JsonElement preset, CharacterAppearance look, string people)
    {
        // A modular groom starts from its own export on every pass; the body's surfaces from the preset set they were just given.
        var material = mesh.HasMeta("creator_part_kind") ? Origin(mesh, surface) : mesh.GetSurfaceOverrideMaterial(surface) ?? Origin(mesh, surface);
        string name = Exported(mesh, surface);
        if (material is ShaderMaterial skin && name.EndsWith("_Skin", StringComparison.OrdinalIgnoreCase))
        {
            var local = (ShaderMaterial)skin.Duplicate();
            if (people == PeopleLooks.Siann)
            {
                // Chalk white with a cool pearl sheen, the same opal the races wear in play.
                local.SetShaderParameter("use_opal", true);
                local.SetShaderParameter("opal_procedural_mask", true);
                local.SetShaderParameter("opal_pale", .9f);
                local.SetShaderParameter("opal_grain", .05f);
                local.SetShaderParameter("opal_protect_garment", true);
                local.SetShaderParameter("opal_warm", .3f);
                local.SetShaderParameter("opal_sheen", .5f);
                local.SetShaderParameter("opal_traces", 0f);
                local.SetShaderParameter("native_tone", 1.18f);
                local.SetShaderParameter("native_redness", -.35f);
                local.SetShaderParameter("native_creator_saturation", .3f);
                local.SetShaderParameter("subsurface_scattering_strength", .6f);
            }
            else
            {
                var skinPreset = preset.GetProperty("skin");
                local.SetShaderParameter("native_tone", Ratio(Slider(look, "skin.tone", skinPreset.GetProperty("tone").GetSingle()),
                    skinPreset.GetProperty("tone").GetSingle(), .35f, 2.2f));
                local.SetShaderParameter("native_redness", Slider(look, "skin.redness", skinPreset.GetProperty("redness").GetSingle())
                    - skinPreset.GetProperty("redness").GetSingle());
                local.SetShaderParameter("native_creator_saturation", Ratio(Slider(look, "skin.saturation", skinPreset.GetProperty("saturation").GetSingle()),
                    skinPreset.GetProperty("saturation").GetSingle(), .3f, 1.8f));
            }
            if (people == PeopleLooks.Kal)
            {
                local.SetShaderParameter("use_stone", true);
                local.SetShaderParameter("stone_procedural", true);
                var tint = LookSwatches.Chosen(LookSwatches.Skin(people), look.Sliders.TryGetValue(LookSwatches.SkinSlider, out int stoneColour) ? stoneColour : null);
                local.SetShaderParameter("stone_tint", tint is null ? Vector3.One : new Vector3(tint.R, tint.G, tint.B));
                local.SetShaderParameter("stone_coverage", Mathf.Clamp(LookSwatches.StoneCoverage(
                    look.Sliders.TryGetValue(LookSwatches.StoneSlider, out int level) ? level : null), 0f, 1f));
            }
            mesh.SetSurfaceOverrideMaterial(surface, local);
            return;
        }
        if (material is not BaseMaterial3D groom) return;
        if (!name.EndsWith("_Haircap", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("_HairCards", StringComparison.OrdinalIgnoreCase)) return;
        bool face = mesh.Name.ToString().Contains("Eyebrow", StringComparison.OrdinalIgnoreCase)
            || mesh.Name.ToString().Contains("Eyelash", StringComparison.OrdinalIgnoreCase);
        if (people == PeopleLooks.Siann)
        {
            // White hair at every age, the brows and lashes an ash a tone darker than it.
            bool cap = !face && name.EndsWith("_Haircap", StringComparison.OrdinalIgnoreCase);
            var pearl = new ShaderMaterial { Shader = GD.Load<Shader>(cap ? "res://Art/siann_white_hair.gdshader" : "res://Art/siann_groom.gdshader"), ResourceName = groom.ResourceName };
            pearl.SetShaderParameter("strand_atlas", groom.AlbedoTexture);
            if (cap)
            {
                pearl.SetShaderParameter("strand_colour", new Vector3(1f, 1f, 1f));
                pearl.SetShaderParameter("strand_roughness", .6f);
                pearl.SetShaderParameter("grain_range", new Vector2(.6f, .96f));
            }
            else if (face) pearl.SetShaderParameter("strand_colour", new Color(.6f, .59f, .56f));
            else
            {
                pearl.SetShaderParameter("strand_colour", new Color(1f, 1f, 1f));
                pearl.SetShaderParameter("grain_range", new Vector2(.62f, 1f));
                pearl.SetShaderParameter("strand_roughness", .35f);
            }
            mesh.SetSurfaceOverrideMaterial(surface, pearl);
            return;
        }
        var hair = preset.GetProperty("hair").GetProperty(face ? "eyebrows" : "regular_hair");
        float native = hair.GetProperty("lightness").GetSingle();
        string slider = face ? "brows." : "hair.";
        float tone = Ratio(Slider(look, slider + "lightness", native), native, .35f, 3f);
        float redness = Slider(look, slider + "redness", hair.GetProperty("redness").GetSingle()) - hair.GetProperty("redness").GetSingle();
        var local2 = (BaseMaterial3D)groom.Duplicate();
        local2.AlbedoColor = new Color(tone * (1f + redness * .5f), tone, tone * (1f - redness * .35f), local2.AlbedoColor.A);
        mesh.SetSurfaceOverrideMaterial(surface, local2);
    }

    private void Parts(Node model, ArtLibrary art, string id, CharacterAppearance look, string people, string sex)
    {
        var body = model as SkinnedModel ?? model.FindChildren("*", "", true, false).OfType<SkinnedModel>().FirstOrDefault();
        if (body is null) return;
        string face = look.Face ?? _data.FirstFace(sex);
        foreach (var choice in new[] { CreatorChoice.Hair, CreatorChoice.FacialHair, CreatorChoice.Outfit, CreatorChoice.Footwear })
        {
            string? selected = choice switch
            {
                CreatorChoice.Hair => look.Hair, CreatorChoice.FacialHair => look.FacialHair,
                CreatorChoice.Outfit => look.Outfit, _ => look.Footwear,
            };
            // A Siann's hair is long and white whatever head the preset face was authored with.
            if (choice == CreatorChoice.Hair && people == PeopleLooks.Siann && look.Hair is null) selected = "hair.long.medium_center_part";
            selected ??= _data.PresetPart(face, choice, _catalogue, sex);
            if (choice == CreatorChoice.Hair && look.Sliders.GetValueOrDefault("look.native_bald") != 0) selected = null;
            if (choice == CreatorChoice.FacialHair && (people == PeopleLooks.Siann || look.Sliders.GetValueOrDefault("look.native_clean_shaven") != 0)) selected = null;
            if (choice == CreatorChoice.Outfit && selected is null) selected = _catalogue.Options(choice, sex).FirstOrDefault(o => o.Label == "Pirate")?.Id;
            if (choice == CreatorChoice.Footwear && selected is null) selected = _catalogue.Options(choice, sex).FirstOrDefault(o => o.Label.Contains("Pirate", StringComparison.OrdinalIgnoreCase))?.Id;
            string? asset = selected is null ? null : HumGenCreatorData.Part(choice, sex, selected);
            if (_parts.TryGetValue(choice, out var previous) && previous == asset) continue;
            var removed = body.Skeleton.GetChildren().OfType<MeshInstance3D>().Where(m => m.HasMeta("creator_part_kind")
                && m.GetMeta("creator_part_kind").AsString() == choice.ToString()).ToArray();
            var added = new List<MeshInstance3D>();
            // A part this build does not hold yet is not drawn: the body keeps its own native
            // groom rather than the preview failing on an asset an export batch has not written.
            if (asset is not null && art.RiggedFile(asset, "creator_part.json") is { } contractPath)
            {
                using var part = JsonDocument.Parse(File.ReadAllText(contractPath));
                using var identity = JsonDocument.Parse(File.ReadAllText(art.RiggedFile(id, "identity_weights.json")!));
                if (part.RootElement.GetProperty("target_base").GetString() != id || part.RootElement.GetProperty("target_body_sha256").GetString()
                    != identity.RootElement.GetProperty("target_body_sha256").GetString()) throw new InvalidDataException("Native part targets another body: " + asset);
                var source = art.Rigged(asset) ?? throw new InvalidDataException("Native part could not load: " + asset);
                var rig = ArtLibrary.Find<Skeleton3D>(source) ?? throw new InvalidDataException("Native part rig missing");
                CharacterMaterials.Upgrade(source, out _, new Dictionary<string, HumanEye.Maps>(), nativeHumGen: true);
                HumGenMaterials.Apply(source, art, asset);
                added.AddRange(source.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>());
                Outfit.NameSkinBinds(added, rig);
                foreach (var mesh in added)
                {
                    mesh.Owner = null;
                    mesh.GetParent().RemoveChild(mesh);
                    body.Skeleton.AddChild(mesh);
                    mesh.Transform = Transform3D.Identity;
                    mesh.Skeleton = new NodePath("..");
                    mesh.SetMeta("creator_part_kind", choice.ToString());
                }
                source.Free();
            }
            body.SwapParts(removed, added);
            foreach (var mesh in removed)
            {
                _meshes.RemoveAll(m => m.Mesh == mesh);
                // Off the skeleton now: the face driver rebinds below and must not find a mesh that is about to be freed.
                body.Skeleton.RemoveChild(mesh);
                mesh.QueueFree();
            }
            foreach (var mesh in added)
            {
                var keys = new Dictionary<string, int>(StringComparer.Ordinal);
                if (mesh.Mesh is ArrayMesh array)
                    for (int i = 0; i < array.GetBlendShapeCount(); i++)
                    {
                        string name = array.GetBlendShapeName(i);
                        if (name.StartsWith("f_", StringComparison.Ordinal) || name.StartsWith("b_", StringComparison.Ordinal) || name.StartsWith("s_", StringComparison.Ordinal)) keys[name] = i;
                    }
                _meshes.Add((mesh, keys));
            }
            foreach (var driver in (body.GetParent() ?? model).FindChildren("*", "", true, false).OfType<FaceDriver>()) driver.Rebind(body);
            _parts[choice] = asset;
        }
        // A modular groom is coloured on every pass: its own atlas is never in the preset set.
        var preset = _data.Preset(face);
        foreach (var mesh in body.Skeleton.GetChildren().OfType<MeshInstance3D>().Where(m => m.HasMeta("creator_part_kind")))
            for (int surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
                Colour(mesh, surface, preset, look, people);
    }

    /// <summary>The surface's own exported material: every pass starts here, so a colour never compounds.</summary>
    private static Material Origin(MeshInstance3D mesh, int surface)
    {
        string key = "creator_base_" + surface;
        if (!mesh.HasMeta(key))
            mesh.SetMeta(key, (GodotObject?)(mesh.GetSurfaceOverrideMaterial(surface) ?? mesh.Mesh!.SurfaceGetMaterial(surface)) ?? new StandardMaterial3D());
        return mesh.GetMeta(key).As<Material>();
    }

    /// <summary>The surface's exported material name without its level and Blender's copy suffix ("_Haircap_LOD1.003"): it says which native part it is.</summary>
    private static string Exported(MeshInstance3D mesh, int surface) =>
        System.Text.RegularExpressions.Regex.Replace(mesh.Mesh?.SurfaceGetMaterial(surface)?.ResourceName ?? "", @"(_LOD[0-9]+)?(\.[0-9]+)?$", "");

    /// <summary>A creator slider's value, else the value the preset itself was authored with (in the slider's stored units).</summary>
    private static float Slider(CharacterAppearance look, string id, float native) =>
        look.Sliders.TryGetValue(id, out int value) ? value * .001f : native;

    private static float Ratio(float chosen, float native, float low, float high) =>
        Mathf.Clamp(chosen / Mathf.Max(native, .01f), low, high);
}
