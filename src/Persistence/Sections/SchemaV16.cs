// UNNAMED Persistence - schema 16 section shapes (the owner's ruling on the M7 E8.5 STOP). FROZEN.
// No Godot references - pure C#
//
// The creature shape schemas 14-16 wrote (13 -> 14 writes it; 14 -> 15 and 15 -> 16 do not touch it) and the entities shape schemas 15
// and 16 wrote (14 -> 15 writes it). The v14-v16 fixtures pin them. Their parts that schema 17 did not change are the current DTOs
// (EntityDto, CreatedDto, CellBaselineDto, ContainerDto, NoiseDto, PieceDto, NpcErrandDto); the step that next changes one of those
// must freeze a copy of it first. Schema 17 changed only the creature's continuation: the player and cells shapes schema 16 wrote are
// the current ones.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V16;

[MessagePackObject]
public sealed class CreatureContinuation
{
    [Key("next_charge_tick")] public long NextChargeTick { get; set; }
    [Key("stagger_immune_until")] public long StaggerImmuneUntil { get; set; }
    [Key("staggered_tick")] public long? StaggeredTick { get; set; }
    [Key("stagger_lasts_ticks")] public int StaggerLastsTicks { get; set; }
}

[MessagePackObject]
public sealed class Creature
{
    [Key("key")] public string Key { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("host_cell")] public string HostCell { get; set; } = "";
    [Key("generation")] public int Generation { get; set; }
    [Key("condition")] public string Condition { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("facing_mdeg")] public int FacingMdeg { get; set; }
    [Key("health")] public int Health { get; set; }
    [Key("died_tick")] public long DiedTick { get; set; }
    [Key("respawn_tick")] public long RespawnTick { get; set; }
    [Key("mind")] public string Mind { get; set; } = "";
    [Key("awareness")] public int Awareness { get; set; }
    [Key("knows")] public bool Knows { get; set; }
    [Key("known_x_mm")] public long KnownXMm { get; set; }
    [Key("known_z_mm")] public long KnownZMm { get; set; }
    [Key("last_seen_tick")] public long LastSeenTick { get; set; }
    [Key("search_until")] public long SearchUntil { get; set; }
    [Key("has_called")] public bool HasCalled { get; set; }
    [Key("continuation")] public CreatureContinuation? Continuation { get; set; }
}

[MessagePackObject]
public sealed class EntitiesSection
{
    [Key("records")] public EntityDto[] Records { get; set; } = Array.Empty<EntityDto>();
    [Key("created")] public CreatedDto[] Created { get; set; } = Array.Empty<CreatedDto>();
    [Key("baselines")] public CellBaselineDto[] Baselines { get; set; } = Array.Empty<CellBaselineDto>();
    [Key("containers")] public ContainerDto[]? Containers { get; set; }
    [Key("creatures")] public Creature[]? Creatures { get; set; }
    [Key("noises")] public NoiseDto[]? Noises { get; set; }
    [Key("pieces")] public PieceDto[]? Pieces { get; set; }
    [Key("structure_seq")] public long? StructureSeq { get; set; }
    [Key("npc_errands")] public NpcErrandDto[]? NpcErrands { get; set; }
}
