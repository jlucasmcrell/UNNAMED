// UNNAMED World - what the simulation shows of building (M7 design §4.6, §4.19; re-ported onto humgen-main by WP-1G)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

public sealed partial class Simulation
{
    private readonly BuildingSystem _building;
    private NavScratch? _previewScratch;

    /// <summary>Where bodies move in the space the character is in: its ground and walls, and outdoors every placed piece's solid parts (M7).</summary>
    public WalkSpace ActiveSpace => _context.SpaceFor(_state.Space);

    /// <summary>Every placed piece, by ID (M7).</summary>
    public ImmutableArray<PieceView> Pieces => _building.Views();

    /// <summary>The structure sequence (M7): one more on every place and take-down.</summary>
    public long StructureRevision => _state.World.StructureSequence;

    /// <summary>What the load found wrong with the saved pieces, kept and reported (M7).</summary>
    public ImmutableArray<StructureConflict> StructureAudit => _state.StructureAudit;

    /// <summary>Every placed part as navigation reads it (M7).</summary>
    public ImmutableArray<NavFootprint> StructureFootprints => _state.StructureFootprints;

    /// <summary>
    /// The placement ghost (M7 design §4.6): what placing this piece here would do, by the command's own rules on a scratch of its own.
    /// Advisory and read-only - it dispatches, publishes, counts and writes nothing.
    /// </summary>
    public PlacementPreview PreviewPlacement(string pieceDefId, long xMm, long zMm, int rotation, bool checkNavigability)
    {
        _previewScratch ??= new NavScratch();
        var check = BuildingRules.Validate(new PlacementContext(_context, _navigation, _identity.Id, _identity.Id, _previewScratch, null, checkNavigability),
            pieceDefId, xMm, zMm, rotation);
        return BuildingRules.Preview(_context, check, pieceDefId);
    }
}
