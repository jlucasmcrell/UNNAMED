// UNNAMED Domain - what worn gear does beyond armour and damage: equip effects, mobility and noise, and enhancements - infusions of
// "other" materials and enchanted runes in a weapon's or armour's slots (spec 47 and its owner rulings, 2026-10-21; ATD §1; ITM-19, 20, 64..69)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Crafting;

namespace UNNAMED.Domain.Items;

/// <summary>Slice 1's material infusion, or slice 2's enchantment (a rune made at the enchanting table).</summary>
public enum EnhancementKind
{
    Infusion,
    Enchantment,
}

/// <summary>What an enhancement can be set into.</summary>
public enum EnhancementTarget
{
    Weapon,
    Armour,
}

/// <summary>
/// How an enhancement shows (owner ruling 2026-10-21): a plain stat bump on one piece has no visual; an elemental or otherwise visible
/// property shows on the item itself; a full-body buff may have a body effect.
/// </summary>
public enum EnhancementVisual
{
    None,
    Item,
    Body,
}

public static class EnhancementKeys
{
    public static string Key(EnhancementKind value) => value.ToString().ToLowerInvariant();

    public static string Key(EnhancementTarget value) => value.ToString().ToLowerInvariant();

    public static string Key(EnhancementVisual value) => value.ToString().ToLowerInvariant();

    public static T Parse<T>(string key) where T : struct, Enum
    {
        foreach (var value in Enum.GetValues<T>())
        {
            if (value.ToString().ToLowerInvariant() == key)
                return value;
        }
        throw new FormatException($"'{key}' is not one of {string.Join(", ", Enum.GetValues<T>().Select(v => v.ToString().ToLowerInvariant()))}");
    }
}

/// <summary>
/// What a material or rune does once set into a slot (<c>enhancement:</c> on its item): what it goes in, its tier (how hard it is to set
/// and to recover whole), how readily it takes (enchantability), how much of the piece's stability it uses, the effect it makes the wearer
/// carry while the piece is equipped, the effect it puts on what a weapon wounds and how often, how it shows, and the essence it leaves
/// when it is destroyed rather than recovered.
/// </summary>
public sealed record EnhancementSpec(
    EnhancementKind Kind,
    EnhancementTarget Target,
    int Tier,
    int Enchantability,
    int Stability,
    string? WornEffect,
    string? OnHitEffect,
    int OnHitPercent,
    EnhancementVisual Visual)
{
    /// <summary>The visual hook presentation draws for it (<c>ember</c>), when <see cref="Visual"/> is not none.</summary>
    public string? VisualHook { get; init; }

    /// <summary>The essence item a destroyed enhancement leaves behind, or null for none.</summary>
    public string? Essence { get; init; }

    /// <summary>Why this would break the owner's visual ruling, or null when it keeps it.</summary>
    public string? VisualRefusal() => Visual switch
    {
        EnhancementVisual.None => VisualHook is null ? null : "an enhancement with no visual names no visual hook",
        _ when VisualHook is null => "a visible enhancement names its visual hook",
        EnhancementVisual.Item when OnHitEffect is null => "a plain stat bump shows nothing on the item; only a property it puts on what it strikes does",
        EnhancementVisual.Body when WornEffect is null => "a body effect belongs to a full-body buff, one worn while equipped",
        _ => null,
    };
}

/// <summary>
/// A piece's enhancements, on its instance (schema 41): what fills its slots in order (item IDs of the infusion or rune), how many
/// slots a failed setting cracked, and how many settings it has had - which keys the next roll, so a reload never rolls again.
/// </summary>
public sealed record ItemEnhancements(ImmutableArray<string> Filled, int Cracked, int Attempts)
{
    public static ItemEnhancements None { get; } = new(ImmutableArray<string>.Empty, 0, 0);

    public bool IsValid => !Filled.IsDefault && Filled.All(f => !string.IsNullOrWhiteSpace(f)) && Cracked >= 0 && Attempts >= Filled.Length + Cracked;
}

