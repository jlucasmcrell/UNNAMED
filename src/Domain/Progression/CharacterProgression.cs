// UNNAMED Domain - a character's progression state (PROGRESSION.md §3-§4; PERSISTENCE.md §5.1, schema 4)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Progression;

/// <summary>A discipline's competence: its level and the XP gathered toward the next one.</summary>
public sealed record SkillState(int Level, long ProgressXp);

/// <summary>
/// How a known technique, formula or recipe was learned. <see cref="SourceRef"/> is provenance (a
/// teacher's instance ID, a book's or quest's definition ID); it is never read as a reference.
/// </summary>
public sealed record KnownTechnique(LearningSource Source, string? SourceRef, long Tick);

/// <summary>A one-time attribute grant (§4.1, §11.3). The same source can grant only once.</summary>
public sealed record AttributeGrant(CharacterAttribute Attribute, int Amount, GrantSource Source, string SourceRef);

/// <summary>Current pool values. Null means full: maxima are derived, never stored. Strain starts at zero.</summary>
public sealed record PoolState(int? Health, int? Stamina, int? Focus, int Strain)
{
    public static PoolState Full { get; } = new(null, null, null, 0);
}

/// <summary>AG-2: how many of a species this character has killed on a given world day.</summary>
public sealed record SpeciesDay(long Day, int Kills);

/// <summary>The anti-farm guards' memory (§3.4). Keys are ordinal-sorted so the digest and the save are stable.</summary>
public sealed record XpGuardState(
    ImmutableSortedDictionary<string, SpeciesDay> SpeciesToday,
    ImmutableSortedSet<string> SpeciesEverKilled,
    ImmutableSortedDictionary<string, ImmutableArray<long>> ClusterKills)
{
    public static XpGuardState Empty { get; } = new(
        ImmutableSortedDictionary.Create<string, SpeciesDay>(StringComparer.Ordinal),
        ImmutableSortedSet.Create<string>(StringComparer.Ordinal),
        ImmutableSortedDictionary.Create<string, ImmutableArray<long>>(StringComparer.Ordinal));
}

/// <summary>
/// Everything a character's progression persists (PROGRESSION.md §3-§4, PERSISTENCE.md §5.1). Immutable:
/// <see cref="ProgressionEngine"/> returns a new value for every change. Derived values (pool maxima,
/// attribute totals) are computed from this and <see cref="ProgressionRules"/>, never stored.
/// </summary>
public sealed record CharacterProgression
{
    public static CharacterProgression Empty { get; } = new();

    public int Level { get; init; } = 1;
    public long LevelProgressXp { get; init; }

    /// <summary>AG-8: XP owed after deaths, repaid from later XP before it counts as progress.</summary>
    public long XpDebt { get; init; }

    public ImmutableSortedDictionary<XpSource, long> LifetimeXp { get; init; } = ImmutableSortedDictionary<XpSource, long>.Empty;

    public ImmutableSortedDictionary<CharacterAttribute, int> Allocation { get; init; } = ImmutableSortedDictionary<CharacterAttribute, int>.Empty;
    public int UnspentAttributePoints { get; init; }
    public ImmutableArray<AttributeGrant> Grants { get; init; } = ImmutableArray<AttributeGrant>.Empty;

    public ImmutableSortedDictionary<string, SkillState> Skills { get; init; } =
        ImmutableSortedDictionary.Create<string, SkillState>(StringComparer.Ordinal);

    public ImmutableSortedDictionary<string, KnownTechnique> Known { get; init; } =
        ImmutableSortedDictionary.Create<string, KnownTechnique>(StringComparer.Ordinal);

    /// <summary>Definitions whose first-time production XP has been claimed (AG-7: first time only).</summary>
    public ImmutableSortedSet<string> ProductionFirsts { get; init; } = ImmutableSortedSet.Create<string>(StringComparer.Ordinal);

    /// <summary>Techniques, formulas or outputs whose one-time skill novelty bonus has been claimed (§4.2).</summary>
    public ImmutableSortedSet<string> NoveltyFirsts { get; init; } = ImmutableSortedSet.Create<string>(StringComparer.Ordinal);

    public PoolState Pools { get; init; } = PoolState.Full;

    public XpGuardState Guards { get; init; } = XpGuardState.Empty;

    /// <summary>Throws when a field is out of range: a decoded save that fails this is corrupt, not a character.</summary>
    public CharacterProgression Validate()
    {
        Require(Level >= 1, $"level {Level}");
        Require(LevelProgressXp >= 0 && XpDebt >= 0 && UnspentAttributePoints >= 0, "negative XP, debt or unspent points");
        Require(LifetimeXp.Values.All(v => v >= 0), "negative lifetime XP");
        Require(Allocation.Values.All(v => v >= 0), "negative attribute allocation");
        foreach (var grant in Grants)
            Require(grant.Amount > 0 && !string.IsNullOrEmpty(grant.SourceRef), $"attribute grant {grant}");
        Require(Grants.Select(g => (g.Source, g.SourceRef)).Distinct().Count() == Grants.Length, "an attribute grant source used twice");
        foreach (var (id, skill) in Skills)
            Require(ProgressionKeys.IsSkillId(id) && skill.Level is >= 0 and <= 100 && skill.ProgressXp >= 0, $"skill {id} {skill}");
        foreach (var id in Known.Keys)
            Require(ProgressionKeys.IsTechniqueId(id), $"known technique '{id}'");
        foreach (var id in ProductionFirsts.Concat(NoveltyFirsts))
            Require(DefinitionId.IsValid(id), $"first-time record '{id}'");
        Require(Pools.Health is null or >= 0 && Pools.Stamina is null or >= 0 && Pools.Focus is null or >= 0 && Pools.Strain >= 0, $"pools {Pools}");
        foreach (var (species, day) in Guards.SpeciesToday)
            Require(DefinitionId.IsValid(species) && day.Day >= 0 && day.Kills > 0, $"AG-2 record {species}");
        Require(Guards.SpeciesEverKilled.All(DefinitionId.IsValid), "AG-1 species record");
        return this;

        static void Require(bool condition, string what)
        {
            if (!condition)
                throw new FormatException($"Invalid progression record: {what}");
        }
    }

