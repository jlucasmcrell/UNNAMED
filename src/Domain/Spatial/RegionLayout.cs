// UNNAMED Domain - an authored region's layout (WORLD_ARCHITECTURE.md §4, DATA_MODEL.md §4.18; PROTOTYPE.md §3)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Spatial;

/// <summary>
/// A door: an interactable whose open state is a <c>world.*</c> flag in the cell it stands in. Closed, its
/// footprint blocks movement; open, it does not. The key is what an interaction command names.
/// </summary>
public sealed record DoorSite(string Key, string FlagId, BoxBlocker ClosedFootprint);

/// <summary>A named discoverable place (DATA_MODEL.md §4.18): entering its radius discovers it.</summary>
public sealed record LocationSite(string Id, long XMm, long ZMm, long DiscoveryRadiusMm, long DiscoveryXp);

/// <summary>
/// An authored world container (SYSTEMS.md S-14): until the player first changes it, its contents are its loot table,
/// rolled the same way every time; the key is its identity in saves.
/// </summary>
public sealed record ContainerSite(string Key, string LootTableId, long XMm, long ZMm, int StackSlots);

/// <summary>
/// An authored resource node (M3f): a node definition at a fixed place. Its identity and harvest state belong to the world
/// generator (PERSISTENCE.md §5.5); <see cref="Name"/> is its semantic key there.
/// </summary>
public sealed record NodeSite(string Name, string NodeDefId, long XMm, long ZMm);

/// <summary>A crafting station (M3f): a place where recipes of its kind are worked - the forge's hearth, its anvil.</summary>
public sealed record StationSite(string Key, string Kind, long XMm, long ZMm);

/// <summary>The cell-level generation parameters the region declares (WORLD_ARCHITECTURE.md §4).</summary>
public sealed record RegionGeneration(int TerrainBaseHeightMm, int TerrainAmplitudeMm, int TerrainSamplesPerAxis);

/// <summary>
/// Everything authored about one region that the simulation needs: the cells it covers, where a body can walk,
/// the structures and doors that stand in it, its named places, and where a new character starts. Built from
/// content by <c>UNNAMED.Content.WorldContent</c>; presentation builds its greybox from the same data, so what
/// is drawn and what blocks are one thing.
/// </summary>
public sealed record RegionLayout(
    string Id,
    ImmutableArray<string> CellKeys,
    WalkSpace Space,
    ImmutableArray<DoorSite> Doors,
    ImmutableArray<LocationSite> Locations,
    Body Spawn,
    RegionGeneration Generation)
{
    /// <summary>The region's authored containers.</summary>
    public ImmutableArray<ContainerSite> Containers { get; init; } = ImmutableArray<ContainerSite>.Empty;

    /// <summary>The region's authored resource nodes (M3f).</summary>
    public ImmutableArray<NodeSite> Nodes { get; init; } = ImmutableArray<NodeSite>.Empty;

    /// <summary>The region's crafting stations (M3f).</summary>
    public ImmutableArray<StationSite> Stations { get; init; } = ImmutableArray<StationSite>.Empty;

    public DoorSite? FindDoor(string key) => Doors.FirstOrDefault(d => d.Key == key);

    public ContainerSite? FindContainer(string key) => Containers.FirstOrDefault(c => c.Key == key);

    /// <summary>The footprints that block movement given which doors are open.</summary>
    public ImmutableArray<Blocker> ClosedDoors(Func<DoorSite, bool> isOpen) =>
        Doors.Where(d => !isOpen(d)).Select(d => (Blocker)d.ClosedFootprint).ToImmutableArray();
}