/// <summary>The tuning of enhancement (<c>config.inventory enhancement</c>).</summary>
public sealed record EnhancementRules
{
    public static EnhancementRules Default { get; } = new();

    /// <summary>Slots a weapon or armour piece has at standard quality when its content names none; quality adds or takes one a step.</summary>
    public int BaseSlots { get; init; } = 1;

    public int MaxSlots { get; init; } = 3;

    /// <summary>A setting's chance: this, plus per skill level, plus the enhancement's enchantability, less per tier.</summary>
    public int BasePercent { get; init; } = 40;

    public int PercentPerSkill { get; init; } = 3;

    public int PercentPerTier { get; init; } = 10;

    public int MinPercent { get; init; } = 5;

    public int MaxPercent { get; init; } = 95;

    /// <summary>Of failed settings, this share cracks the slot it was set into (spec 47: "failure can crack the slot").</summary>
    public int CrackPercent { get; init; } = 35;

    /// <summary>A piece's stability: this at standard quality, plus per quality step; enhancements past it make the piece unstable.</summary>
    public int StabilityBase { get; init; } = 4;

    public int StabilityPerQuality { get; init; } = 1;

    /// <summary>The side effect an unstable piece puts on its wearer (spec 47's "enchantment stability").</summary>
    public string? InstabilityEffect { get; init; }

    /// <summary>The bench each kind of work is done at, and the skill it takes and trains there.</summary>
    public string WeaponStation { get; init; } = "forge";

    public string ArmourStation { get; init; } = "workbench";

    public string EnchantingStation { get; init; } = "enchanting_table";

    public string WeaponSkill { get; init; } = "skill.smithing";

    public string ArmourSkill { get; init; } = "skill.survival";

    public string EnchantingSkill { get; init; } = "skill.enchanting";

    /// <summary>Salvage keeps an enhancement whole from this skill per tier up, and then with this chance plus per level past it.</summary>
    public int RecoverSkillPerTier { get; init; } = 5;

    public int RecoverBasePercent { get; init; } = 50;

    public int RecoverPercentPerSkill { get; init; } = 5;

    /// <summary>How much worn armour may slow the wearer at most, as a share of speed.</summary>
    public double MaxMovementPenalty { get; init; } = 0.5;
}

/// <summary>A setting's outcome: set, failed with the slot kept, or failed and the slot cracked.</summary>
public enum SettingOutcome
{
    Set,
    Failed,
    Cracked,
}

/// <summary>One enhancement a piece carries, and what it does, as presentation and combat read it.</summary>
public sealed record WornEnhancement(EquipSlot Slot, string EnhancementId, EnhancementSpec Spec);

/// <summary>A visual hook on what is worn (owner ruling 2026-10-21): on the item in that slot, or on the body.</summary>
public sealed record EnhancementVisualHook(EquipSlot Slot, EnhancementVisual On, string Hook);

public static class EnhancementRulesEngine
{
    /// <summary>Whether a definition is a weapon or armour piece that takes enhancements at all: equipped, one of a kind, not heritage.</summary>
    public static bool Enhanceable(ItemDefinition item) =>
        item.StackMax == 1 && item.Heritage is null && item.Slot is not null && (item.Weapon is not null || item.Category == "armor");

    public static EnhancementTarget? TargetOf(ItemDefinition item) =>
        !Enhanceable(item) ? null : item.Weapon is not null ? EnhancementTarget.Weapon : EnhancementTarget.Armour;

    /// <summary>Slots on a piece (spec 47: 1-3 by quality and base material): its authored base, one more or less a quality step.</summary>
    public static int Slots(ItemDefinition item, int quality, EnhancementRules rules) =>
        !Enhanceable(item) ? 0 : Math.Clamp((item.EnhancementSlots ?? rules.BaseSlots) + quality, 1, rules.MaxSlots);

    public static int FreeSlots(ItemDefinition item, int quality, ItemEnhancements? enhancements, EnhancementRules rules) =>
        Math.Max(0, Slots(item, quality, rules) - (enhancements?.Filled.Length ?? 0) - (enhancements?.Cracked ?? 0));

