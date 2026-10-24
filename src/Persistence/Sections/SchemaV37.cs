// Frozen schema 37 entity envelope and creature state, before persisted encounter scale.
using MessagePack;
namespace UNNAMED.Persistence.Sections.V37;

[MessagePackObject]
public sealed class EntitiesSection
{
    [Key("records")] public EntityDto[] Records { get; set; } = Array.Empty<EntityDto>();

    /// <summary>Persistent instances no baseline slot generates (schema 3).</summary>
    [Key("created")] public CreatedDto[] Created { get; set; } = Array.Empty<CreatedDto>();

    /// <summary>The baseline hash of every cell hosting a record: each record is proven against its host cell.</summary>
    [Key("baselines")] public CellBaselineDto[] Baselines { get; set; } = Array.Empty<CellBaselineDto>();

    /// <summary>Changed world containers with their whole contents. Required from schema 6.</summary>
    [Key("containers")] public ContainerDto[]? Containers { get; set; }

    /// <summary>Spawners' creatures that left their baseline. Required from schema 8.</summary>
    [Key("creatures")] public CreatureDto[]? Creatures { get; set; }

    /// <summary>The sounds made on the last tick that creatures hear on the next, in order. Required from schema 14.</summary>
    [Key("noises")] public NoiseDto[]? Noises { get; set; }

    /// <summary>The town markets, the road and the economy's day (WP14); null in a save from before the economy first ran (schema 22).</summary>
    [Key("economy")] public EconomyDto? Economy { get; set; }

    /// <summary>The merchants' purses and restocks (WP14). Required from schema 22.</summary>
    [Key("merchants")] public MerchantDto[]? Merchants { get; set; }

    /// <summary>Every NPC life and the vacancies (WP12); null in a save from before lives first ran (schema 26).</summary>
    [Key("npc_life")] public NpcLifeDto? NpcLife { get; set; }

    /// <summary>Every NPC adventurer and the slots awaiting arrivals (WP13); null in a save from before adventurers first lived (schema 29).</summary>
    [Key("adventurers")] public AdventurersDto? Adventurers { get; set; }
}

[MessagePackObject]
public sealed class CreatureDto
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

    /// <summary>Required from schema 14. The 13 -> 14 step gives older saves none: the charge, the stagger and the stun were not kept.</summary>
    [Key("continuation")] public CreatureContinuationDto? Continuation { get; set; }
}
