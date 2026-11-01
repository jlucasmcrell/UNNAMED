using Godot;
using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Diagnostics;

/// <summary>CPU development timings for the real figure factory; never character acceptance evidence.</summary>
public partial class CharacterCreationProfile : Node
{
    public override async void _Ready()
    {
        try
        {
            if (DisplayServer.GetName() != "headless") throw new InvalidOperationException("CPU profile requires --headless");
            string[] args = OS.GetCmdlineUserArgs();
            string package = Path.GetFullPath(args[0]);
            VisualOptions.Parse("tier=ultra,people=humgen,body=none,textures=cache");
            OS.SetEnvironment("OTHERREACH_CHARACTER_ROOT", Path.Combine(package, "humgen_characters"));
            ArtLibrary.FallbackRoot = Path.Combine(package, "fallback");
            var art = new ArtLibrary(Path.Combine(package, "primary"), useTextureCache: true,
                buildSceneCache: args.Contains("--native-cache-build"));
            var bindings = ArtBindings.LoadWithOverlay(ArtBindings.ResourcePath, Path.Combine(package, "content", "_art_bindings.json"));
            var timings = new List<object>();
            string[] people = args.Length > 2 ? args[2].Split(',') : bindings.People.Keys.Order(StringComparer.Ordinal).ToArray();
            for (int round = 0; round < 2; round++)
                foreach (string entry in people)
                {
                    string[] fields = entry.Split('=');
                    string id = fields[0];
                    var watch = Stopwatch.StartNew();
                    int nativeBefore = art.NativeRiggedHits;
                    var look = bindings.Person(id, null, id);
                    var figure = SkinnedFigure.Create(art, bindings, id, body: fields.Length > 1 ? fields[1] : null, appearance: look)
                        ?? throw new InvalidDataException("Figure missing: " + id);
                    AddChild(figure);
                    double milliseconds = watch.Elapsed.TotalMilliseconds;
                    int nativeHits = art.NativeRiggedHits - nativeBefore;
                    if (round == 0 && args.Contains("--require-native") && nativeHits == 0)
                        throw new InvalidDataException("Required native cache did not load: " + id);
                    string geometry = GeometryFingerprint(figure);
                    string textures = TextureFingerprint(figure);
                    timings.Add(new { id, round, milliseconds, model = figure.ModelId, geometry, textures, nativeHits, memory = OS.GetStaticMemoryUsage() });
                    GD.Print($"CHARACTER_CREATE_PROFILE {round} {id} {figure.ModelId} {milliseconds:0.0} ms");
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    figure.Free();
                }
            bool mutationControls = false;
            if (args.Contains("--mutation-controls"))
            {
                var left = SkinnedFigure.Create(art, bindings, "npc.cairnwold.mara_ling")!;
                var right = SkinnedFigure.Create(art, bindings, "npc.cairnwold.mara_ling")!;
                AddChild(left); AddChild(right);
                var leftBody = left.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
                    .First(mesh => mesh.Name.ToString().EndsWith("_Body_LOD0", StringComparison.Ordinal));
                var rightBody = right.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
                    .First(mesh => mesh.Name.ToString().EndsWith("_Body_LOD0", StringComparison.Ordinal));
                var skinLeft = (ShaderMaterial)leftBody.GetSurfaceOverrideMaterial(0);
                var skinRight = (ShaderMaterial)rightBody.GetSurfaceOverrideMaterial(0);
                Color original = skinRight.GetShaderParameter("albedo").AsColor();
                skinLeft.SetShaderParameter("albedo", new Color(.123f, .234f, .345f));
                float originalShape = rightBody.GetBlendShapeValue(0);
                leftBody.SetBlendShapeValue(0, .37f);
                int bone = left.Skeleton.FindBone("Hips");
                var originalRotation = right.Skeleton.GetBonePoseRotation(bone);
                left.Skeleton.SetBonePoseRotation(bone, Quaternion.FromEuler(new Vector3(.1f, .2f, .3f)));
                if (skinLeft.GetInstanceId() == skinRight.GetInstanceId() || skinRight.GetShaderParameter("albedo").AsColor() != original
                    || rightBody.GetBlendShapeValue(0) != originalShape || right.Skeleton.GetBonePoseRotation(bone) != originalRotation)
                    throw new InvalidDataException("A cached actor's mutable skin/shape/bone state affected another actor");
                mutationControls = true;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                left.Free(); right.Free();
            }
            File.WriteAllText(args[1], JsonSerializer.Serialize(new { development_only = true, gpu = false, timings, mutationControls, artProblems = art.Problems }));
            CaptureQuit.At(this);
        }
        catch (Exception error) { GD.PushError(error.ToString()); CaptureQuit.At(this, 1); }
    }

