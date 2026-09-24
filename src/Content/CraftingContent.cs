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
        return errors;
    }

    public static CraftingSetup Build(ContentLoader loader) =>
        new(BuildConstants(loader), BuildNodes(loader), BuildRecipes(loader)) { YieldBonuses = BuildYieldBonuses(loader) };

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
        };
        if (constants.FinePercentAtComplexity is < 0 or > 100 || constants.FinePercentMax is < 0 or > 100 || constants.CrudePercentMax is < 0 or > 100
            || constants.FinePercentPerPoint < 0 || constants.CrudePercentPerPoint < 0 || constants.FinePercentMax + constants.CrudePercentMax > 100
            || constants.WeaponDamagePerStep < 0)
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
                Int(map, "charges"), respawn, skill, Int(map, "difficulty"));
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
                output.GetValueOrDefault("quality_roll") as string == "true", xp);
            if (recipe.Complexity < 0 || recipe.OutputCount < 1 || recipe.FirstTimeXp < 0 || inputs.Any(i => i.Count < 1) || inputs.IsEmpty)
                throw new FormatException($"{id}: complexity and XP not below 0, at least one input, and every count at least 1");
            foreach (string field in new[] { "tools", "craft_time_min", "discovery", "quality_curve", "profession", "required_skill" })
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
