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
    /// The authored LOD chain (meshes named <c>..._LOD1</c>, <c>..._LOD2</c> ... on the same skeleton: charprod's lods.py, or a
    /// MetaHuman's own LOD0-3 from the Blender assembly) switched by distance - a hard switch, as ArtLibrary's models switch: a dithered
    /// cross-fade drew two levels of a figure at once across its margin, each half-stippled, and a MetaHuman's band (4-8 m) is where the
    /// creator and the third-person camera stand - the body showed through its clothes and the hair cards (alpha scissor) toggled
    /// black over the face (WP17, owner 2026-10-17). <see cref="ShareLodBox"/> then switches a dressed figure as one. Returns the number of levels
    /// found (0: a model without a chain is left alone). Where each level ends comes from <c>lods</c>: <c>chain_&lt;n&gt;</c> for a chain of
    /// n levels when it is given, else <c>lod0_end</c> and <c>lod1_end</c>. <c>chain_&lt;n&gt;_shadow_levels</c> limits shadow casting to that many
    /// nearest levels: a distant figure's shadow costs a draw per surface per cascade. A mesh named <c>..._shadow</c> (the assembler's
    /// merged low-poly body and head) is that figure's shadow from where its levels stop casting: one draw per cascade instead of one
    /// per surface, and the figure keeps its shadow at every distance.
    /// </summary>
    public static int ApplyLods(Node model)
    {
        var all = model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().ToList();
        var proxies = all.Where(m => m.Name.ToString().EndsWith("_shadow", StringComparison.Ordinal)).ToList();
        var meshes = all.Except(proxies).ToList();
        static int Level(MeshInstance3D m)
        {
            string name = m.Name.ToString();
            int at = name.LastIndexOf("_LOD", StringComparison.Ordinal);
            return at >= 0 && int.TryParse(name.AsSpan(at + 4), out int level) ? level : 0;
        }
        int levels = meshes.Count == 0 ? 0 : meshes.Max(Level);
        if (levels == 0)
            return 0;
        Kinds();
        float[] ends = _chains.TryGetValue(levels + 1, out var chain) && chain.Length == levels
            ? chain
            : new[] { _lods.GetValueOrDefault("lod0_end", 18f), _lods.GetValueOrDefault("lod1_end", 45f) };
        int shadowLevels = (int)_lods.GetValueOrDefault($"chain_{levels + 1}_shadow_levels", levels + 1);
        foreach (var m in meshes)
        {
            int level = Math.Min(Level(m), ends.Length);
            m.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
            m.VisibilityRangeBegin = level == 0 ? 0 : ends[level - 1];
            m.VisibilityRangeEnd = level == levels ? 0 : MathF.BitDecrement(ends[level]);
            if (level >= shadowLevels)
                m.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        foreach (var proxy in proxies)
        {
            proxy.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
            proxy.VisibilityRangeBegin = shadowLevels <= levels ? ends[shadowLevels - 1] : 0;
        }
        return levels + 1;
    }

    /// <summary>
    /// One visibility box for every mesh on a figure's skeleton - body, head, hair and each garment - so all of them change level at the
    /// same distance (Godot measures a visibility range to each mesh's own box centre: a cap's sits half a metre above the body's). The
    /// box is their rest bounds grown by half a metre, room for any clip's reach.
    /// </summary>
    public static void ShareLodBox(Skeleton3D skeleton)
    {
        var meshes = skeleton.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().Where(m => m.Mesh is not null).ToList();
        if (meshes.Count == 0)
            return;
        var box = meshes.Select(m => m.Transform * m.Mesh.GetAabb()).Aggregate((a, b) => a.Merge(b)).Grow(0.5f);
        foreach (var m in meshes)
            m.CustomAabb = m.Transform.AffineInverse() * box;
    }

    private static readonly Dictionary<string, float> _lods = new(StringComparer.Ordinal);

    /// <summary>Where each level of an n-level chain ends (<c>lods.chain_&lt;n&gt;</c>), by n.</summary>
    private static readonly Dictionary<int, float[]> _chains = new();

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
        // A MetaHuman groom's cards carry one- or two-texel strands (its atlas's coverage is 4-10 %, made for UE's dithered, temporally
        // resolved hair). Cut per pixel they drew as broken strands, blended as a thin grey film (WP17, owner 2026-10-17). Cut per MSAA
        // sample (alpha to coverage) a strand keeps the part of a pixel it covers, and the texture cache's mip chain keeps the cards'
        // coverage with distance: dense, dark, solid strands, as the Blender reference draws them.
        if (spec.TryGetProperty("alpha_coverage", out var coverage) && coverage.ValueKind == JsonValueKind.True && m.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor)
            m.AlphaAntialiasingMode = BaseMaterial3D.AlphaAntiAliasing.AlphaToCoverageAndToOne;
        // A MetaHuman's lash strips are a translucent dark fringe (bl_assemble.py LASH_ALPHA): blended, not cut.
        if (spec.TryGetProperty("alpha_blend", out var blend) && blend.ValueKind == JsonValueKind.True)
            m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
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
                foreach (var entry in lods.EnumerateObject().Where(e => e.Name.StartsWith("chain_", StringComparison.Ordinal) && e.Value.ValueKind == JsonValueKind.Array))
                    _chains[int.Parse(entry.Name.AsSpan(6))] = entry.Value.EnumerateArray().Select(v => (float)v.GetDouble()).ToArray();
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            GD.PushWarning($"UNNAMED art: {ResourcePath} could not be read ({e.Message}); character materials stay as imported");
        }
        return _kinds;
    }
}
