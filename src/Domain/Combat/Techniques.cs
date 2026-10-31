// UNNAMED Domain - player techniques: an active technique is a weapon family's own blow, drawn from the weapon in hand, or one of the
// slice's set - a parry, a guard-beat, a retreat, a plant, a gap thrust, a silent strike, a held draw (SYSTEMS_COMPLETENESS_PLAN
// §2.3 C5, D11; DATA_MODEL.md §4.7; SYSTEMS.md S-10; SKILLS_AND_TECHNIQUES.md §3.1; CMB-24)
// No Godot references - pure C#

namespace UNNAMED.Domain.Combat;

/// <summary>
/// What using a technique does. Most are a weapon's own blow changed; the vertical slice's seven also do something a blow cannot - they wait
/// for a blow (<see cref="Parry"/>), take a guard away (<see cref="GuardBeat"/>), trade ground for the point (<see cref="GivingGround"/>),
/// plant against a charge (<see cref="SetAgainstCharge"/>), find a gap (<see cref="GapThrust"/>), make no noise (<see cref="QuietBlade"/>),
/// or hold an aim (<see cref="SteadyDraw"/>).
/// </summary>
public enum TechniqueKind
{
    /// <summary>The weapon family's own blow, changed: the three the build has had since WP9.</summary>
    Blow,

    /// <summary>Catch a blow on the flat inside the parry window: it is gone, and the attacker is open (SKILLS_AND_TECHNIQUES §3.1, sword).</summary>
    Parry,

    /// <summary>Strike the guard instead of the body: the guard goes, and what it was guarding does not (sword).</summary>
    GuardBeat,

    /// <summary>Retreat with the point live: the enemy advances into a decision (polearm).</summary>
    GivingGround,

    /// <summary>Brace the butt and take a charge on the point: the charger's own mass does the work (polearm).</summary>
    SetAgainstCharge,

    /// <summary>A short point into whatever is not covered (dagger).</summary>
    GapThrust,

    /// <summary>A strike that spends nothing noisy: no shout, no clatter, no alarm (dagger).</summary>
    QuietBlade,

    /// <summary>Hold a draw without the aim failing: the difference between a shot and a hope (bow).</summary>
    SteadyDraw,
}

