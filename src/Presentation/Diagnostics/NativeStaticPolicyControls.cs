using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Diagnostics;

/// <summary>Synthetic CPU controls against the original ModelLevels path; never art evidence.</summary>
public partial class NativeStaticPolicyControls : Node
{
    public override void _Ready()
    {
        try
        {
            if (DisplayServer.GetName() != "headless" || !OS.HasFeature("template"))
                throw new InvalidOperationException("Static policy controls require a headless exported template");
            string receipt = Path.GetFullPath(CpuDiagnostic.Arguments()[0]);
            string fixture = Path.Combine(Path.GetDirectoryName(receipt)!, "static-policy-" + Guid.NewGuid().ToString("N"));
            string primary = Path.Combine(fixture, "primary"), id = "bld_synthetic_native_policy";
            string folder = Path.Combine(primary, "ready", id);
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(Path.Combine(fixture, "fallback"));
            Directory.CreateDirectory(Path.Combine(primary, TextureCache.Folder));
            File.WriteAllText(Path.Combine(primary, TextureCache.Folder, "manifest.json"), "{\"entries\":{}}");
            var paths = new List<string>();
            for (int level = 0; level < 4; level++)
            {
                string name = id + (level == 0 ? "" : "_lod" + level);
                var root = new Node3D { Name = "SyntheticBuilding" };
                var mesh = new MeshInstance3D { Name = "OpaqueBox", Mesh = new BoxMesh
                    { Size = new Vector3(3 + level, 2, 2), Material = new StandardMaterial3D() } };
                root.AddChild(mesh); mesh.Owner = root;
                using var document = new GltfDocument();
                using var state = new GltfState();
                if (document.AppendFromScene(root, state) != Error.Ok
                    || document.WriteToFilesystem(state, Path.Combine(folder, name + ".glb")) != Error.Ok)
                    throw new InvalidDataException("Synthetic GLB generation failed");
                root.Free();
                paths.Add("primary/ready/" + id + "/" + name + ".glb");
            }
            VisualOptions.Parse("tier=ultra,people=humgen,body=none,textures=cache");
            ArtLibrary.FallbackRoot = Path.Combine(fixture, "fallback");
            var source = new ArtLibrary(primary, useTextureCache: true);
            var original = source.ModelLevels(id);
            if (original.Count != 4) throw new InvalidDataException("Original ModelLevels did not supply all four levels");
            var expected = original.Select(node => (geometry: CharacterCreationProfile.GeometryFingerprint(node),
                occluders: node.FindChildren("*", nameof(OccluderInstance3D), true, false).Count)).ToArray();
            foreach (var node in original) node.Free();
            if (expected[0].occluders == 0 || expected.Skip(1).Any(row => row.occluders != 0))
                throw new InvalidDataException("Original building LOD occluder policy changed");
            Directory.CreateDirectory(Path.Combine(primary, "manifests"));
            File.WriteAllText(Path.Combine(primary, "manifests", "native_static_sources.json"), JsonSerializer.Serialize(new { schema = 1, paths }));
            var builder = new ArtLibrary(primary, useTextureCache: true, buildSceneCache: true);
            foreach (string path in paths) { builder.NativeStaticSource(path)!.Free(); builder.ReleaseModels(id); }
            var cached = new ArtLibrary(primary, useTextureCache: true);
            var rows = new List<object>();
            for (int level = 0; level < 4; level++)
            {
                var node = cached.NativeStaticSource(paths[level]) ?? throw new InvalidDataException("Synthetic native model missing");
                string geometry = CharacterCreationProfile.GeometryFingerprint(node);
                int occluders = node.FindChildren("*", nameof(OccluderInstance3D), true, false).Count;
                if (geometry != expected[level].geometry || occluders != expected[level].occluders)
                    throw new InvalidDataException("Native cache differs from real ModelLevels at LOD" + level);
                rows.Add(new { level, geometry, occluders });
                node.Free();
            }
            if (cached.NativeRiggedHits != 4) throw new InvalidDataException("Synthetic control silently fell back");
            string proofFile = Directory.GetFiles(Path.Combine(primary, "character_native"), "*.proof.json", SearchOption.AllDirectories).First();
            string proof = File.ReadAllText(proofFile);
            if (!proof.Contains("static-template-v2", StringComparison.Ordinal)) throw new InvalidDataException("Static policy is not versioned v2");
            File.WriteAllText(proofFile, proof.Replace("static-template-v2", "static-template-v1", StringComparison.Ordinal));
            bool oldPolicyRejected = false;
            try
            {
                var stale = new ArtLibrary(primary, useTextureCache: true);
                foreach (string path in paths) stale.NativeStaticSource(path)!.Free();
            }
            catch (InvalidDataException) { oldPolicyRejected = true; }
            finally { File.WriteAllText(proofFile, proof); }
            if (!oldPolicyRejected) throw new InvalidDataException("Old static policy was accepted");
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { status = "PASS", development_only = true, gpu = false,
                packaged = true, fixture, reference = "ArtLibrary.ModelLevels", rows, nativeHits = cached.NativeRiggedHits, oldPolicyRejected }));
            CaptureQuit.At(this);
        }
        catch (Exception error) { GD.PushError(error.ToString()); CaptureQuit.At(this, 1); }
    }
}
