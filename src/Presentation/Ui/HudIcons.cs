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

    public HudIcons(AssetCatalog catalog, IReadOnlyDictionary<string, string> map)
    {
        _catalog = catalog;
        _map = map;
    }

    public static HudIcons None { get; } = new(AssetCatalog.Empty, new Dictionary<string, string>());

    public Texture2D? For(string? key) => key is not null && _map.TryGetValue(key, out string? id) ? _catalog.Icon(id) : null;

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
