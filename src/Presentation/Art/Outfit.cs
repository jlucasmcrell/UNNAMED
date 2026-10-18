// UNNAMED Presentation - garments worn on a MetaHuman body: layered, refitted meshes on the body's own skeleton (character pipeline 4.1)
// Godot presentation only: no gameplay state lives here (D-11)

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// An outfit: garments refitted onto a body by the character pipeline (asset_work/metahuman/tools/bl_garment.py), each a rigged model on
/// the same game skeleton with a sidecar <c>&lt;id&gt;_garment.json</c> giving its layer (CHARACTER_ANIMATION_STRATEGY.md 4.3: 1 under-layer
/// to 6 cloak) and <c>hides_mask</c>, the body regions it covers as bits of the region IDs. Its meshes join the body's skeleton; the body,
/// and every garment on a lower layer, then drop the triangles of the regions an outer garment covers (region_mask.gdshaderinc), so
/// neither can poke through it whatever the pose. A garment with loose parts names the skeleton's cloth chains it is weighted to
/// (<c>chains</c>: prefix to stiffness, drag and gravity); only those chains spring, round capsules on the legs (memo 4.3 rule 7).
/// A garment is baked per body (<c>&lt;garment&gt;__&lt;body&gt;</c>, WP17: authored once, the body's build carried into it at build time):
/// a figure wears the variant baked for its own body, and a garment with no variant for that body is not worn rather than worn clipping.
/// An outfit may list garments for several bodies (a shirt for each body type): each figure wears those baked for its own.
/// </summary>
public static partial class Outfit
{
    private const string ClothShaderPath = "res://Art/cloth.gdshader";
    private static Shader? _cloth;
    private static readonly Dictionary<BaseMaterial3D.CullModeEnum, Shader> ClothCullShaders = new();
    private const string OwnedGarment = "outfit_garment";

    /// <summary>Remove only meshes/springs installed by Dress, retaining the body, pose and animation player.</summary>
    public static void Clear(SkinnedModel figure)
    {
        foreach (var child in figure.Skeleton.GetChildren().Where(n => n.HasMeta(OwnedGarment)).ToList())
            child.Free();
        foreach (var mesh in figure.Skeleton.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            mesh.SetInstanceShaderParameter("region_hidden_mask", 0);
            mesh.SetInstanceShaderParameter("source_hidden_mask", 0);
        }
    }

