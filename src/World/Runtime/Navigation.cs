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

/// <summary>
/// To <see cref="InteractionSystem"/>: an NPC opens a door in their way - a companion, until the errand mover (M7 design §6). NPCs open
/// doors and never close them.
/// </summary>
internal sealed record OpenDoor(string DoorKey, string NpcId) : InternalCommand;

/// <summary>A gate as the navigation view shows it: a door (<c>door</c>, <c>piece_door</c>) or a barrier, and whether it is open now.</summary>
public sealed record NavGateView(string Key, string Kind, Blocker Footprint, bool Open);

/// <summary>A mover's committed route, and whether it has been blocked long enough to show.</summary>
public sealed record NavMoverView(string NpcId, NavRoute Route, bool Blocked);

/// <summary>Navigation, read-only (M7 design §3.14): the grid, every gate and its state, the movers' routes, and the work counts.</summary>
public sealed record NavigationView(NavGrid Grid, ImmutableArray<NavGateView> Gates, ImmutableArray<NavMoverView> Movers, NavCounters Counters);

/// <summary>
/// Owns: <see cref="StateSlice.Navigation"/>. Builds the region's navigation grid (M7 design §3.4) from what
/// <see cref="Kinematics"/> collides with, answers reachability, and shows it. It never writes a body, reads no faction state and has no
/// tick. Its scratch and counters are its own, so two worlds never share them; neither is ever read by a decision.
/// </summary>
internal sealed class NavigationSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly ImmutableSortedDictionary<string, DoorSite> _doors;
    private readonly ImmutableSortedDictionary<string, BarrierSite> _barriers;

    public NavigationSystem(SystemContext context, SliceOwner owner)
    {
        _context = context;
        _owner = owner;
        _doors = context.Setup.Layout.Doors.ToImmutableSortedDictionary(d => d.Key, d => d, StringComparer.Ordinal);
        _barriers = context.Setup.Layout.Barriers.ToImmutableSortedDictionary(b => b.Key, b => b, StringComparer.Ordinal);
    }

    /// <summary>The authoritative search scratch: the command path's.</summary>
    internal NavScratch Scratch { get; } = new();

    /// <summary>Where the authoritative path's work is counted.</summary>
    internal NavCounterSink Counters { get; } = new();

    private NavConfig Config => _context.Setup.Navigation;

    /// <summary>The grid now. Built in the simulation's constructor, so never null once the simulation exists.</summary>
    public NavGrid Grid => _context.State.Navigation ?? throw new InvalidOperationException("The navigation grid is built when the simulation starts");

    /// <summary>Build every tile of the region from the current inputs. Runs in the constructor, for a new game and a load alike.</summary>
    public void Build()
    {
        var layout = _context.Setup.Layout;
        _context.State.SetNavigation(_owner, NavGrid.Build(Config, NavigationLayout.Bounds(layout), NavigationLayout.TileKeys(layout), CurrentInputs(), Counters));
    }

    /// <summary>The inputs in canonical order: the authored statics that stop a standing body, and the authored doors and barriers as gates.</summary>
    public ImmutableArray<NavInput> CurrentInputs() =>
        NavigationLayout.AuthoredInputs(_context.Setup.Layout, Config).Sort(NavInputOrder.Instance);

    /// <summary>
    /// Whether an agent can get from one point to another now (the plan is found), doors planned through as the agent may, barriers by
    /// their current flags. Counted.
    /// </summary>
    public bool Reachable(NavAgent agent, NavPoint from, NavPoint to) =>
        NavSearch.Plan(new NavQuery(Grid, IsGateOpen, Config, Scratch, Counters), agent, from, to).Outcome == NavOutcome.Found;

    public NavigationView View() => new(Grid, Gates(), ImmutableArray<NavMoverView>.Empty, Counters.Snapshot());

    /// <summary>A gate's state now: a door's flag, a barrier's lift.</summary>
    private bool IsGateOpen(NavInput gate) => gate.Kind switch
    {
        NavInputKind.Door => _doors.TryGetValue(gate.GateKey!, out var door) && _context.IsOpen(door),
        NavInputKind.Barrier => _barriers.TryGetValue(gate.GateKey!, out var barrier) && _context.IsLifted(barrier),
        _ => false,
    };

    private ImmutableArray<NavGateView> Gates() =>
        _context.Setup.Layout.Doors.Select(d => new NavGateView(d.Key, "door", d.ClosedFootprint, _context.IsOpen(d)))
            .Concat(_context.Setup.Layout.Barriers.Select(b => new NavGateView(b.Key, "barrier", b.Footprint, _context.IsLifted(b))))
            .ToImmutableArray();
}
