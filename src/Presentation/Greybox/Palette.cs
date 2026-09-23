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

    public static StandardMaterial3D Water { get; } = new()
    {
        AlbedoColor = new Color(0.20f, 0.35f, 0.45f, 0.75f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.1f,
    };

    private static StandardMaterial3D Flat(Color color, float roughness = 0.9f) => new() { AlbedoColor = color, Roughness = roughness };
}
