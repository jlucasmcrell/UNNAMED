// UNNAMED World - navigation over a region (M7 design §3.4, §3.14; D-13)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>
/// What navigation reads from a region's authored layout: its tiles, its walkable bounds, and the footprints that stop a standing body -
/// the statics as solids, the doors and barriers as gates. Shared by the running simulation and the content lint, so both build the same
/// grid.
/// </summary>
public static class NavigationLayout
{
    /// <summary>A cell's tile: its global 100 m index on each axis, <c>Rx * 20 + Cx</c> and <c>Rz * 20 + Cz</c>.</summary>
    public static NavTileKey TileOf(CellKey cell) =>
        new((long)cell.Region.Rx * WorldMath.CellsPerRegionAxis + cell.Cx, (long)cell.Region.Rz * WorldMath.CellsPerRegionAxis + cell.Cz);

    /// <summary>One tile per cell of the region, in (Tz, Tx) order.</summary>
    public static ImmutableArray<NavTileKey> TileKeys(RegionLayout layout) =>
        layout.CellKeys.Select(CellKey.Parse).Select(TileOf).Distinct().OrderBy(k => k).ToImmutableArray();

    public static NavRect Bounds(RegionLayout layout) => new(layout.Space.MinXMm, layout.Space.MinZMm, layout.Space.MaxXMm, layout.Space.MaxZMm);

    /// <summary>
    /// The authored footprints that stop a body standing on the ground (<see cref="Kinematics.Blocks"/> at the agent height): statics
    /// as solids, doors and barriers as gates keyed by their site. A beam a standing body cannot pass under counts; nothing is read from
    /// the terrain, which never blocks.
    /// </summary>
    public static ImmutableArray<NavInput> AuthoredInputs(RegionLayout layout, NavConfig config)
    {
        bool Stops(Blocker b) => Kinematics.Blocks(b, 0, config.AgentHeightMm);
        return layout.Space.Blockers.Where(Stops).Select(b => new NavInput(NavInputKind.Solid, b, null))
            .Concat(layout.Doors.Where(d => Stops(d.ClosedFootprint)).Select(d => new NavInput(NavInputKind.Door, d.ClosedFootprint, d.Key)))
            .Concat(layout.Barriers.Where(b => Stops(b.Footprint)).Select(b => new NavInput(NavInputKind.Barrier, b.Footprint, b.Key)))
            .ToImmutableArray();
    }

    /// <summary>The grid of the region's authored layout alone.</summary>
    public static NavGrid Build(RegionLayout layout, NavConfig config, NavCounterSink? counters = null) =>
        NavGrid.Build(config, Bounds(layout), TileKeys(layout), AuthoredInputs(layout, config), counters);
}
