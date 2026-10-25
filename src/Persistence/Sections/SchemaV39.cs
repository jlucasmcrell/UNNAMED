// Frozen schema 38-39 entity envelope, before enhancements and the World Event Director.
using MessagePack;
namespace UNNAMED.Persistence.Sections.V39;

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
    [Key("creatures")] public UNNAMED.Persistence.Sections.CreatureDto[]? Creatures { get; set; }

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
