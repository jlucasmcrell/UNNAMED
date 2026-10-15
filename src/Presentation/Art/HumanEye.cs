// UNNAMED Presentation - a MetaHuman's eyes drawn by HumanShaders' eye shader (character pipeline, 2026-10-15)
// Godot presentation only: no gameplay state lives here (D-11)

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// HumanShaders' <c>eye_shader.gdshader</c> (MatMADNESS, MIT + CC BY 4.0, the same pinned commit as the skin shader) on a character's eye
/// surfaces: separate sclera and iris colour, an iris that stays smooth under the cornea while the sclera keeps its normal map, its own
/// GGX specular, and a tunable roughness and specular for the Veth's reflective iris.
///
/// glTF cannot carry the shader's inputs, so the character pipeline (asset_work/metahuman/tools, the Blender assembly) writes them beside
/// the rigged model: <c>rigged/&lt;id&gt;/eye_shader/eye_shader_params.json</c> names, per eye material, its sclera and iris colour, both
/// normals and the iris heightmap (blue 0 inside the iris), with the parameters measured on 2026-10-06 (iris_scale 2.016, roughness
/// 0.05, specular 0.6, heightmap parallax off). A model without that file keeps its imported eye material and the eye kind's settings.
/// </summary>
public static class HumanEye
{
    private const string ShaderPath = "res://Art/HumanShaders/eye_shader.gdshader";
    public const string ParamsFile = "eye_shader/eye_shader_params.json";

    private static Shader? _shader;
    private static bool _tried;

    /// <summary>One eye's inputs, read from the params file.</summary>
    public sealed record Maps(Texture2D Sclera, Texture2D Iris, Texture2D ScleraNormal, Texture2D IrisNormal, Texture2D Height,
        float IrisScale, float Roughness, float Specular, bool UseHeightmap);

    /// <summary>
    /// The eye inputs a rigged model ships, by eye material name; empty when it ships none. A params file naming a map that does not load
    /// is reported and that eye left out.
    /// </summary>
    public static IReadOnlyDictionary<string, Maps> Load(ArtLibrary art, string assetId)
    {
        var maps = new Dictionary<string, Maps>(StringComparer.Ordinal);
        if (art.RiggedFile(assetId, ParamsFile) is not { } path)
            return maps;
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var eye in json.RootElement.GetProperty("materials").EnumerateObject())
        {
            Texture2D? Map(string key, string role) =>
                art.RiggedFile(assetId, eye.Value.GetProperty(key).GetString()!) is { } file ? art.Texture(file, role) : null;
            float Num(string key) => (float)eye.Value.GetProperty(key).GetDouble();
            if (Map("sclera", "color") is not { } sclera || Map("iris", "color") is not { } iris || Map("sclera_normal", "normal") is not { } scleraNormal
                || Map("iris_normal", "normal") is not { } irisNormal || Map("heightmap", "data") is not { } height)
            {
                art.Report(assetId, $"{ParamsFile}: a map of {eye.Name} would not load; that eye keeps its imported material", assetId);
                continue;
            }
            maps[eye.Name] = new Maps(sclera, iris, scleraNormal, irisNormal, height, Num("iris_scale"), Num("roughness"), Num("specular"),
                eye.Value.GetProperty("use_heightmap").GetBoolean());
        }
        return maps;
    }

    /// <summary>Replace one eye surface with the HumanShaders eye material; false when the shader will not load.</summary>
    public static bool Apply(MeshInstance3D mesh, int surface, string name, Maps maps)
    {
        if (!_tried)
        {
            _tried = true;
            _shader = ResourceLoader.Exists(ShaderPath) ? GD.Load<Shader>(ShaderPath) : null;
            if (_shader is null)
                GD.PrintErr($"UNNAMED character materials: HumanShaders eye shader did not load ({ShaderPath}) - eyes keep the imported material");
        }
        if (_shader is null)
            return false;
        var m = new ShaderMaterial { ResourceName = name + "_HumanShaders", Shader = _shader };
        m.SetShaderParameter("sclera_albedo_texture", maps.Sclera);
        m.SetShaderParameter("iris_albedo_texture", maps.Iris);
        m.SetShaderParameter("sclera_normal_texture", maps.ScleraNormal);
        m.SetShaderParameter("iris_normal_texture", maps.IrisNormal);
        m.SetShaderParameter("texture_heightmap", maps.Height);
        m.SetShaderParameter("use_heightmap", maps.UseHeightmap);
        m.SetShaderParameter("iris_scale", maps.IrisScale);
        m.SetShaderParameter("roughness", maps.Roughness);
        m.SetShaderParameter("specular", maps.Specular);
        // The shader scales its UVs by uv1_scale, whose declared default is zero: unset, every texel would sample one corner.
        m.SetShaderParameter("uv1_scale", Vector3.One);
        m.SetShaderParameter("uv1_offset", Vector3.Zero);
        mesh.SetSurfaceOverrideMaterial(surface, m);
        return true;
    }
}
