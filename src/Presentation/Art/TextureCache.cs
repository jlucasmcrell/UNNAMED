// UNNAMED Presentation - the asset workspace's textures, compressed once and shared (Phase B, B1: texture compression and LOD texture sharing)
// Godot presentation only (D-11): a cache of how images are uploaded; what is drawn is the same picture

using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// The runtime glTF path and the loose material maps upload every texture as uncompressed RGBA8, and every level of a model (each LOD file
/// embeds its own copy) uploads it again. This cache holds each distinct image once, block-compressed (BC7, a quarter of RGBA8) with its
/// mip chain - alpha-tested colour maps keep their coverage down the chain - in <c>texture_cache/&lt;hash&gt;.dds</c>, and a manifest from
/// each source (a model file and its image index, or a map file) to its entry, checked against the source's size and time. A source not in
/// the manifest, or changed since, loads as before. Compressing needs the editor binary (<see cref="Image.Compress"/> is editor-only):
/// run any harness with <c>--texture-cache</c> and every texture it loads is added; the exported game only reads.
/// </summary>
public sealed class TextureCache
{
    public const string Folder = "texture_cache";
    private const string ManifestName = "manifest.json";

    private sealed record Entry(string Dds, long Size, long Ticks, string Role);

    private readonly string? _root;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D?> _loaded = new(StringComparer.Ordinal);
    private bool _dirty;

    /// <summary>True when this run adds what it loads to the cache (<c>--texture-cache</c>, editor binary).</summary>
    public bool Building { get; }

    public int Hits { get; private set; }
    public int Added { get; private set; }
    public int Failed { get; private set; }

    /// <summary>What the cached textures take on the GPU (their files' payload, mips included).</summary>
    public long CompressedBytes { get; private set; }

    public TextureCache(string? root, bool building)
    {
        _root = root;
        Building = building && root is not null;
        if (root is null)
            return;
        string manifest = Path.Combine(root, Folder, ManifestName);
        if (!File.Exists(manifest))
            return;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(manifest));
            foreach (var e in json.RootElement.GetProperty("entries").EnumerateObject())
                _entries[e.Name] = new Entry(e.Value.GetProperty("dds").GetString()!, e.Value.GetProperty("size").GetInt64(),
                    e.Value.GetProperty("ticks").GetInt64(), e.Value.GetProperty("role").GetString()!);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            GD.PushWarning($"UNNAMED texture cache: the manifest could not be read ({e.Message}); loading uncompressed");
            _entries.Clear();
        }
    }

    /// <summary>The key of a source: a path under the workspace, with the image's index for a model file.</summary>
    public static string Key(string relative, int image = -1) => (image >= 0 ? $"{relative}#{image}" : relative).Replace('\\', '/');

    /// <summary>
    /// The cached texture for <paramref name="key"/> (whose source file is <paramref name="source"/>), or - while building - the texture
    /// <paramref name="image"/> compressed now and added; null when there is none and nothing is built (the caller uploads as before).
    /// </summary>
    public Texture2D? For(string key, string source, Func<Image?> image, string role)
    {
        if (_root is null)
            return null;
        var info = new FileInfo(source);
        if (!info.Exists)
            return null;
        if (_entries.TryGetValue(key, out var entry) && entry.Size == info.Length && entry.Ticks == info.LastWriteTimeUtc.Ticks
            && Load(entry.Dds) is { } cached)
        {
            Hits++;
            return cached;
        }
        if (!Building || image() is not { } picture)
            return null;
        string? dds = Compress(picture, role);
        if (dds is null)
        {
            Failed++;
            return null;
        }
        _entries[key] = new Entry(dds, info.Length, info.LastWriteTimeUtc.Ticks, role);
        _dirty = true;
        Added++;
        Save();
        return Load(dds);
    }

    /// <summary>A mip-chained BC7 copy of <paramref name="picture"/> in the cache, named by its content; null when it cannot be made.</summary>
    private string? Compress(Image picture, string role)
    {
        var image = (Image)picture.Duplicate();
        if (image.IsCompressed() && image.Decompress() != Error.Ok)
            return null;
        image.ClearMipmaps();
        image.Convert(Image.Format.Rgba8);
        string name = Convert.ToHexString(SHA256.HashData(image.GetData()))[..24].ToLowerInvariant() + $".{role}.dds";
        string path = Path.Combine(_root!, Folder, name);
        if (File.Exists(path))
            return name;
        // An alpha-tested colour map keeps its coverage down the chain; plain averaging thins leaves and fringes to nothing with distance.
        image = role == "cutout" ? ImageMips.Coverage(image, 0.5f) : WithMips(image);
        var source = role switch { "normal" => Image.CompressSource.Normal, "color" or "cutout" => Image.CompressSource.Srgb, _ => Image.CompressSource.Generic };
        if (image.Compress(Image.CompressMode.Bptc, source) != Error.Ok)
            return null;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return image.SaveDds(path) == Error.Ok ? name : null;
    }

    private static Image WithMips(Image image)
    {
        image.GenerateMipmaps();
        return image;
    }

    private Texture2D? Load(string name)
    {
        if (_loaded.TryGetValue(name, out var texture))
            return texture;
        texture = null;
        string path = Path.Combine(_root!, Folder, name);
        if (File.Exists(path))
        {
            var image = new Image();
            if (image.LoadDdsFromBuffer(File.ReadAllBytes(path)) == Error.Ok && !image.IsEmpty())
            {
                texture = ImageTexture.CreateFromImage(image);
                CompressedBytes += image.GetData().Length;
            }
        }
        _loaded[name] = texture;
        return texture;
    }

    /// <summary>Write the manifest when this run added to it.</summary>
    public void Save()
    {
        if (!_dirty || _root is null)
            return;
        var entries = _entries.OrderBy(e => e.Key, StringComparer.Ordinal).ToDictionary(e => e.Key,
            e => new Dictionary<string, object> { ["dds"] = e.Value.Dds, ["size"] = e.Value.Size, ["ticks"] = e.Value.Ticks, ["role"] = e.Value.Role });
        var document = new Dictionary<string, object>
        {
            ["comment"] = "Phase B texture cache (TextureCache.cs): each source image (a model file's image index, or a map file) to its BC7 DDS copy.",
            ["entries"] = entries,
        };
        Directory.CreateDirectory(Path.Combine(_root, Folder));
        File.WriteAllText(Path.Combine(_root, Folder, ManifestName), JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        _dirty = false;
    }
}

