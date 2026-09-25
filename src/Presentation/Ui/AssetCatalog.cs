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
    /// <summary>An effect's manifest entry, read and checked once at load: everything a flipbook needs but its texture.</summary>
    private sealed record EffectEntry(string Atlas, int Columns, int Rows, int Frames, double Fps, bool Loop, double? TtlSeconds, Color Tint,
        float Emission, bool Additive);

    private readonly Dictionary<string, string> _icons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EffectEntry> _effects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.Ordinal);
    private readonly List<string> _problems = new();

    private AssetCatalog(string? root) => Root = root;

    /// <summary>The workspace the art was read from, or null when there was none.</summary>
    public string? Root { get; }

    /// <summary>
    /// Manifest entries withheld at load because they are malformed, each with why: what they named is drawn in greybox. The manifests
    /// are generated elsewhere, and an entry read only when a bolt flies would throw inside the tick (the Phase-1 technical audit, H-02).
    /// </summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>The Strain overlay's curve (the effect manifest's <c>strain_feedback</c>): Strain as a share of tolerance to the overlay's alpha.</summary>
    public IReadOnlyList<(double Strain, double Alpha)> StrainOverlay { get; private set; } = Array.Empty<(double, double)>();

    /// <summary>The pulse added to the overlay at full Strain, in hertz (the manifest: from 0.65 up).</summary>
    public double StrainPulseHz { get; private set; }

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
                    if (icon.Value.ValueKind == JsonValueKind.Object && icon.Value.TryGetProperty("sprite", out var sprite)
                        && sprite.ValueKind == JsonValueKind.String && sprite.GetString() is { Length: > 0 } path)
                        catalog._icons[icon.Name] = path;
                    else
                        catalog._problems.Add($"{icon.Name}: no sprite path");
                }
            }
            string effects = Path.Combine(root, "manifests", "magic_vfx.json");
            if (File.Exists(effects))
            {
                using var vfx = JsonDocument.Parse(File.ReadAllText(effects));
                foreach (var effect in vfx.RootElement.GetProperty("effects").EnumerateObject())
                {
                    if (ReadEffect(effect.Value, out string? why) is { } entry)
                        catalog._effects[effect.Name] = entry;
                    else
                        catalog._problems.Add($"{effect.Name}: {why}");
                }
                if (vfx.RootElement.TryGetProperty("strain_feedback", out var strain))
                {
                    if (ReadStrainOverlay(strain) is { } points)
                    {
                        catalog.StrainOverlay = points;
                        catalog.StrainPulseHz = strain.TryGetProperty("pulse_hz_at_max", out var hz) && hz.ValueKind == JsonValueKind.Number ? hz.GetDouble() : 0;
                    }
                    else
                    {
                        catalog._problems.Add("strain_feedback: its points are not (strain, overlay_alpha) numbers");
                    }
                }
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

    /// <summary>An effect's flipbook by its ID (<c>vfx.force.impulse_bolt_travel</c>), or null. Its entry was checked at load.</summary>
    public Flipbook? Effect(string id) =>
        _effects.TryGetValue(id, out var e) && Texture(e.Atlas) is { } texture
            ? new Flipbook(texture, e.Columns, e.Rows, e.Frames, e.Fps, e.Loop, e.TtlSeconds, e.Tint, e.Emission, e.Additive)
            : null;

    /// <summary>An effect's entry, or null with why: a grid of at least one cell holding its frames, a positive rate, a real loop flag.</summary>
    private static EffectEntry? ReadEffect(JsonElement effect, out string? why)
    {
        why = null;
        if (effect.ValueKind != JsonValueKind.Object)
            return Refuse("not an object", out why);
        if (!effect.TryGetProperty("atlas", out var atlas) || atlas.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(atlas.GetString()))
            return Refuse("no atlas path", out why);
        if (!effect.TryGetProperty("grid", out var grid) || grid.ValueKind != JsonValueKind.Object)
            return Refuse("no grid", out why);
        int columns = Int(grid, "columns"), rows = Int(grid, "rows"), frames = Int(effect, "frame_count");
        if (columns < 1 || rows < 1)
            return Refuse($"a grid of {columns} x {rows}", out why);
        if (frames < 1 || frames > columns * rows)
            return Refuse($"{frames} frames on a {columns} x {rows} grid", out why);
        double fps = effect.TryGetProperty("fps", out var rate) && rate.ValueKind == JsonValueKind.Number ? rate.GetDouble() : double.NaN;
        if (!(fps >= 0) || double.IsInfinity(fps) || (fps == 0 && frames > 1))   // a still (one frame) may play at 0
            return Refuse(frames > 1 ? "no positive fps for its frames" : "no fps", out why);
        if (!effect.TryGetProperty("loop", out var loop) || loop.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return Refuse("loop is not true or false", out why);
        double? ttl = null;
        if (effect.TryGetProperty("ttl_s", out var life) && life.ValueKind == JsonValueKind.Number)
            ttl = life.GetDouble() is var seconds && seconds >= 0 ? seconds : null;
        var tint = Colors.White;
        float emission = 1f;
        bool additive = true;
        if (effect.TryGetProperty("blend", out var blend) && blend.ValueKind == JsonValueKind.Object)
        {
            if (blend.TryGetProperty("tint", out var t))
            {
                if (t.ValueKind != JsonValueKind.String || !Color.HtmlIsValid(t.GetString()!))
                    return Refuse("its tint is not a colour", out why);
                tint = Color.FromHtml(t.GetString()!);
            }
            if (blend.TryGetProperty("emission_energy", out var energy))
            {
                if (energy.ValueKind != JsonValueKind.Number)
                    return Refuse("its emission is not a number", out why);
                emission = (float)energy.GetDouble();
            }
            if (blend.TryGetProperty("blend_mode", out var mode))
                additive = mode.ValueKind != JsonValueKind.String || mode.GetString() == "add";
        }
        return new EffectEntry(atlas.GetString()!, columns, rows, frames, fps, loop.GetBoolean(), ttl, tint, emission, additive);
    }

    private static EffectEntry? Refuse(string reason, out string? why)
    {
        why = reason;
        return null;
    }

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n) ? n : -1;

    private static List<(double Strain, double Alpha)>? ReadStrainOverlay(JsonElement strain)
    {
        if (strain.ValueKind != JsonValueKind.Object || !strain.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Array)
            return null;
        var read = new List<(double, double)>();
        foreach (var point in points.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Object || !point.TryGetProperty("strain", out var s) || s.ValueKind != JsonValueKind.Number
                || !point.TryGetProperty("overlay_alpha", out var a) || a.ValueKind != JsonValueKind.Number)
                return null;
            read.Add((s.GetDouble(), a.GetDouble()));
        }
        return read.OrderBy(p => p.Item1).ToList();
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
