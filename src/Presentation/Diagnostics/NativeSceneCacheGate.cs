using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Diagnostics;

/// <summary>Build or verify portable raw rigged caches on CPU; never a visual acceptance scene.</summary>
public partial class NativeSceneCacheGate : Node
{
    public override void _Ready()
    {
        try
        {
            if (DisplayServer.GetName() != "headless") throw new InvalidOperationException("Native cache gate requires --headless");
            string[] args = OS.GetCmdlineUserArgs();
            string root = Path.GetFullPath(args[0]);
            bool build = args.Contains("--build");
            if (build && OS.HasFeature("template")) throw new InvalidOperationException("Build caches with the matching editor, verify them in the package");
            string[] models = JsonSerializer.Deserialize<string[]>(File.ReadAllText(args[1]))
                ?? throw new InvalidDataException("Missing native cache model list");
            if (models.Length == 0) throw new InvalidDataException("Empty native cache model list");
            VisualOptions.Parse("tier=ultra,people=humgen,body=none,textures=cache");
            ArtLibrary.FallbackRoot = Path.Combine(root, "fallback");
            var art = new ArtLibrary(Path.Combine(root, "primary"), useTextureCache: true, buildSceneCache: build);
            foreach (string id in models.Distinct(StringComparer.Ordinal))
            {
                int before = art.NativeRiggedHits;
                var model = art.Rigged(id) ?? throw new InvalidDataException("Native cache model missing: " + id);
                model.Free();
                if (!build && art.NativeRiggedHits == before) throw new InvalidDataException("Required native cache missing: " + id);
                art.ReleaseModels(id);
                GD.Print("NATIVE_RIGGED_CACHE_" + (build ? "BUILT " : "VERIFIED ") + id);
            }
            if (art.Problems.Count != 0) throw new InvalidDataException("Art problems during native cache gate");
            File.WriteAllText(args[2], JsonSerializer.Serialize(new { status = "PASS", development_only = true, gpu = false,
                packaged = OS.HasFeature("template"), build, models, nativeHits = art.NativeRiggedHits }));
            CaptureQuit.At(this);
        }
        catch (Exception error) { GD.PushError(error.ToString()); CaptureQuit.At(this, 1); }
    }
}