public static class TechniqueKinds
{
    /// <summary>The word content names a kind by (<c>executes:</c>); <see cref="TechniqueKind.Blow"/> writes none at all.</summary>
    public static string Key(TechniqueKind kind) => kind switch
    {
        TechniqueKind.Blow => "blow",
        TechniqueKind.Parry => "parry",
        TechniqueKind.GuardBeat => "guard_beat",
        TechniqueKind.GivingGround => "giving_ground",
        TechniqueKind.SetAgainstCharge => "set_against_charge",
        TechniqueKind.GapThrust => "gap_thrust",
        TechniqueKind.QuietBlade => "quiet_blade",
        TechniqueKind.SteadyDraw => "steady_draw",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static TechniqueKind? Parse(string key) => key switch
    {
        "blow" => TechniqueKind.Blow,
        "parry" => TechniqueKind.Parry,
        "guard_beat" => TechniqueKind.GuardBeat,
        "giving_ground" => TechniqueKind.GivingGround,
        "set_against_charge" => TechniqueKind.SetAgainstCharge,
        "gap_thrust" => TechniqueKind.GapThrust,
        "quiet_blade" => TechniqueKind.QuietBlade,
        "steady_draw" => TechniqueKind.SteadyDraw,
        _ => null,
    };

    /// <summary>Whether the kind is a blow at all: the three that are not never start an attack.</summary>
    public static bool IsBlow(TechniqueKind kind) =>
        kind is TechniqueKind.Blow or TechniqueKind.GuardBeat or TechniqueKind.GapThrust or TechniqueKind.QuietBlade or TechniqueKind.SteadyDraw;
}

/// <summary>
/// An active technique (<c>ability</c> of <c>class: active</c>, DATA_MODEL.md §4.7): one weapon family's (<see cref="SkillId"/>, its
/// <c>discipline</c>) blow, made with whatever weapon of that family is in hand, or one of the kinds above. A blow changes the weapon's own
/// attack - its damage by <see cref="DamageMultiplier"/>, its windup (a bow's draw) by <see cref="WindupMultiplier"/>, a swing's reach by
/// <see cref="ReachBonusMm"/> - costs <see cref="StaminaCost"/> instead of the weapon's, may put an effect on what it wounds, and cannot be
/// used again for <see cref="CooldownTicks"/>. Known only through a learning event; it trains the family's skill like any effective blow.
/// </summary>
public sealed record TechniqueDefinition(string Id, string SkillId, double DamageMultiplier, double WindupMultiplier, long ReachBonusMm,
    int StaminaCost, int CooldownTicks)
{
    public string? OnHitEffect { get; init; }
    public int OnHitEffectPercent { get; init; }

    public TechniqueKind Kind { get; init; } = TechniqueKind.Blow;

    /// <summary>The weapons it is made with, by item definition: empty means any weapon of its discipline's family.</summary>
    public System.Collections.Immutable.ImmutableArray<string> Weapons { get; init; } = System.Collections.Immutable.ImmutableArray<string>.Empty;

    /// <summary>Parry: how long after the technique is used a blow can be caught, and how long the caught attacker is open.</summary>
    public int ParryTicks { get; init; }
    public int OpeningTicks { get; init; }

    /// <summary>Beat the Guard: how long the guard it beats stays down.</summary>
    public int GuardBreakTicks { get; init; }

    /// <summary>Giving Ground: how far the retreat may carry, how long the point stays live, and what the live point adds.</summary>
    public long RetreatMm { get; init; }
    public int PointLiveTicks { get; init; }
    public long PointLiveReachMm { get; init; }
    public double PointLiveDamageMultiplier { get; init; } = 1.0;

    /// <summary>Set Against the Charge: how long the plant holds, and what a stopped charge takes beyond the point's own blow.</summary>
    public int BraceTicks { get; init; }
    public double ChargeStopMultiplier { get; init; } = 1.0;

    /// <summary>Gap Thrust: the gap it aims at.</summary>
    public ArmourGap? Gap { get; init; }

    /// <summary>Quiet Blade: the blow spends nothing noisy.</summary>
    public bool Silent { get; init; }

    /// <summary>Steady Draw: how long the aim holds, and how much truer the shot is while it does.</summary>
    public int SteadyTicks { get; init; }
    public int SteadyCritPercent { get; init; }

    /// <summary>Whether using it begins a blow at all.</summary>
    public bool IsBlow => TechniqueKinds.IsBlow(Kind);

    /// <summary>Whether this weapon is one it can be made with.</summary>
    public bool MadeWith(string weaponDefId, string skillId) =>
        SkillId == skillId && (Weapons.IsDefaultOrEmpty || Weapons.Contains(weaponDefId, StringComparer.Ordinal));

    public string? Problem()
    {
        if (DamageMultiplier <= 0)
            return "multiplier_of_weapon is positive";
        if (WindupMultiplier < 1)
            return "windup_multiplier is at least 1 (a technique never comes out faster than the plain blow)";
        if (ReachBonusMm < 0)
            return "reach_bonus_m is not negative";
        if (StaminaCost < 0)
            return "cost.stamina is not negative";
        if (CooldownTicks < 0)
            return "cooldown_s is not negative";
        if (OnHitEffectPercent is < 0 or > 100)
            return "an apply_effect's chance is from 0 to 1";
        return Kind switch
        {
            TechniqueKind.Parry => ParryTicks >= 1 && OpeningTicks >= 0 ? null : "a parry catches a blow for at least a tick, and opens the attacker for none or more",
            TechniqueKind.GuardBeat => GuardBreakTicks >= 1 ? null : "a guard beat keeps the guard down for at least a tick",
            TechniqueKind.GivingGround => RetreatMm >= 0 && PointLiveTicks >= 1 && PointLiveReachMm >= 0 && PointLiveDamageMultiplier >= 1
                ? null
                : "giving ground retreats no less than nothing, holds the point live for at least a tick, and never makes it worse",
            TechniqueKind.SetAgainstCharge => BraceTicks >= 1 && ChargeStopMultiplier >= 0
                ? null
                : "set against the charge plants for at least a tick and stops a charge for no less than nothing",
            TechniqueKind.GapThrust => Gap is not null ? null : "gap thrust names the gap it aims at",
            TechniqueKind.QuietBlade => Silent ? null : "quiet blade makes no swing noise",
            TechniqueKind.SteadyDraw => SteadyTicks >= 1 && SteadyCritPercent >= 0 ? null : "steady draw holds an aim for at least a tick",
            _ => null,
        };
    }
}

public static class TechniqueRules
{
    /// <summary>
    /// The blow a technique makes with this weapon's attack: the weapon's own, changed as the technique says, under the technique's name
    /// (its source), still training the weapon's skill. A bow's technique still looses one of its arrows; reach changes only a swing. What
    /// the blow does beyond damage - breaks a guard, makes no noise, finds a gap, holds an aim - rides on the profile as a flag, because the
    /// one damage pipeline reads the profile and nothing else.
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
            BreaksGuard = technique.Kind == TechniqueKind.GuardBeat,
            Silent = technique.Kind == TechniqueKind.QuietBlade,
            Gap = technique.Kind == TechniqueKind.GapThrust ? technique.Gap : null,
            Steady = technique.Kind == TechniqueKind.SteadyDraw,
        };
    }

    /// <summary>
    /// The blow a point kept live by giving ground makes (SKILLS_AND_TECHNIQUES §3.1, polearm): longer by the point's reach and harder by its
    /// multiplier, because the enemy walked onto it.
    /// </summary>
    public static AttackProfile PointLive(AttackProfile attack, TechniqueDefinition givingGround)
    {
        int Scaled(int damage) => Math.Max(attack.DamageMin, (int)Math.Round(damage * givingGround.PointLiveDamageMultiplier, MidpointRounding.AwayFromZero));
        return attack with
        {
            DamageMin = Scaled(attack.DamageMin),
            DamageMax = Math.Max(Scaled(attack.DamageMin), Scaled(attack.DamageMax)),
            ReachMm = attack.Ranged ? attack.ReachMm : attack.ReachMm + givingGround.PointLiveReachMm,
        };
    }
}