    /// <summary>
    /// Dress <paramref name="figure"/>, drawn with the model <paramref name="body"/>, in <paramref name="garments"/> (rigged asset IDs, each
    /// worn as its variant for that body); returns how many were put on.
    /// </summary>
    public static int Dress(ArtLibrary art, SkinnedModel figure, string body, IReadOnlyList<string> garments)
    {
        if (garments.Count == 0)
            return 0;
        var skin = figure.Skeleton.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>().ToList();
        var worn = new List<(List<MeshInstance3D> Meshes, int Layer, int Hides, int SourceHides)>();
        var baked = garments.Select(g => PlayerBodies.Garment(g, body)).Distinct()
            .Select(id => (Id: id, Spec: art.RiggedFile(id, id + "_garment.json"))).Where(g => g.Spec is not null).ToList();
        if (baked.Count == 0)
            art.Report(body, $"no garment of the outfit ({string.Join(", ", garments)}) is baked for {body}; drawn without it", body);
        foreach (var (id, spec) in baked)
        {
            if (art.Rigged(id) is not { } model)
                continue;
            using var json = JsonDocument.Parse(File.ReadAllText(spec!));
            int layer = json.RootElement.GetProperty("layer").GetInt32();
            int hides = json.RootElement.GetProperty("hides_mask").GetInt32();
            int sourceHides = json.RootElement.TryGetProperty("occlusion_mask", out var sourceMask) ? sourceMask.GetInt32() : 0;
            if (sourceHides is < 0 or > 1023)
                throw new InvalidDataException($"{spec}: occlusion_mask must fit the ten exact UV bits");
            if (json.RootElement.TryGetProperty("chains", out var chains))
                Springs(figure.Skeleton, id, chains);
            CharacterMaterials.ApplyLods(model);
            var sourceSkeleton = ArtLibrary.Find<Skeleton3D>(model);
            var namedSkins = new Dictionary<Skin, Skin>();
            var meshes = model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>().ToList();
            foreach (var mesh in meshes)
            {
                // Modular exports can number the same bones differently. Godot's
                // named binds keep the native joint identity when the mesh moves.
                if (mesh.Skin is { } sourceSkin)
                {
                    if (!namedSkins.TryGetValue(sourceSkin, out var named))
                    {
                        named = new Skin();
                        for (int bind = 0; bind < sourceSkin.GetBindCount(); bind++)
                        {
                            var name = sourceSkin.GetBindName(bind);
                            if (name.IsEmpty)
                            {
                                int bone = sourceSkin.GetBindBone(bind);
                                if (sourceSkeleton is null || bone < 0 || bone >= sourceSkeleton.GetBoneCount())
                                    throw new InvalidDataException($"{id}: skin bind {bind} has no source joint");
                                name = sourceSkeleton.GetBoneName(bone);
                            }
                            named.AddNamedBind(name, sourceSkin.GetBindPose(bind));
                        }
                        namedSkins.Add(sourceSkin, named);
                    }
                    mesh.Skin = named;
                }
                mesh.Owner = null;
                mesh.GetParent().RemoveChild(mesh);
                figure.Skeleton.AddChild(mesh);
                mesh.Transform = Transform3D.Identity;
                mesh.Skeleton = new NodePath("..");
                mesh.SetMeta(OwnedGarment, id);
                for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
                {
                    if (Cloth(mesh.Mesh.SurfaceGetMaterial(s)) is { } cloth)
                        mesh.SetSurfaceOverrideMaterial(s, cloth);
                }
            }
            model.Free();
            worn.Add((meshes, layer, hides, sourceHides));
        }
        int all = worn.Aggregate(0, (mask, g) => mask | g.Hides);
        int allSources = worn.Aggregate(0, (mask, g) => mask | g.SourceHides);
        foreach (var mesh in skin)
        {
            mesh.SetInstanceShaderParameter("region_hidden_mask", all);
            mesh.SetInstanceShaderParameter("source_hidden_mask", allSources);
        }
        foreach (var (meshes, layer, _, _) in worn)
        {
            int over = worn.Where(g => g.Layer > layer).Aggregate(0, (mask, g) => mask | g.Hides);
            int overSources = worn.Where(g => g.Layer > layer).Aggregate(0, (mask, g) => mask | g.SourceHides);
            foreach (var mesh in meshes)
            {
                mesh.SetInstanceShaderParameter("region_hidden_mask", over);
                mesh.SetInstanceShaderParameter("source_hidden_mask", overSources);
            }
        }
        return worn.Count;
    }

    // Colliders the loose cloth swings round, each from a leg bone to its child joint. The skeleton keeps UE's joint frames - a limb
    // runs along -X on the left and +X on the right - so each capsule is built from the rest offset to the child, not a fixed axis.
    private static readonly (string Bone, string Child, float Radius)[] LegCapsules =
        { ("thigh_l", "calf_l", 0.085f), ("thigh_r", "calf_r", 0.085f), ("calf_l", "foot_l", 0.06f), ("calf_r", "foot_r", 0.06f) };

    /// <summary>One spring simulator for a garment's chains: every chain of the skeleton whose bones are <c>&lt;prefix&gt;[_n]_&lt;k&gt;</c>.</summary>
    private static void Springs(Skeleton3D skeleton, string garment, JsonElement chains)
    {
        var settings = new List<(string Root, string End, JsonElement Spec)>();
        foreach (var chain in chains.EnumerateObject())
        {
            var bones = Enumerable.Range(0, skeleton.GetBoneCount()).Select(skeleton.GetBoneName)
                .Where(n => n.StartsWith(chain.Name + "_", StringComparison.Ordinal)).ToList();
            foreach (var group in bones.GroupBy(n => n[..n.LastIndexOf('_')]))
            {
                var ordered = group.OrderBy(n => int.Parse(n[(n.LastIndexOf('_') + 1)..])).ToList();
                settings.Add((ordered[0], ordered[^1], chain.Value));
            }
        }
        if (settings.Count == 0)
            return;
        var springs = new SpringBoneSimulator3D { Name = "Springs." + garment };
        springs.SetMeta(OwnedGarment, true);
        skeleton.AddChild(springs);
        springs.SetSettingCount(settings.Count);
        for (int i = 0; i < settings.Count; i++)
        {
            var (root, end, spec) = settings[i];
            springs.SetRootBoneName(i, root);
            springs.SetEndBoneName(i, end);
            springs.SetStiffness(i, (float)spec.GetProperty("stiffness").GetDouble());
            springs.SetDrag(i, (float)spec.GetProperty("drag").GetDouble());
            springs.SetGravity(i, (float)spec.GetProperty("gravity").GetDouble());
            springs.SetRadius(i, 0.02f);
            springs.SetEnableAllChildCollisions(i, true);
        }
        if (OS.GetEnvironment("UNNAMED_SPRING_PROBE") == "1")
            springs.AddChild(new SpringProbe { Chains = settings.Select(c => c.End).ToArray() });
        foreach (var (bone, child, radius) in LegCapsules)
        {
            int at = skeleton.FindBone(child);
            if (skeleton.FindBone(bone) < 0 || at < 0)
                continue;
            var along = skeleton.GetBoneRest(at).Origin;
            springs.AddChild(new SpringBoneCollisionCapsule3D
            {
                BoneName = bone, Radius = radius, Height = along.Length() + 2 * radius, PositionOffset = along / 2,
                RotationOffset = new Quaternion(Vector3.Up, along.Normalized()),
            });
        }
    }

