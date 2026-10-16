// UNNAMED Domain - an authored region's layout (WORLD_ARCHITECTURE.md §4, DATA_MODEL.md §4.18; PROTOTYPE.md §3)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Spatial;

/// <summary>
/// A door: an interactable whose open state is a <c>world.*</c> flag in the cell it stands in. Closed, its
/// footprint blocks movement; open, it does not. The key is what an interaction command names.
/// </summary>
public sealed record DoorSite(string Key, string FlagId, BoxBlocker ClosedFootprint);

/// <summary>
/// A switch (M6): an interactable standing on a structure (<see cref="Body"/>) that sets a <c>world.*</c> flag in its cell to 1 -
/// a Quiet Stone turned into line, the Foldscar's heart steadied. It works only once every flag in <see cref="Requires"/> is set in
/// that cell, and what it sets it never unsets. The words are the content's: the prompt is <see cref="Verb"/> and
/// <see cref="Name"/>, <see cref="DoneText"/> is what happens, <see cref="LockedText"/> why it will not work yet.
/// </summary>
public sealed record SwitchSite(string Key, string FlagId, Blocker Body, ImmutableArray<string> Requires, string Name, string Verb,
    string DoneText, string? LockedText);

/// <summary>
/// A barrier (M6): a footprint that blocks like a closed door while a <c>world.*</c> flag in its cell is 0 - the fold that holds Tavar
/// until the Foldscar is steadied. It has no handle: only what sets its flag lifts it. <see cref="Prompt"/> says what it is to someone
/// standing at it.
/// </summary>
public sealed record BarrierSite(string Key, string FlagId, Blocker Footprint, string Prompt);

/// <summary>
/// A named discoverable place (DATA_MODEL.md §4.18): entering its radius discovers it. Its space is the
/// layout's, which is where an interior's places get theirs (M8 D10, <see cref="RegionLayout.SpaceId"/>).
/// </summary>
public sealed record LocationSite(string Id, long XMm, long ZMm, long DiscoveryRadiusMm, long DiscoveryXp)
{
    /// <summary>How much ground round the place the map shows once it is discovered (its <c>map.reveals_radius_m</c>).</summary>
    public long RevealRadiusMm { get; init; }
}

/// <summary>
/// A Waystone (OWNER OD-03, OD-C16): an old standing stone on a structure (<see cref="Body"/>) that every traveller who touches it knows
/// again. Travel between stones a character knows arrives at <see cref="ArrivalXMm"/>, <see cref="ArrivalZMm"/>, a clear spot beside it.
/// <see cref="LocationId"/> is the place it stands in. A dormant stone (<see cref="Dormant"/>) does not answer until it is woken.
/// </summary>
public sealed record WaystoneSite(string Key, string Name, Blocker Body, string LocationId, long ArrivalXMm, long ArrivalZMm)
{
    public DormantStone? Dormant { get; init; }
}

/// <summary>
/// What a dormant Waystone needs (19 §9.8): it wakes for someone who has heard its rite - the line <see cref="RiteNodeId"/> of the
/// conversation <see cref="RiteDialogueId"/> - and once woken its <see cref="FlagId"/> is set in its cell, for everyone, for good.
/// </summary>
public sealed record DormantStone(string FlagId, string RiteDialogueId, string RiteNodeId, string DormantText, string WakeText);

/// <summary>A road as a line of points (the region's road graph): what Waystone travel measures, and what the map draws once walked.</summary>
public sealed record RoadSite(string Id, string Name, ImmutableArray<(long XMm, long ZMm)> Points);

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

/// <summary>Where a named NPC stands (M4), and which way they face: their place in the settlement's authored layout.</summary>
public sealed record NpcSite(string NpcId, long XMm, long ZMm, int FacingMdeg);

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

    /// <summary>The region's named NPCs, each where they stand (M4).</summary>
    public ImmutableArray<NpcSite> Npcs { get; init; } = ImmutableArray<NpcSite>.Empty;

    /// <summary>The region's switches (M6).</summary>
    public ImmutableArray<SwitchSite> Switches { get; init; } = ImmutableArray<SwitchSite>.Empty;

    /// <summary>The region's barriers (M6).</summary>
    public ImmutableArray<BarrierSite> Barriers { get; init; } = ImmutableArray<BarrierSite>.Empty;

    /// <summary>The region's Waystones.</summary>
    public ImmutableArray<WaystoneSite> Waystones { get; init; } = ImmutableArray<WaystoneSite>.Empty;

    /// <summary>The region's roads.</summary>
    public ImmutableArray<RoadSite> Roads { get; init; } = ImmutableArray<RoadSite>.Empty;

    /// <summary>
    /// The portals leading out of this region and into an interior space (M8 D10). Empty for every region
    /// that has no interior, which is all of them until the Barrowworks is gated on; a region's own
    /// entrances are the exterior anchors of these.
    /// </summary>
    public SpacePortalSet Portals { get; init; } = SpacePortalSet.Empty;

    /// <summary>
    /// The space this layout's own sites stand in (M8 D10). The exterior for every region that is part of
    /// the world; an interior layout - the Barrowworks' thirteen rooms - sets its own, and then every node,
    /// spawn, container, station, NPC and place in it resolves to a cell in that space rather than to the
    /// exterior ground its local metres happen to name.
    /// </summary>
    /// <remarks>
    /// This sits on the layout rather than on each site type because a layout <em>is</em> one space: its
    /// sites are authored together in one coordinate frame. It was once on the door, the switch and the
    /// barrier too, and nothing ever set them - so an interior's switches wrote their flags to exterior
    /// cells while the quest read space-qualified ones and no reading could ever be satisfied (review B2).
    /// </remarks>
    public SpaceId SpaceId { get; init; } = SpaceId.Exterior;

    public DoorSite? FindDoor(string key) => Doors.FirstOrDefault(d => d.Key == key);

    public WaystoneSite? FindWaystone(string key) => Waystones.FirstOrDefault(w => w.Key == key);

    public SwitchSite? FindSwitch(string key) => Switches.FirstOrDefault(s => s.Key == key);

    public ContainerSite? FindContainer(string key) => Containers.FirstOrDefault(c => c.Key == key);

    /// <summary>The footprints that block movement given which doors are open and which barriers are lifted.</summary>
    public ImmutableArray<Blocker> ClosedDoors(Func<DoorSite, bool> isOpen, Func<BarrierSite, bool>? isLifted = null) =>
        Doors.Where(d => !isOpen(d)).Select(d => (Blocker)d.ClosedFootprint)
            .Concat(Barriers.Where(b => isLifted is null || !isLifted(b)).Select(b => b.Footprint))
            .ToImmutableArray();
}
