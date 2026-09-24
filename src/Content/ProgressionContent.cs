// UNNAMED Content - progression rules and skills from content (DATA_MODEL.md §4.19, §4.21; PROGRESSION.md)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Progression;
using YamlDotNet.Serialization;

namespace UNNAMED.Content;

/// <summary>
/// Builds the domain's <see cref="ProgressionRules"/> from <c>config.time</c>, <c>config.xp_curve</c>,
/// <c>config.level_cap</c> and <c>config.progression</c>, and checks every <c>skill</c> definition. The lint
/// reports problems as PRG errors. A pack with no progression config (test fixtures) is valid; building rules
/// from it is not.
/// </summary>
public static class ProgressionContent
{
    public static readonly ImmutableArray<string> SkillFamilies =
        ImmutableArray.Create("combat", "magic", "crafting", "gathering", "world", "social");

    private static readonly IDeserializer Yaml = new DeserializerBuilder().Build();

    /// <summary>Lint: skill families, and the progression config when the pack has one.</summary>
    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        foreach (var skill in loader.GetByKind("skill").Values)
        {
            string? family = Read(skill.YamlSource).GetValueOrDefault("family") as string;
            if (family is null || !SkillFamilies.Contains(family))
                errors.Add(Error("PRG001", $"Skill {skill.Id} needs a family, one of {string.Join(", ", SkillFamilies)} (DATA_MODEL.md §4.21)", skill.SourceFile));
        }
        if (loader.Definitions.ContainsKey("config.progression"))
            TryBuild(loader, errors);
        return errors;
    }

    /// <summary>The rules this content defines. Throws with every problem listed when the config is missing or invalid.</summary>
    public static ProgressionRules BuildRules(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        var rules = TryBuild(loader, errors);
        if (rules is null || errors.Count > 0)
            throw new InvalidOperationException("The progression config is invalid:\n  " + string.Join("\n  ", errors.Select(e => $"{e.Code}: {e.Message}")));
        return rules;
    }

    private static ProgressionRules? TryBuild(ContentLoader loader, List<ValidationError> errors)
    {
        var configs = new Dictionary<string, (Dictionary<object, object> Map, string? File)>(StringComparer.Ordinal);
        foreach (string id in new[] { "config.time", "config.xp_curve", "config.level_cap", "config.progression" })
        {
            if (loader.Definitions.TryGetValue(id, out var definition))
                configs[id] = (Read(definition.YamlSource), definition.SourceFile);
            else
                errors.Add(Error("PRG002", $"{id} is missing; progression needs it (DATA_MODEL.md §4.19)", null));
        }
        if (errors.Count > 0)
            return null;

        try
        {
            var time = configs["config.time"].Map;
            double ticksPerDay = Number(time, "ticks_per_second") * Number(time, "seconds_per_game_minute")
                * Number(time, "game_minutes_per_hour") * Number(time, "game_hours_per_day");

            var curveMap = configs["config.xp_curve"].Map;
            var band = Map(curveMap, "band_multiplier");
            var bandLevels = List(band, "levels");
            var curve = new XpCurve(
                Int(curveMap, "base"), Number(curveMap, "exponent"), Int(curveMap, "round_to"),
                IntOf(bandLevels, 0, "band_multiplier.levels"), IntOf(bandLevels, 1, "band_multiplier.levels"), Number(band, "factor"));
            long expected = Long(curveMap, "total_to_level_50_expected");
            if (curve.TotalTo(50) != expected)
                errors.Add(Error("PRG003",
                    $"config.xp_curve sums to {curve.TotalTo(50)} XP at level 50, but records {expected} (PROGRESSION.md §3.2)",
                    configs["config.xp_curve"].File));

            var caps = configs["config.level_cap"].Map;
            int softCap = Int(caps, "soft_cap");
            int levelCap = caps.ContainsKey("level_cap_phase1") ? Int(caps, "level_cap_phase1") : softCap;

            var (p, file) = configs["config.progression"];
            var derived = Map(p, "derived");
            var skills = Map(p, "skills");
            var guards = Map(p, "guards");
            var package = Map(p, "starting_package");

            var rules = new ProgressionRules
            {
                Curve = curve,
                LevelCap = levelCap,
                SoftCap = softCap,
                AttributeBase = Int(p, "attribute_base"),
                AttributePointsPerLevel = Int(p, "attribute_points_per_level"),
                AttributeGrantCapFraction = Number(p, "attribute_grant_cap_fraction"),
                Derived = new DerivedFormulas(
                    Formula(derived, "health_max"), Formula(derived, "stamina_max"), Formula(derived, "focus_max"),
                    Formula(derived, "resonance"), Formula(derived, "strain_tolerance")),
                Skills = new SkillModel(
                    Int(skills, "difficulty_margin"), Int(skills, "common_ceiling"),
                    Long(skills, "xp_per_level_base"), Long(skills, "xp_per_level_growth"), Long(skills, "use_xp"),
                    Number(skills, "failure_factor"), Number(skills, "max_challenge_factor"), Long(skills, "novelty_bonus_xp")),
                Guards = new XpGuards(
                    List(guards, "level_band").Select((row, i) => BandRow(row, i)).ToImmutableArray(),
                    Number(guards, "new_species_floor"), Number(guards, "species_decay"), Number(guards, "species_floor"),
                    Int(guards, "cluster_threshold"), Long(guards, "cluster_window_ticks"), Number(guards, "cluster_floor"),
                    Number(guards, "debt_fraction"), Number(guards, "max_debt_spans")),
                TicksPerWorldDay = (long)ticksPerDay,
                StartingPackage = new StartingPackage(
                    Map(package, "attribute_bias").ToImmutableSortedDictionary(
                        kv => ProgressionKeys.ParseAttribute((string)kv.Key), kv => ParseInt(kv.Value, "starting_package.attribute_bias")),
                    Map(package, "skills").ToImmutableSortedDictionary(
                        kv => (string)kv.Key, kv => ParseInt(kv.Value, "starting_package.skills"), StringComparer.Ordinal),
                    List(package, "techniques").Select(t => (string)t).ToImmutableArray()),
                TierTargets = List(p, "tier_targets").Select((row, i) => TierRow(row, i)).ToImmutableArray(),
            };

            CheckRules(rules, loader, errors, file);
            return rules;
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(Error("PRG004", $"Progression config is malformed: {e.Message}", configs["config.progression"].File));
            return null;
        }
    }

    private static void CheckRules(ProgressionRules rules, ContentLoader loader, List<ValidationError> errors, string? file)
    {
        void Check(bool ok, string message)
        {
            if (!ok)
                errors.Add(Error("PRG005", message, file));
        }

        Check(rules.LevelCap >= 2 && rules.LevelCap <= rules.SoftCap, $"level cap {rules.LevelCap} must be between 2 and the soft cap {rules.SoftCap}");
        Check(rules.AttributePointsPerLevel >= 1, "a level-up must grant at least one attribute point (PROGRESSION.md §3.1)");
        Check(rules.AttributeGrantCapFraction is >= 0 and <= 0.08, "post-creation attribute grants are capped at 8% (§11.3)");
        Check(rules.Skills.DifficultyMargin > 0 && rules.Skills.CommonCeiling is > 0 and <= 100, "skills need a positive difficulty margin and a ceiling within 1..100");
        Check(rules.Skills.XpPerLevelBase > 0 && rules.Skills.XpPerLevelGrowth >= 0 && rules.Skills.UseXp > 0, "skill XP constants must be positive");
        Check(rules.Skills.FailureFactor is >= 0 and <= 1 && rules.Skills.MaxChallengeFactor >= 1, "failure factor in 0..1, max challenge factor at least 1");
        Check(rules.TicksPerWorldDay > 0, "config.time gives a world day of no ticks");
        Check(rules.TierTargets.Length > 0 && rules.TierTargets.All(t => t.FromLevel >= 1 && t.ToLevel > t.FromLevel && t.MinHours > 0 && t.MaxHours >= t.MinHours),
            "tier_targets rows must be [from < to, min hours <= max hours]");

        // AG-1 must answer every level difference, exactly once.
        var band = rules.Guards.LevelBand.OrderBy(r => r.Min).ToList();
        bool covered = band.Count > 0 && band[0].Min <= -100 && band[^1].Max >= 100
            && band.Zip(band.Skip(1)).All(pair => pair.First.Max + 1 == pair.Second.Min) && band.All(r => r.Min <= r.Max && r.Multiplier >= 0);
        Check(covered, "guards.level_band must cover every level difference exactly once, with non-negative multipliers (AG-1)");
        var g = rules.Guards;
        Check(g.SpeciesDecay is > 0 and <= 1 && g.SpeciesFloor is > 0 and <= 1 && g.ClusterFloor is > 0 and <= 1 && g.NewSpeciesFloor is > 0 and <= 1,
            "AG-1..AG-3 decays and floors must be in (0, 1]");
        Check(g.ClusterThreshold > 0 && g.ClusterWindowTicks > 0, "AG-3 needs a positive threshold and window");
        Check(g.DebtFraction is > 0 and <= 1 && g.MaxDebtSpans >= g.DebtFraction, "AG-8 debt fraction in (0, 1], and the cap at least one death's debt");

        // The starting package may name only definitions that exist.
        foreach (string skill in rules.StartingPackage.Skills.Keys)
            Check(ProgressionKeys.IsSkillId(skill) && loader.Definitions.ContainsKey(skill), $"starting package skill {skill} is not a defined skill");
        foreach (string technique in rules.StartingPackage.Techniques)
            Check(ProgressionKeys.IsTechniqueId(technique) && loader.Definitions.ContainsKey(technique),
                $"starting package technique {technique} is not a defined ability, spell or recipe");
    }

    private static DerivedFormula Formula(Dictionary<object, object> derived, string key)
    {
        var map = Map(derived, key);
        return new DerivedFormula(
            Long(map, "base"),
            Map(map, "per_point").ToImmutableSortedDictionary(
                kv => ProgressionKeys.ParseAttribute((string)kv.Key), kv => (long)ParseInt(kv.Value, $"derived.{key}.per_point")));
    }

    private static LevelBandRow BandRow(object row, int index)
    {
        var cells = row as List<object> ?? throw new FormatException($"guards.level_band[{index}] must be [min, max, multiplier]");
        return new LevelBandRow(IntOf(cells, 0, "guards.level_band"), IntOf(cells, 1, "guards.level_band"), NumberOf(cells, 2, "guards.level_band"));
    }

    private static TierTarget TierRow(object row, int index)
    {
        var cells = row as List<object> ?? throw new FormatException($"tier_targets[{index}] must be [from, to, min_hours, max_hours]");
        return new TierTarget(IntOf(cells, 0, "tier_targets"), IntOf(cells, 1, "tier_targets"), NumberOf(cells, 2, "tier_targets"), NumberOf(cells, 3, "tier_targets"));
    }

    private static Dictionary<object, object> Read(string? yaml) =>
        string.IsNullOrEmpty(yaml) ? new Dictionary<object, object>() : Yaml.Deserialize<Dictionary<object, object>>(yaml) ?? new Dictionary<object, object>();

    private static Dictionary<object, object> Map(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value)
            ? value as Dictionary<object, object> ?? (value is null ? new Dictionary<object, object>() : throw new FormatException($"'{key}' must be a map"))
            : throw new KeyNotFoundException($"'{key}' is missing");

    private static List<object> List(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value)
            ? value as List<object> ?? (value is null ? new List<object>() : throw new FormatException($"'{key}' must be a list"))
            : throw new KeyNotFoundException($"'{key}' is missing");

    private static int Int(Dictionary<object, object> map, string key) => ParseInt(Get(map, key), key);
    private static long Long(Dictionary<object, object> map, string key) =>
        long.TryParse(Scalar(Get(map, key), key), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : throw new FormatException($"'{key}' must be a whole number");
    private static double Number(Dictionary<object, object> map, string key) => ParseNumber(Get(map, key), key);
    private static int IntOf(List<object> cells, int i, string what) => i < cells.Count ? ParseInt(cells[i], what) : throw new FormatException($"{what} row is too short");
    private static double NumberOf(List<object> cells, int i, string what) => i < cells.Count ? ParseNumber(cells[i], what) : throw new FormatException($"{what} row is too short");

    private static object Get(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var value) && value is not null ? value : throw new KeyNotFoundException($"'{key}' is missing");

    private static int ParseInt(object value, string what) =>
        int.TryParse(Scalar(value, what), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : throw new FormatException($"'{what}' must be a whole number");

    private static double ParseNumber(object value, string what) =>
        double.TryParse(Scalar(value, what), NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
            ? result
            : throw new FormatException($"'{what}' must be a number");

    private static string Scalar(object value, string what) =>
        value as string ?? throw new FormatException($"'{what}' must be a number");

    private static ValidationError Error(string code, string message, string? file) => new()
    {
        SeverityLevel = ValidationError.Severity.Error,
        Code = code,
        Message = message,
        FilePath = file ?? string.Empty,
    };
}