    private static string TextureFingerprint(SkinnedFigure figure)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var images = new Dictionary<ulong, string>();
        foreach (var mesh in figure.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
                     .Where(mesh => mesh.Mesh is not null).OrderBy(mesh => mesh.Name.ToString(), StringComparer.Ordinal))
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                if (mesh.Mesh.SurfaceGetMaterial(surface) is BaseMaterial3D material)
                    for (int slot = 0; slot < (int)BaseMaterial3D.TextureParam.Max; slot++)
                        if (material.GetTexture((BaseMaterial3D.TextureParam)slot) is { } texture)
                        {
                            if (!images.TryGetValue(texture.GetInstanceId(), out string? fingerprint))
                            {
                                using var image = texture.GetImage();
                                if (image is null) throw new InvalidDataException("CPU texture readback failed");
                                fingerprint = image.GetWidth() + ":" + image.GetHeight() + ":" + image.GetFormat() + ":" + image.GetMipmapCount()
                                    + ":" + Convert.ToHexString(SHA256.HashData(image.GetData()));
                                images[texture.GetInstanceId()] = fingerprint;
                            }
                            hash.AppendData(Encoding.UTF8.GetBytes(mesh.Name + ":" + surface + ":" + slot + ":" + fingerprint + "\n"));
                        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string GeometryFingerprint(SkinnedFigure figure)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Text(string value) => hash.AppendData(Encoding.UTF8.GetBytes(value + "\n"));
        var rig = figure.Skeleton;
        for (int bone = 0; bone < rig.GetBoneCount(); bone++)
        {
            Text(rig.GetBoneName(bone) + ":" + rig.GetBoneParent(bone));
            hash.AppendData(GD.VarToBytes(rig.GetBoneRest(bone)));
        }
        foreach (var mesh in figure.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
                     .Where(mesh => mesh.Mesh is ArrayMesh).OrderBy(mesh => mesh.Name.ToString(), StringComparer.Ordinal))
        {
            var array = (ArrayMesh)mesh.Mesh;
            Text(mesh.Name + ":" + array.GetBlendShapeCount());
            hash.AppendData(GD.VarToBytes(mesh.Transform));
            if (mesh.Skin is { } skin)
                for (int bind = 0; bind < skin.GetBindCount(); bind++)
                {
                    Text(skin.GetBindName(bind) + ":" + skin.GetBindBone(bind));
                    hash.AppendData(GD.VarToBytes(skin.GetBindPose(bind)));
                }
            for (int shape = 0; shape < array.GetBlendShapeCount(); shape++) Text(array.GetBlendShapeName(shape));
            for (int surface = 0; surface < array.GetSurfaceCount(); surface++)
            {
                Text(array.SurfaceGetFormat(surface).ToString());
                using var vertices = array.SurfaceGetArrays(surface);
                hash.AppendData(GD.VarToBytes(vertices));
                var shapes = array.SurfaceGetBlendShapeArrays(surface);
                hash.AppendData(GD.VarToBytes(shapes));
                ((Godot.Collections.Array)shapes).Dispose();
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
