// UNNAMED Domain - the one damage pipeline, creature combat definitions and status effects
// (SYSTEMS.md S-11, S-12; COMBAT_DAMAGE_ARMOR_AND_DEATH.md §17-§19, §32; M3c)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Creatures;

namespace UNNAMED.Domain.Combat;

/// <summary>Where a blow lands (COMBAT §17: a few regions, never every finger). Arms and legs share one region in Phase 1.</summary>
public enum BodyRegion
{
    Head,
    Torso,
    Limbs,
}

public static class BodyRegions
{
    public static string Key(BodyRegion region) => region switch
    {
        BodyRegion.Head => "head",
        BodyRegion.Torso => "torso",
        BodyRegion.Limbs => "limbs",
        _ => throw new ArgumentOutOfRangeException(nameof(region), region, "Unknown body region"),
    };

    public static BodyRegion Parse(string key)
    {
        foreach (var region in Enum.GetValues<BodyRegion>())
        {
            if (Key(region) == key)
                return region;
        }
        throw new FormatException($"Unknown body region '{key}'");
    }
}

public static class DamageTypes
{
    /// <summary>DATA_MODEL.md §4.2's closed set.</summary>
    public static readonly ImmutableArray<string> All = ImmutableArray.Create(
        "physical_slash", "physical_pierce", "physical_blunt", "fire", "frost", "shock", "arcane", "holy", "necrotic", "poison");

    public static bool IsPhysical(string type) => type.StartsWith("physical_", StringComparison.Ordinal);
}

/// <summary>
/// One attack as the pipeline sees it: what hits, how hard, how far, and when. Timing is in ticks and is the authority:
/// presentation plays a clip scaled so its <c>hit_window_start</c> event lands on <see cref="WindupTicks"/>, and never
/// the other way round (ANIMATION_METADATA_SCHEMA.md "events are gameplay timing, not frame numbers").
/// </summary>
public sealed record AttackProfile(
    string Source,
    int DamageMin,
    int DamageMax,
    string DamageType,
    long ReachMm,
    int WindupTicks,
    int ActiveTicks,
    int RecoveryTicks,
    int StaminaCost)
{
    /// <summary>A ranged attack resolves once, at release, along the attacker's facing; a melee one sweeps its arc every active tick.</summary>
    public bool Ranged { get; init; }

    /// <summary>The ammunition a ranged attack spends at release.</summary>
    public string? AmmoDefId { get; init; }

    /// <summary>The weapon-family skill an effective hit trains (PROGRESSION.md §6).</summary>
    public string? SkillId { get; init; }

    public string? OnHitEffect { get; init; }
    public int OnHitEffectPercent { get; init; }

    /// <summary>A lunge: the attacker carries itself this far forward across the active window, so the blow reaches further.</summary>
    public long LungeMm { get; init; }

    /// <summary>A charge (M3d): after the windup the attacker runs straight at this speed, committed, until it hits or has run <see cref="ReachMm"/>.</summary>
    public long ChargeSpeedMmPerSecond { get; init; }

    /// <summary>A charge is only started from at least this far away.</summary>
    public long ChargeMinRangeMm { get; init; }

    /// <summary>How long a charge that runs into something solid leaves the charger stunned.</summary>
    public int StunTicks { get; init; }

    /// <summary>Ticks between two uses.</summary>
    public int CooldownTicks { get; init; }

    /// <summary>The blow knocks its target off balance whatever its size (a charge), unless it is guarded or dodged.</summary>
    public bool ForcesStagger { get; init; }

    /// <summary>The attacker keeps running at its target through the windup (the hound bites on the run), so fleeing does not open the gap.</summary>
    public bool Advances { get; init; }

    public bool IsCharge => ChargeSpeedMmPerSecond > 0;

    public int TotalTicks => WindupTicks + ActiveTicks + RecoveryTicks;
}

