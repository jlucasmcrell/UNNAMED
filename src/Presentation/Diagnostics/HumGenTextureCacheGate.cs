using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Diagnostics;

/// <summary>Offline editor-only entry point for the existing signed BC7 cache; never changes source GLBs.</summary>
public partial class HumGenTextureCacheGate : Node
{
    public override void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            string root = args[0], id = args[1], receipt = args[2];
            bool buildCache = args.Length < 4 || args[3] != "--face-only";
            string body = Path.Combine(root, "rigged", id, id + "_rigged.glb");
            string before = Godot.FileAccess.GetSha256(body);
            ArtLibrary.BuildTextureCache = buildCache;
            var art = new ArtLibrary(root, useTextureCache: true);
            var model = art.Rigged(id) ?? throw new InvalidDataException("Cannot load native body: " + id);
            bool textured = model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
                .Where(mesh => mesh.Mesh is not null).SelectMany(mesh => Enumerable.Range(0, mesh.Mesh.GetSurfaceCount())
                    .Select(surface => mesh.Mesh.SurfaceGetMaterial(surface))).OfType<BaseMaterial3D>()
                .Any(material => material.AlbedoTexture is not null || material.NormalTexture is not null || material.RoughnessTexture is not null);
            int ultraMeshes = 0;
            if (art.RiggedFile(id, "native_skinning.json") is { } skinningPath)
            {
                using var skinning = JsonDocument.Parse(File.ReadAllText(skinningPath));
                foreach (var row in skinning.RootElement.GetProperty("meshes").EnumerateArray())
                {
                    string name = row.GetProperty("mesh").GetString()!;
                    var mesh = model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().Single(m => m.Name == name);
                    if (mesh.Mesh is not ArrayMesh array || Enumerable.Range(0, array.GetSurfaceCount())
                        .Any(surface => (array.SurfaceGetFormat(surface) & Mesh.ArrayFormat.FlagUse8BoneWeights) == 0))
                        throw new InvalidDataException("Godot dropped native Ultra eight-weight data: " + id + "/" + name);
                    ultraMeshes++;
                }
            }
            int nativeInputs = 0;
            if (art.RiggedFile(id, "native_materials.json") is { } nativeManifest)
            {
                using var native = JsonDocument.Parse(File.ReadAllText(nativeManifest));
                foreach (var input in native.RootElement.EnumerateObject())
                {
                    if (input.Value.ValueKind != JsonValueKind.String) continue;
                    string path = art.RiggedFile(id, input.Value.GetString()!)
                        ?? throw new FileNotFoundException("Missing native input: " + input.Name);
                    string role = input.Name.Contains("normal", StringComparison.Ordinal) ? "normal" : "data";
                    if (art.Texture(path, role) is null) throw new InvalidDataException("Cannot cache native input: " + input.Name);
                    nativeInputs++;
                }
            }
            int faceBindings = 0, eyeBindings = 0;
            if (id.StartsWith("humgen_", StringComparison.Ordinal) && model.FindChildren("*", nameof(MeshInstance3D), true, false)
                .Any(mesh => mesh.Name.ToString().EndsWith("_Body_LOD0", StringComparison.Ordinal)))
            {
                // Exercise the same wrapper and exact-target motion path as real
                // gameplay; a raw imported scene is not a SkinnedModel.
                model.Free();
                VisualOptions.Parse("tier=ultra,people=humgen,body=none,textures=cache");
                OS.SetEnvironment("OTHERREACH_CHARACTER_ROOT", Path.GetFullPath(Path.Combine(root, "..", "humgen_characters")));
                model = SkinnedModel.Create(art, id, new Dictionary<string, string>())
                    ?? throw new InvalidDataException("Native character runtime wrapper missing: " + id);
                AddChild(model);
                var face = FaceDriver.Attach(model, id) ?? throw new InvalidDataException("Native face channels missing: " + id);
                AddChild(face);
                faceBindings = face.ChannelBindings; eyeBindings = face.NativeEyeBindings;
                string[] required = ["eyeBlinkLeft", "eyeBlinkRight", "jawOpen", "mouthClose", "mouthPucker", "mouthFunnel"];
                if (!face.NativeJawBound || eyeBindings != 2 || required.Except(face.BoundChannels).Any())
                    throw new InvalidDataException($"Native facial/eye/jaw bindings missing: {id}; jaw={face.NativeJawBound}, eyes={eyeBindings}, channels={string.Join(',', face.BoundChannels)}");
                face.Free();
            }
            model.Free();
            art.ReleaseModels(id);
            art.Cache.Save();
            if (art.Cache.Failed != 0 || buildCache && (textured || nativeInputs > 0) && art.Cache.Added + art.Cache.Hits == 0)
                throw new InvalidDataException("Native texture cache did not build: " + id);
            if (Godot.FileAccess.GetSha256(body) != before)
                throw new InvalidDataException("Body changed during cache build");
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { id, body_sha256 = before, art.Cache.Added,
                art.Cache.Hits, art.Cache.Failed, art.Cache.CompressedBytes, nativeInputs, ultraMeshes, faceBindings, eyeBindings,
                route = "Existing ArtLibrary/TextureCache native Godot BC7; original resolution", gpu = false }));
            GD.Print("HUMGEN_NATIVE_TEXTURE_CACHE_PASS " + id);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            CaptureQuit.At(this, 1);
        }
    }
}
