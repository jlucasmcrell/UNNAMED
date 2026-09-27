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

/// <summary>A named discoverable place (DATA_MODEL.md §4.18): entering its radius discovers it.</summary>
public sealed record LocationSite(string Id, long XMm, long ZMm, long DiscoveryRadiusMm, long DiscoveryXp);

/// <summary>
/// An authored world container (SYSTEMS.md S-14): until the player first changes it, its contents are its loot table,
/// rolled the same way every time; the key is its identity in saves.
/// </summary>
public sealed record ContainerSite(string Key, string LootTableId, long XMm, long ZMm, int StackSlots)
{
    /// <summary>A placed chest's identity, derived from its piece (M7); null for an authored container, which is given one when first changed.</summary>
    public EntityId? InstanceId { get; init; }

    /// <summary>Who may use a placed chest (M7); null for an authored container, which anyone may.</summary>
    public EntityId? Owner { get; init; }
}

/// <summary>
/// An authored resource node (M3f): a node definition at a fixed place. Its identity and harvest state belong to the world
/// generator (PERSISTENCE.md §5.5); <see cref="Name"/> is its semantic key there.
/// </summary>
public sealed record NodeSite(string Name, string NodeDefId, long XMm, long ZMm);

/// <summary>A crafting station (M3f): a place where recipes of its kind are worked - the forge's hearth, its anvil.</summary>
public sealed record StationSite(string Key, string Kind, long XMm, long ZMm);

/// <summary>Where a named NPC stands (M4), and which way they face: their place in the settlement's authored layout.</summary>
public sealed record NpcSite(string NpcId, long XMm, long ZMm, int FacingMdeg);

/// <summary>
/// Where the player may build (M7 design §4.16): a box on the building lattice, edges included, and how many pieces may stand in it.
/// Build areas are layout, not baseline.
/// </summary>
public sealed record BuildAreaSite(string Key, long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, int MaxPieces);

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

    /// <summary>Where the player may build (M7).</summary>
    public ImmutableArray<BuildAreaSite> BuildAreas { get; init; } = ImmutableArray<BuildAreaSite>.Empty;

    public DoorSite? FindDoor(string key) => Doors.FirstOrDefault(d => d.Key == key);

    public SwitchSite? FindSwitch(string key) => Switches.FirstOrDefault(s => s.Key == key);

    public ContainerSite? FindContainer(string key) => Containers.FirstOrDefault(c => c.Key == key);

    /// <summary>The footprints that block movement given which doors are open and which barriers are lifted.</summary>
    public ImmutableArray<Blocker> ClosedDoors(Func<DoorSite, bool> isOpen, Func<BarrierSite, bool>? isLifted = null) =>
        Doors.Where(d => !isOpen(d)).Select(d => (Blocker)d.ClosedFootprint)
            .Concat(Barriers.Where(b => isLifted is null || !isLifted(b)).Select(b => b.Footprint))
            .ToImmutableArray();
}
