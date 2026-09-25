// UNNAMED Presentation - water surfaces in the scenery (Phase B, B0.8): a river on the ravine floor, drawn by Boujie's shader or the classic one
// Godot presentation only (D-11): a surface to look at; no water is simulated, and nothing reads it

using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// <c>--visual water=boujie|classic</c>: a river along the north ravine's floor (scenery beyond the walkable edge, seen from the edge),
/// drawn with Boujie Water (MIT, Zach Bernal, v1.0.1: an open-water shader - Gerstner waves, depth-fade shore foam, screen refraction)
/// or with the Phase-A stream material, so the two can be compared on the same surface. Boujie has no flow maps: rivers and creeks
/// need a flow layer of their own; this proof judges its surface, depth and foam.
/// </summary>
public static class WaterView
{
    private const string BoujieMaterial = "res://addons/boujie_water_shader/prefabs/outset_ocean_material.tres";

    public static Node3D? BuildRavineRiver(Node3D parent, Rect2 region, Func<float, float, float> height, out string? why)
    {
        why = null;
        Material? material = VisualOptions.Water switch
        {
            "boujie" => ResourceLoader.Exists(BoujieMaterial) ? GD.Load<Material>(BoujieMaterial)?.Duplicate() as Material : null,
            "classic" => Palette.StreamWater,
            _ => null,
        };
        if (material is null)
        {
            why = VisualOptions.Water == "boujie" ? "the Boujie water addon is not in the project" : null;
            return null;
        }
        if (material is ShaderMaterial shader && VisualOptions.Water == "boujie")
        {
            // A river in a rock gulf, not an ocean: muddier and greener, calmer, with the shore foam kept close.
            shader.SetShaderParameter("albedo", new Color(0.10f, 0.20f, 0.18f, 0.55f));
            shader.SetShaderParameter("shore_start_blend", 0.6f);
            shader.SetShaderParameter("shore_end_blend", 2.2f);
        }
        // The floor of the gulf, 12-20 m beyond the north edge; the surface sits just over the floor's low line.
        float z0 = region.End.Y + 11f, z1 = region.End.Y + 21f;
        float level = height(region.Position.X + region.Size.X / 2, (z0 + z1) / 2) + 0.9f;
        var river = new MeshInstance3D
        {
            Name = "RavineRiver",
            Mesh = new PlaneMesh { Size = new Vector2(region.Size.X + 60f, z1 - z0), SubdivideWidth = 160, SubdivideDepth = 16 },
            MaterialOverride = material,
            Position = new Vector3(region.Position.X + region.Size.X / 2, level, (z0 + z1) / 2),
        };
        parent.AddChild(river);
        return river;
    }
}
