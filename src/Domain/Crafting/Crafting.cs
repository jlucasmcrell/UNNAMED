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
    string OutputItemId, int OutputCount, bool QualityRoll, long FirstTimeXp)
{
    /// <summary>A fine roll at an upgraded station may come out masterwork (<c>outputs[0].masterwork_possible</c>; WP3a).</summary>
    public bool MasterworkPossible { get; init; }

    /// <summary>One of its station's experiment candidates (<c>discovery: { experiment: true }</c>): it can be found by experimenting.</summary>
    public bool Experiment { get; init; }
}

/// <summary>An item's quality, per stack: it lands on the instance, never the definition (PROTOTYPE.md C14).</summary>
public static class Quality
{
    public const int Crude = -1;
    public const int Standard = 0;
    public const int Fine = 1;

    /// <summary>Craft-only, two steps past standard (C12:186-205): no drop and no trader's stock is ever masterwork (PROGRESSION.md §11.2).</summary>
    public const int Masterwork = 2;

    public static bool IsValid(int quality) => quality is >= Crude and <= Masterwork;

    public static string Key(int quality) => quality switch
    {
        Crude => "crude",
        Fine => "fine",
        Masterwork => "masterwork",
        _ => "standard",
    };

    public static int Parse(string key) => key switch
    {
        "crude" => Crude,
        "standard" => Standard,
        "fine" => Fine,
        "masterwork" => Masterwork,
        _ => throw new FormatException($"Unknown quality '{key}' (crude, standard, fine, masterwork)"),
    };
}

/// <summary>The tuning of crafting (<c>config.crafting</c>).</summary>
public sealed record CraftingConstants
{
    public int SalvageBasePercent {get;init;}=25;
    public int SalvagePercentPerSkill {get;init;}=2;
    public int SalvageMaxPercent {get;init;}=75;
    public int SalvageBrokenPercent {get;init;}=20;
    public int SalvageHintPercent {get;init;}=5;

    /// <summary>At the recipe's complexity, this share of the work comes out a step finer.</summary>
    public int FinePercentAtComplexity { get; init; } = 10;

    public int FinePercentPerPoint { get; init; } = 2;

    public int FinePercentMax { get; init; } = 60;

    /// <summary>Per point of complexity above the smith's skill, this share comes out a step cruder.</summary>
    public int CrudePercentPerPoint { get; init; } = 3;

    public int CrudePercentMax { get; init; } = 50;

    /// <summary>What a step of quality adds to (or takes from) a weapon's damage.</summary>
    public int WeaponDamagePerStep { get; init; } = 2;

    /// <summary>What a step of quality adds to (or takes from) a piece of armour's armour value (WP3a: mirrors weapons).</summary>
    public int ArmorPerStep { get; init; } = 1;

    /// <summary>
    /// Of the fine pieces a recipe flagged <c>masterwork_possible</c> makes at a station of at least <see cref="MasterworkStationGrade"/>,
    /// this share comes out masterwork instead (C12:186-205).
    /// </summary>
    public int MasterworkPercentOfFine { get; init; } = 25;

    /// <summary>The station grade masterwork needs: an upgraded station, never a common one.</summary>
    public int MasterworkStationGrade { get; init; } = 1;

    /// <summary>How many recipes of each station kind a world's experiment set holds (C12:263-302): seeded from the world, fixed for it.</summary>
    public ImmutableSortedDictionary<string, int> ExperimentsPerStation { get; init; } = ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal);
}

/// <summary>
/// How a dropped stack's quality is rolled for creatures from <see cref="LevelMin"/> up (DCP:662): crude and fine percentages, the rest
/// standard. A drop is never masterwork.
/// </summary>
public sealed record DropQualityBand(int LevelMin, int CrudePercent, int FinePercent);

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

    /// <summary>
    /// A fine piece becomes masterwork when the recipe allows it, the station is upgraded far enough, and the second sample falls in
    /// <see cref="CraftingConstants.MasterworkPercentOfFine"/>. Anything else is returned as it was.
    /// </summary>
    public static int Masterwork(int quality, bool possible, int stationGrade, CraftingConstants rules, double sample) =>
        quality == Quality.Fine && possible && stationGrade >= rules.MasterworkStationGrade && sample * 100 < rules.MasterworkPercentOfFine
            ? Quality.Masterwork
            : quality;

    /// <summary>
    /// How many whole crafts the supply allows - the player's "Make All" (note 12): the smallest number of complete sets any one input
    /// reaches, so a recipe wanting two iron and one ash works as many times as the iron lasts. <paramref name="available"/> answers the
    /// count of an item id anywhere the station can draw on. A recipe with no inputs is nothing to make, so nothing.
    /// </summary>
    public static int MakeableFrom(RecipeDefinition recipe, Func<string, int> available)
    {
        if (recipe.Inputs.IsDefaultOrEmpty)
            return 0;
        int most = int.MaxValue;
        foreach (var input in recipe.Inputs)
            most = Math.Min(most, Math.Max(0, available(input.ItemId)) / input.Count);
        return Math.Max(0, most);
    }

    /// <summary>
    /// A drop's quality from the creature's level (DCP:662): the highest band at or below the level decides; below every band, standard.
    /// <paramref name="sample"/> is in [0, 1). Never masterwork (PROGRESSION.md §11.2).
    /// </summary>
    public static int DropQuality(int level, IReadOnlyList<DropQualityBand> bands, double sample)
    {
        var band = bands.Where(b => b.LevelMin <= level).OrderByDescending(b => b.LevelMin).FirstOrDefault();
        if (band is null)
            return Quality.Standard;
        double percent = sample * 100;
        return percent < band.CrudePercent ? Quality.Crude : percent >= 100 - band.FinePercent ? Quality.Fine : Quality.Standard;
    }

    /// <summary>
    /// A world's experiment set at one station kind (C12:263-302): <paramref name="count"/> of the kind's candidates, picked by a seeded
    /// partial shuffle - so the same world always holds the same set, and a reload changes nothing. Sorted by ID.
    /// </summary>
    public static ImmutableArray<string> ExperimentSet(IEnumerable<string> candidates, int count, Func<uint, double> random)
    {
        var pool = candidates.OrderBy(c => c, StringComparer.Ordinal).ToList();
        int take = Math.Clamp(count, 0, pool.Count);
        for (int i = 0; i < take; i++)
        {
            int j = i + (int)Math.Floor(random((uint)i) * (pool.Count - i));
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool.Take(take).OrderBy(c => c, StringComparer.Ordinal).ToImmutableArray();
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