/// <summary>Combat tuning from <c>config.damage_constants</c> (PROTOTYPE.md §4.4). Durations are in ticks, distances in millimetres.</summary>
public sealed record CombatConstants
{
    public double ArmorK { get; init; } = 50;
    public double PierceArmorIgnored { get; init; } = 0.3;
    public double CritMultiplier { get; init; } = 1.5;
    public int BaseCritPercent { get; init; } = 5;
    public ImmutableSortedDictionary<BodyRegion, int> RegionWeights { get; init; } = ImmutableSortedDictionary<BodyRegion, int>.Empty;
    public ImmutableSortedDictionary<BodyRegion, double> RegionMultipliers { get; init; } = ImmutableSortedDictionary<BodyRegion, double>.Empty;
    public double StaggerThresholdPercent { get; init; } = 14.5;
    public int StaggerTicks { get; init; } = 12;
    public int StaggerImmunityTicks { get; init; } = 40;
    public int BlockMitigationPercent { get; init; } = 70;
    public int BlockStaminaPerHit { get; init; } = 10;
    public long BlockArcMdeg { get; init; } = 120_000;
    public int DodgeStaminaCost { get; init; } = 20;
    public int DodgeTicks { get; init; } = 5;
    public int DodgeRecoveryTicks { get; init; } = 5;
    public long DodgeDistanceMm { get; init; } = 2_500;
    public int StaminaRegenPerSecond { get; init; } = 20;
    public int StaminaRegenDelayTicks { get; init; } = 20;
    public int SprintStaminaPerSecond { get; init; } = 10;
    public int HealthRegenPerSecond { get; init; } = 1;
    public int OutOfCombatTicks { get; init; } = 160;
    public double MightDamagePercentPerPoint { get; init; } = 4;
    public long MeleeArcMdeg { get; init; } = 90_000;
    public long RangedRangeMm { get; init; } = 40_000;
    public int WindupPercent { get; init; } = 40;
    public int ActivePercent { get; init; } = 20;
    public int BowRecoveryTicks { get; init; } = 6;
    public int DefaultStaminaCost { get; init; } = 8;
    public long LeashMm { get; init; } = 40_000;
    public string DeathEffect { get; init; } = "effect.weakened";
    public AttackProfile Unarmed { get; init; } = new("unarmed", 2, 3, "physical_blunt", 1_200, 5, 2, 5, 4);
}

/// <summary>
/// Who strikes, and how: the attack plus what the attacker brings to it (Might, effects, skill). <see cref="ForcedRegion"/>
/// is a blow placed rather than rolled - into a weak point the attacker's position opens.
/// </summary>
public sealed record Strike(AttackProfile Attack, double DamageMultiplier, int CritPercent, double StaggerPower)
{
    public BodyRegion? ForcedRegion { get; init; }
}

/// <summary>What stands between the blow and the body. Armor is coverage: each region has its own (COMBAT §11).</summary>
public sealed record DefenseProfile(
    int MaxHealth,
    ImmutableSortedDictionary<BodyRegion, int> Armor,
    ImmutableSortedDictionary<string, double> Resistances,
    bool Blocking,
    bool Dodging);

/// <summary>What one blow did. <see cref="Final"/> is what the target loses; <see cref="Raw"/> is the impact before armor.</summary>
public sealed record HitResult(BodyRegion Region, int Raw, int Final, bool Critical, bool Blocked, bool Dodged, bool Staggered, bool EffectApplied);

