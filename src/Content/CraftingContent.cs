// UNNAMED Content - gathering and crafting from content: resources, nodes, recipes, and the tuning of quality
// (DATA_MODEL.md §4.9, §4.10, §4.16, §4.19; PROTOTYPE.md §4.1; the content bible's §12; M3f)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Crafting;
using UNNAMED.World.Runtime;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// Builds the simulation's <see cref="CraftingSetup"/> and lints what the reference and semantic passes cannot see (CRF001):
/// a node's material yields one item; a finite node has charges and a daily one exactly one; a recipe names a station
/// kind, a skill, its inputs and one output; <c>config.crafting</c> is in range; a yield passive adds to gathering.
/// </summary>
public static class CraftingContent
{
    /// <summary>The stat a gathering skill's passive may raise (survival's yield at level 3).</summary>
    public const string GatherYield = "stat.gather_yield";

    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        Try(() => BuildConstants(loader), "config.crafting", errors);
        Try(() => BuildNodes(loader), "nodes", errors);
        Try(() => BuildRecipes(loader), "recipes", errors);
        Try(() => BuildYieldBonuses(loader), "skills", errors);
        Try(() => BuildDropQuality(loader), "config.crafting", errors);
        Try(() => CheckExperiments(BuildConstants(loader), BuildRecipes(loader)), "experiments", errors);
        return errors;
    }

    public static CraftingSetup Build(ContentLoader loader) =>
        new(BuildConstants(loader), BuildNodes(loader), BuildRecipes(loader))
        {
            YieldBonuses = BuildYieldBonuses(loader),
            DropQuality = BuildDropQuality(loader),
        };

    /// <summary>
    /// <c>config.crafting drop_quality</c>: from which creature level which share of a corpse's stacks is crude and which fine (DCP:662).
    /// Absent: every drop is standard.
    /// </summary>
    public static ImmutableArray<DropQualityBand> BuildDropQuality(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey("config.crafting"))
            return ImmutableArray<DropQualityBand>.Empty;
        var bands = Rows(Config(loader, "config.crafting"), "drop_quality", "config.crafting")
            .Select(row => new DropQualityBand(Int(row, "level_min"), Int(row, "crude_percent"), Int(row, "fine_percent")))
            .OrderBy(b => b.LevelMin).ToImmutableArray();
        if (bands.Any(b => b.LevelMin < 0 || b.CrudePercent < 0 || b.FinePercent < 0 || b.CrudePercent + b.FinePercent > 100)
            || bands.Select(b => b.LevelMin).Distinct().Count() != bands.Length)
            throw new FormatException("config.crafting drop_quality: distinct level_min not below 0, and crude and fine percentages together at most 100");
        return bands;
    }

    /// <summary>Every station kind a world sets experiments at needs at least that many candidate recipes worked there.</summary>
    private static bool CheckExperiments(CraftingConstants constants, ImmutableSortedDictionary<string, RecipeDefinition> recipes)
    {
        if (recipes.IsEmpty)
            return true;   // a pack with no recipes (a config alone) sets no station's experiments
        foreach (var (kind, count) in constants.ExperimentsPerStation)
        {
            int candidates = recipes.Values.Count(r => r.Experiment && r.StationKind == kind);
            if (candidates < count)
                throw new FormatException($"config.crafting experiments.{kind} is {count}, but only {candidates} recipe(s) worked at a {kind} are experiment candidates");
        }
        return true;
    }

    private static int ParseCount(object? value, string what) =>
        int.TryParse(value as string, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) && n >= 0
            ? n
            : throw new FormatException($"config.crafting {what} must be a whole number not below 0");

    /// <summary><c>config.crafting</c>: how a smith's skill against a recipe's complexity moves quality, and what quality does to a weapon.</summary>
    public static CraftingConstants BuildConstants(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey("config.crafting"))
            return new CraftingConstants();
        var quality = Map(Config(loader, "config.crafting"), "quality");
        var constants = new CraftingConstants
        {
            FinePercentAtComplexity = Int(quality, "fine_percent_at_complexity"),
            FinePercentPerPoint = Int(quality, "fine_percent_per_point"),
            FinePercentMax = Int(quality, "fine_percent_max"),
            CrudePercentPerPoint = Int(quality, "crude_percent_per_point"),
            CrudePercentMax = Int(quality, "crude_percent_max"),
            WeaponDamagePerStep = Int(quality, "weapon_damage_per_step"),
            ArmorPerStep = quality.ContainsKey("armor_per_step") ? Int(quality, "armor_per_step") : new CraftingConstants().ArmorPerStep,
        };
        var config = Config(loader, "config.crafting");
        if (config.GetValueOrDefault("masterwork") is Dictionary<object, object> masterwork)
        {
            constants = constants with
            {
                MasterworkPercentOfFine = Int(masterwork, "percent_of_fine"),
                MasterworkStationGrade = Int(masterwork, "station_grade_min"),
            };
            if (constants.MasterworkPercentOfFine is < 0 or > 100 || constants.MasterworkStationGrade < 1)
                throw new FormatException("config.crafting masterwork: percent_of_fine in [0, 100], and station_grade_min at least 1 (an upgraded station)");
        }
        if (config.GetValueOrDefault("experiments") is Dictionary<object, object> experiments)
        {
            var counts = experiments.ToImmutableSortedDictionary(kv => kv.Key as string ?? "", kv => ParseCount(kv.Value, $"experiments.{kv.Key}"), StringComparer.Ordinal);
            constants = constants with { ExperimentsPerStation = counts };
        }
        if (constants.FinePercentAtComplexity is < 0 or > 100 || constants.FinePercentMax is < 0 or > 100 || constants.CrudePercentMax is < 0 or > 100
            || constants.FinePercentPerPoint < 0 || constants.CrudePercentPerPoint < 0 || constants.FinePercentMax + constants.CrudePercentMax > 100
            || constants.WeaponDamagePerStep < 0 || constants.ArmorPerStep < 0)
            throw new FormatException("config.crafting holds a value out of range (percentages in [0, 100], fine and crude together at most 100)");
        return constants;
    }

    /// <summary>
    /// Nodes (DATA_MODEL.md §4.16) with their material folded in (§4.10): <c>resource_ref</c>'s single yield, <c>charges</c>,
    /// <c>respawn</c> (<c>none</c> or <c>daily</c>), <c>harvest_skill</c> and the <c>difficulty</c> a harvest trains it at.
    /// </summary>
    public static ImmutableSortedDictionary<string, NodeDefinition> BuildNodes(ContentLoader loader)
    {
        var resources = loader.GetByKind("resource");
        var skills = loader.GetByKind("skill");
        return loader.GetByKind("node").Values.Select(definition =>
        {
            var map = Read(definition.YamlSource);
            string id = definition.Id;
            string resourceId = Text(map, "resource_ref");
            var resource = resources.GetValueOrDefault(resourceId) ?? throw new FormatException($"{id}: {resourceId} is not a resource");
            var yields = Rows(Read(resource.YamlSource), "yields", resourceId).ToList();
            if (yields.Count != 1)
                throw new FormatException($"{resourceId}: Phase 1 builds a resource of exactly one yield");
            var yield = yields[0];
            var range = List(yield, "count_range");
            var respawn = Text(map, "respawn") switch
            {
                "none" => Respawn.None,
                "daily" => Respawn.Daily,
                var other => throw new FormatException($"{id}: respawn '{other}' is not built in Phase 1 (none, daily)"),
            };
            string skill = Text(map, "harvest_skill");
            if (!skills.ContainsKey(skill))
                throw new FormatException($"{id}: harvest_skill {skill} is not a skill");
            var node = new NodeDefinition(id, Text(yield, "item_ref"), IntOf(range, 0, "count_range"), IntOf(range, 1, "count_range"),
                Int(map, "charges"), respawn, skill, Int(map, "difficulty")) { ResourceId = resourceId };
            if (node.YieldMin < 1 || node.YieldMax < node.YieldMin || node.Charges < 1 || node.Difficulty < 0)
                throw new FormatException($"{id}: a yield of [min, max] with 1 <= min <= max, at least one charge, and a difficulty not below 0");
            if (node.Respawn == Respawn.Daily && node.Charges != 1)
                throw new FormatException($"{id}: a daily node holds one charge a day");
            if (map.ContainsKey("tool_tier_min"))
                throw new FormatException($"{id}: tool_tier_min is not built in Phase 1 (there are no tools)");
            return node;
        }).ToImmutableSortedDictionary(n => n.Id, n => n, StringComparer.Ordinal);
    }

    /// <summary>
    /// Recipes (DATA_MODEL.md §4.9): the <c>station</c> kind worked at, the <c>skill_ref</c> it trains, a <c>complexity</c>, the
    /// <c>inputs</c> it spends, one output (<c>quality_roll</c> when the smith's hand decides its quality), and the level XP its
    /// first output earns (<c>xp_award.xp</c>, once, AG-7).
    /// </summary>
    public static ImmutableSortedDictionary<string, RecipeDefinition> BuildRecipes(ContentLoader loader)
    {
        var skills = loader.GetByKind("skill");
        var items = new[] { "item", "item.weapon", "item.armor" }.SelectMany(k => loader.GetByKind(k)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        return loader.GetByKind("recipe").Values.Select(definition =>
        {
            var map = Read(definition.YamlSource);
            string id = definition.Id;
            string skill = Text(map, "skill_ref");
            if (!skills.ContainsKey(skill))
                throw new FormatException($"{id}: skill_ref {skill} is not a skill");
            var inputs = Rows(map, "inputs", id).Select(row => new RecipeInput(Text(row, "item_ref"), Int(row, "count"))).ToImmutableArray();
            var outputs = Rows(map, "outputs", id).ToList();
            if (outputs.Count != 1)
                throw new FormatException($"{id}: Phase 1 builds a recipe of exactly one output");
            var output = outputs[0];
            long xp = map.GetValueOrDefault("xp_award") is Dictionary<object, object> award ? Int(award, "xp") : 0;
            var recipe = new RecipeDefinition(id, Text(map, "station"), skill, Int(map, "complexity"), inputs, Text(output, "item_ref"), Int(output, "count"),
                output.GetValueOrDefault("quality_roll") as string == "true", xp)
            {
                MasterworkPossible = output.GetValueOrDefault("masterwork_possible") as string == "true",
                Experiment = map.GetValueOrDefault("discovery") is Dictionary<object, object> discovery && discovery.GetValueOrDefault("experiment") as string == "true",
            };
            if (map.GetValueOrDefault("discovery") is Dictionary<object, object> found && found.Keys.Any(k => k as string != "experiment"))
                throw new FormatException($"{id}: discovery holds only experiment (reveal_tags is not built)");
            if (recipe.MasterworkPossible && (!recipe.QualityRoll || recipe.OutputCount != 1))
                throw new FormatException($"{id}: masterwork_possible needs quality_roll and one piece out (a masterwork is one of a kind)");
            if (recipe.MasterworkPossible && items.GetValueOrDefault(recipe.OutputItemId) is { } made && Read(made.YamlSource).GetValueOrDefault("stack_max") as string != "1")
                throw new FormatException($"{id}: masterwork_possible makes {recipe.OutputItemId}, which stacks: a masterwork is one of a kind (stack_max 1)");
            if (recipe.Complexity < 0 || recipe.OutputCount < 1 || recipe.FirstTimeXp < 0 || inputs.Any(i => i.Count < 1) || inputs.IsEmpty)
                throw new FormatException($"{id}: complexity and XP not below 0, at least one input, and every count at least 1");
            foreach (string field in new[] { "tools", "craft_time_min", "quality_curve", "profession", "required_skill" })
            {
                if (map.ContainsKey(field))
                    throw new FormatException($"{id}: {field} is not built in Phase 1");
            }
            return recipe;
        }).ToImmutableSortedDictionary(r => r.Id, r => r, StringComparer.Ordinal);
    }

    /// <summary>Skill passives that add to a harvest (<c>stat.gather_yield</c>, op <c>add</c>).</summary>
    public static ImmutableArray<YieldBonus> BuildYieldBonuses(ContentLoader loader) =>
        loader.GetByKind("skill").Values.OrderBy(d => d.Id, StringComparer.Ordinal).SelectMany(definition =>
        {
            var map = Read(definition.YamlSource);
            return Rows(map, "passives", definition.Id).SelectMany(passive => Rows(passive, "modifiers", definition.Id)
                .Where(modifier => modifier.GetValueOrDefault("target") as string == GatherYield)
                .Select(modifier =>
                {
                    if (Text(modifier, "op") != "add")
                        throw new FormatException($"{definition.Id}: {GatherYield} is raised with op add");
                    return new YieldBonus(definition.Id, Int(passive, "at"), Int(modifier, "value"));
                }));
        }).ToImmutableArray();

    private static void Try(Func<object> build, string what, List<ValidationError> errors)
    {
        try
        {
            build();
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(new ValidationError { SeverityLevel = ValidationError.Severity.Error, Code = "CRF001", Message = $"{what}: {e.Message}" });
        }
    }
}
