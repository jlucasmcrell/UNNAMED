// UNNAMED Persistence - the player's progression record (PERSISTENCE.md §5.1, schema 4; PROGRESSION.md §3-§4)
// No Godot references - pure C#

using System.Collections.Immutable;
using MessagePack;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Persistence.Sections;

// Enums are saved as their snake_case keys (ProgressionKeys), never as ordinals, and every collection is
// written in its canonical sorted order so equal progressions encode to equal bytes (T-03).

[MessagePackObject]
public sealed class ProgressionDto
{
    [Key("level")] public int Level { get; set; } = 1;
    [Key("level_progress_xp")] public long LevelProgressXp { get; set; }
    [Key("xp_debt")] public long XpDebt { get; set; }
    [Key("lifetime_xp")] public SourceXpDto[] LifetimeXp { get; set; } = Array.Empty<SourceXpDto>();
    [Key("attribute_allocation")] public AttributePointsDto[] AttributeAllocation { get; set; } = Array.Empty<AttributePointsDto>();
    [Key("unspent_attribute_points")] public int UnspentAttributePoints { get; set; }
    [Key("attribute_grants")] public AttributeGrantDto[] AttributeGrants { get; set; } = Array.Empty<AttributeGrantDto>();
    [Key("skills")] public SkillDto[] Skills { get; set; } = Array.Empty<SkillDto>();
    [Key("known")] public KnownDto[] Known { get; set; } = Array.Empty<KnownDto>();
    [Key("production_firsts")] public string[] ProductionFirsts { get; set; } = Array.Empty<string>();
    [Key("novelty_firsts")] public string[] NoveltyFirsts { get; set; } = Array.Empty<string>();
    [Key("pools")] public PoolsDto Pools { get; set; } = new();
    [Key("guards")] public GuardsDto Guards { get; set; } = new();
}

[MessagePackObject]
public sealed class SourceXpDto
{
    [Key("source")] public string Source { get; set; } = "";
    [Key("xp")] public long Xp { get; set; }
}

[MessagePackObject]
public sealed class AttributePointsDto
{
    [Key("attribute")] public string Attribute { get; set; } = "";
    [Key("points")] public int Points { get; set; }
}

[MessagePackObject]
public sealed class AttributeGrantDto
{
    [Key("attribute")] public string Attribute { get; set; } = "";
    [Key("amount")] public int Amount { get; set; }
    [Key("source")] public string Source { get; set; } = "";
    [Key("source_ref")] public string SourceRef { get; set; } = "";
}

[MessagePackObject]
public sealed class SkillDto
{
    [Key("skill_id")] public string SkillId { get; set; } = "";
    [Key("level")] public int Level { get; set; }
    [Key("progress_xp")] public long ProgressXp { get; set; }
}

[MessagePackObject]
public sealed class KnownDto
{
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("source")] public string Source { get; set; } = "";
    [Key("source_ref")] public string? SourceRef { get; set; }
    [Key("tick")] public long Tick { get; set; }
}

/// <summary>Current pools. A nil pool is full: maxima are derived from attributes, never stored.</summary>
[MessagePackObject]
public sealed class PoolsDto
{
    [Key("health")] public int? Health { get; set; }
    [Key("stamina")] public int? Stamina { get; set; }
    [Key("focus")] public int? Focus { get; set; }
    [Key("strain")] public int Strain { get; set; }
}

[MessagePackObject]
public sealed class GuardsDto
{
    [Key("species_today")] public SpeciesDayDto[] SpeciesToday { get; set; } = Array.Empty<SpeciesDayDto>();
    [Key("species_ever_killed")] public string[] SpeciesEverKilled { get; set; } = Array.Empty<string>();
    [Key("cluster_kills")] public ClusterKillsDto[] ClusterKills { get; set; } = Array.Empty<ClusterKillsDto>();
}

[MessagePackObject]
public sealed class SpeciesDayDto
{
    [Key("species_id")] public string SpeciesId { get; set; } = "";
    [Key("day")] public long Day { get; set; }
    [Key("kills")] public int Kills { get; set; }
}

[MessagePackObject]
public sealed class ClusterKillsDto
{
    [Key("cluster_key")] public string ClusterKey { get; set; } = "";
    [Key("ticks")] public long[] Ticks { get; set; } = Array.Empty<long>();
}