    public static int StabilityCapacity(int quality, EnhancementRules rules) => Math.Max(0, rules.StabilityBase + quality * rules.StabilityPerQuality);

    public static int StabilityUsed(ItemEnhancements? enhancements, ItemCatalog catalog) =>
        enhancements?.Filled.Sum(f => catalog.Find(f)?.Enhancement?.Stability ?? 0) ?? 0;

    public static bool Unstable(int quality, ItemEnhancements? enhancements, ItemCatalog catalog, EnhancementRules rules) =>
        StabilityUsed(enhancements, catalog) > StabilityCapacity(quality, rules);

    /// <summary>The bench a kind of enhancement on a kind of piece is set at, and the skill that sets it.</summary>
    public static (string Station, string Skill) Bench(EnhancementSpec spec, EnhancementRules rules) => spec.Kind == EnhancementKind.Enchantment
        ? (rules.EnchantingStation, rules.EnchantingSkill)
        : spec.Target == EnhancementTarget.Weapon ? (rules.WeaponStation, rules.WeaponSkill) : (rules.ArmourStation, rules.ArmourSkill);

    /// <summary>A setting's chance in percent (spec 47: skill and the material's enchantability; harder the higher its tier).</summary>
    public static int SetPercent(int skill, EnhancementSpec spec, EnhancementRules rules) =>
        Math.Clamp(rules.BasePercent + Math.Max(0, skill) * rules.PercentPerSkill + spec.Enchantability - spec.Tier * rules.PercentPerTier,
            rules.MinPercent, rules.MaxPercent);

    /// <summary>A setting rolled: <paramref name="roll"/> and <paramref name="crack"/> are in [0, 1).</summary>
    public static SettingOutcome Setting(int skill, EnhancementSpec spec, EnhancementRules rules, double roll, double crack) =>
        roll * 100 < SetPercent(skill, spec, rules) ? SettingOutcome.Set : crack * 100 < rules.CrackPercent ? SettingOutcome.Cracked : SettingOutcome.Failed;

    /// <summary>
    /// The piece after a setting. <paramref name="replace"/> names a filled slot to reforge (spec 47: the old enhancement is consumed
    /// whatever the outcome); null fills a free slot.
    /// </summary>
    public static ItemEnhancements Apply(ItemEnhancements? before, string enhancementId, int? replace, SettingOutcome outcome)
    {
        var current = before ?? ItemEnhancements.None;
        var filled = replace is { } index ? current.Filled.RemoveAt(index) : current.Filled;
        return outcome switch
        {
            SettingOutcome.Set => current with
            {
                Filled = replace is { } at ? filled.Insert(at, enhancementId) : filled.Add(enhancementId),
                Attempts = current.Attempts + 1,
            },
            SettingOutcome.Cracked => current with { Filled = filled, Cracked = current.Cracked + 1, Attempts = current.Attempts + 1 },
            _ => current with { Filled = filled, Attempts = current.Attempts + 1 },
        };
    }

    /// <summary>
    /// What salvage keeps of an enhancement (owner ruling 2026-10-21): from <c>tier x RecoverSkillPerTier</c> of skill the enhancement
    /// may come back whole, more likely the further past it; otherwise it is destroyed and leaves only its essence, if any.
    /// </summary>
    public static int RecoverPercent(int skill, EnhancementSpec spec, EnhancementRules rules)
    {
        int threshold = spec.Tier * rules.RecoverSkillPerTier;
        return skill < threshold ? 0 : Math.Clamp(rules.RecoverBasePercent + (skill - threshold) * rules.RecoverPercentPerSkill, 0, 100);
    }

    /// <summary>What salvage returns for one enhancement: the enhancement's own item, its essence, or nothing.</summary>
    public static string? Salvaged(string enhancementId, int skill, EnhancementSpec spec, EnhancementRules rules, double sample) =>
        sample * 100 < RecoverPercent(skill, spec, rules) ? enhancementId : spec.Essence;
}

