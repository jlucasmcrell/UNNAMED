// UNNAMED Presentation - rock geometry dressing the scenery landforms beyond the walkable edge (Phase B, B0.2 / B2)
// Godot presentation only (D-11): scenery the body never reaches; placed by a seeded rule on the landform's own height function

using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// Real rock meshes (Poly Haven CC0, prepared with LODs in the asset workspace) over the landforms round the hollow, so the terrain
/// renderer's smooth steep faces read as broken rock: on the ravine's three sides (north, west, east) its far face, rim and floor; on the
/// south, the cliff rising from the edge. Deterministic: a seeded generator along each edge (each side its own seed), heights from the same
/// scenery function the terrain renderer draws (never read back from it). Nothing is inside the walkable region: every piece stands far
/// enough out that its own depth stays beyond the edge.
/// </summary>
public static class RavineDressing
{
    private const string CliffFace = "env_ph_mountainside";          // 10.2 x 10.5 m, 5.25 m deep, a one-sided shell facing +Z
    private const string CliffWall = "env_ph_coastal_cliff_01";      // a 92 m cliff run, 10.3 m tall, 11 m deep, facing +Z
    private const string RockShelf = "env_ph_coast_rocks_01";        // a 60 m rocky shelf, 90 % facing up
    private static readonly string[] Boulders = { "env_ph_boulder_01", "env_ph_namaqualand_boulder_02" };

    /// <summary>An edge of the region: where it is, which way is out, and which way the far side must turn to face the hollow.</summary>
    private readonly record struct Edge(string Name, float From, float To, Func<float, float, Vector3> At, float FacingYaw, ulong Seed);

    /// <summary>Dress every edge; returns how many pieces were placed. <paramref name="height"/> is the scenery surface at (x, z).</summary>
    public static int Dress(Node3D parent, ArtLibrary art, Rect2 region, Func<float, float, float> height, ArtCoverage coverage)
    {
        var root = new Node3D { Name = "RavineDressing" };
        parent.AddChild(root);
        float x0 = region.Position.X, x1 = region.End.X, z0 = region.Position.Y, z1 = region.End.Y;
        // (along the edge, out from it) to the world; the far side faces back towards the hollow. The north keeps B0.2's seed and placing.
        var north = new Edge("north", x0, x1, (a, o) => new Vector3(a, 0, z1 + o), 180f, 0x5EED_B002);
        var west = new Edge("west", z0, z1, (a, o) => new Vector3(x0 - o, 0, a), 90f, 0x5EED_B003);
        var east = new Edge("east", z0, z1, (a, o) => new Vector3(x1 + o, 0, a), -90f, 0x5EED_B004);
        var south = new Edge("south", x0, x1, (a, o) => new Vector3(a, 0, z0 - o), 0f, 0x5EED_B005);
        int placed = 0;
        foreach (var edge in new[] { north, west, east })
        {
            int n = Ravine(root, art, edge, height);
            coverage.Resolved("scenery", "ravine_" + edge.Name, $"{n} rock pieces (Poly Haven CC0) on the {edge.Name} ravine");
            placed += n;
        }
        int s = Cliff(root, art, south, height);
        coverage.Resolved("scenery", "cliff_south", $"{s} rock pieces (Poly Haven CC0) on the south cliff");
        return placed + s;
    }

