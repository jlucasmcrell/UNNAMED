// UNNAMED Domain - gathering and crafting: nodes, recipes, and quality as pure functions
// (DATA_MODEL.md §4.9, §4.10, §4.16; PROTOTYPE.md C12-C14; the content bible's §12; M3f)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Crafting;

/// <summary>How a harvested node comes back: never (a finite seam), or at the next world-day boundary (a stand).</summary>
public enum Respawn
{
    None,
    Daily,
}

/// <summary>
/// A resource node (DATA_MODEL.md §4.16) with its material's yield folded in (§4.10): what one harvest gives, how many
/// harvests it holds, how it comes back, and what gathering it trains at what difficulty.
/// </summary>
public sealed record NodeDefinition(string Id, string ItemId, int YieldMin, int YieldMax, int Charges, Respawn Respawn, string SkillId, int Difficulty)
{
    /// <summary>The resource the node yields (DATA_MODEL.md §4.16's <c>resource_ref</c>): what a quest's <c>harvest_resource</c> counts (M5).</summary>
    public string? ResourceId { get; init; }
}

public sealed record RecipeInput(string ItemId, int Count);

/// <summary>
/// A recipe (DATA_MODEL.md §4.9): worked at a station of its kind, from its inputs, into its output. The smith's skill
/// against its complexity decides the output's quality and what the work teaches; no rank gates it.
/// </summary>
public sealed record RecipeDefinition(string Id, string StationKind, string SkillId, int Complexity, ImmutableArray<RecipeInput> Inputs,
    string OutputItemId, int OutputCount, bool QualityRoll, long FirstTimeXp);

/// <summary>An item's quality, per stack: it lands on the instance, never the definition (PROTOTYPE.md C14).</summary>
public static class Quality
{
    public const int Crude = -1;
    public const int Standard = 0;
    public const int Fine = 1;

    public static bool IsValid(int quality) => quality is >= Crude and <= Fine;

    public static string Key(int quality) => quality switch
    {
        Crude => "crude",
        Fine => "fine",
        _ => "standard",
    };
}

/// <summary>The tuning of crafting (<c>config.crafting</c>).</summary>
public sealed record CraftingConstants
{
    /// <summary>At the recipe's complexity, this share of the work comes out a step finer.</summary>
    public int FinePercentAtComplexity { get; init; } = 10;

    public int FinePercentPerPoint { get; init; } = 2;

    public int FinePercentMax { get; init; } = 60;

    /// <summary>Per point of complexity above the smith's skill, this share comes out a step cruder.</summary>
    public int CrudePercentPerPoint { get; init; } = 3;

    public int CrudePercentMax { get; init; } = 50;

    /// <summary>What a step of quality adds to (or takes from) a weapon's damage.</summary>
    public int WeaponDamagePerStep { get; init; } = 2;
}

/// <summary>The rules of making things.</summary>
public static class CraftingRules
{
    public static int FinePercent(int skill, int complexity, CraftingConstants rules) =>
        Math.Clamp(rules.FinePercentAtComplexity + (skill - complexity) * rules.FinePercentPerPoint, 0, rules.FinePercentMax);

    public static int CrudePercent(int skill, int complexity, CraftingConstants rules) =>
        Math.Clamp((complexity - skill) * rules.CrudePercentPerPoint, 0, rules.CrudePercentMax);

    /// <summary>
    /// A crafted output's quality: the weakest material caps it, then the smith's hand moves it a step up or down - more
    /// often up the more skilled, more often down the more the work is beyond them. <paramref name="sample"/> is in [0, 1).
    /// </summary>
    public static int Roll(int weakestInput, int skill, int complexity, CraftingConstants rules, double sample)
    {
        double percent = sample * 100;
        int step = percent < CrudePercent(skill, complexity, rules) ? -1 : percent >= 100 - FinePercent(skill, complexity, rules) ? 1 : 0;
        return Math.Clamp(weakestInput + step, Quality.Crude, Quality.Fine);
    }

    /// <summary>Whether a node that was last harvested at a tick, that many times, can be harvested now.</summary>
    public static bool Ready(NodeDefinition node, long? lastHarvestTick, int harvests, long tick, long ticksPerWorldDay) => node.Respawn switch
    {
        // A finite node holds its charges and no more.
        Respawn.None => harvests < node.Charges,
        // A stand refills at the next world-day boundary after its last harvest.
        _ => lastHarvestTick is not { } last || tick / ticksPerWorldDay > last / ticksPerWorldDay,
    };
}