public static class CombatRules
{
    /// <summary>
    /// COMBAT §18: attack -> contact region -> armor on that region -> penetration -> damage, stagger, effect. Level is not
    /// an input: the same blow on the same unarmoured spot wounds a level-50 body as much as a level-1 one (§1, §32).
    /// Stagger comes from impact - the blow before armor - scaled by the striker's stagger power; blocking absorbs it.
    /// <paramref name="random"/> maps a sample index to [0, 1); callers key it so a replay rolls the same.
    /// </summary>
    public static HitResult Resolve(Strike strike, DefenseProfile defense, CombatConstants constants, Func<uint, double> random)
    {
        var attack = strike.Attack;
        var region = strike.ForcedRegion ?? Region(constants, random(0));
        if (defense.Dodging)
            return new HitResult(region, 0, 0, false, false, true, false, false);

        int span = attack.DamageMax - attack.DamageMin + 1;
        int roll = attack.DamageMin + Math.Min(span - 1, (int)Math.Floor(random(1) * span));
        double damage = roll * strike.DamageMultiplier * constants.RegionMultipliers.GetValueOrDefault(region, 1.0);
        bool critical = random(2) * 100 < strike.CritPercent;
        if (critical)
            damage *= constants.CritMultiplier;
        double impact = damage;

        bool physical = DamageTypes.IsPhysical(attack.DamageType);
        if (physical)
        {
            double armor = defense.Armor.GetValueOrDefault(region);
            if (attack.DamageType == "physical_pierce")
                armor *= 1 - constants.PierceArmorIgnored;
            damage *= 1 - armor / (armor + constants.ArmorK);
        }
        damage *= 1 - Math.Clamp(defense.Resistances.GetValueOrDefault(attack.DamageType), -1.0, 0.9);
        bool blocked = defense.Blocking && physical;
        if (blocked)
            damage *= 1 - constants.BlockMitigationPercent / 100.0;

        // Whole points, with the fraction kept as a chance of one more, so a few points of armor matter on average
        // instead of rounding away. A blow that lands always wounds: there is no immunity by numbers (COMBAT §32).
        double whole = Math.Floor(damage);
        int final = roll > 0 ? Math.Max(1, (int)whole + (random(4) < damage - whole ? 1 : 0)) : 0;
        bool staggered = !blocked && impact * strike.StaggerPower * 100 >= (double)defense.MaxHealth * constants.StaggerThresholdPercent;
        bool effect = attack.OnHitEffect is not null && !blocked && random(3) * 100 < attack.OnHitEffectPercent;
        return new HitResult(region, (int)Math.Round(impact, MidpointRounding.AwayFromZero), final, critical, blocked, false, staggered, effect);
    }

    private static BodyRegion Region(CombatConstants constants, double roll)
    {
        int total = constants.RegionWeights.Values.Sum();
        double pick = roll * total;
        foreach (var (region, weight) in constants.RegionWeights)
        {
            pick -= weight;
            if (pick < 0)
                return region;
        }
        return BodyRegion.Torso;
    }

    /// <summary>Might above the base adds to a blow and below it takes away (Phase 1's live combat attribute).</summary>
    public static double MightMultiplier(int might, int attributeBase, CombatConstants constants) =>
        Math.Max(0.1, 1 + (might - attributeBase) * constants.MightDamagePercentPerPoint / 100.0);

    /// <summary>True when a point lies within <paramref name="reachMm"/> and inside the front arc of a body facing <paramref name="facingMdeg"/>.</summary>
    public static bool InFront(long fromXMm, long fromZMm, int facingMdeg, long toXMm, long toZMm, long reachMm, long arcMdeg)
    {
        double dx = toXMm - fromXMm, dz = toZMm - fromZMm;
        double distance = Math.Sqrt(dx * dx + dz * dz);
        if (distance > reachMm)
            return false;
        if (distance < 1)
            return true;
        double facing = facingMdeg / 1000.0 * Math.PI / 180.0;
        double cos = (dx * Math.Sin(facing) + dz * Math.Cos(facing)) / distance;
        return cos >= Math.Cos(arcMdeg / 2000.0 * Math.PI / 180.0);
    }

    /// <summary>Millidegrees from +Z towards +X, in [0, 360000), of the direction from one point to another.</summary>
    public static int FacingTowards(long fromXMm, long fromZMm, long toXMm, long toZMm)
    {
        double degrees = Math.Atan2(toXMm - fromXMm, toZMm - fromZMm) * 180 / Math.PI;
        int mdeg = (int)Math.Round(degrees * 1000) % 360_000;
        return mdeg < 0 ? mdeg + 360_000 : mdeg;
    }
}

/// <summary>A weak point (COMBAT_DAMAGE_ARMOR_AND_DEATH.md §19): a region a blow reaches only from the right side.</summary>
public sealed record WeakPoint(BodyRegion Region, bool FromBehind);

