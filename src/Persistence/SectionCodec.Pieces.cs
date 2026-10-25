// UNNAMED Persistence - placed pieces in the entities section (schema 44; M7 design §4.18, re-ported by WP-1G)
// No Godot references - pure C#

using System.Collections.Immutable;
using MessagePack;
using UNNAMED.Domain;
using UNNAMED.World;

namespace UNNAMED.Persistence.Sections;

/// <summary>A player-placed piece (schema 44): its derived ID, definition, anchor cell and anchor (absolute mm), turn, owner, health, door.</summary>
[MessagePackObject]
public sealed class PieceDto
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("host_cell")] public string HostCell { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("rotation")] public int Rotation { get; set; }
    [Key("owner")] public string Owner { get; set; } = "";
    [Key("health")] public int Health { get; set; }
    [Key("door_open")] public bool DoorOpen { get; set; }
}

public static partial class SectionCodec
{
    private static PieceDto[] PiecesToDto(DeltaSnapshot snapshot) => snapshot.Pieces.Select(p => new PieceDto
    {
        InstanceId = p.InstanceId.Value, DefId = p.DefId, HostCell = p.HostCell, XMm = p.XMm, ZMm = p.ZMm, Rotation = p.Rotation,
        Owner = p.Owner.Value, Health = p.HealthCurrent, DoorOpen = p.DoorOpen,
    }).ToArray();

    /// <summary>The entities section's placed pieces and structure sequence (schema 44), each piece proven against its anchor cell's baseline.</summary>
    public static (ImmutableArray<PieceRecord> Pieces, long StructureSequence) DecodePieces(byte[] bytes)
    {
        var section = MessagePackSerializer.Deserialize<EntitiesSectionDto>(bytes, Options);
        var baselines = section.Baselines.ToDictionary(b => b.CellKey, b => b.BaselineHash, StringComparer.Ordinal);
        var pieces = (section.Pieces ?? throw new FormatException("entities.msgpack has no pieces (required from schema 44)"))
            .Select(p => new PieceRecord(EntityId.Parse(p.InstanceId), p.DefId, p.HostCell, p.XMm, p.ZMm, p.Rotation, EntityId.Parse(p.Owner), p.Health,
                baselines.GetValueOrDefault(p.HostCell)) { DoorOpen = p.DoorOpen })
            .ToImmutableArray();
        long sequence = section.StructureSeq ?? throw new FormatException("entities.msgpack has no structure sequence (required from schema 44)");
        return (pieces, sequence);
    }
}
