using Godot;
using System.Security.Cryptography;
using System.Text;

namespace UNNAMED.Presentation.Art;

public sealed partial class SkinnedFigure
{
    internal string QualityActorId { get; private set; } = "";
    internal (float Blink, float Jaw) QualityFaceWeights => (_face?.BlinkWeight ?? 0, _face?.JawWeight ?? 0);
    // Read-only observations of the real world figure; no staging, camera changes or substituted materials.
    internal object QualitySnapshot(Camera3D camera, (float Blink, float Jaw) peaks) =>
        _model.QualityFigureSnapshot(this, QualityActorId, ModelId, _talking, _face, camera, peaks);
}

public sealed partial class SkinnedCreature
{
    internal string QualityActorId { get; private set; } = "";
    internal bool NativeHumGenQuality => _model.Name.ToString().StartsWith("humgen_", StringComparison.Ordinal);
    internal (float Blink, float Jaw) QualityFaceWeights => (_face?.BlinkWeight ?? 0, _face?.JawWeight ?? 0);
    internal object QualitySnapshot(Camera3D camera, (float Blink, float Jaw) peaks) =>
        _model.QualityFigureSnapshot(this, QualityActorId, _model.Name, false, _face, camera, peaks);
}

public sealed partial class SkinnedModel
{
    internal object QualityFigureSnapshot(Node3D owner, string actor, string model, bool talking, FaceDriver? faceDriver,
        Camera3D camera, (float Blink, float Jaw) peaks)
    {
        var relative = GlobalTransform.AffineInverse() * camera.GlobalTransform;
        int head = Skeleton.FindBone("Head");
        var anchor = head >= 0 ? Skeleton.GlobalTransform * Skeleton.GetBoneGlobalPose(head).Origin : owner.GlobalPosition + Vector3.Up;
        return new
        {
        actor, model, visible = owner.IsVisibleInTree() && camera.IsPositionInFrustum(anchor),
        distance = camera.GlobalPosition.DistanceTo(owner.GlobalPosition),
        cameraInModel = new[] { relative.Basis.X.X, relative.Basis.X.Y, relative.Basis.X.Z,
            relative.Basis.Y.X, relative.Basis.Y.Y, relative.Basis.Y.Z,
            relative.Basis.Z.X, relative.Basis.Z.Y, relative.Basis.Z.Z,
            relative.Origin.X, relative.Origin.Y, relative.Origin.Z },
        face = new { attached = faceDriver is not null, bindings = faceDriver?.ChannelBindings ?? 0, channels = faceDriver?.BoundChannels ?? [],
            jawBound = faceDriver?.NativeJawBound ?? false, eyeBindings = faceDriver?.NativeEyeBindings ?? 0,
            blink = faceDriver?.BlinkWeight ?? 0, jaw = faceDriver?.JawWeight ?? 0, blinkPeak = peaks.Blink, jawPeak = peaks.Jaw },
        talking, animation = QualitySnapshot(),
        };
    }
}

public sealed partial class SkinnedModel
{
    internal object QualitySnapshot() => new
    {
        source = GetMeta("quality_source_file", "").AsString(),
        nativeMaterials = GetMeta("quality_native_materials_file", "").AsString(),
        motion = GetMeta("quality_motion_file", "").AsString(),
        preset = GetMeta("quality_preset", "").AsString(),
        presetSource = GetMeta("quality_preset_source", "").AsString(),
        lod = _humgenLods?.Level ?? -1, throttled = _throttled,
        pose = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",
            Enumerable.Range(0, Skeleton.GetBoneCount()).Select(Skeleton.GetBoneGlobalPose))))),
        bodyPose = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",
            new[] { "Hips", "Spine", "Chest", "UpperChest", "LeftUpperArm", "RightUpperArm", "LeftUpperLeg", "RightUpperLeg" }
                .Select(Skeleton.FindBone).Where(bone => bone >= 0).Select(Skeleton.GetBoneGlobalPose))))),
        clip = Current, seconds = Current is null ? 0 : _player.CurrentAnimationPosition, states = _states.Order().ToArray(),
        meshes = FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
            .Where(mesh => mesh.IsVisibleInTree() && mesh.Mesh is not null)
            .Select(mesh => new
            {
                name = mesh.Name.ToString(), shapes = mesh.Mesh is ArrayMesh array ? array.GetBlendShapeCount() : 0,
                skinningInfluences = mesh.Mesh is ArrayMesh skin && skin.GetSurfaceCount() > 0
                    && (skin.SurfaceGetFormat(0) & Mesh.ArrayFormat.FlagUse8BoneWeights) != 0 ? 8 : 4,
                part = mesh.GetMeta("quality_part_id", "").AsString(),
                source = mesh.GetMeta("quality_part_source", "").AsString(),
                surfaces = Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).Select(surface =>
                    QualityMaterial(mesh.GetSurfaceOverrideMaterial(surface) ?? mesh.Mesh.SurfaceGetMaterial(surface))).ToArray(),
            }).ToArray(),
    };

    private static object QualityMaterial(Material? material)
    {
        var textures = new List<object>();
        void Add(string role, Texture2D? texture)
        {
            if (texture is not null) textures.Add(new { role, width = texture.GetWidth(), height = texture.GetHeight(), path = texture.ResourcePath });
        }
        if (material is BaseMaterial3D standard)
        {
            Add("color", standard.AlbedoTexture); Add("normal", standard.NormalTexture);
            Add("roughness", standard.RoughnessTexture); Add("flow", standard.AnisotropyFlowmap);
        }
        if (material is ShaderMaterial shader)
        {
            foreach (var (parameter, role) in new[] { ("texture_albedo", "color"), ("color_map", "color"),
                ("texture_normal", "normal"), ("normal_map", "normal"), ("texture_roughness", "roughness"),
                ("native_rough_spec_cavity", "native_specular"), ("scatter_map", "scatter"), ("strand_atlas", "color"), ("strand_flow", "flow"),
                ("strand_normal", "normal"), ("strand_roughness_map", "roughness") })
                if (shader.GetShaderParameter(parameter).AsGodotObject() is Texture2D texture) Add(role, texture);
        }
        var nativeSkinShader = material as ShaderMaterial;
        if (nativeSkinShader?.Shader?.ResourcePath.EndsWith("/humgen_skin.gdshader", StringComparison.Ordinal) != true) nativeSkinShader = null;
        return new
        {
            name = material?.ResourceName, type = material?.GetType().Name,
            shader = (material as ShaderMaterial)?.Shader?.ResourcePath,
            nativeSkin = nativeSkinShader?.GetShaderParameter("native_humgen").AsBool() ?? false,
            sssEnabled = nativeSkinShader?.GetShaderParameter("use_subsurface_scattering").AsBool() ?? false,
            sssStrength = nativeSkinShader?.GetShaderParameter("subsurface_scattering_strength").AsSingle() ?? 0,
            nativeSpecular = nativeSkinShader?.GetShaderParameter("native_specular_map").AsBool() ?? false,
            transparency = (material as BaseMaterial3D)?.Transparency.ToString(),
            alphaAa = (material as BaseMaterial3D)?.AlphaAntialiasingMode.ToString(), textures,
        };
    }
}
