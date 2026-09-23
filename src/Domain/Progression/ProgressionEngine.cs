// UNNAMED Domain - the progression rules as pure functions (PROGRESSION.md §1-§4, ROADMAP.md M2c)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Progression;

// The non-conversion law as a typed API (§1): each axis advances only through its own currency type.
// There is no overload that takes gold, an item, or another axis's currency, and Domain cannot even
// see item types - it does not reference World.

/// <summary>Level XP (§3.3). Combat needs <see cref="Kill"/>; production needs <see cref="FirstKey"/> (first time only).</summary>
public sealed record XpAward(XpSource Source, long Amount, long Tick)
{
    public KillContext? Kill { get; init; }

    /// <summary>The definition produced for the first time. A repeat earns nothing (AG-7).</summary>
    public string? FirstKey { get; init; }
}

/// <summary>What AG-1..AG-3 need to know about a kill: the species, its level, and its spawn cluster.</summary>
public sealed record KillContext(string SpeciesId, int CreatureLevel, string ClusterKey);

/// <summary>One use of a discipline under some difficulty (§4.2). A first success may name a novelty key.</summary>
public sealed record SkillPractice(string SkillId, int Difficulty, PracticeOutcome Outcome, long Tick)
{
    public string? NoveltyKey { get; init; }
}

/// <summary>A learning event (§4.4): the only way a technique, formula or recipe becomes known.</summary>
public sealed record TechniqueLearning(string DefinitionId, LearningSource Source, long Tick)
{
    public string? SourceRef { get; init; }
}

/// <summary>Spending unspent attribute points (§4.1).</summary>
public sealed record AttributeAllocation(CharacterAttribute Attribute, int Points);

/// <summary>One step of progress on one axis, for the telemetry log (§13.2).</summary>
public sealed record Advancement(Axis Axis, string Key, long Amount, string Source, long Tick);

public sealed record XpResult(CharacterProgression Progression, long Awarded, long Repaid, int LevelsGained, double Multiplier,
    ImmutableArray<Advancement> Advancements);

public sealed record SkillResult(CharacterProgression Progression, long XpGained, int LevelsGained, ImmutableArray<Advancement> Advancements);

public sealed record LearnResult(CharacterProgression Progression, bool Learned, ImmutableArray<Advancement> Advancements);

public sealed record DeathResult(CharacterProgression Progression, long DebtAdded);

public sealed record DerivedStats(long HealthMax, long StaminaMax, long FocusMax, long Resonance, long StrainTolerance);

public static class ProgressionEngine
{
    /// <summary>A new character: level 1, plus the starting package (§5).</summary>
    public static CharacterProgression Create(ProgressionRules rules)
    {
        var package = rules.StartingPackage;
        var progression = CharacterProgression.Empty;
        foreach (var (attribute, points) in package.AttributeBias)
            progression = Grant(progression,
                new AttributeGrant(attribute, points, GrantSource.Creation, "starting_package." + ProgressionKeys.Key(attribute)), rules);
        var skills = progression.Skills.ToBuilder();
        foreach (var (skill, level) in package.Skills)
        {
            if (!ProgressionKeys.IsSkillId(skill) || level < 0 || level > rules.Skills.CommonCeiling)
                throw new ArgumentException($"Starting package skill {skill} at {level} is not a valid starting competence");
            skills[skill] = new SkillState(level, 0);
        }
        progression = progression with { Skills = skills.ToImmutable() };
        foreach (string technique in package.Techniques)
            progression = Learn(progression, new TechniqueLearning(technique, LearningSource.StartingPackage, 0)).Progression;
        return progression;
    }

