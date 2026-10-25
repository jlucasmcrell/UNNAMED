// UNNAMED World - the player's placed pieces in the world delta (M7 design §4.18-§4.19; re-ported onto humgen-main by WP-1G)
// No Godot references - pure C#

using UNNAMED.Domain;

namespace UNNAMED.World;

/// <summary>
/// A player-placed building piece (M7): one row, anchored to its anchor's cell and proven against that cell's baseline. Its shape is its
/// definition's; only what the definition cannot know is stored. Its owner is a plain record: who may enter or take is property law, which
/// waits for the crime and law package (owner ruling). It exposes no computed public property (the <c>StateDump</c> rule).
/// </summary>
public sealed record PieceRecord(
    EntityId InstanceId,
    string DefId,
    string HostCell,
    long XMm,
    long ZMm,
    int Rotation,
    EntityId Owner,
    int HealthCurrent,
    string? BaselineHash = null)
{
    /// <summary>Whether a door piece stands open; false for every other piece.</summary>
    public bool DoorOpen { get; init; }
}

public sealed partial class WorldDelta
{
    private readonly SortedDictionary<string, PieceRecord> _pieces = new(StringComparer.Ordinal);
    private long _structureSequence;

    /// <summary>A placed piece, or null.</summary>
    public PieceRecord? Piece(EntityId id) => _pieces.GetValueOrDefault(id.Value);

    /// <summary>Every placed piece, sorted by instance ID.</summary>
    public IReadOnlyList<PieceRecord> Pieces => _pieces.Values.ToList();

    /// <summary>The pieces anchored in a cell, sorted by instance ID.</summary>
    public IReadOnlyList<PieceRecord> PiecesIn(CellKey cell)
    {
        string key = cell.ToString();
        return _pieces.Values.Where(p => p.HostCell == key).ToList();
    }

    /// <summary>+1 on every place, dismantle and destroy; never reused, never decreasing. The next piece's ordinal is this plus one.</summary>
    public long StructureSequence => _structureSequence;

    /// <summary>A new piece: its derived identity is registered, and the sequence moves to <paramref name="sequence"/>.</summary>
    internal void PlacePiece(PieceRecord record, long sequence)
    {
        if (sequence <= _structureSequence)
            throw new ArgumentException($"The structure sequence only rises: {sequence} after {_structureSequence}", nameof(sequence));
        if (_pieces.ContainsKey(record.InstanceId.Value))
            throw new InvalidOperationException($"{record.InstanceId} is already placed");
        _registry.CreateEntity(DefinitionId.Parse(record.DefId), record.InstanceId);
        _pieces[record.InstanceId.Value] = record;
        _structureSequence = sequence;
    }

    /// <summary>A placed piece's row changed - its health, whether its door stands open.</summary>
    internal void SetPiece(PieceRecord record)
    {
        if (!_pieces.ContainsKey(record.InstanceId.Value))
            throw new InvalidOperationException($"{record.InstanceId} is not placed");
        _pieces[record.InstanceId.Value] = record;
    }

    /// <summary>A piece taken down or destroyed: its identity retires, and the sequence moves to <paramref name="sequence"/>.</summary>
    internal void RemovePiece(EntityId id, long sequence)
    {
        if (sequence <= _structureSequence)
            throw new ArgumentException($"The structure sequence only rises: {sequence} after {_structureSequence}", nameof(sequence));
        if (!_pieces.Remove(id.Value))
            throw new InvalidOperationException($"{id} is not placed");
        if (_registry.Exists(id))
            _registry.DestroyEntity(id);
        _structureSequence = sequence;
    }

    /// <summary>A container's record goes while its items live on elsewhere (a destroyed chest's spill): only its own identity retires.</summary>
    internal void ReleaseContainer(string key)
    {
        if (_containers.Remove(key, out var record) && _registry.Exists(record.InstanceId))
            _registry.DestroyEntity(record.InstanceId);
    }

    /// <summary>
    /// A cell's pieces in its effective digest (M7 design §4.18). A cell with none adds nothing, so every digest taken before building
    /// existed reads the same; a straddling piece is hashed once, in its anchor's cell.
    /// </summary>
    private void AddPieces(CanonicalHasher h, CellKey cell)
    {
        var pieces = PiecesIn(cell);
        if (pieces.Count == 0)
            return;
        h.Add("pieces").Add(pieces.Count);
        foreach (var p in pieces)
            h.Add(p.InstanceId.Value).Add(p.DefId).Add(p.XMm).Add(p.ZMm).Add(p.Rotation).Add(p.Owner.Value).Add(p.HealthCurrent).Add(p.DoorOpen);
    }
}