public static class ProgressionCodec
{
    public static ProgressionDto ToDto(CharacterProgression p) => new()
    {
        Level = p.Level,
        LevelProgressXp = p.LevelProgressXp,
        XpDebt = p.XpDebt,
        LifetimeXp = p.LifetimeXp.Select(kv => new SourceXpDto { Source = ProgressionKeys.Key(kv.Key), Xp = kv.Value }).ToArray(),
        AttributeAllocation = p.Allocation
            .Select(kv => new AttributePointsDto { Attribute = ProgressionKeys.Key(kv.Key), Points = kv.Value })
            .ToArray(),
        UnspentAttributePoints = p.UnspentAttributePoints,
        AttributeGrants = p.Grants.Select(g => new AttributeGrantDto
        {
            Attribute = ProgressionKeys.Key(g.Attribute),
            Amount = g.Amount,
            Source = ProgressionKeys.Key(g.Source),
            SourceRef = g.SourceRef,
        }).ToArray(),
        Skills = p.Skills.Select(kv => new SkillDto { SkillId = kv.Key, Level = kv.Value.Level, ProgressXp = kv.Value.ProgressXp }).ToArray(),
        Known = p.Known.Select(kv => new KnownDto
        {
            DefId = kv.Key,
            Source = ProgressionKeys.Key(kv.Value.Source),
            SourceRef = kv.Value.SourceRef,
            Tick = kv.Value.Tick,
        }).ToArray(),
        ProductionFirsts = p.ProductionFirsts.ToArray(),
        NoveltyFirsts = p.NoveltyFirsts.ToArray(),
        Pools = new PoolsDto { Health = p.Pools.Health, Stamina = p.Pools.Stamina, Focus = p.Pools.Focus, Strain = p.Pools.Strain },
        Guards = new GuardsDto
        {
            SpeciesToday = p.Guards.SpeciesToday
                .Select(kv => new SpeciesDayDto { SpeciesId = kv.Key, Day = kv.Value.Day, Kills = kv.Value.Kills })
                .ToArray(),
            SpeciesEverKilled = p.Guards.SpeciesEverKilled.ToArray(),
            ClusterKills = p.Guards.ClusterKills
                .Select(kv => new ClusterKillsDto { ClusterKey = kv.Key, Ticks = kv.Value.ToArray() })
                .ToArray(),
        },
    };

    /// <summary>Decodes and validates. A malformed record throws <see cref="FormatException"/>: the save is corrupt.</summary>
    public static CharacterProgression FromDto(ProgressionDto dto) => new CharacterProgression
    {
        Level = dto.Level,
        LevelProgressXp = dto.LevelProgressXp,
        XpDebt = dto.XpDebt,
        LifetimeXp = Unique(dto.LifetimeXp, e => ProgressionKeys.ParseXpSource(e.Source), e => e.Xp, "lifetime XP source")
            .ToImmutableSortedDictionary(),
        Allocation = Unique(dto.AttributeAllocation, e => ProgressionKeys.ParseAttribute(e.Attribute), e => e.Points, "attribute")
            .ToImmutableSortedDictionary(),
        UnspentAttributePoints = dto.UnspentAttributePoints,
        Grants = dto.AttributeGrants
            .Select(g => new AttributeGrant(ProgressionKeys.ParseAttribute(g.Attribute), g.Amount, ProgressionKeys.ParseGrantSource(g.Source), g.SourceRef))
            .ToImmutableArray(),
        Skills = Unique(dto.Skills, s => s.SkillId, s => new SkillState(s.Level, s.ProgressXp), "skill")
            .ToImmutableSortedDictionary(StringComparer.Ordinal),
        Known = Unique(dto.Known, k => k.DefId, k => new KnownTechnique(ProgressionKeys.ParseLearningSource(k.Source), k.SourceRef, k.Tick), "known technique")
            .ToImmutableSortedDictionary(StringComparer.Ordinal),
        ProductionFirsts = dto.ProductionFirsts.ToImmutableSortedSet(StringComparer.Ordinal),
        NoveltyFirsts = dto.NoveltyFirsts.ToImmutableSortedSet(StringComparer.Ordinal),
        Pools = new PoolState(dto.Pools.Health, dto.Pools.Stamina, dto.Pools.Focus, dto.Pools.Strain),
        Guards = new XpGuardState(
            Unique(dto.Guards.SpeciesToday, s => s.SpeciesId, s => new SpeciesDay(s.Day, s.Kills), "AG-2 species")
                .ToImmutableSortedDictionary(StringComparer.Ordinal),
            dto.Guards.SpeciesEverKilled.ToImmutableSortedSet(StringComparer.Ordinal),
            Unique(dto.Guards.ClusterKills, c => c.ClusterKey, c => c.Ticks.Order().ToImmutableArray(), "AG-3 cluster")
                .ToImmutableSortedDictionary(StringComparer.Ordinal)),
    }.Validate();

    private static Dictionary<TKey, TValue> Unique<TItem, TKey, TValue>(
        IEnumerable<TItem> items, Func<TItem, TKey> key, Func<TItem, TValue> value, string what) where TKey : notnull
    {
        var result = new Dictionary<TKey, TValue>();
        foreach (var item in items)
        {
            if (!result.TryAdd(key(item), value(item)))
                throw new FormatException($"The progression record lists {what} '{key(item)}' twice");
        }
        return result;
    }
}