    /// <summary>
    /// Level XP through the anti-farm guards (§3.4), then debt repayment, then level-ups. A level-up grants
    /// attribute points and nothing else (§3.1).
    /// </summary>
    public static XpResult Award(CharacterProgression progression, XpAward award, ProgressionRules rules)
    {
        if (award.Amount < 0)
            throw new ArgumentOutOfRangeException(nameof(award), award.Amount, "XP awards are never negative");

        double multiplier = 1.0;
        var guards = progression.Guards;
        var productionFirsts = progression.ProductionFirsts;
        switch (award.Source)
        {
            case XpSource.Combat:
                var kill = award.Kill ?? throw new ArgumentException("A combat award needs its kill context (AG-1..AG-3)", nameof(award));
                (multiplier, guards) = CombatMultiplier(progression.Level, kill, award.Tick, guards, rules);
                break;
            case XpSource.Production:
                string first = award.FirstKey is { } key && DefinitionId.IsValid(key)
                    ? key
                    : throw new ArgumentException("A production award names the definition produced for the first time (AG-7)", nameof(award));
                if (productionFirsts.Contains(first))
                    multiplier = 0.0;
                else
                    productionFirsts = productionFirsts.Add(first);
                break;
        }

        long awarded = (long)Math.Round(award.Amount * multiplier, MidpointRounding.AwayFromZero);
        long repaid = Math.Min(awarded, progression.XpDebt);
        long debt = progression.XpDebt - repaid;
        long progress = progression.LevelProgressXp + awarded - repaid;
        int level = progression.Level;
        int unspent = progression.UnspentAttributePoints;
        int gained = 0;
        while (level < rules.LevelCap && progress >= rules.Curve.ToReach(level + 1))
        {
            progress -= rules.Curve.ToReach(level + 1);
            level++;
            gained++;
            unspent += rules.AttributePointsPerLevel;
        }
        if (level >= rules.LevelCap)
            progress = 0;   // the cap is the cap: XP past it is recorded in lifetime totals only

        var advancements = ImmutableArray.CreateBuilder<Advancement>();
        if (awarded > 0)
            advancements.Add(new Advancement(Axis.Level, "xp", awarded, ProgressionKeys.Key(award.Source), award.Tick));
        if (gained > 0)
            advancements.Add(new Advancement(Axis.Attributes, "unspent_points", gained * (long)rules.AttributePointsPerLevel, "level_up", award.Tick));

        var result = progression with
        {
            Level = level,
            LevelProgressXp = progress,
            XpDebt = debt,
            UnspentAttributePoints = unspent,
            LifetimeXp = progression.LifetimeXp.SetItem(award.Source, progression.LifetimeXp.GetValueOrDefault(award.Source) + awarded),
            ProductionFirsts = productionFirsts,
            Guards = guards,
        };
        return new XpResult(result, awarded, repaid, gained, multiplier, advancements.ToImmutable());
    }

    /// <summary>
    /// Use under challenge (§4.2): XP only past the difficulty gate, scaled by how far past it, reduced on failure,
    /// plus a one-time novelty bonus for a first success. A skill stops at the common ceiling (designations are M12).
    /// </summary>
    public static SkillResult Practice(CharacterProgression progression, SkillPractice practice, ProgressionRules rules)
    {
        if (!ProgressionKeys.IsSkillId(practice.SkillId))
            throw new ArgumentException($"'{practice.SkillId}' is not a skill ID", nameof(practice));
        var model = rules.Skills;
        var state = progression.Skills.GetValueOrDefault(practice.SkillId) ?? new SkillState(0, 0);

        int gate = state.Level - model.DifficultyMargin;
        double challenge = practice.Difficulty <= gate
            ? 0.0
            : Math.Min((double)(practice.Difficulty - gate) / model.DifficultyMargin, model.MaxChallengeFactor);
        double outcome = practice.Outcome == PracticeOutcome.Success ? 1.0 : model.FailureFactor;
        long xp = (long)Math.Round(model.UseXp * challenge * outcome, MidpointRounding.AwayFromZero);

        var novelty = progression.NoveltyFirsts;
        if (practice.Outcome == PracticeOutcome.Success && practice.NoveltyKey is { } key)
        {
            if (!DefinitionId.IsValid(key))
                throw new ArgumentException($"Novelty key '{key}' must be the definition performed or produced", nameof(practice));
            if (!novelty.Contains(key))
            {
                novelty = novelty.Add(key);
                xp += model.NoveltyBonusXp;
            }
        }

        int level = state.Level;
        long progress = state.ProgressXp + xp;
        int gained = 0;
        while (level < model.CommonCeiling && progress >= model.ToNext(level))
        {
            progress -= model.ToNext(level);
            level++;
            gained++;
        }
        if (level >= model.CommonCeiling)
            progress = 0;

        var advancements = xp > 0
            ? ImmutableArray.Create(new Advancement(Axis.Skills, practice.SkillId, xp, "practice", practice.Tick))
            : ImmutableArray<Advancement>.Empty;
        var result = progression with
        {
            Skills = progression.Skills.SetItem(practice.SkillId, new SkillState(level, progress)),
            NoveltyFirsts = novelty,
        };
        return new SkillResult(result, xp, gained, advancements);
    }

