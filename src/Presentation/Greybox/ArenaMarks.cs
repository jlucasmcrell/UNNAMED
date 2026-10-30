// UNNAMED Presentation - the arenas' paint: the chalked circle and the measured floor, laid on the ground where each
// boss's court already is (D19). Paint, not architecture: nothing here blocks, and the boundary a fight reads is still
// the court the boss record carries.

using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Art;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// One boss arena, drawn. The court's extent decides the mark's size, and the boss decides which mark: the Custodian's
/// measured floor for the machine that reads measures, the chalked circle for everything that fights in a ring.
/// </summary>
public static class ArenaMarks
{
    private const string Chalk = "arena_chalk_ring";
    private const string Measure = "arena_measure_floor";

    /// <summary>Adds a mark to <paramref name="view"/> for every boss spawn of <paramref name="layout"/> that has a court.</summary>
    public static void Build(Node3D view, ArtLibrary art, IEnumerable<SpawnSite> spawns, RegionLayout layout)
    {
        foreach (var spawn in spawns)
        {
            if (spawn.Recovery is not { } court || spawn.Space != layout.SpaceId)
                continue;
            bool measured = spawn.Key.Contains("custodian", StringComparison.Ordinal);
            if (art.Model(measured ? Measure : Chalk) is not { } mark)
                continue;
            // The ring is authored 19.2 m across and the floor 26 x 20 m; a wider court is drawn wider, never below a
            // third of the authored size, so a small court still reads as a ring rather than a disc.
            float width = (court.MaxXMm - court.MinXMm) / 1000f;
            float depth = (court.MaxZMm - court.MinZMm) / 1000f;
            float across = measured ? 26f : 19.2f;
            float deep = measured ? 20f : 19.2f;
            mark.Scale = new Vector3(Mathf.Max(width / across, 0.33f), 1f, Mathf.Max(depth / deep, 0.33f));
            float x = (court.MinXMm + court.MaxXMm) / 2000f;
            float z = (court.MinZMm + court.MaxZMm) / 2000f;
            float ground = layout.Space.Terrain.HeightAtMm((long)(x * 1000), (long)(z * 1000)) / 1000f;
            mark.Position = new Vector3(x, ground + 0.012f, z);
            mark.Name = "Arena:" + spawn.Key;
            view.AddChild(mark);
        }
    }
}
