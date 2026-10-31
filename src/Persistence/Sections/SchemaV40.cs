// UNNAMED Persistence - schema 40 section shapes (the entity section as schema 40 wrote it). FROZEN.
// No Godot references - pure C#
//
// The entity shape schema 40 wrote: read by the 40 -> 41 step, which freezes it because it adds a field to the creature record. Everything
// else in the section is unchanged by that step and is referenced live, exactly as the 39 -> 40 step referenced the inventory, progression
// and equipment DTOs.

using MessagePack;

namespace UNNAMED.Persistence.Sections.V40;

[MessagePackObject]
public sealed class EntitySection
{
    [Key("records")] public EntityDto[] Records { get; set; } = Array.Empty<EntityDto>();
    [Key("created")] public CreatedDto[] Created { get; set; } = Array.Empty<CreatedDto>();
    [Key("baselines")] public CellBaselineDto[] Baselines { get; set; } = Array.Empty<CellBaselineDto>();
    [Key("containers")] public ContainerDto[]? Containers { get; set; }
    [Key("creatures")] public Creature[]? Creatures { get; set; }
    [Key("noises")] public NoiseDto[]? Noises { get; set; }
    [Key("economy")] public EconomyDto? Economy { get; set; }
    [Key("merchants")] public MerchantDto[]? Merchants { get; set; }
    [Key("npc_life")] public NpcLifeDto? NpcLife { get; set; }
    [Key("adventurers")] public AdventurersDto? Adventurers { get; set; }
}

/// <summary>One spawner's creature as schema 40 wrote it: no carcass count, because schema 40 did not keep one.</summary>
[MessagePackObject]
public sealed class Creature
{
    [Key("scale_percent")] public int ScalePercent { get; set; } = 100;
    [Key("level_bonus")] public int LevelBonus { get; set; }
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
    [Key("continuation")] public CreatureContinuationDto? Continuation { get; set; }
}
