// UNNAMED Presentation - wildlife made "plausible but wrong" (DIRECTION_AND_CONTENT_PLAN.md D.1, owner 10-06: "not the local dog park")
// Godot presentation only: no gameplay state lives here (D-11)

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// A creature binding's "otherfauna" block: the WildMesh coat kept as scanned and one or two of its properties moved, by shader and
/// proportion only (no generative step touches the WildMesh content). <see cref="Shader"/> holds otherfauna.gdshader uniforms by name;
/// <see cref="ByMaterial"/> overrides them on the surfaces whose material name holds the key (a stag's antlers); <see cref="Bones"/> lengthens bones (an ear, a tail) along their own length after the clips have posed them; each animal is drawn at a
/// size between the two <see cref="Scale"/> values, and its coat a little off its kin's (the shader's <c>variant</c>).
/// </summary>
public sealed record FaunaLook(IReadOnlyDictionary<string, JsonElement> Shader, IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>> ByMaterial,
    IReadOnlyDictionary<string, float> Bones, float ScaleMin, float ScaleMax)
{
    public static FaunaLook? Parse(JsonElement creature)
    {
        if (creature.ValueKind != JsonValueKind.Object || !creature.TryGetProperty("otherfauna", out var look))
            return null;
        static IReadOnlyDictionary<string, JsonElement> Uniforms(JsonElement e) => e.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        var shader = look.TryGetProperty("shader", out var s) ? Uniforms(s) : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var byMaterial = look.TryGetProperty("by_material", out var m) ? m.EnumerateObject().ToDictionary(p => p.Name, p => Uniforms(p.Value), StringComparer.Ordinal)
            : new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(StringComparer.Ordinal);
        var bones = look.TryGetProperty("bones", out var b) ? b.EnumerateObject().ToDictionary(p => p.Name, p => (float)p.Value.GetDouble(), StringComparer.Ordinal)
            : new Dictionary<string, float>(StringComparer.Ordinal);
        float min = 1, max = 1;
        if (look.TryGetProperty("scale", out var scale))
            (min, max) = ((float)scale[0].GetDouble(), (float)scale[1].GetDouble());
        return new FaunaLook(shader, byMaterial, bones, min, max);
    }
}

public static partial class OtherFauna
{
    private const string ShaderPath = "res://Art/otherfauna.gdshader";
    private const string Night = "otherfauna_night";

    private static Shader? _shader;
    // Per bound species: its rest-posed meshes (the pattern's coordinates in CUSTOM0) and its materials, shared by every animal of it.
    private static readonly Dictionary<(Mesh, string), ArrayMesh> Meshes = new();
    private static readonly Dictionary<(Material, string), ShaderMaterial> Materials = new();

    /// <summary>The night the marks glow by, 0 by day and 1 at night (the sky's own daylight); call once a frame.</summary>
    public static void Dusk()
    {
        if (_shader is not null)
            RenderingServer.GlobalShaderParameterSet(Night, 1f - Greybox.SkyView.DaylightLevel);
    }

    /// <summary>
    /// Redraw <paramref name="model"/> (species <paramref name="species"/>) with its otherfauna look, as the individual
    /// <paramref name="individual"/>: its coat, its bones and its size. Returns the size it is drawn at (1 with no look, or under
    /// <c>--visual fauna=earth</c>, which draws the WildMesh animals as scanned for a before/after).
    /// </summary>
    public static float Apply(SkinnedModel model, string species, FaunaLook? look, string individual)
    {
        if (look is null || VisualOptions.All.GetValueOrDefault("fauna") == "earth")
            return 1f;
        if (_shader is null)
        {
            RenderingServer.GlobalShaderParameterAdd(Night, RenderingServer.GlobalShaderParameterType.Float, 0f);
            _shader = GD.Load<Shader>(ShaderPath);
        }
        uint hash = 2166136261u;
        foreach (char c in individual)
            hash = (hash ^ c) * 16777619u;
        float variant = (hash % 1000u) / 1000f;
        foreach (var node in model.FindChildren("*", nameof(MeshInstance3D), true, false))
        {
            var mesh = (MeshInstance3D)node;
            if (mesh.Mesh is not ArrayMesh source)
                continue;
            if (!Meshes.TryGetValue((source, species), out var rest))
                Meshes[(source, species)] = rest = Rest(source, species, look);
            mesh.Mesh = rest;
            mesh.SetInstanceShaderParameter("variant", variant);
        }
        if (look.Bones.Count > 0)
            model.Skeleton.AddChild(new Proportions(model.Skeleton, look.Bones));
        float size = look.ScaleMin + (look.ScaleMax - look.ScaleMin) * ((hash >> 10) % 1000u) / 1000f;
        model.Scale = new Vector3(size, size, size);
        return size;
    }

