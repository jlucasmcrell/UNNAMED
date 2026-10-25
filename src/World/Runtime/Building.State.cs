// UNNAMED World - the structures slice of the runtime state (M7 design §4.4, §4.19; re-ported onto humgen-main by WP-1G)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

internal sealed partial class RuntimeState
{
    // Derived from the piece rows (M7), never saved: rebuilt by BuildingSystem on every place and take-down and at world start.

    /// <summary>The exterior's ground and walls with every placed piece's solid parts; null while nothing is built.</summary>
    public WalkSpace? StructureSpace { get; private set; }

    public ImmutableArray<Blocker> ClosedPieceLeaves { get; private set; } = ImmutableArray<Blocker>.Empty;
    public StructureIndex StructureIndex { get; private set; } = StructureIndex.Empty;
    public ImmutableArray<NavFootprint> StructureFootprints { get; private set; } = ImmutableArray<NavFootprint>.Empty;
    public ImmutableArray<StructureConflict> StructureAudit { get; private set; } = ImmutableArray<StructureConflict>.Empty;

    public void PlacePiece(SliceOwner owner, PieceRecord record, long sequence)
    {
        Require(owner, StateSlice.Structures);
        World.PlacePiece(record, sequence);
    }

    public void SetPiece(SliceOwner owner, PieceRecord record)
    {
        Require(owner, StateSlice.Structures);
        World.SetPiece(record);
    }

    public void RemovePiece(SliceOwner owner, EntityId id, long sequence)
    {
        Require(owner, StateSlice.Structures);
        World.RemovePiece(id, sequence);
    }

    public void SetStructureDerived(SliceOwner owner, WalkSpace? space, ImmutableArray<Blocker> closedLeaves, StructureIndex index,
        ImmutableArray<NavFootprint> footprints)
    {
        Require(owner, StateSlice.Structures);
        StructureSpace = space;
        ClosedPieceLeaves = closedLeaves;
        StructureIndex = index;
        StructureFootprints = footprints;
    }

    public void SetClosedPieceLeaves(SliceOwner owner, ImmutableArray<Blocker> closedLeaves)
    {
        Require(owner, StateSlice.Structures);
        ClosedPieceLeaves = closedLeaves;
    }

    /// <summary>A container's record goes while its items live on elsewhere (a destroyed chest's spill).</summary>
    public void ReleaseContainer(SliceOwner owner, string key)
    {
        Require(owner, StateSlice.WorldItems);
        World.ReleaseContainer(key);
    }

    public void SetStructureAudit(SliceOwner owner, ImmutableArray<StructureConflict> lines)
    {
        Require(owner, StateSlice.Structures);
        StructureAudit = lines;
    }
}