/// <summary>
/// A creature as combat knows it (DATA_MODEL.md §4.4): an authored level and stat block that nothing scales to the
/// player (charter §1). Its attack comes from its <c>attack_set</c>; a second, charging ability may follow it.
/// </summary>
public sealed record CreatureDefinition(
    string Id,
    string Family,
    int Level,
    int MaxHealth,
    ImmutableSortedDictionary<BodyRegion, int> Armor,
    ImmutableSortedDictionary<string, double> Resistances,
    AttackProfile Attack,
    long MoveSpeedMmPerSecond,
    long RadiusMm,
    long XpValue)
{
    public string? LootTableId { get; init; }

    /// <summary>What it perceives with (M3d; DATA_MODEL.md §4.4 <c>perception</c>).</summary>
    public Senses Senses { get; init; } = new(25_000, 140_000, 30_000);

    /// <summary>A charge it can open with (the boar's), or null.</summary>
    public AttackProfile? Charge { get; init; }

    /// <summary>How fast it turns, in millidegrees a second: a heavy body cannot swing round onto whoever gets behind it.</summary>
    public long TurnMdegPerSecond { get; init; } = 720_000;

    public WeakPoint? WeakPoint { get; init; }

    /// <summary>Its content tags (<c>undead</c>, <c>construct</c>): effects whose immunity tags meet them do not take.</summary>
    public ImmutableSortedSet<string> Tags { get; init; } = ImmutableSortedSet<string>.Empty;
}

public enum StackPolicy
{
    Refresh,
    StackIntensity,
}

/// <summary>
/// A status effect definition (DATA_MODEL.md §4.8) in ticks. Modifiers are the Phase-1 stat set: damage dealt and
/// stamina regeneration multiply, armor adds.
/// </summary>
public sealed record EffectDefinition(
    string Id,
    string Category,
    StackPolicy Stacking,
    int MaxStacks,
    int DurationTicks,
    int TickIntervalTicks)
{
    public int DamagePerTick { get; init; }
    public string DamageType { get; init; } = "physical_pierce";
    public int HealPerTick { get; init; }
    public double DamageDealtMultiplier { get; init; } = 1.0;
    public double StaminaRegenMultiplier { get; init; } = 1.0;
    public int ArmorBonus { get; init; }

    /// <summary>DATA_MODEL.md §4.8 <c>immunity_tags</c>: a body carrying any of these does not take the effect (no blood, no bleeding).</summary>
    public ImmutableSortedSet<string> ImmuneTags { get; init; } = ImmutableSortedSet<string>.Empty;
}

/// <summary>An effect on a body. Deadlines are absolute world ticks, so a save between ticks loses nothing (SYSTEMS.md S-04, S-11).</summary>
public sealed record ActiveEffect(string EffectId, int Stacks, long ExpiresTick, long NextTickAt);

public static class EffectRules
{
    /// <summary>One more application: the duration restarts, and a stacking effect gains a stack up to its maximum.</summary>
    public static ImmutableArray<ActiveEffect> Apply(ImmutableArray<ActiveEffect> effects, EffectDefinition definition, long now)
    {
        var existing = effects.FirstOrDefault(e => e.EffectId == definition.Id);
        int stacks = existing is null ? 1 : definition.Stacking == StackPolicy.StackIntensity ? Math.Min(definition.MaxStacks, existing.Stacks + 1) : existing.Stacks;
        var applied = new ActiveEffect(definition.Id, stacks, now + definition.DurationTicks,
            existing?.NextTickAt ?? now + Math.Max(1, definition.TickIntervalTicks));
        return Sorted((existing is null ? effects : effects.Remove(existing)).Add(applied));
    }

    public static ImmutableArray<ActiveEffect> Sorted(IEnumerable<ActiveEffect> effects) =>
        effects.OrderBy(e => e.EffectId, StringComparer.Ordinal).ToImmutableArray();

    public static double DamageDealtMultiplier(IEnumerable<ActiveEffect> effects, IReadOnlyDictionary<string, EffectDefinition> definitions) =>
        effects.Aggregate(1.0, (m, e) => m * Math.Pow(definitions.GetValueOrDefault(e.EffectId)?.DamageDealtMultiplier ?? 1.0, e.Stacks));

    public static double StaminaRegenMultiplier(IEnumerable<ActiveEffect> effects, IReadOnlyDictionary<string, EffectDefinition> definitions) =>
        effects.Aggregate(1.0, (m, e) => m * Math.Pow(definitions.GetValueOrDefault(e.EffectId)?.StaminaRegenMultiplier ?? 1.0, e.Stacks));

    public static int ArmorBonus(IEnumerable<ActiveEffect> effects, IReadOnlyDictionary<string, EffectDefinition> definitions) =>
        effects.Sum(e => (definitions.GetValueOrDefault(e.EffectId)?.ArmorBonus ?? 0) * e.Stacks);
}