    /// <summary>Learn a technique, formula or recipe (§4.4). Learning changes nothing but the knowledge record.</summary>
    public static LearnResult Learn(CharacterProgression progression, TechniqueLearning learning)
    {
        if (!ProgressionKeys.IsTechniqueId(learning.DefinitionId))
            throw new ArgumentException($"'{learning.DefinitionId}' is not a technique, formula or recipe ID", nameof(learning));
        if (progression.Known.ContainsKey(learning.DefinitionId))
            return new LearnResult(progression, false, ImmutableArray<Advancement>.Empty);
        var result = progression with
        {
            Known = progression.Known.Add(learning.DefinitionId, new KnownTechnique(learning.Source, learning.SourceRef, learning.Tick)),
        };
        var advancement = new Advancement(Axis.Techniques, learning.DefinitionId, 1, ProgressionKeys.Key(learning.Source), learning.Tick);
        return new LearnResult(result, true, ImmutableArray.Create(advancement));
    }

    /// <summary>Spend unspent attribute points (§4.1). Points come only from level-ups.</summary>
    public static CharacterProgression Allocate(CharacterProgression progression, AttributeAllocation allocation)
    {
        if (allocation.Points <= 0 || allocation.Points > progression.UnspentAttributePoints)
            throw new ArgumentException(
                $"Cannot allocate {allocation.Points} point(s) with {progression.UnspentAttributePoints} unspent", nameof(allocation));
        return progression with
        {
            Allocation = progression.Allocation.SetItem(allocation.Attribute,
                progression.Allocation.GetValueOrDefault(allocation.Attribute) + allocation.Points),
            UnspentAttributePoints = progression.UnspentAttributePoints - allocation.Points,
        };
    }

    /// <summary>
    /// A bounded one-time grant (§4.1, §11.3). Each source grants once. Grants after creation (trainer, quest,
    /// item) share a budget of at most 8% of the attribute points levels give.
    /// </summary>
    public static CharacterProgression Grant(CharacterProgression progression, AttributeGrant grant, ProgressionRules rules)
    {
        if (grant.Amount <= 0 || string.IsNullOrEmpty(grant.SourceRef))
            throw new ArgumentException($"Invalid attribute grant {grant}", nameof(grant));
        if (progression.Grants.Any(g => g.Source == grant.Source && g.SourceRef == grant.SourceRef))
            throw new InvalidOperationException($"{ProgressionKeys.Key(grant.Source)} '{grant.SourceRef}' has already granted an attribute");
        if (grant.Source is GrantSource.Trainer or GrantSource.Quest or GrantSource.Item)
        {
            int used = progression.Grants.Where(g => g.Source is GrantSource.Trainer or GrantSource.Quest or GrantSource.Item).Sum(g => g.Amount);
            if (used + grant.Amount > rules.AttributeGrantBudget)
                throw new InvalidOperationException(
                    $"Post-creation attribute grants are capped at {rules.AttributeGrantBudget} point(s) (§11.3); {used} already used");
        }
        return progression with { Grants = progression.Grants.Add(grant) };
    }