    /// <summary>The mesh again with each vertex's rest position (metres) in CUSTOM0, and the species' materials on its surfaces.</summary>
    private static ArrayMesh Rest(ArrayMesh source, string species, FaunaLook look)
    {
        var body = source.GetAabb();
        var mesh = new ArrayMesh { ResourceName = source.ResourceName + "_otherfauna" };
        var custom = (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
        for (int s = 0; s < source.GetSurfaceCount(); s++)
        {
            var arrays = source.SurfaceGetArrays(s);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var at = new float[vertices.Length * 4];
            for (int v = 0; v < vertices.Length; v++)
                (at[4 * v], at[4 * v + 1], at[4 * v + 2]) = (vertices[v].X, vertices[v].Y, vertices[v].Z);
            arrays[(int)Mesh.ArrayType.Custom0] = at;
            mesh.AddSurfaceFromArrays(source.SurfaceGetPrimitiveType(s), arrays, null, null,
                custom | (source.SurfaceGetFormat(s) & Mesh.ArrayFormat.FlagUse8BoneWeights));
            mesh.SurfaceSetName(s, source.SurfaceGetName(s));
            if (source.SurfaceGetMaterial(s) is { } material)
            {
                if (!Materials.TryGetValue((material, species), out var coat))
                    Materials[(material, species)] = coat = Coat(material, look, body);
                mesh.SurfaceSetMaterial(s, coat);
            }
        }
        return mesh;
    }

    private static ShaderMaterial Coat(Material material, FaunaLook look, Aabb body)
    {
        var m = new ShaderMaterial { ResourceName = material.ResourceName + "_otherfauna", Shader = _shader };
        if (material is BaseMaterial3D source)
        {
            m.SetShaderParameter("albedo_tex", source.AlbedoTexture);
            m.SetShaderParameter("albedo_mul", source.AlbedoColor);
            if (source.NormalEnabled && source.NormalTexture is not null)
            {
                m.SetShaderParameter("normal_tex", source.NormalTexture);
                m.SetShaderParameter("normal_scale", source.NormalScale);
            }
            m.SetShaderParameter("orm_tex", source.RoughnessTexture);
            m.SetShaderParameter("roughness_mul", source.Roughness);
            m.SetShaderParameter("ao_light_affect", source.AOEnabled ? source.AOLightAffect : 0f);
            if (source.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor)
                m.SetShaderParameter("alpha_scissor", source.AlphaScissorThreshold);
        }
        m.SetShaderParameter("body_min", body.Position);
        m.SetShaderParameter("body_size", body.Size);
        foreach (var (name, value) in look.Shader.Concat(look.ByMaterial.Where(b => material.ResourceName.Contains(b.Key, StringComparison.Ordinal)).SelectMany(b => b.Value)))
            m.SetShaderParameter(name, value.ValueKind == JsonValueKind.Array
                ? new Vector3((float)value[0].GetDouble(), (float)value[1].GetDouble(), (float)value[2].GetDouble())
                : (float)value.GetDouble());
        return m;
    }

    /// <summary>Bones drawn longer than the rig made them (a hare's ears, a fox's brush): stretched along their own length, after the clips.</summary>
    private sealed partial class Proportions : SkeletonModifier3D
    {
        private readonly (int Bone, Vector3 Scale)[] _bones;

        public Proportions() => _bones = Array.Empty<(int, Vector3)>();

        public Proportions(Skeleton3D skeleton, IReadOnlyDictionary<string, float> bones)
        {
            Name = "Proportions";
            // Godot's bones run along their own +Y; the girth grows a third as much, so a longer ear is not a thin one.
            _bones = bones.Select(b => (skeleton.FindBone(b.Key), new Vector3(1 + (b.Value - 1) / 3, b.Value, 1 + (b.Value - 1) / 3)))
                .Where(b => b.Item1 >= 0).ToArray();
        }

        public override void _ProcessModificationWithDelta(double delta)
        {
            if (GetSkeleton() is not { } skeleton)
                return;
            foreach (var (bone, scale) in _bones)
                skeleton.SetBonePoseScale(bone, skeleton.GetBoneRest(bone).Basis.Scale * scale);
        }
    }
}
