// UNNAMED Domain - work done on a piece after it was made: reforging it up the tiers, and an inscribed working (queue #5; canon 13 §6,
// crafting_webs/enchanting_runecraft, SKILLS_AND_TECHNIQUES §Enchanting)
// No Godot references - pure C#

namespace UNNAMED.Domain.Items;

/// <summary>Plain pieces add nothing, preserving historical digests; worked pieces include their tier, pattern and stability.</summary>
public static class WorkDigest
{
    public static CanonicalHasher Add(CanonicalHasher h, ItemWork? work) =>
        work is null ? h : h.Add("work").Add(work.Tier).Add(work.Inscription?.WorkingId ?? "-").Add(work.Inscription?.Stability ?? 0);
}

/// <summary>
/// A working cut into a piece (enchanting: pattern, anchor, effect, stability): which working, and how much stability it has left. Each point
/// is a tolerance before the pattern drifts; at none the working has faded and the piece is plain again, never harmed (canon 12 §5.4).
/// </summary>
public sealed record Inscription(string WorkingId, int Stability)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(WorkingId) && Stability > 0;
}

/// <summary>
/// The work a piece carries (schema 44): how many times it has been reforged up a tier (0 = as made), and the working inscribed on it. A piece
/// with neither carries none, so a plain stack is what it always was.
/// </summary>
public sealed record ItemWork(int Tier, Inscription? Inscription)
{
    public bool IsValid => Tier >= 0 && Inscription is not { IsValid: false } && (Tier > 0 || Inscription is not null);

    /// <summary>Null when nothing is left on it.</summary>
    public static ItemWork? Of(int tier, Inscription? inscription) => tier == 0 && inscription is null ? null : new ItemWork(tier, inscription);
}

/// <summary>
/// One working an enchanter can cut (<c>config.crafting enchanting.workings</c>): what it goes on (a weapon or a piece of armour), the
/// enchanting it needs, the anchor material it spends, the stability it starts with, and what it does - extra damage, a damage type the blow
/// carries instead of the weapon's, extra armour.
/// </summary>
public sealed record WorkingDefinition(string Id, string Name, WorkingTarget Target, int SkillMin, string MaterialId, int MaterialCount, int Stability)
{
    /// <summary>The teachable inscription technique in the content catalogue and the character's saved knowledge.</summary>
    public string KnowledgeId => "ability.enchanting." + Id["working.".Length..];

    public int Damage { get; init; }

    public string? DamageType { get; init; }

    public int Armor { get; init; }
}

public enum WorkingTarget
{
    Weapon,
    Armor,
}

/// <summary>
/// The tuning of reforging and enchanting (<c>config.crafting upgrade</c> and <c>enchanting</c>). Reforging is the piece's own repair, done
/// further: at its repair station, with its repair skill and material. Every tier asks <see cref="SkillPerTier"/> more of the hand than the
/// last and <see cref="MaterialPerTier"/> more material, up to <see cref="MaxTier"/>, and adds <see cref="DamagePerTier"/> to a weapon's blows
/// or <see cref="ArmorPerTier"/> to a piece's armour. Nothing is rolled: canon's crafting never gambles the piece (12 §5.4).
/// </summary>
public sealed record UpgradeRules(int MaxTier, int SkillPerTier, int MaterialPerTier, int DamagePerTier, int ArmorPerTier)
{
    public static readonly UpgradeRules Default = new(3, 10, 2, 1, 1);

    /// <summary>The station kind workings are cut at (the hooks' enchanting bench).</summary>
    public string EnchantingStation { get; init; } = "enchanting_bench";

    /// <summary>The skill workings are cut with.</summary>
    public string EnchantingSkill { get; init; } = "skill.enchanting";

    /// <summary>A working loses a point of stability for every this many points of wear its piece takes (use is what drifts a pattern).</summary>
    public int WearPerStability { get; init; } = 20;

    /// <summary>A working gains a point of stability for every this many levels the enchanter has past its least (the Stability technique).</summary>
    public int SkillPerStability { get; init; } = 15;

    /// <summary>The skill reforging a piece from <paramref name="tier"/> to the next asks: the piece's repair least, and more for every tier.</summary>
    public int SkillFor(RepairSpec repair, int tier) => repair.SkillMin + SkillPerTier * (tier + 1);

    /// <summary>How much of the piece's repair material reforging from <paramref name="tier"/> to the next spends.</summary>
    public int MaterialFor(int tier) => MaterialPerTier * (tier + 1);

    /// <summary>
    /// A working's stability as it is cut: its own, one more for every step of the piece's quality past standard and one less for crude (canon 12
    /// §3.5, enchantment stability ±1 a step), and one more for every <see cref="SkillPerStability"/> levels past the working's least. At least 1.
    /// </summary>
    public int StabilityOf(WorkingDefinition working, int quality, int skill) =>
        Math.Max(1, working.Stability + quality + Math.Max(0, skill - working.SkillMin) / Math.Max(1, SkillPerStability));

    /// <summary>
    /// A piece reforged a tier up. Reforging the core tests the working on it (canon 13 §6.2): the working loses a point of stability, and at
    /// none it is gone - <paramref name="faded"/> names it.
    /// </summary>
    public static ItemWork? Reforged(ItemWork? work, out Inscription? faded)
    {
        var (inscription, lost) = Drift(work?.Inscription, 1);
        faded = lost;
        return ItemWork.Of((work?.Tier ?? 0) + 1, inscription);
    }

    /// <summary>
    /// A piece after its wear went from <paramref name="pointsBefore"/> to <paramref name="pointsAfter"/>: the working loses a point for every
    /// multiple of <see cref="WearPerStability"/> crossed, and at none it is gone - <paramref name="faded"/> names it.
    /// </summary>
    public ItemWork? Worn(ItemWork? work, int pointsBefore, int pointsAfter, out Inscription? faded)
    {
        faded = null;
        if (work?.Inscription is null || WearPerStability <= 0 || pointsAfter <= pointsBefore)
            return work;
        int drift = pointsAfter / WearPerStability - pointsBefore / WearPerStability;
        if (drift <= 0)
            return work;
        var (inscription, lost) = Drift(work.Inscription, drift);
        faded = lost;
        return ItemWork.Of(work.Tier, inscription);
    }

    /// <summary>What the piece's work adds to its blows: the tiers, and a weapon working's damage.</summary>
    public int DamageBonus(ItemWork? work, WorkingDefinition? working) =>
        work is null ? 0 : work.Tier * DamagePerTier + (working is { Target: WorkingTarget.Weapon } ? working.Damage : 0);

    /// <summary>What the piece's work adds to its armour: the tiers, and an armour working's armour. A piece with no armour gains none.</summary>
    public int ArmorBonus(ItemWork? work, WorkingDefinition? working) =>
        work is null ? 0 : work.Tier * ArmorPerTier + (working is { Target: WorkingTarget.Armor } ? working.Armor : 0);

    private static (Inscription? Left, Inscription? Faded) Drift(Inscription? inscription, int points) =>
        inscription is null ? (null, null)
        : inscription.Stability > points ? (inscription with { Stability = inscription.Stability - points }, null)
        : (null, inscription);
}
