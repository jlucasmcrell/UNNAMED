using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Diagnostics;

/// <summary>Editor-only, headless preflight. Uses the production signed cache without changing source images.</summary>
public partial class PackageTextureCacheGate : Node
{
    public override void _Ready()
    {
        try
        {
            using var request = JsonDocument.Parse(File.ReadAllText(OS.GetCmdlineUserArgs()[0]));
            var caches = new Dictionary<string, TextureCache>();
            int count = 0;
            foreach (var row in request.RootElement.GetProperty("images").EnumerateArray())
            {
                string root = row.GetProperty("root").GetString()!, file = row.GetProperty("file").GetString()!;
                string key = row.GetProperty("key").GetString()!, format = row.GetProperty("format").GetString()!;
                if (!caches.TryGetValue(root, out var cache)) caches[root] = cache = new TextureCache(root, true);
                Image? decoded = null;
                try
                {
                    Image Decode()
                    {
                        if (row.GetProperty("offset").GetInt64() == 0) return decoded = Image.LoadFromFile(file);
                        using var stream = File.OpenRead(file);
                        stream.Position = row.GetProperty("offset").GetInt64();
                        byte[] bytes = new byte[row.GetProperty("length").GetInt32()]; stream.ReadExactly(bytes);
                        decoded = new Image();
                        var error = format switch {
                            "png" => decoded.LoadPngFromBuffer(bytes), "jpg" => decoded.LoadJpgFromBuffer(bytes),
                            "webp" => decoded.LoadWebpFromBuffer(bytes), _ => throw new InvalidDataException("Unsupported image " + format) };
                        if (error != Error.Ok) throw new InvalidDataException("Image decode failed " + key);
                        return decoded;
                    }
                    var texture = cache.For(key, file, Decode, row.GetProperty("role").GetString()!)
                        ?? throw new InvalidDataException("Texture cache build failed " + key);
                    using var image = texture.GetImage();
                    int width = row.GetProperty("width").GetInt32(), height = row.GetProperty("height").GetInt32();
                    if (!image.IsCompressed() || image.GetWidth() < width || image.GetWidth() > (width+3)/4*4
                        || image.GetHeight() < height || image.GetHeight() > (height+3)/4*4)
                        throw new InvalidDataException("Compression/resolution gate failed " + key);
                }
                finally { decoded?.Dispose(); }
                if (++count % 100 == 0) GD.Print("PACKAGE_TEXTURE_CACHE_PROGRESS " + count);
            }
            GD.Print("PACKAGE_TEXTURE_CACHE_PASS " + count);
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
