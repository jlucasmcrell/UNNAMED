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
    public DoorSite? FindDoor(string key) => Doors.FirstOrDefault(d => d.Key == key);

    /// <summary>The footprints that block movement given which doors are open.</summary>
    public ImmutableArray<Blocker> ClosedDoors(Func<DoorSite, bool> isOpen) =>
        Doors.Where(d => !isOpen(d)).Select(d => (Blocker)d.ClosedFootprint).ToImmutableArray();
}
