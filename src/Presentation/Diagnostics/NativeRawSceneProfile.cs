using Godot;
using System.Diagnostics;
using System.Text.Json;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Diagnostics;

/// <summary>CPU raw static/rigged parity in the actual exported importer, not gameplay evidence.</summary>
public partial class NativeRawSceneProfile : Node
{
    public override void _Ready()
    {
        try
        {
            if (DisplayServer.GetName() != "headless") throw new InvalidOperationException("CPU profile requires --headless");
            string[] args = OS.GetCmdlineUserArgs();
            string root = Path.GetFullPath(args[0]);
            bool referenceModelLevels = args.Contains("--reference-model-levels");
            if (referenceModelLevels && args.Contains("--require-native")) throw new InvalidDataException("Reference must exercise the original ModelLevels path");
            VisualOptions.Parse("tier=ultra,people=humgen,body=none,textures=cache");
            ArtLibrary.FallbackRoot = Path.Combine(root, "fallback");
            var art = new ArtLibrary(Path.Combine(root, "primary"), useTextureCache: true);
            string[] paths = JsonSerializer.Deserialize<string[]>(File.ReadAllText(args[1]))!;
            var rows = new List<object>();
            foreach (string path in paths)
            {
                int before = art.NativeRiggedHits;
                var watch = Stopwatch.StartNew();
                Node3D? model;
                if (referenceModelLevels)
                {
                    string id = Path.GetFileName(Path.GetDirectoryName(path))!;
                    string stem = Path.GetFileNameWithoutExtension(path);
                    int level = stem == id ? 0 : int.Parse(stem[(id.Length + 4)..]);
                    var levels = art.ModelLevels(id);
                    if (level >= levels.Count) throw new InvalidDataException("Original ModelLevels did not provide " + path);
                    model = levels[level];
                    foreach (var other in levels.Where(node => node != model)) other.Free();
                }
                else model = path.Contains('/') || path.Contains('\\') ? art.NativeStaticSource(path, requireSelected: args.Contains("--require-native")) : art.Rigged(path);
                if (model is null) throw new InvalidDataException("Raw scene missing: " + path);
                double milliseconds = watch.Elapsed.TotalMilliseconds;
                int hits = art.NativeRiggedHits - before;
                if (args.Contains("--require-native") && hits != 1) throw new InvalidDataException("Raw profile fell back to glTF: " + path);
                string geometry = CharacterCreationProfile.GeometryFingerprint(model);
                string textures = CharacterCreationProfile.TextureFingerprint(model);
                int occluders = model.FindChildren("*", nameof(OccluderInstance3D), true, false).Count;
                rows.Add(new { path, milliseconds, hits, geometry, textures, occluders });
                model.Free();
                art.ReleaseModels(path.Contains('/') || path.Contains('\\') ? Path.GetFileNameWithoutExtension(path) : path);
            }
            File.WriteAllText(args[2], JsonSerializer.Serialize(new { status = "PASS", development_only = true,
                gpu = false, packaged = OS.HasFeature("template"), referenceModelLevels, rows, artProblems = art.Problems }));
            CaptureQuit.At(this);
        }
        catch (Exception error) { GD.PushError(error.ToString()); CaptureQuit.At(this, 1); }
    }
}
