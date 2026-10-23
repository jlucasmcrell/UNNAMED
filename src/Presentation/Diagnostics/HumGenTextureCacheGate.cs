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
            string body = Path.Combine(root, "rigged", id, id + "_rigged.glb");
            string before = Godot.FileAccess.GetSha256(body);
            ArtLibrary.BuildTextureCache = true;
            var art = new ArtLibrary(root, useTextureCache: true);
            var model = art.Rigged(id) ?? throw new InvalidDataException("Cannot load native body: " + id);
            model.Free();
            art.ReleaseModels(id);
            art.Cache.Save();
            if (art.Cache.Failed != 0 || art.Cache.Added + art.Cache.Hits == 0)
                throw new InvalidDataException("Native texture cache did not build: " + id);
            if (Godot.FileAccess.GetSha256(body) != before)
                throw new InvalidDataException("Body changed during cache build");
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { id, body_sha256 = before, art.Cache.Added,
                art.Cache.Hits, art.Cache.Failed, art.Cache.CompressedBytes,
                route = "Existing ArtLibrary/TextureCache native Godot BC7; original resolution", gpu = false }));
            GD.Print("HUMGEN_NATIVE_TEXTURE_CACHE_PASS " + id);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }
}
