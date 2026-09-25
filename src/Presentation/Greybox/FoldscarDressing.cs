// UNNAMED Presentation - the Foldscar's ground: shards of the thing the heart is part of, and stones that do not quite rest (Phase B, B9)
// Godot presentation only (D-11): small, non-colliding dressing placed by a seeded rule; nothing here is read by the game

using Godot;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// The science-fantasy language at the Foldscar, used with restraint (<c>--visual foldscar=proof</c>): shards of the heart's own kind (the
/// same sculptor's split monolith, small, lying half-buried and wearing the same dark polished stone) scattered round it, so the heart reads
/// as part of something larger and broken rather than a prop; and a few pebbles that rest a hand's breadth above the ground - still, not
/// glowing, not turning: a fact about them, not an effect. All small and off the paths (they do not collide), and never inside the fold.
/// </summary>
public static class FoldscarDressing
{
    private const string Shard = "scifi_artifact_b";           // zonked's split monolith (CC0), 2.1 x 2.1 x 3.9 m as authored
    private const string Pebble = "env_ph_boulder_01";

    public static int Dress(Node3D parent, ArtLibrary art, GroundField ground, Vector2 heart, Vector2 fold, float foldRadius)
    {
        var root = new Node3D { Name = "FoldscarDressing" };
        parent.AddChild(root);
        var random = new RandomNumberGenerator { Seed = 0xF01D5CA7 };
        var onyx = art.WorldMaps("mat_acg_onyx015");
        int placed = 0;

        Vector2? Spot(float from, float to)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                float angle = random.RandfRange(0, Mathf.Tau), distance = random.RandfRange(from, to);
                var p = heart + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
                if (p.DistanceTo(fold) < foldRadius + 1.2f || ground.PathAt(p.X, p.Y) > 0.15f || !ground.Region.HasPoint(p))
                    continue;
                return p;
            }
            return null;
        }

        for (int i = 0; i < 9; i++)
        {
            if (Spot(3.5f, 13f) is not { } p || art.ModelWithLods(Shard) is not { } shard)
                continue;
            float s = random.RandfRange(0.2f, 0.34f);
            // Lying on its side as authored (0.8-1.3 m long), sunk by a third to a half of its height (2.1 m x the scale).
            shard.Position = new Vector3(p.X, ground.Height(p.X, p.Y) - 2.1f * s * random.RandfRange(0.3f, 0.5f), p.Y);
            shard.RotationDegrees = new Vector3(random.RandfRange(-12, 12), random.RandfRange(0, 360), random.RandfRange(-12, 12));
            shard.Scale = Vector3.One * s;
            root.AddChild(shard);
            if (onyx is not null)
                Palette.Wear(shard, onyx, new Color(0.2f, 0.21f, 0.26f), Colors.Black, 0f);
            placed++;
        }
        for (int i = 0; i < 26; i++)
        {
            if (Spot(2.5f, 10f) is not { } p || art.ModelWithLods(Pebble) is not { } pebble)
                continue;
            float s = random.RandfRange(0.07f, 0.13f);
            pebble.Position = new Vector3(p.X, ground.Height(p.X, p.Y) + random.RandfRange(0.18f, 0.4f), p.Y);
            pebble.RotationDegrees = new Vector3(random.RandfRange(0, 360), random.RandfRange(0, 360), random.RandfRange(0, 360));
            pebble.Scale = Vector3.One * s;
            root.AddChild(pebble);
            placed++;
        }
        art.Coverage.Resolved("scenery", "foldscar_ground", $"{placed} pieces: shards of the heart's kind, stones resting on nothing");
        return placed;
    }
}
