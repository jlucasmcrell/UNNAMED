// UNNAMED Presentation - rock geometry dressing the scenery landforms beyond the walkable edge (Phase B, B0.2)
// Godot presentation only (D-11): scenery the body never reaches; placed by a seeded rule on the landform's own height function

using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// B0.2's proof: real rock meshes (Poly Haven CC0, prepared with LODs in the asset workspace) over the ravine's far face, rim and floor on
/// one side of the hollow, so the terrain renderer's smooth steep faces read as broken rock. Deterministic: a seeded generator along the
/// edge, heights from the same scenery function the terrain renderer draws (never read back from it). Nothing here is inside the
/// walkable region.
/// </summary>
public static class RavineDressing
{
    private const string CliffFace = "env_ph_mountainside";          // ~10 x 10.5 m, a one-sided shell facing +Z
    private const string RockShelf = "env_ph_coast_rocks_01";        // a 60 m rocky shelf, 90 % facing up
    private static readonly string[] Boulders = { "env_ph_boulder_01", "env_ph_namaqualand_boulder_02" };

    /// <summary>
    /// Dress the north side (the edge at <paramref name="region"/>'s far Z). <paramref name="height"/> is the scenery surface at (x, z);
    /// <paramref name="depth"/> maps a distance beyond the edge to the landform's profile. Returns how many pieces were placed.
    /// </summary>
    public static int DressNorth(Node3D parent, ArtLibrary art, Rect2 region, Func<float, float, float> height, ArtCoverage coverage)
    {
        var root = new Node3D { Name = "RavineDressing" };
        parent.AddChild(root);
        var random = new RandomNumberGenerator { Seed = 0x5EED_B002 };
        int placed = 0;
        float edge = region.End.Y;

        // The far face (26-40 m out, rising from the floor at -31 m to about +14 m): three staggered rows of cliff shells facing the hollow.
        (float Out, float Scale)[] rows = { (27.5f, 2.3f), (32.5f, 2.1f), (37f, 1.7f) };
        foreach (var (outward, scale) in rows)
        {
            for (float x = region.Position.X - 20f + random.RandfRange(0, 8); x < region.End.X + 20f; x += 17f + random.RandfRange(-3, 4))
            {
                float z = edge + outward + random.RandfRange(-1.5f, 1.5f);
                float s = scale * random.RandfRange(0.88f, 1.15f);
                // The shell's top edge is a clean cut: keep it under the ground a few metres further out, so the crag grows out of the
                // slope instead of standing on it as a block.
                // The lower rows stand out of the face as crags; the top row's cut edge would meet the skyline, so it stays buried.
                float y = outward < 35f ? height(x, z) - 1.5f * s : Mathf.Min(height(x, z) - 1.5f * s, height(x, z + 6f) - 1.0f - 10.5f * s);
                var tilt = new Vector3(random.RandfRange(-9, 4), 180f + random.RandfRange(-22, 22), random.RandfRange(-5, 5));
                if (Place(root, art, CliffFace, new Vector3(x, y, z), tilt, s))
                    placed++;
            }
        }
        // The rim: boulders just beyond the edge (a metre or more out, sunk to their waists), never inside the region.
        for (float x = region.Position.X + random.RandfRange(0, 10); x < region.End.X; x += random.RandfRange(7, 16))
        {
            float z = edge + random.RandfRange(1.2f, 4.5f);
            float s = random.RandfRange(0.9f, 2.2f);
            if (Place(root, art, Boulders[random.RandiRange(0, Boulders.Length - 1)], new Vector3(x, height(x, z) - 0.35f * s, z),
                    new Vector3(random.RandfRange(-8, 8), random.RandfRange(0, 360), random.RandfRange(-8, 8)), s))
                placed++;
        }
        // The floor: rocky shelves and scattered boulders along the gulf.
        for (float x = region.Position.X - 10f; x < region.End.X + 20f; x += random.RandfRange(38, 55))
        {
            float z = edge + random.RandfRange(12, 20);
            if (Place(root, art, RockShelf, new Vector3(x, height(x, z) - 1.2f, z), new Vector3(0, random.RandfRange(0, 360), 0), random.RandfRange(0.35f, 0.5f)))
                placed++;
        }
        coverage.Resolved("scenery", "ravine_north", $"{placed} rock pieces (Poly Haven CC0) on the north ravine");
        return placed;
    }

    private static bool Place(Node3D root, ArtLibrary art, string id, Vector3 position, Vector3 rotationDegrees, float scale)
    {
        if (art.ModelWithLods(id) is not { } model)
            return false;
        model.Position = position;
        model.RotationDegrees = rotationDegrees;
        model.Scale = Vector3.One * scale;
        root.AddChild(model);
        return true;
    }
}
