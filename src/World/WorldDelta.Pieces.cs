// UNNAMED World - the player's placed pieces in the world delta (M7 design §4.18-§4.19; re-ported onto humgen-main by WP-1G)
// No Godot references - pure C#

using System.Collections.Immutable;
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

public sealed partial record DeltaSnapshot
{
    /// <summary>Player-placed pieces, sorted by instance ID (schema 44, WP-1G).</summary>
    public ImmutableArray<PieceRecord> Pieces { get; init; } = ImmutableArray<PieceRecord>.Empty;

    /// <summary>The structure sequence: every piece ID's ordinal, and the structure revision (schema 44, WP-1G).</summary>
    public long StructureSequence { get; init; }
}

public sealed partial class WorldDelta
{
    private const string PieceChestPrefix = "container.pce_";

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

    /// <summary>Every piece, each with the baseline hash of its anchor's cell: what a save writes.</summary>
    private ImmutableArray<PieceRecord> PieceSnapshot() =>
        _pieces.Values.Select(p => p with { BaselineHash = Baseline(CellKey.Parse(p.HostCell)).Digest }).ToImmutableArray();

    /// <summary>A load's pieces (M7 design §4.18), each validated and registered, or dropped with its reason; the sequence first.</summary>
    private void ApplyPieces(DeltaSnapshot snapshot, HashSet<EntityId> seenIds, ImmutableArray<RejectedRecord>.Builder problems)
    {
        _structureSequence = snapshot.StructureSequence;
        foreach (var record in snapshot.Pieces)
        {
            if (TryApplyPiece(record, seenIds) is { } reason)
                problems.Add(new RejectedRecord("entities", record.InstanceId.Value, reason));
        }
    }

    /// <summary>
    /// A placed piece. Its ID's timestamp is read as the derivation ordinal (the D-04 note), which never exceeds the saved sequence. It is
    /// not compared with <c>Derived(..., Owner)</c>, so a later change of owner is a row edit, not a re-key.
    /// </summary>
    private string? TryApplyPiece(PieceRecord record, HashSet<EntityId> seenIds)
    {
        if (!CellKey.TryParse(record.HostCell, out var cell))
            return "unparseable host cell";
        string hostBaseline = Baseline(cell).Digest;
        if (record.BaselineHash != hostBaseline)
            return $"baseline_hash {record.BaselineHash ?? "(none)"} is not the host cell's regenerated baseline {hostBaseline}";
        if (record.InstanceId.Kind != EntityKind.Piece)
            return $"{record.InstanceId} is not a piece ID";
        if (!DefinitionId.IsValid(record.DefId) || !record.DefId.StartsWith("piece.", StringComparison.Ordinal))
            return $"'{record.DefId}' is not a piece definition ID";
        if (record.Owner.Kind != EntityKind.Character)
            return $"its owner {record.Owner} is not a character";
        if (record.Rotation is < 0 or > 3)
            return $"rotation {record.Rotation} is not a quarter turn 0-3";
        if (record.HealthCurrent < 1)
            return $"health {record.HealthCurrent} is below 1";
        if (record.HostCell != CellKey.OfWorld(record.XMm / 1000.0, record.ZMm / 1000.0).ToString())
            return $"host cell {record.HostCell} is not the cell of its anchor";
        if (record.InstanceId.Timestamp < 1 || record.InstanceId.Timestamp > _structureSequence)
            return $"ordinal {record.InstanceId.Timestamp} is outside 1..{_structureSequence}, the structure sequence";
        if (!seenIds.Add(record.InstanceId) || _pieces.ContainsKey(record.InstanceId.Value))
            return $"instance ID {record.InstanceId} appears twice";
        if (_registry.Exists(record.InstanceId))
            return $"instance ID {record.InstanceId} is already registered";
        _registry.CreateEntity(DefinitionId.Parse(record.DefId), record.InstanceId);
        _pieces[record.InstanceId.Value] = record;
        return null;
    }

    /// <summary>A placed chest's record: its piece must stand, and its identity is the one derived from that piece. Null for any other container.</summary>
    private string? PieceChestProblem(ContainerRecord record)
    {
        if (!record.Key.StartsWith(PieceChestPrefix, StringComparison.Ordinal))
            return null;
        if (!EntityId.TryParse("pce_" + record.Key[PieceChestPrefix.Length..].ToUpperInvariant(), out var pieceId) || Piece(pieceId) is not { } piece)
            return "its chest is gone";
        return record.InstanceId == PieceChestId(piece.InstanceId) ? null : "not its chest's identity";
    }

    /// <summary>A placed chest's container identity (M7, D-04 §2.2): derived from its piece, so the same commands make the same chest.</summary>
    internal static EntityId PieceChestId(EntityId pieceId) =>
        EntityId.Derived(EntityKind.Container, pieceId.Timestamp, "unnamed.piece-container/v1", pieceId.Value);

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
