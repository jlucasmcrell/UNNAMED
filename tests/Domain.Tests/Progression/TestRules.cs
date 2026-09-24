using System.Collections.Immutable;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Domain.Tests.Progression;

/// <summary>
/// Rules with the values of content/config (PROGRESSION.md §3-§4). Domain tests build them in code - the
/// domain never reads files - and Content.Tests checks the shipped config builds the same numbers.
/// </summary>
internal static class TestRules
{
    public const string Blade = "skill.one_hand_blade";
    public const string Athletics = "skill.athletics";
    public const string Bolt = "spell.ember.bolt";
    public const string Salve = "recipe.alchemy.salve_minor";
    public const string Wolf = "creature.beast.wolf_grey";

    public static ProgressionRules Default(int levelCap = 5, StartingPackage? package = null) => new()
    {
        Curve = new XpCurve(100, 1.85, 25, 11, 30, 1.15),
        LevelCap = levelCap,
        SoftCap = 50,
        AttributeBase = 10,
        AttributePointsPerLevel = 1,
        AttributeGrantCapFraction = 0.08,
        Derived = new DerivedFormulas(
            Formula(50, (CharacterAttribute.Endurance, 5), (CharacterAttribute.Might, 2)),
            Formula(50, (CharacterAttribute.Endurance, 5)),
            Formula(20, (CharacterAttribute.Will, 6)),
            Formula(0, (CharacterAttribute.Will, 2)),
            Formula(10, (CharacterAttribute.Will, 2), (CharacterAttribute.Endurance, 1))),
        Skills = new SkillModel(
            DifficultyMargin: 15, CommonCeiling: 60, XpPerLevelBase: 20, XpPerLevelGrowth: 4, UseXp: 10,
            FailureFactor: 0.5, MaxChallengeFactor: 2.0, NoveltyBonusXp: 25),
        Guards = new XpGuards(
            ImmutableArray.Create(
                new LevelBandRow(-999, -16, 0.00), new LevelBandRow(-15, -11, 0.05), new LevelBandRow(-10, -6, 0.25),
                new LevelBandRow(-5, -1, 0.60), new LevelBandRow(0, 0, 1.00), new LevelBandRow(1, 5, 1.15),
                new LevelBandRow(6, 10, 1.30), new LevelBandRow(11, 999, 1.15)),
            NewSpeciesFloor: 0.05, SpeciesDecay: 0.9, SpeciesFloor: 0.10,
            ClusterThreshold: 25, ClusterWindowTicks: 36_000, ClusterFloor: 0.10,
            DebtFraction: 0.10, MaxDebtSpans: 1.0),
        TicksPerWorldDay = 57_600,
        StartingPackage = package ?? StartingPackage.None,
        TierTargets = ImmutableArray.Create(
            new TierTarget(1, 10, 4, 6), new TierTarget(11, 20, 8, 12), new TierTarget(21, 30, 13, 18),
            new TierTarget(31, 40, 16, 22), new TierTarget(41, 50, 20, 28)),
    };

    /// <summary>Rules whose AG-1..AG-3 change nothing: every multiplier 1, no floor reachable, no window.</summary>
    public static ProgressionRules WithoutGuards(ProgressionRules rules) => rules with
    {
        Guards = rules.Guards with
        {
            LevelBand = ImmutableArray.Create(new LevelBandRow(-999, 999, 1.0)),
            SpeciesDecay = 1.0,
            ClusterThreshold = int.MaxValue,
        },
    };

    public static XpAward Kill(string species, int creatureLevel, string cluster, long tick, long amount = 1_000) =>
        new(XpSource.Combat, amount, tick) { Kill = new KillContext(species, creatureLevel, cluster) };

    private static DerivedFormula Formula(long @base, params (CharacterAttribute Attribute, long PerPoint)[] terms) =>
        new(@base, terms.ToImmutableSortedDictionary(t => t.Attribute, t => t.PerPoint));
}