    /// <summary>
    /// The same progression with every stored definition ID passed through <paramref name="resolve"/> - the
    /// definition-ID pass (PERSISTENCE.md §6.4, M2b). A null result drops the entry. Two entries that resolve to
    /// one ID merge: a skill keeps its higher competence, a technique its earliest learning, and records union.
    /// </summary>
    public CharacterProgression RewriteDefinitionIds(Func<string, string, string?> resolve)
    {
        var skills = ImmutableSortedDictionary.CreateBuilder<string, SkillState>(StringComparer.Ordinal);
        foreach (var (id, state) in Skills)
        {
            if (resolve(id, "skill") is not { } current)
                continue;
            if (!skills.TryGetValue(current, out var existing) || (state.Level, state.ProgressXp).CompareTo((existing.Level, existing.ProgressXp)) > 0)
                skills[current] = state;
        }

        var known = ImmutableSortedDictionary.CreateBuilder<string, KnownTechnique>(StringComparer.Ordinal);
        foreach (var (id, technique) in Known)
        {
            if (resolve(id, "known technique") is not { } current)
                continue;
            if (!known.TryGetValue(current, out var existing) || technique.Tick < existing.Tick)
                known[current] = technique;
        }

        ImmutableSortedSet<string> Map(ImmutableSortedSet<string> ids, string role) =>
            ids.Select(id => resolve(id, role)).OfType<string>().ToImmutableSortedSet(StringComparer.Ordinal);

        var speciesToday = ImmutableSortedDictionary.CreateBuilder<string, SpeciesDay>(StringComparer.Ordinal);
        foreach (var (species, day) in Guards.SpeciesToday)
        {
            if (resolve(species, "kill record") is not { } current)
                continue;
            if (!speciesToday.TryGetValue(current, out var existing) || day.Day > existing.Day)
                speciesToday[current] = day;
            else if (day.Day == existing.Day)
                speciesToday[current] = existing with { Kills = existing.Kills + day.Kills };
        }

        return this with
        {
            Skills = skills.ToImmutable(),
            Known = known.ToImmutable(),
            ProductionFirsts = Map(ProductionFirsts, "production record"),
            NoveltyFirsts = Map(NoveltyFirsts, "novelty record"),
            Guards = Guards with
            {
                SpeciesToday = speciesToday.ToImmutable(),
                SpeciesEverKilled = Map(Guards.SpeciesEverKilled, "kill record"),
            },
        };
    }

    /// <summary>Full-equality digest over every field: two progressions are equal exactly when their digests are.</summary>
    public string Digest
    {
        get
        {
            using var h = new CanonicalHasher();
            h.Add("unnamed.progression/v1").Add(Level).Add(LevelProgressXp).Add(XpDebt);
            h.Add(LifetimeXp.Count);
            foreach (var (source, xp) in LifetimeXp)
                h.Add(ProgressionKeys.Key(source)).Add(xp);
            h.Add(Allocation.Count);
            foreach (var (attribute, points) in Allocation)
                h.Add(ProgressionKeys.Key(attribute)).Add(points);
            h.Add(UnspentAttributePoints).Add(Grants.Length);
            foreach (var g in Grants)
                h.Add(ProgressionKeys.Key(g.Attribute)).Add(g.Amount).Add(ProgressionKeys.Key(g.Source)).Add(g.SourceRef);
            h.Add(Skills.Count);
            foreach (var (id, skill) in Skills)
                h.Add(id).Add(skill.Level).Add(skill.ProgressXp);
            h.Add(Known.Count);
            foreach (var (id, technique) in Known)
                h.Add(id).Add(ProgressionKeys.Key(technique.Source)).Add(technique.SourceRef ?? "").Add(technique.Tick);
            h.Add(ProductionFirsts.Count);
            foreach (var id in ProductionFirsts)
                h.Add(id);
            h.Add(NoveltyFirsts.Count);
            foreach (var id in NoveltyFirsts)
                h.Add(id);
            h.Add(Pools.Health ?? -1).Add(Pools.Stamina ?? -1).Add(Pools.Focus ?? -1).Add(Pools.Strain);
            h.Add(Guards.SpeciesToday.Count);
            foreach (var (species, day) in Guards.SpeciesToday)
                h.Add(species).Add(day.Day).Add(day.Kills);
            h.Add(Guards.SpeciesEverKilled.Count);
            foreach (var species in Guards.SpeciesEverKilled)
                h.Add(species);
            h.Add(Guards.ClusterKills.Count);
            foreach (var (cluster, ticks) in Guards.ClusterKills)
            {
                h.Add(cluster).Add(ticks.Length);
                foreach (long tick in ticks)
                    h.Add(tick);
            }
            return h.Finish();
        }
    }
}
