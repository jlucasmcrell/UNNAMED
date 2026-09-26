// UNNAMED Presentation - the production characters' material response by zone (Phase B, character fidelity)
// Godot presentation only: no gameplay state lives here (D-11)

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// The character pipeline (tools/asset_pipeline/charprod) splits a character into material zones named
/// <c>&lt;prefix&gt;_&lt;kind&gt;</c> (skin, hair, cloth, cloth_heavy, leather, metal) plus <c>eye</c>, all on one atlas whose ORM map
/// already carries each zone's roughness and metalness. What glTF cannot say is set here by kind from
/// <c>res://Art/character_materials.json</c>: subsurface scattering for skin, a cloth's rim sheen, leather's thin clearcoat,
/// the eye's wet cornea, the specular level. A material whose name ends in no known kind (every Phase-A model) is left alone.
/// </summary>
public static class CharacterMaterials
{
    public const string ResourcePath = "res://Art/character_materials.json";

    private static Dictionary<string, JsonElement>? _kinds;

    /// <summary>Upgrade every zone material under <paramref name="model"/>; returns how many surfaces were upgraded.</summary>
    public static int Upgrade(Node model)
    {
        var kinds = Kinds();
        int upgraded = 0;
        foreach (var node in model.FindChildren("*", nameof(MeshInstance3D), true, false))
        {
            if (node is not MeshInstance3D instance || instance.Mesh is null)
                continue;
            for (int s = 0; s < instance.Mesh.GetSurfaceCount(); s++)
            {
                if (instance.Mesh.SurfaceGetMaterial(s) is not BaseMaterial3D material)
                    continue;
                string name = material.ResourceName ?? string.Empty;
                string kind = name == "eye" ? "eye" : kinds.Keys.Where(k => name.EndsWith("_" + k, StringComparison.Ordinal)).OrderByDescending(k => k.Length).FirstOrDefault() ?? string.Empty;
                if (kind.Length == 0 || !kinds.TryGetValue(kind, out var spec))
                    continue;
                Apply(material, spec);
                upgraded++;
            }
        }
        return upgraded;
    }

    /// <summary>
    /// The authored LOD chain (tools/asset_pipeline/charprod/lods.py: meshes named <c>..._LOD1</c>, <c>..._LOD2</c> on the same skeleton)
    /// switched by distance with a dithered cross-fade; returns the number of levels found (0: a model without a chain is left alone).
    /// </summary>
    public static int ApplyLods(Node model)
    {
        var meshes = model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().ToList();
        int Level(MeshInstance3D m) => m.Name.ToString().EndsWith("_LOD2", StringComparison.Ordinal) ? 2
            : m.Name.ToString().EndsWith("_LOD1", StringComparison.Ordinal) ? 1 : 0;
        int levels = meshes.Count == 0 ? 0 : meshes.Max(Level);
        if (levels == 0)
            return 0;
        Kinds();
        float lod0 = _lods.GetValueOrDefault("lod0_end", 18f), lod1 = _lods.GetValueOrDefault("lod1_end", 45f), margin = _lods.GetValueOrDefault("margin", 2f);
        foreach (var m in meshes)
        {
            int level = Level(m);
            m.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
            m.VisibilityRangeBegin = level == 0 ? 0 : level == 1 ? lod0 : lod1;
            m.VisibilityRangeEnd = level == levels ? 0 : level == 0 ? lod0 : lod1;
            m.VisibilityRangeBeginMargin = level == 0 ? 0 : margin;
            m.VisibilityRangeEndMargin = level == levels ? 0 : margin;
        }
        return levels + 1;
    }

    private static readonly Dictionary<string, float> _lods = new(StringComparer.Ordinal);

    private static void Apply(BaseMaterial3D m, JsonElement spec)
    {
        float Num(string key, float fallback) => spec.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? (float)v.GetDouble() : fallback;
        Color Tint(string key, Color fallback) => spec.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() >= 3
            ? new Color((float)v[0].GetDouble(), (float)v[1].GetDouble(), (float)v[2].GetDouble())
            : fallback;
        m.MetallicSpecular = Num("specular", m.MetallicSpecular);
        if (spec.TryGetProperty("sss", out _))
        {
            m.SubsurfScatterEnabled = true;
            m.SubsurfScatterSkinMode = spec.TryGetProperty("sss_skin", out var skin) && skin.ValueKind == JsonValueKind.True;
            m.SubsurfScatterStrength = Num("sss", 0.2f);
        }
        if (spec.TryGetProperty("rim", out _))
        {
            m.RimEnabled = true;
            m.Rim = Num("rim", 0.2f);
            m.RimTint = Num("rim_tint", 0.5f);
        }
        if (spec.TryGetProperty("clearcoat", out _))
        {
            m.ClearcoatEnabled = true;
            m.Clearcoat = Num("clearcoat", 0.2f);
            m.ClearcoatRoughness = Num("clearcoat_roughness", 0.4f);
        }
        if (spec.TryGetProperty("backlight", out _))
        {
            m.BacklightEnabled = true;
            m.Backlight = Tint("backlight", new Color(0.1f, 0.06f, 0.04f));
        }
        if (spec.TryGetProperty("roughness", out _))
        {
            // A fixed roughness (the eye's cornea): the map's value does not apply.
            m.RoughnessTexture = null;
            m.Roughness = Num("roughness", m.Roughness);
        }
    }

    private static Dictionary<string, JsonElement> Kinds()
    {
        if (_kinds is not null)
            return _kinds;
        _kinds = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!Godot.FileAccess.FileExists(ResourcePath))
            return _kinds;
        try
        {
            using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(ResourcePath));
            foreach (var kind in document.RootElement.GetProperty("kinds").EnumerateObject())
                _kinds[kind.Name] = kind.Value.Clone();
            if (document.RootElement.TryGetProperty("lods", out var lods))
            {
                foreach (var entry in lods.EnumerateObject().Where(e => e.Value.ValueKind == JsonValueKind.Number))
                    _lods[entry.Name] = (float)entry.Value.GetDouble();
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            GD.PushWarning($"UNNAMED art: {ResourcePath} could not be read ({e.Message}); character materials stay as imported");
        }
        return _kinds;
    }
}
