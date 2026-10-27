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
        if (controller._face != face)
        {
            controller.Materials(art, face);
            controller._face = face;
        }
        var nativeBase = controller._data.Preset(controller._catalogue.Options(CreatorChoice.Face, sex)
            .First(f => f.Label == "Caucasian 2").Id);
        if (model is SkinnedFigure figure) figure.SetCreatorStature(look.StatureMm, nativeBase.GetProperty("height").GetProperty("set").GetSingle() * 10);
        return true;
    }

    private void Materials(ArtLibrary art, string face)
    {
        string manifest = art.RiggedFile("humgen_creator_materials", face + "/materials.json")
            ?? throw new InvalidDataException("Native preset materials missing: " + face);
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var parts = document.RootElement.GetProperty("parts");
        foreach (var (mesh, _) in _meshes)
        for (int surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
        {
            if (mesh.HasMeta("creator_part_kind")) continue;
            var original = mesh.Mesh!.SurfaceGetMaterial(surface);
            string materialName = System.Text.RegularExpressions.Regex.Replace(original.ResourceName, "_LOD[0-9]+$", "");
            string part = parts.EnumerateObject().Select(p => p.Name).FirstOrDefault(p =>
                materialName.EndsWith("_" + p, StringComparison.OrdinalIgnoreCase)) ?? "";
            if (!parts.TryGetProperty(part, out var entry)) continue;
            var maps = entry.GetProperty("maps");
            Texture2D? Map(string name, string role) => maps.TryGetProperty(name, out var relative)
                ? art.Texture(art.RiggedFile("humgen_creator_materials", face + "/" + relative.GetString())
                    ?? throw new InvalidDataException("Native material map missing"), role) : null;
            var material = mesh.GetSurfaceOverrideMaterial(surface) ?? original;
            material = (Material)material.Duplicate();
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
            selected ??= _data.PresetPart(face, choice, _catalogue, sex);
            if (choice == CreatorChoice.Hair && people == PeopleLooks.Siann && selected is null) selected = "hair.long.medium_center_part";
            if (choice == CreatorChoice.Hair && look.Sliders.GetValueOrDefault("look.native_bald") != 0) selected = null;
            if (choice == CreatorChoice.FacialHair && (people == PeopleLooks.Siann || look.Sliders.GetValueOrDefault("look.native_clean_shaven") != 0)) selected = null;
            if (choice == CreatorChoice.Outfit && selected is null) selected = _catalogue.Options(choice, sex).FirstOrDefault(o => o.Label == "Pirate")?.Id;
            if (choice == CreatorChoice.Footwear && selected is null) selected = _catalogue.Options(choice, sex).FirstOrDefault(o => o.Label.Contains("Pirate", StringComparison.OrdinalIgnoreCase))?.Id;
            string? asset = selected is null ? null : HumGenCreatorData.Part(choice, sex, selected);
            if (_parts.TryGetValue(choice, out var previous) && previous == asset) continue;
            var removed = body.Skeleton.GetChildren().OfType<MeshInstance3D>().Where(m => m.HasMeta("creator_part_kind")
                && m.GetMeta("creator_part_kind").AsString() == choice.ToString()).ToArray();
            var added = new List<MeshInstance3D>();
            if (asset is not null)
            {
                string contract = art.RiggedFile(asset, "creator_part.json") ?? throw new InvalidDataException("Native part missing: " + asset);
                using var part = JsonDocument.Parse(File.ReadAllText(contract));
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
            foreach (var mesh in removed) { _meshes.RemoveAll(m => m.Mesh == mesh); mesh.QueueFree(); }
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
    }
}
