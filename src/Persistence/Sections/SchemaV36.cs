// Schema 36 entities/adventurer shapes. FROZEN before full kept-item records in schema 37.
using MessagePack;
namespace UNNAMED.Persistence.Sections.V36;

using ContainerDto = UNNAMED.Persistence.Sections.V41.ContainerDto;

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
    [Key("creatures")] public UNNAMED.Persistence.Sections.V37.CreatureDto[]? Creatures { get; set; }

    /// <summary>The sounds made on the last tick that creatures hear on the next, in order. Required from schema 14.</summary>
    [Key("noises")] public NoiseDto[]? Noises { get; set; }

    /// <summary>The town markets, the road and the economy's day (WP14); null in a save from before the economy first ran (schema 22).</summary>
    [Key("economy")] public EconomyDto? Economy { get; set; }

    /// <summary>The merchants' purses and restocks (WP14). Required from schema 22.</summary>
    [Key("merchants")] public MerchantDto[]? Merchants { get; set; }

    /// <summary>Every NPC life and the vacancies (WP12); null in a save from before lives first ran (schema 26).</summary>
    [Key("npc_life")] public NpcLifeDto? NpcLife { get; set; }

    /// <summary>Every NPC adventurer and the slots awaiting arrivals (WP13); null in a save from before adventurers first lived (schema 29).</summary>
    [Key("adventurers")] public Adventurers? Adventurers { get; set; }
}

/// <summary>
/// The NPC adventurers (schema 29; WP13): the last day lived, every adventurer - keyed with no limit on how many (DN5) - and the slots waiting
/// for an arrival.
/// </summary>
[MessagePackObject]
public sealed class Adventurers
{
    [Key("day")] public long Day { get; set; }
    [Key("agents")] public Adventurer[] Agents { get; set; } = Array.Empty<Adventurer>();
    [Key("vacancies")] public AdventurerVacancyDto[] Vacancies { get; set; } = Array.Empty<AdventurerVacancyDto>();
    [Key("bodies")] public AdventurerBodyDto[] Bodies { get; set; } = Array.Empty<AdventurerBodyDto>();
}

/// <summary>An adventurer's body standing outside the party: whose, by name, and where - a load stands exactly these.</summary>
[MessagePackObject]
public sealed class AdventurerBodyDto
{
    [Key("key")] public string Key { get; set; } = "";
    [Key("name")] public string Name { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("facing_mdeg")] public int FacingMdeg { get; set; }
}

/// <summary>One adventurer: who, where, their progression on the character's rules, gear as real items, coin, pack, condition, plan and party.</summary>
[MessagePackObject]
public sealed class Adventurer
{
    [Key("key")] public string Key { get; set; } = "";
    [Key("slot")] public string Slot { get; set; } = "";
    [Key("generation")] public int Generation { get; set; }
    [Key("name")] public string Name { get; set; } = "";
    [Key("people_id")] public string? PeopleId { get; set; }
    [Key("settlement")] public string Settlement { get; set; } = "";
    [Key("at")] public string At { get; set; } = "";
    [Key("progression")] public ProgressionDto Progression { get; set; } = new();
    [Key("coin")] public long Coin { get; set; }
    [Key("risk_percent")] public int RiskPercent { get; set; }
    [Key("arrived_day")] public long ArrivedDay { get; set; }
    [Key("gear")] public AdventurerGear[] Gear { get; set; } = Array.Empty<AdventurerGear>();
    [Key("pack")] public GoodCountDto[] Pack { get; set; } = Array.Empty<GoodCountDto>();
    [Key("condition")] public string Condition { get; set; } = "active";
    [Key("injured_until_day")] public long InjuredUntilDay { get; set; }
    [Key("plan")] public AdventurerPlanDto Plan { get; set; } = new();
    [Key("party")] public string? Party { get; set; }
    [Key("acquired")] public int Acquired { get; set; }
    [Key("kills")] public long Kills { get; set; }
    [Key("bounties_claimed")] public int BountiesClaimed { get; set; }
}

[MessagePackObject]
public sealed class AdventurerGear
{
    [Key("slot")] public string Slot { get; set; } = "";
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("quality")] public int Quality { get; set; }
}