    /// <summary>A ravine side: the far face (26-40 m out, from the floor at -31 m to about +14 m), the rim, and the floor.</summary>
    private static int Ravine(Node3D root, ArtLibrary art, Edge edge, Func<float, float, float> height)
    {
        var random = new RandomNumberGenerator { Seed = edge.Seed };
        int placed = 0;
        Vector3 Ground(float along, float outward)
        {
            var p = edge.At(along, outward);
            return new Vector3(p.X, height(p.X, p.Z), p.Z);
        }
        // Three staggered rows of cliff shells facing the hollow. The shell's top edge is a clean cut: the lower rows stand out of the face
        // as crags; the top row's cut edge would meet the skyline, so it stays buried a few metres further out.
        (float Out, float Scale)[] rows = { (27.5f, 2.3f), (32.5f, 2.1f), (37f, 1.7f) };
        foreach (var (outward, scale) in rows)
        {
            for (float a = edge.From - 20f + random.RandfRange(0, 8); a < edge.To + 20f; a += 17f + random.RandfRange(-3, 4))
            {
                float o = outward + random.RandfRange(-1.5f, 1.5f);
                float s = scale * random.RandfRange(0.88f, 1.15f);
                var at = Ground(a, o);
                float y = outward < 35f ? at.Y - 1.5f * s : Mathf.Min(at.Y - 1.5f * s, Ground(a, o + 6f).Y - 1.0f - 10.5f * s);
                var tilt = new Vector3(random.RandfRange(-9, 4), edge.FacingYaw + random.RandfRange(-22, 22), random.RandfRange(-5, 5));
                if (Place(root, art, CliffFace, new Vector3(at.X, y, at.Z), tilt, s))
                    placed++;
            }
        }
        // The rim: boulders just beyond the edge (a metre or more out, sunk to their waists), never inside the region.
        for (float a = edge.From + random.RandfRange(0, 10); a < edge.To; a += random.RandfRange(7, 16))
        {
            float s = random.RandfRange(0.9f, 2.2f);
            var at = Ground(a, random.RandfRange(1.2f, 4.5f));
            if (Place(root, art, Boulders[random.RandiRange(0, Boulders.Length - 1)], at - new Vector3(0, 0.35f * s, 0),
                    new Vector3(random.RandfRange(-8, 8), random.RandfRange(0, 360), random.RandfRange(-8, 8)), s))
                placed++;
        }
        // The floor: rocky shelves along the gulf.
        for (float a = edge.From - 10f; a < edge.To + 20f; a += random.RandfRange(38, 55))
        {
            var at = Ground(a, random.RandfRange(12, 20));
            if (Place(root, art, RockShelf, at - new Vector3(0, 1.2f, 0), new Vector3(0, random.RandfRange(0, 360), 0), random.RandfRange(0.35f, 0.5f)))
                placed++;
        }
        return placed;
    }

    /// <summary>
    /// The south cliff: long cliff runs standing on the rising face (their 11 m depth kept beyond the edge), overlapping end to end, and
    /// a scree of boulders at their foot.
    /// </summary>
    private static int Cliff(Node3D root, ArtLibrary art, Edge edge, Func<float, float, float> height)
    {
        var random = new RandomNumberGenerator { Seed = edge.Seed };
        int placed = 0;
        Vector3 Ground(float along, float outward)
        {
            var p = edge.At(along, outward);
            return new Vector3(p.X, height(p.X, p.Z), p.Z);
        }
        for (float a = edge.From - 30f + random.RandfRange(0, 12); a < edge.To + 30f; a += 62f + random.RandfRange(-6, 8))
        {
            float s = random.RandfRange(1.35f, 1.65f);
            // Half the run's depth at this scale plus a metre: its face stands clear of the walkable edge.
            float o = 5.48f * s + 1.2f + random.RandfRange(0, 1.5f);
            var at = Ground(a, o);
            var turn = new Vector3(random.RandfRange(-4, 2), edge.FacingYaw + random.RandfRange(-5, 5), random.RandfRange(-2, 2));
            if (Place(root, art, CliffWall, at - new Vector3(0, 2.0f * s, 0), turn, s))
                placed++;
        }
        for (float a = edge.From + random.RandfRange(0, 8); a < edge.To; a += random.RandfRange(6, 13))
        {
            float s = random.RandfRange(0.7f, 1.8f);
            var at = Ground(a, random.RandfRange(1.4f, 3.8f));
            if (Place(root, art, Boulders[random.RandiRange(0, Boulders.Length - 1)], at - new Vector3(0, 0.3f * s, 0),
                    new Vector3(random.RandfRange(-10, 10), random.RandfRange(0, 360), random.RandfRange(-10, 10)), s))
                placed++;
        }
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
