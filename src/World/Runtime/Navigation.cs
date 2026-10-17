// UNNAMED World - navigation over a region (WP12, ported from the M7 design §3.4, §3.14; D-13)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>
/// What navigation reads from a region's authored layout: its tiles, its walkable bounds, and the footprints that stop a standing body -
/// the statics as solids, the doors and barriers as gates. Shared by the running simulation and the content lint, so both build the same
/// grid.
/// </summary>
public static class NavigationLayout
{
    /// <summary>A cell's tile: its global 100 m index on each axis.</summary>
    public static NavTileKey TileOf(CellKey cell) =>
        new((long)cell.Region.Rx * WorldMath.CellsPerRegionAxis + cell.Cx, (long)cell.Region.Rz * WorldMath.CellsPerRegionAxis + cell.Cz);

    /// <summary>One tile per cell of the region, in (Tz, Tx) order.</summary>
    public static ImmutableArray<NavTileKey> TileKeys(RegionLayout layout) =>
        layout.CellKeys.Select(CellKey.Parse).Select(TileOf).Distinct().OrderBy(k => k).ToImmutableArray();

    public static NavRect Bounds(RegionLayout layout) => new(layout.Space.MinXMm, layout.Space.MinZMm, layout.Space.MaxXMm, layout.Space.MaxZMm);

    /// <summary>
    /// The authored footprints that stop a body standing on the ground (<see cref="Kinematics.Blocks"/> at the agent height): statics as
    /// solids, doors and barriers as gates keyed by their site. Nothing is read from the terrain, which never blocks.
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

/// <summary>A mover planned: who, how the plan ended (<see cref="NavSearch.OutcomeKey"/>), why it replanned, its corners and its cost. For views.</summary>
public sealed record RoutePlanned(string MoverKey, string Outcome, string Reason, int Corners, int Expansions, long Tick);

/// <summary>A gate as the navigation view shows it - a door or a barrier - and whether it is open now.</summary>
public sealed record NavGateView(string Key, string Kind, Blocker Footprint, bool Open);

/// <summary>A mover's committed route, and whether it has been stuck long enough to show as blocked.</summary>
public sealed record NavMoverView(string NpcId, NavRoute Route, bool Blocked);

/// <summary>Navigation, read-only: the grid, every gate and its state, the movers' routes, and the work counts.</summary>
public sealed record NavigationView(NavGrid Grid, ImmutableArray<NavGateView> Gates, ImmutableArray<NavMoverView> Movers, NavCounters Counters);

/// <summary>
/// One world's navigation: the grid of what <see cref="Kinematics"/> collides with, built on first use (a world nobody walks in pays
/// nothing), the search scratch and the work counts. Derived, never saved: a load builds the same grid. Its scratch and counters are this
/// world's own, and neither is ever read by a decision. Building (WP8) restamps the grid through <see cref="NavGrid.With"/>.
/// </summary>
internal sealed class WorldNavigation
{
    private readonly SystemContext _context;
    private readonly ImmutableSortedDictionary<string, DoorSite> _doors;
    private readonly ImmutableSortedDictionary<string, BarrierSite> _barriers;
    private NavGrid? _grid;

    public WorldNavigation(SystemContext context)
    {
        _context = context;
        _doors = context.Setup.Layout.Doors.ToImmutableSortedDictionary(d => d.Key, d => d, StringComparer.Ordinal);
        _barriers = context.Setup.Layout.Barriers.ToImmutableSortedDictionary(b => b.Key, b => b, StringComparer.Ordinal);
    }

    public NavScratch Scratch { get; } = new();

    public NavCounterSink Counters { get; } = new();

    public NavConfig Config => _context.Setup.Navigation;

    public NavGrid Grid => _grid ??= NavigationLayout.Build(_context.Setup.Layout, Config, Counters);

    /// <summary>Whether a grid has been built yet: views and tests only.</summary>
    public bool Built => _grid is not null;

    public NavQuery Query() => new(Grid, IsGateOpen, Config, Scratch, Counters);

    /// <summary>Whether an agent can get from one point to another now, doors as the agent may, barriers by their current flags.</summary>
    public bool Reachable(NavAgent agent, NavPoint from, NavPoint to) => NavSearch.Plan(Query(), agent, from, to).Outcome == NavOutcome.Found;

    /// <summary>One tick of a mover's route: replanned on this world's scratch, a closed door opened only from within a hand's reach.</summary>
    public NavStep Follow(NavAgent agent, NavRoute route, NavPoint body, NavPoint goal, int stuckTicks, long tick) =>
        NavFollower.Next(Query(), agent, route, body, goal, stuckTicks, tick, _context.Setup.Movement.InteractReachMm);

    /// <summary>Whether a body of the person class can stand at a point now.</summary>
    public bool Standable(NavPoint p) =>
        NavSearch.Standable(Grid, Person, IsGateOpen, p, Config.Classes[Config.PersonClass].RadiusMm);

    /// <summary>A person, who opens doors: the class people walk as.</summary>
    public NavAgent Person => new(Config.PersonClass, true);

    /// <summary>Whether an edit keeps the world navigable: the one rule, on the grid as it stands. For building (WP8).</summary>
    public NavEditVerdict CheckEdit(ImmutableArray<NavInput> addSolids, ImmutableArray<NavInput> addDoors, ImmutableArray<NavProtectedPoint> points)
    {
        var verdict = NavEditCheck.Check(Grid, Config, Scratch, addSolids, addDoors, points);
        Counters.CountEditCheck(verdict.Ok ? null : verdict.Rule, verdict.NodesFlooded);
        return verdict;
    }

    public DoorSite? Door(string key) => _doors.GetValueOrDefault(key);

    /// <summary>A gate's state now: a door's flag, a barrier's lift.</summary>
    public bool IsGateOpen(NavInput gate) => gate.Kind switch
    {
        NavInputKind.Door => _doors.TryGetValue(gate.GateKey!, out var door) && _context.IsOpen(door),
        NavInputKind.Barrier => _barriers.TryGetValue(gate.GateKey!, out var barrier) && _context.IsLifted(barrier),
        _ => false,
    };

    public ImmutableArray<NavGateView> Gates() =>
        _context.Setup.Layout.Doors.Select(d => new NavGateView(d.Key, "door", d.ClosedFootprint, _context.IsOpen(d)))
            .Concat(_context.Setup.Layout.Barriers.Select(b => new NavGateView(b.Key, "barrier", b.Footprint, _context.IsLifted(b))))
            .ToImmutableArray();
}
