// UNNAMED Presentation - the HUD's icons: a game key (a pool, a formula, an effect, an item) to the asset pipeline's icon by its ID
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The icon for what the HUD shows - a pool (<c>health</c>), a formula, an effect, an item, a companion order - through the art
/// bindings' <c>icons</c> map to the icon manifest's ID (<c>ui.resource.health</c>). No icon, no picture: every caller keeps its text.
/// The icons are opaque painted tiles (the manifest's <c>alpha: none</c>), drawn framed.
/// </summary>
public sealed class HudIcons
{
    private readonly AssetCatalog _catalog;
    private readonly IReadOnlyDictionary<string, string> _map;
    private readonly Art.ArtCoverage? _coverage;

    public HudIcons(AssetCatalog catalog, IReadOnlyDictionary<string, string> map, Art.ArtCoverage? coverage = null)
    {
        _catalog = catalog;
        _map = map;
        _coverage = coverage;
    }

    public static HudIcons None { get; } = new(AssetCatalog.Empty, new Dictionary<string, string>());

    /// <summary>The icon for a key, or null (recorded in the coverage report: an icon the HUD asked for and drew without).</summary>
    public Texture2D? For(string? key)
    {
        if (key is null)
            return null;
        if (!_map.TryGetValue(key, out string? id))
        {
            _coverage?.Fallback("icon", key, "no icon bound: its text stands alone, or an empty tile");
            return null;
        }
        var icon = _catalog.Icon(id);
        if (icon is null)
            _coverage?.Fallback("icon", key, "its icon is not in the icon manifest, or would not load", id);
        else
            _coverage?.Resolved("icon", key, id);
        return icon;
    }

    /// <summary>An icon tile of a size, or an empty space of that size when there is no icon (so rows stay aligned).</summary>
    public Control Tile(string? key, float size)
    {
        var texture = For(key);
        return new TextureRect
        {
            Texture = texture,
            CustomMinimumSize = new Vector2(size, size),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }
}