    /// <summary>
    /// AG-8: death owes a fraction of the current level's XP span as debt. Progress and level are never
    /// reduced, and total debt never exceeds the configured number of spans.
    /// </summary>
    public static DeathResult Die(CharacterProgression progression, ProgressionRules rules)
    {
        long span = rules.Curve.ToReach(progression.Level + 1);
        long owed = (long)Math.Round(span * rules.Guards.DebtFraction, MidpointRounding.AwayFromZero);
        long cap = (long)Math.Round(span * rules.Guards.MaxDebtSpans, MidpointRounding.AwayFromZero);
        long debt = Math.Min(progression.XpDebt + owed, Math.Max(cap, progression.XpDebt));
        return new DeathResult(progression with { XpDebt = debt }, debt - progression.XpDebt);
    }

    public static int AttributeValue(CharacterProgression progression, CharacterAttribute attribute, ProgressionRules rules) =>
        rules.AttributeBase
        + progression.Allocation.GetValueOrDefault(attribute)
        + progression.Grants.Where(g => g.Attribute == attribute).Sum(g => g.Amount);

    public static DerivedStats Derive(CharacterProgression progression, ProgressionRules rules)
    {
        int Attribute(CharacterAttribute a) => AttributeValue(progression, a, rules);
        var derived = rules.Derived;
        return new DerivedStats(
            derived.HealthMax.Evaluate(Attribute),
            derived.StaminaMax.Evaluate(Attribute),
            derived.FocusMax.Evaluate(Attribute),
            derived.Resonance.Evaluate(Attribute),
            derived.StrainTolerance.Evaluate(Attribute));
    }

    public static int SkillLevel(CharacterProgression progression, string skillId) =>
        progression.Skills.GetValueOrDefault(skillId)?.Level ?? 0;

    public static bool Knows(CharacterProgression progression, string definitionId) =>
        progression.Known.ContainsKey(definitionId);

    private static (double Multiplier, XpGuardState Guards) CombatMultiplier(
        int playerLevel, KillContext kill, long tick, XpGuardState guards, ProgressionRules rules)
    {
        if (!DefinitionId.IsValid(kill.SpeciesId) || string.IsNullOrEmpty(kill.ClusterKey))
            throw new ArgumentException($"Invalid kill context {kill}");
        var g = rules.Guards;

        // AG-1, which must never zero out a species this character has never killed.
        double band = g.LevelBandMultiplier(kill.CreatureLevel - playerLevel);
        if (band == 0.0 && !guards.SpeciesEverKilled.Contains(kill.SpeciesId))
            band = g.NewSpeciesFloor;

        // AG-2: novelty per world day.
        long day = tick / rules.TicksPerWorldDay;
        int earlierToday = guards.SpeciesToday.TryGetValue(kill.SpeciesId, out var today) && today.Day == day ? today.Kills : 0;
        double species = Math.Max(Math.Pow(g.SpeciesDecay, earlierToday), g.SpeciesFloor);

        // AG-3: saturation of one spawn cluster inside a rolling window of world ticks.
        var recent = (guards.ClusterKills.TryGetValue(kill.ClusterKey, out var kills) ? kills : ImmutableArray<long>.Empty)
            .Where(t => t > tick - g.ClusterWindowTicks)
            .ToImmutableArray();
        double cluster = recent.Length >= g.ClusterThreshold ? g.ClusterFloor : 1.0;

        // Both saturation guards floor at the same value; together they never compound below it.
        double saturation = Math.Max(species * cluster, Math.Min(g.SpeciesFloor, g.ClusterFloor));

        var updated = guards with
        {
            SpeciesEverKilled = guards.SpeciesEverKilled.Add(kill.SpeciesId),
            SpeciesToday = guards.SpeciesToday.SetItem(kill.SpeciesId, new SpeciesDay(day, earlierToday + 1)),
            ClusterKills = guards.ClusterKills.SetItem(kill.ClusterKey, recent.Add(tick)),
        };
        return (band * saturation, updated);
    }
}