    /// <summary>UNNAMED_SPRING_PROBE=1: two seconds in, how far each chain's end joint stands from its rest (m), so a misbehaving
    /// garment chain is measured off a run rather than argued from a picture.</summary>
    private sealed partial class SpringProbe : Node
    {
        public string[] Chains { get; init; } = Array.Empty<string>();
        private double _t;
        private bool _done;

        public override void _Ready() => ((Skeleton3D)GetParent().GetParent()).SkeletonUpdated += Measure;

        public override void _Process(double delta) => _t += delta;

        // Modifier results live only inside the skeleton's update (Godot 4.3+), so they are read in skeleton_updated.
        private void Measure()
        {
            if (_t < 2.0 || _done)
                return;
            _done = true;
            var skeleton = (Skeleton3D)GetParent().GetParent();
            int pelvis = skeleton.FindBone("pelvis");
            var moved = Chains.Select(c => skeleton.FindBone(c)).Where(b => b >= 0)
                .Select(b => ((skeleton.GetBoneGlobalPose(pelvis).AffineInverse() * skeleton.GetBoneGlobalPose(b)).Origin
                              - (skeleton.GetBoneGlobalRest(pelvis).AffineInverse() * skeleton.GetBoneGlobalRest(b)).Origin).Length());
            GD.Print($"UNNAMED spring probe {GetParent().Name}: chain ends from rest, in the pelvis's frame, {string.Join(" ", moved.Select(m => m.ToString("0.000")))} m");
        }
    }

    /// <summary>A garment surface's imported glTF material as the cloth shader (it carries the region mask); null for anything else.</summary>
    private static ShaderMaterial? Cloth(Material? source)
    {
        if (source is not BaseMaterial3D imported)
            return null;
        _cloth ??= GD.Load<Shader>(ClothShaderPath);
        var shader = _cloth;
        if (imported.CullMode != BaseMaterial3D.CullModeEnum.Back)
        {
            if (!ClothCullShaders.TryGetValue(imported.CullMode, out shader))
            {
                string cull = imported.CullMode == BaseMaterial3D.CullModeEnum.Front ? "cull_front" : "cull_disabled";
                shader = new Shader { Code = _cloth.Code.Replace("cull_back", cull, StringComparison.Ordinal) };
                ClothCullShaders.Add(imported.CullMode, shader);
            }
        }
        var m = new ShaderMaterial { ResourceName = imported.ResourceName, Shader = shader };
        m.SetShaderParameter("albedo", imported.AlbedoColor);
        m.SetShaderParameter("roughness", imported.Roughness);
        m.SetShaderParameter("metallic", imported.Metallic);
        if (imported.AlbedoTexture is { } albedo)
            m.SetShaderParameter("texture_albedo", albedo);
        if (imported.RoughnessTexture is { } orm)
            m.SetShaderParameter("texture_orm", orm);
        if (imported.NormalEnabled && imported.NormalTexture is { } normal)
        {
            m.SetShaderParameter("texture_normal", normal);
            m.SetShaderParameter("normal_strength", imported.NormalScale);
        }
        if (imported.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor)
            m.SetShaderParameter("alpha_scissor", imported.AlphaScissorThreshold);
        return m;
    }
}
