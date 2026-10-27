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

    public static bool Apply(Node model, string? people, CharacterAppearance? look, ArtLibrary art, string? id)
    {
        if (id?.StartsWith("humgen_creator_", StringComparison.Ordinal) != true) return false;
        var controller = model.FindChildren("*", "", true, false).OfType<HumGenCreatorAppearance>().FirstOrDefault();
        if (controller is null)
        {
            string json = Godot.FileAccess.GetFileAsString("res://Art/humgen_creator.json");
            controller = new HumGenCreatorAppearance { Name = "NativeCreatorAppearance", _data = new(json), _catalogue = CreatorCatalogue.Read(json) };
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
        var weights = controller._data.Weights(look, controller._catalogue, people, sex);
        foreach (var (mesh, keys) in controller._meshes)
        foreach (var (key, index) in keys)
            mesh.SetBlendShapeValue(index, weights.GetValueOrDefault(key));
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
}