/// <summary>
/// What everything equipped does beyond armour and damage: the effects the wearer carries while it is worn (an equip effect, worn
/// enhancements, a piece's instability), the effects a weapon's enhancements put on what it wounds, the visual hooks, and how armour
/// slows and sounds (ATD §1: heavier rungs cost mobility and make noise).
/// </summary>
public static class GearRules
{
    /// <summary>Every enhancement on what is equipped, slot by slot, in slot order and then setting order.</summary>
    public static ImmutableArray<WornEnhancement> Enhancements(IEnumerable<(EquipSlot Slot, ItemDefinition Item, ItemEnhancements? Enhancements)> worn,
        ItemCatalog catalog) =>
        worn.OrderBy(w => w.Slot).SelectMany(w => (w.Enhancements?.Filled ?? ImmutableArray<string>.Empty)
                .Select(id => (w.Slot, Id: id, Spec: catalog.Find(id)?.Enhancement)))
            .Where(e => e.Spec is not null).Select(e => new WornEnhancement(e.Slot, e.Id, e.Spec!)).ToImmutableArray();

    /// <summary>
    /// The effects the wearer carries while the gear is equipped: each piece's equip effect (ITM-19), each enhancement's worn effect, and a
    /// piece's instability. Sorted, each once: two of the same do not stack.
    /// </summary>
    public static ImmutableArray<string> WornEffects(IEnumerable<(EquipSlot Slot, ItemDefinition Item, int Quality, ItemEnhancements? Enhancements)> worn,
        ItemCatalog catalog, EnhancementRules rules)
    {
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (slot, item, quality, enhancements) in worn)
        {
            if (item.EquipEffect is { } equip)
                effects.Add(equip);
            foreach (string id in enhancements?.Filled ?? ImmutableArray<string>.Empty)
            {
                if (catalog.Find(id)?.Enhancement?.WornEffect is { } effect)
                    effects.Add(effect);
            }
            if (rules.InstabilityEffect is { } unstable && EnhancementRulesEngine.Unstable(quality, enhancements, catalog, rules))
                effects.Add(unstable);
        }
        return effects.ToImmutableArray();
    }

    /// <summary>The on-hit effects of the main-hand weapon's enhancements, with their chances, in setting order.</summary>
    public static ImmutableArray<(string Effect, int Percent)> Procs(ItemEnhancements? weapon, ItemCatalog catalog) =>
        (weapon?.Filled ?? ImmutableArray<string>.Empty).Select(id => catalog.Find(id)?.Enhancement)
            .Where(s => s?.OnHitEffect is not null && s.OnHitPercent > 0).Select(s => (s!.OnHitEffect!, s.OnHitPercent)).ToImmutableArray();

    /// <summary>The visual hooks worn gear carries (owner ruling 2026-10-21): only enhancements whose visual is not none.</summary>
    public static ImmutableArray<EnhancementVisualHook> Visuals(IEnumerable<WornEnhancement> enhancements) =>
        enhancements.Where(e => e.Spec.Visual != EnhancementVisual.None && e.Spec.VisualHook is not null)
            .Select(e => new EnhancementVisualHook(e.Slot, e.Spec.Visual, e.Spec.VisualHook!)).Distinct().ToImmutableArray();

    /// <summary>How much worn gear slows the wearer, [0, cap]: each piece's <c>movement_penalty</c> summed (ATD §1: -10% run at rung 3).</summary>
    public static double MovementPenalty(IEnumerable<ItemDefinition> worn, EnhancementRules rules) =>
        Math.Clamp(worn.Sum(i => i.MovementPenalty), 0, rules.MaxMovementPenalty);

    /// <summary>How much louder worn gear makes every footfall: 1 plus each piece's <c>noise</c>.</summary>
    public static double NoiseFactor(IEnumerable<ItemDefinition> worn) => 1 + worn.Sum(i => i.Noise);
}
