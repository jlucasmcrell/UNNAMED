// UNNAMED Domain - player techniques: an active technique is a weapon family's own blow, drawn from the weapon in hand
// (SYSTEMS_COMPLETENESS_PLAN_20261017.md §2.3 C5, D11; DATA_MODEL.md §4.7; SYSTEMS.md S-10)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Combat;

public enum TechniqueUse { Active, Reaction, Contextual }
public enum TechniqueKind { Strike, Parry, GuardBreak, GivingGround, SetAgainstCharge, GapThrust, QuietBlade, SteadyDraw }
public enum TechniqueDiscovery { None, GuardedBlow, GuardBroken, RetreatingHit, ChargeStopped, PlacedHit, RearAmbush, SteadyShot }

/// <summary>
/// An active technique (<c>ability</c> of <c>class: active</c>, DATA_MODEL.md §4.7): one weapon family's (<see cref="SkillId"/>, its
/// <c>discipline</c>) blow, made with whatever weapon of that family is in hand. It changes the weapon's own attack - its damage by
/// <see cref="DamageMultiplier"/>, its windup (a bow's draw) by <see cref="WindupMultiplier"/>, a swing's reach by <see cref="ReachBonusMm"/>
/// - costs <see cref="StaminaCost"/> instead of the weapon's, may put an effect on what it wounds, and cannot be used again for
/// <see cref="CooldownTicks"/>. Known only through a learning event; it trains the family's skill like any effective blow.
/// </summary>
public sealed record TechniqueDefinition(string Id, string SkillId, double DamageMultiplier, double WindupMultiplier, long ReachBonusMm,
    int StaminaCost, int CooldownTicks)
{
    public string Web { get; init; } = "martial";
    public string Lane { get; init; } = "basic";
    public TechniqueUse Use { get; init; }
    public TechniqueKind Kind { get; init; }
    public TechniqueDiscovery Discovery { get; init; }
    public ImmutableArray<TechniqueRequirement> Requirements { get; init; } = ImmutableArray<TechniqueRequirement>.Empty;
    public ImmutableArray<string> Refines { get; init; } = ImmutableArray<string>.Empty;
    public bool RequiresDagger { get; init; }
    public string? OnHitEffect { get; init; }
    public int OnHitEffectPercent { get; init; }

    public string? Problem() =>
        !double.IsFinite(DamageMultiplier) || DamageMultiplier <= 0 ? "multiplier_of_weapon is positive"
        : !double.IsFinite(WindupMultiplier) || WindupMultiplier < 1 ? "windup_multiplier is at least 1 (a technique never comes out faster than the plain blow)"
        : ReachBonusMm < 0 ? "reach_bonus_m is not negative"
        : StaminaCost < 0 ? "cost.stamina is not negative"
        : CooldownTicks < 0 ? "cooldown_s is not negative"
        : OnHitEffectPercent is < 0 or > 100 ? "an apply_effect's chance is from 0 to 1"
        : Requirements.Select(TechniqueRequirements.Problem).FirstOrDefault(p => p is not null);
}

public static class TechniqueRules
{
    /// <summary>
    /// The blow a technique makes with this weapon's attack: the weapon's own, changed as the technique says, under the technique's name
    /// (its source), still training the weapon's skill. A bow's technique still looses one of its arrows; reach changes only a swing.
    /// </summary>
    public static AttackProfile Profile(AttackProfile weapon, TechniqueDefinition technique)
    {
        int Scaled(int damage) => Math.Max(1, (int)Math.Round(damage * technique.DamageMultiplier, MidpointRounding.AwayFromZero));
        return weapon with
        {
            Source = technique.Id,
            DamageMin = Scaled(weapon.DamageMin),
            DamageMax = Math.Max(Scaled(weapon.DamageMin), Scaled(weapon.DamageMax)),
            WindupTicks = Math.Max(1, (int)Math.Round(weapon.WindupTicks * technique.WindupMultiplier, MidpointRounding.AwayFromZero)),
            ReachMm = weapon.Ranged ? weapon.ReachMm : weapon.ReachMm + technique.ReachBonusMm,
            StaminaCost = technique.StaminaCost,
            OnHitEffect = technique.OnHitEffect ?? weapon.OnHitEffect,
            OnHitEffectPercent = technique.OnHitEffect is null ? weapon.OnHitEffectPercent : technique.OnHitEffectPercent,
            CooldownTicks = technique.CooldownTicks,
            GuardStaminaDamage = technique.Kind == TechniqueKind.GuardBreak ? 25 : weapon.GuardStaminaDamage,
            UsesGap = technique.Kind == TechniqueKind.GapThrust,
            Quiet = technique.Kind == TechniqueKind.QuietBlade,
            RetreatMm = technique.Kind == TechniqueKind.GivingGround ? 1_000 : 0,
        };
    }
}
