// UNNAMED Presentation - greybox materials
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>Flat greybox materials, one per kind of thing, so the hollow reads at a glance.</summary>
public static class Palette
{
    public static StandardMaterial3D Terrain { get; } = Flat(new Color(0.36f, 0.42f, 0.27f), roughness: 1f);
    public static StandardMaterial3D Wood { get; } = Flat(new Color(0.42f, 0.30f, 0.20f));
    public static StandardMaterial3D Roof { get; } = Flat(new Color(0.30f, 0.22f, 0.16f));
    public static StandardMaterial3D Door { get; } = Flat(new Color(0.55f, 0.22f, 0.16f));
    public static StandardMaterial3D Rock { get; } = Flat(new Color(0.45f, 0.45f, 0.47f));
    public static StandardMaterial3D Trunk { get; } = Flat(new Color(0.33f, 0.24f, 0.17f));
    public static StandardMaterial3D Canopy { get; } = Flat(new Color(0.20f, 0.33f, 0.18f));
    public static StandardMaterial3D Cliff { get; } = Flat(new Color(0.32f, 0.30f, 0.29f));
    public static StandardMaterial3D Skin { get; } = Flat(new Color(0.78f, 0.63f, 0.52f));
    public static StandardMaterial3D Cloth { get; } = Flat(new Color(0.28f, 0.33f, 0.45f));
    public static StandardMaterial3D Leather { get; } = Flat(new Color(0.35f, 0.25f, 0.18f));
    public static StandardMaterial3D Proxy { get; } = Flat(new Color(0.55f, 0.52f, 0.45f));
    public static StandardMaterial3D Metal { get; } = Flat(new Color(0.62f, 0.60f, 0.56f), roughness: 0.4f);
    public static StandardMaterial3D Ore { get; } = Flat(new Color(0.55f, 0.32f, 0.22f), roughness: 0.6f);
    public static StandardMaterial3D WorkedOut { get; } = Flat(new Color(0.40f, 0.38f, 0.37f));
    public static StandardMaterial3D AshBark { get; } = Flat(new Color(0.78f, 0.74f, 0.64f));
    public static StandardMaterial3D Leaves { get; } = Flat(new Color(0.36f, 0.48f, 0.26f));
    public static StandardMaterial3D Hearth { get; } = Flat(new Color(0.38f, 0.34f, 0.32f));
    public static StandardMaterial3D Iron { get; } = Flat(new Color(0.18f, 0.18f, 0.20f), roughness: 0.35f);
    public static StandardMaterial3D Shaft { get; } = Flat(new Color(0.60f, 0.50f, 0.36f));

    public static StandardMaterial3D Embers { get; } = new()
    {
        AlbedoColor = new Color(1f, 0.45f, 0.1f),
        EmissionEnabled = true,
        Emission = new Color(1f, 0.35f, 0.05f),
    };

    /// <summary>The fold that holds Tavar (M6): a faint violet haze, restrained (content bible §8).</summary>
    public static StandardMaterial3D Fold { get; } = new()
    {
        AlbedoColor = new Color(0.55f, 0.45f, 0.85f, 0.22f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        EmissionEnabled = true,
        Emission = new Color(0.25f, 0.18f, 0.45f),
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// <summary>A Quiet Stone turned into line (M6): a pale band round its top.</summary>
    public static StandardMaterial3D Aligned { get; } = new()
    {
        AlbedoColor = new Color(0.85f, 0.88f, 0.95f),
        EmissionEnabled = true,
        Emission = new Color(0.35f, 0.38f, 0.48f),
    };

    public static StandardMaterial3D Water { get; } = new()
    {
        AlbedoColor = new Color(0.20f, 0.35f, 0.45f, 0.75f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.1f,
    };

    private static StandardMaterial3D Flat(Color color, float roughness = 0.9f) => new() { AlbedoColor = color, Roughness = roughness };
}
