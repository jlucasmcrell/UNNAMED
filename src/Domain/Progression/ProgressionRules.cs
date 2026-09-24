// UNNAMED Domain - progression rules (PROGRESSION.md §3-§4; DATA_MODEL.md §4.19, §4.21)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Progression;

/// <summary>
/// Every progression number, as data (D-03). The domain never reads files: Content builds this from
/// <c>config.xp_curve</c>, <c>config.level_cap</c>, <c>config.time</c> and <c>config.progression</c>.
/// </summary>
public sealed record ProgressionRules
{
    public required XpCurve Curve { get; init; }

    /// <summary>The cap in force (Phase 1: 5).</summary>
    public required int LevelCap { get; init; }

    /// <summary>The game's design cap (50). It sizes the §11.3 grant budget, which must not shrink with a prototype cap.</summary>
    public required int SoftCap { get; init; }

    public required int AttributeBase { get; init; }

    /// <summary>The only thing a level-up grants (§3.1).</summary>
    public required int AttributePointsPerLevel { get; init; }

    /// <summary>§11.3: post-creation attribute grants total at most this fraction of the level-earned points.</summary>
    public required double AttributeGrantCapFraction { get; init; }

    public required DerivedFormulas Derived { get; init; }

    public required SkillModel Skills { get; init; }

    public required XpGuards Guards { get; init; }

    /// <summary>One world_time day in domain ticks (<c>config.time</c>). AG-2 counts per world day, never per wall-clock day.</summary>
    public required long TicksPerWorldDay { get; init; }

    public required StartingPackage StartingPackage { get; init; }

    /// <summary>AG-6's target-hours table (§3.2).</summary>
    public required ImmutableArray<TierTarget> TierTargets { get; init; }

    /// <summary>The attribute points §11.3 lets post-creation grants add in total.</summary>
    public int AttributeGrantBudget => (int)Math.Floor(AttributeGrantCapFraction * (SoftCap - 1) * AttributePointsPerLevel);
}

/// <summary>
/// <c>XP(n) = Base * (n-1)^Exponent</c>, times the band factor for levels inside the band, rounded to the
/// nearest <see cref="RoundTo"/> (§3.2). The published total to level 50 is derived from this, and a test holds it.
/// </summary>
public sealed record XpCurve(int Base, double Exponent, int RoundTo, int BandFromLevel, int BandToLevel, double BandFactor)
{
    /// <summary>The XP needed to go from level <paramref name="level"/> - 1 to <paramref name="level"/>.</summary>
    public long ToReach(int level)
    {
        if (level < 2)
            throw new ArgumentOutOfRangeException(nameof(level), level, "Level 1 is where every character starts");
        double raw = Base * Math.Pow(level - 1, Exponent);
        if (level >= BandFromLevel && level <= BandToLevel)
            raw *= BandFactor;
        return (long)Math.Round(raw / RoundTo, MidpointRounding.AwayFromZero) * RoundTo;
    }

    /// <summary>The XP between level 1 and <paramref name="level"/>.</summary>
    public long TotalTo(int level)
    {
        long total = 0;
        for (int n = 2; n <= level; n++)
            total += ToReach(n);
        return total;
    }
}

/// <summary>A derived value: a base plus integer coefficients per attribute point (§4.1). There is no mana.</summary>
public sealed record DerivedFormula(long Base, ImmutableSortedDictionary<CharacterAttribute, long> PerPoint)
{
    public long Evaluate(Func<CharacterAttribute, int> attribute) =>
        Base + PerPoint.Sum(term => term.Value * attribute(term.Key));
}

public sealed record DerivedFormulas(
    DerivedFormula HealthMax,
    DerivedFormula StaminaMax,
    DerivedFormula FocusMax,
    DerivedFormula Resonance,
    DerivedFormula StrainTolerance)
{
    /// <summary>The attributes some derived value reads: those a point spent on changes something (Phase 1: might, endurance, will).</summary>
    public ImmutableArray<CharacterAttribute> Live =>
        new[] { HealthMax, StaminaMax, FocusMax, Resonance, StrainTolerance }
            .SelectMany(f => f.PerPoint.Where(term => term.Value != 0).Select(term => term.Key)).Distinct().Order().ToImmutableArray();
}

/// <summary>Use under challenge (§4.2): the difficulty gate, the XP curve per level, novelty, and the common ceiling.</summary>
public sealed record SkillModel(
    int DifficultyMargin,
    int CommonCeiling,
    long XpPerLevelBase,
    long XpPerLevelGrowth,
    long UseXp,
    double FailureFactor,
    double MaxChallengeFactor,
    long NoveltyBonusXp)
{
    public long ToNext(int level) => XpPerLevelBase + XpPerLevelGrowth * level;
}

/// <summary>AG-1: a level-XP multiplier for kills whose <c>creature_level - player_level</c> is in [Min, Max].</summary>
public sealed record LevelBandRow(int Min, int Max, double Multiplier);

/// <summary>The anti-farm guards AG-1..AG-3 and the death debt AG-8 (§3.4).</summary>
public sealed record XpGuards(
    ImmutableArray<LevelBandRow> LevelBand,
    double NewSpeciesFloor,
    double SpeciesDecay,
    double SpeciesFloor,
    int ClusterThreshold,
    long ClusterWindowTicks,
    double ClusterFloor,
    double DebtFraction,
    double MaxDebtSpans)
{
    public double LevelBandMultiplier(int difference)
    {
        foreach (var row in LevelBand)
        {
            if (difference >= row.Min && difference <= row.Max)
                return row.Multiplier;
        }
        throw new InvalidOperationException($"AG-1 has no row for a level difference of {difference}");
    }
}

/// <summary>
/// What a new character starts with (§5). Phase 1 has no archetype choice, so one default package from
/// <c>config.progression</c> applies to every character.
/// </summary>
public sealed record StartingPackage(
    ImmutableSortedDictionary<CharacterAttribute, int> AttributeBias,
    ImmutableSortedDictionary<string, int> Skills,
    ImmutableArray<string> Techniques)
{
    public static StartingPackage None { get; } = new(
        ImmutableSortedDictionary<CharacterAttribute, int>.Empty,
        ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal),
        ImmutableArray<string>.Empty);
}

/// <summary>A tier row from §3.2's pacing table: the hours mixed play should take across these levels.</summary>
public sealed record TierTarget(int FromLevel, int ToLevel, double MinHours, double MaxHours)
{
    /// <summary>The XP/hour the tier is paced for, at the mid-point of its hours.</summary>
    public double TargetRatePerHour(XpCurve curve) =>
        (curve.TotalTo(ToLevel) - curve.TotalTo(FromLevel)) / ((MinHours + MaxHours) / 2);
}
