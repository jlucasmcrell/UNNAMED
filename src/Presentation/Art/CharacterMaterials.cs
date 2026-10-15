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
    public static int Upgrade(Node model) => Upgrade(model, out _);

    /// <summary>
    /// As <see cref="Upgrade(Node)"/>, and also reports HOW MANY surfaces carried a name in the zone vocabulary at
    /// all (<paramref name="zoneNamed"/>).
    ///
    /// WHY THAT SECOND NUMBER IS WORTH RETURNING: the caller used to print "the zone names did not match" for any
    /// model that upgraded nothing, which asserts a cause it had not measured. There are three different reasons
    /// for zero, and only one of them is a fault:
    ///
    ///   * the model has NO zone names - every single-atlas creature, `MAT_creature_bristleback_boar_rigged`. This
    ///     is normal, nothing is wrong, and the pass has nothing to say about such a model;
    ///   * the model HAS zone names and none matched a kind in character_materials.json - that IS a mismatch;
    ///   * the kinds file would not load, which `Kinds()` already warns about separately.
    ///
    /// The first case was being reported as the second for as long as this has existed, and a creature reading as
    /// broken every run is how a real gap becomes background noise.
    /// </summary>
    public static int Upgrade(Node model, out int zoneNamed) => Upgrade(model, out zoneNamed, new Dictionary<string, HumanEye.Maps>());

    /// <summary>
    /// As above, with the HumanShaders eye inputs the model ships (<see cref="HumanEye.Load"/>): an eye surface named there is drawn by the
    /// eye shader, any other eye by the eye kind's settings.
    /// </summary>
    public static int Upgrade(Node model, out int zoneNamed, IReadOnlyDictionary<string, HumanEye.Maps> eyes)
    {
        var kinds = Kinds();
        int upgraded = 0, named = 0;
        foreach (var node in model.FindChildren("*", nameof(MeshInstance3D), true, false))
        {
            if (node is not MeshInstance3D instance || instance.Mesh is null)
                continue;
            for (int s = 0; s < instance.Mesh.GetSurfaceCount(); s++)
            {
                if (instance.Mesh.SurfaceGetMaterial(s) is not BaseMaterial3D material)
                    continue;
                string name = material.ResourceName ?? string.Empty;
                string kind = KindOf(name, kinds);
                if (kind.Length == 0 || !kinds.TryGetValue(kind, out var spec))
                    continue;
                named++;
                // THE SKIN GOES THROUGH HumanShaders, NOT THROUGH THE ENGINE'S OWN SSS.
                // Raising SubsurfScatterStrength from 0.22 to 0.75 measured only "marginally better" and the reviewer added
                // that neither frame escaped "opaque game-engine plastic" - because a scalar cannot do what a layered skin
                // shader does. HumanShaders (MIT + CC BY 4.0, res://Art/HumanShaders/) implements the three-lobe wrapped
                // diffuse and dual-lobe specular the same review asked for, and was already used on a character at an owner
                // checkpoint. It returns false if the shader will not load, and the material then falls through to the
                // engine's response rather than drawing nothing.
                if (kind == "skin" && HumanSkin.Apply(instance, s, material))
                {
                    upgraded++;
                    continue;
                }
                if (kind == "eye" && eyes.TryGetValue(name, out var eye) && HumanEye.Apply(instance, s, name, eye))
                {
                    upgraded++;
                    continue;
                }
                Apply(material, spec);
                upgraded++;
            }
        }
        zoneNamed = named;
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

    /// <summary>Explicit material-name-to-kind entries from <c>material_kinds</c> in character_materials.json.</summary>
    private static readonly Dictionary<string, string> _materialKinds = new(StringComparer.Ordinal);

    /// <summary>
    /// Which kind a surface's material is, or empty when it is not in the vocabulary.
    ///
    /// TWO RULES, AND THE SECOND WAS THE ONLY ONE FOR TOO LONG. An explicit <c>material_kinds</c> entry wins,
    /// because a name is an identity and a suffix is a convention that happened to hold. The suffix rule
    /// (<c>&lt;prefix&gt;_&lt;kind&gt;</c>, the charprod zone vocabulary) is kept for the zone-segmented models it
    /// was written for.
    ///
    /// WHY THE EXPLICIT MAP EXISTS. Cairnwold's characters come from charstd, not charprod, and their hair material
    /// is MPFB's own <c>Human.&lt;hairstyle&gt;</c>. Of the four grooms in the cast, exactly one - 
    /// <c>Human.elvs_grump_hair</c> - ends in <c>_hair</c> and therefore matched. The other three did not, so the
    /// <c>hair</c> kind (its specular, its rim, and the anisotropy added this round) was applied to one character
    /// out of four, and the character it was NOT applied to is the player. Nothing reported it: the pass counted
    /// surfaces and printed a total, and a material that does not match a kind is indistinguishable in that total
    /// from a model that has no such material at all.
    /// </summary>
    private static string KindOf(string name, Dictionary<string, JsonElement> kinds)
    {
        if (_materialKinds.TryGetValue(name, out var explicitKind))
            return kinds.ContainsKey(explicitKind) ? explicitKind : string.Empty;
        if (name == "eye")
            return "eye";
        return kinds.Keys.Where(k => name.EndsWith("_" + k, StringComparison.Ordinal))
                         .OrderByDescending(k => k.Length).FirstOrDefault() ?? string.Empty;
    }

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
        // THE HAIR'S ANISOTROPY, WHICH NOTHING COULD USE UNTIL THE TANGENTS EXISTED.
        //
        // Godot documents `anisotropy_enabled` as the lever for "brushed aluminum and hair reflections", and adds
        // that it is inert without mesh tangents: "Mesh tangents are needed for anisotropy to work. If the mesh does
        // not contain tangents, the anisotropy effect will appear broken." Measured on the Cairnwold cast: NOT ONE
        // mesh of not one character carried TANGENT, so hair has never had the one specular response that says
        // "hair" rather than "felt" - and no value of roughness, rim or backlight could produce it, because the
        // term was not being evaluated at all. `tools/asset_pipeline/_hair_tangents.py` now writes a tangent into
        // the hair primitive of each groom, oriented along that groom's OWN measured strand direction (a tangent
        // from UV derivatives follows +U; the player's strands run along V, Tavar's along U, and the two are not the
        // same answer). This is the first setting that can use it.
        //
        // IT APPLIES ONLY WHERE A TANGENT EXISTS. A material without one renders exactly as before rather than
        // breaking, because Godot ignores the term rather than reading a zero tangent.
        if (spec.TryGetProperty("anisotropy", out _))
        {
            m.AnisotropyEnabled = true;
            m.Anisotropy = Num("anisotropy", 0.5f);
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
            if (document.RootElement.TryGetProperty("material_kinds", out var named))
                foreach (var entry in named.EnumerateObject()
                             .Where(e => e.Value.ValueKind == JsonValueKind.String && e.Name != "note"))
                    _materialKinds[entry.Name] = entry.Value.GetString() ?? string.Empty;
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
