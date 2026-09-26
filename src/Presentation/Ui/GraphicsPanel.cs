// UNNAMED Presentation - the start screen's graphics quality choice, saved for the next launch (Phase B, B1)
// Godot presentation only (D-11): how the game is drawn; nothing here is game state or part of a save

using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// Beside the start screen: the graphics quality - Low, Medium, High (the RTX 4070 Ti 1080p/60 target, the default) or Ultra - kept in
/// <c>user://graphics_tier.txt</c> (one word) and applied at the next launch (the world is built before the start screen shows). Harness runs never read it.
/// </summary>
public partial class GraphicsPanel : CanvasLayer
{
    public const string ConfigPath = "user://graphics_tier.txt";
    private static readonly string[] Tiers = { "low", "medium", "high", "ultra" };

    /// <summary>The tier the player saved, or null when none is (or the file cannot be read).</summary>
    public static string? SavedTier()
    {
        string path = ProjectSettings.GlobalizePath(ConfigPath);
        try
        {
            return File.Exists(path) && File.ReadAllText(path).Trim() is var tier && Tiers.Contains(tier) ? tier : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public override void _Ready()
    {
        Layer = 11;
        var box = new VBoxContainer { Position = new Vector2(40, 0), AnchorTop = 1, AnchorBottom = 1, OffsetTop = -110 };
        var label = new Label { Text = "Graphics quality (applies when the game is next started)" };
        label.AddThemeFontSizeOverride("font_size", 16);
        var choice = new OptionButton { CustomMinimumSize = new Vector2(220, 0) };
        choice.AddThemeFontSizeOverride("font_size", 17);
        foreach (string tier in Tiers)
            choice.AddItem(tier == "high" ? "High (recommended)" : char.ToUpperInvariant(tier[0]) + tier[1..]);
        choice.Selected = Array.IndexOf(Tiers, VisualOptions.Tier) is var now and >= 0 ? now : Array.IndexOf(Tiers, VisualOptions.DefaultTier);
        choice.ItemSelected += index =>
        {
            try
            {
                File.WriteAllText(ProjectSettings.GlobalizePath(ConfigPath), Tiers[index]);
            }
            catch (IOException e)
            {
                GD.PushWarning($"UNNAMED graphics: the choice could not be saved ({e.Message})");
            }
            label.Text = $"Graphics quality: {Tiers[index]} from the next start";
        };
        box.AddChild(label);
        box.AddChild(choice);
        AddChild(box);
    }
}
