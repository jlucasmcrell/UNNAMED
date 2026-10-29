// UNNAMED Domain - player techniques: an active technique is a weapon family's own blow, drawn from the weapon in hand
// (SYSTEMS_COMPLETENESS_PLAN_20261017.md §2.3 C5, D11; DATA_MODEL.md §4.7; SYSTEMS.md S-10)
// No Godot references - pure C#

namespace UNNAMED.Domain.Combat;

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
    public string? OnHitEffect { get; init; }
    public int OnHitEffectPercent { get; init; }

    public string? Problem() =>
        DamageMultiplier <= 0 ? "multiplier_of_weapon is positive"
        : WindupMultiplier < 1 ? "windup_multiplier is at least 1 (a technique never comes out faster than the plain blow)"
        : ReachBonusMm < 0 ? "reach_bonus_m is not negative"
        : StaminaCost < 0 ? "cost.stamina is not negative"
        : CooldownTicks < 0 ? "cooldown_s is not negative"
        : OnHitEffectPercent is < 0 or > 100 ? "an apply_effect's chance is from 0 to 1"
        : null;
}

/// <summary>
/// A catalogue technique (D13): a named capability one skill owns - crafting, gathering, survival, social, spellcasting, the cosmic, the
/// Other, technology - with the band it sits at and what it needs. It never changes a blow (weapon techniques stay
/// <see cref="TechniqueDefinition"/>); it is learned through the same nine sources, and the Skills page and the Learn column read it.
/// </summary>
public sealed record CatalogueTechnique(string Id, string SkillId, int Band, string Needs)
{
    public string? Problem() =>
        Band is < 15 or > 75 ? "band is one of 15, 30, 45, 60, 75"
        : Needs.Length == 0 ? "needs says what the technique requires"
        : null;
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
        };
    }
}
