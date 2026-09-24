// UNNAMED Presentation - DeepSeek's generated HUD and effect art, found by manifest ID (the owner's M6 playtest)
// Godot presentation only: no gameplay state lives here (D-11)

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>A flipbook effect as its manifest describes it: an atlas of frames on a grid, played at a rate, once or looping.</summary>
public sealed record Flipbook(Texture2D Atlas, int Columns, int Rows, int Frames, double Fps, bool Loop, double? TtlSeconds, Color Tint, float Emission, bool Additive);

/// <summary>
/// The asset pipeline's Phase-1 art, read through its own interfaces and never copied into this public repository: the icon manifest
/// (<c>manifests/ui_icons.json</c>, an icon's sprite by its ID) and the effect manifest (<c>manifests/magic_vfx.json</c>, a flipbook by
/// its ID). The workspace is <c>--asset-root</c>, else <c>UNNAMED_ASSET_ROOT</c>, else the repository's own untracked <c>assets/</c>.
/// Without one - a fresh clone, CI - every caller draws its greybox instead.
/// </summary>
public sealed class AssetCatalog
{
    private readonly Dictionary<string, string> _icons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _effects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.Ordinal);

    private AssetCatalog(string? root) => Root = root;

    /// <summary>The workspace the art was read from, or null when there was none.</summary>
    public string? Root { get; }

    public static AssetCatalog Empty { get; } = new(null);

    public static AssetCatalog Load(string? explicitRoot, string repositoryRoot)
    {
        string? root = new[] { explicitRoot, System.Environment.GetEnvironmentVariable("UNNAMED_ASSET_ROOT"), Path.Combine(repositoryRoot, "assets") }
            .FirstOrDefault(r => !string.IsNullOrWhiteSpace(r) && File.Exists(Path.Combine(r, "manifests", "ui_icons.json")));
        if (root is null)
            return Empty;
        var catalog = new AssetCatalog(Path.GetFullPath(root));
        try
        {
            using (var icons = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifests", "ui_icons.json"))))
            {
                foreach (var icon in icons.RootElement.GetProperty("icons").EnumerateObject())
                {
                    if (icon.Value.TryGetProperty("sprite", out var sprite) && sprite.GetString() is { } path)
                        catalog._icons[icon.Name] = path;
                }
            }
            string effects = Path.Combine(root, "manifests", "magic_vfx.json");
            if (File.Exists(effects))
            {
                using var vfx = JsonDocument.Parse(File.ReadAllText(effects));
                foreach (var effect in vfx.RootElement.GetProperty("effects").EnumerateObject())
                    catalog._effects[effect.Name] = effect.Value.Clone();
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            GD.PushWarning($"UNNAMED assets: the manifests under {root} could not be read ({e.Message}); drawing greybox");
            return Empty;
        }
        return catalog;
    }

    /// <summary>An icon's sprite by its ID (<c>ui.hud.compass</c>), or null.</summary>
    public Texture2D? Icon(string id) => _icons.TryGetValue(id, out string? path) ? Texture(path) : null;

    /// <summary>An effect's flipbook by its ID (<c>vfx.force.impulse_bolt_travel</c>), or null.</summary>
    public Flipbook? Effect(string id)
    {
        if (!_effects.TryGetValue(id, out var effect) || !effect.TryGetProperty("atlas", out var atlas) || Texture(atlas.GetString()!) is not { } texture)
            return null;
        var grid = effect.GetProperty("grid");
        var blend = effect.TryGetProperty("blend", out var b) ? b : default;
        return new Flipbook(texture, grid.GetProperty("columns").GetInt32(), grid.GetProperty("rows").GetInt32(), effect.GetProperty("frame_count").GetInt32(),
            effect.GetProperty("fps").GetDouble(), effect.GetProperty("loop").GetBoolean(),
            effect.TryGetProperty("ttl_s", out var ttl) && ttl.ValueKind == JsonValueKind.Number ? ttl.GetDouble() : null,
            blend.ValueKind == JsonValueKind.Object && blend.TryGetProperty("tint", out var tint) ? Color.FromHtml(tint.GetString()!) : Colors.White,
            blend.ValueKind == JsonValueKind.Object && blend.TryGetProperty("emission_energy", out var energy) ? (float)energy.GetDouble() : 1f,
            blend.ValueKind != JsonValueKind.Object || !blend.TryGetProperty("blend_mode", out var mode) || mode.GetString() == "add");
    }

    private Texture2D? Texture(string relative)
    {
        if (Root is null)
            return null;
        if (_textures.TryGetValue(relative, out var cached))
            return cached;
        var image = Image.LoadFromFile(Path.Combine(Root, relative));
        var texture = image is null || image.IsEmpty() ? null : ImageTexture.CreateFromImage(image);
        _textures[relative] = texture;
        return texture;
    }
}