/// <summary>Mip chains for alpha-tested images (shared by the scatter's atlases and the texture cache).</summary>
public static class ImageMips
{
    /// <summary>
    /// A mip chain whose alpha keeps the base level's alpha-tested coverage: plain averaging thins cut-out foliage to nothing with
    /// distance; each level's alpha is scaled until as much of it passes <paramref name="cutoff"/> as of the base.
    /// </summary>
    public static Image Coverage(Image source, float cutoff)
    {
        int w = source.GetWidth(), h = source.GetHeight();
        float target = Covered(source.GetData(), cutoff, 1f);
        var data = new List<byte>(source.GetData());
        int lw = w, lh = h;
        while (lw > 1 || lh > 1)
        {
            lw = Math.Max(1, lw / 2);
            lh = Math.Max(1, lh / 2);
            var level = (Image)source.Duplicate();
            level.Resize(lw, lh, Image.Interpolation.Lanczos);
            var bytes = level.GetData();
            float lo = 0.5f, hi = 4f;
            for (int i = 0; i < 16; i++)
            {
                float mid = (lo + hi) / 2;
                if (Covered(bytes, cutoff, mid) < target)
                    lo = mid;
                else
                    hi = mid;
            }
            float scale = (lo + hi) / 2;
            for (int i = 3; i < bytes.Length; i += 4)
                bytes[i] = (byte)Math.Clamp(bytes[i] * scale, 0, 255);
            data.AddRange(bytes);
        }
        return Image.CreateFromData(w, h, true, Image.Format.Rgba8, data.ToArray());
    }

    private static float Covered(byte[] rgba, float cutoff, float scale)
    {
        int n = 0, pass = 0;
        for (int i = 3; i < rgba.Length; i += 4, n++)
        {
            if (rgba[i] / 255f * scale >= cutoff)
                pass++;
        }
        return n == 0 ? 0 : (float)pass / n;
    }
}
