// UNNAMED Domain - combat: shove, trip to prone and disarm (CMB-16, COMBAT:368-381, OD-C17(f))
// No Godot references - pure C#. Prone is NOT downed: downed is defeat and revive (CombatConstants.DownedTicks),
// prone is one second of lost footing with three seconds of immunity afterwards, and the two never share a timer.

using System;

namespace UNNAMED.Domain.Combat;

/// <summary>What a control move is. Each is a deliberate act, not a hit: it costs stamina whether or not it lands.</summary>
public enum ControlMove
{
    Shove,
    Trip,
    Disarm,
}

/// <summary>
/// A character's footing. <see cref="Prone"/> is the state a trip or an unguarded shove leaves you in; <see cref="ProneRiseTicksRemaining"/>
/// counts down to standing and <see cref="ImmunityTicksRemaining"/> keeps you from being knocked down again the moment you rise, so a
/// pair of shoves cannot chain a player to the floor. Deliberately separate from <c>CombatConstants.DownedTicks</c>: being downed is
/// defeat and revives at <c>DownedRevivePercent</c>, being prone is a stumble.
/// </summary>
public readonly record struct Footing(bool Prone, int ProneRiseTicksRemaining, int ImmunityTicksRemaining)
{
    public static Footing Standing => new(false, 0, 0);

    /// <summary>Whether another trip or unguarded shove can knock this character down right now.</summary>
    public bool CanBeKnockedDown => !Prone && ImmunityTicksRemaining <= 0;

    /// <summary>The footing a tick later. Rising is automatic: nothing has to be pressed to get up.</summary>
    public Footing Tick()
    {
        int rise = Prone ? Math.Max(0, ProneRiseTicksRemaining - 1) : 0;
        int immunity = Math.Max(0, ImmunityTicksRemaining - 1);
        return new Footing(Prone && rise > 0, rise, immunity);
    }
}

/// <summary>
/// What the actor brings to a control move and what it costs. <see cref="ReachM"/> is the actor's weapon reach: a disarm against a
/// longer weapon is harder and a shove at arm's length is not available at all, which is the reach-weapon penalty CMB-16 asks for.
/// </summary>
public sealed record ControlProfile(int Stamina, int ReachM = 1, double WeightKg = 80);

/// <summary>The outcome of one attempt, in the terms presentation needs: what happened, what it cost, and what state the target is in.</summary>
public sealed record ControlOutcome(
    ControlMove Move,
    bool Landed,
    int StaminaSpent,
    Footing Target,
    bool Disarmed,
    string Reason)
{
    public static ControlOutcome Refused(ControlMove move, string reason) => new(move, false, 0, Footing.Standing, false, reason);
}

/// <summary>
/// The three moves of CMB-16. Pure functions over the numbers the simulation already keeps, so a test can assert the rule without a
/// scene: OD-C17 allows shove, prone and disarm at 1.0, which is why each is a full capability rather than a weapon property.
/// </summary>
public static class ControlRules
{
    /// <summary>One second at the world's 20 ticks a second: the rise CMB-16 names.</summary>
    public const int ProneRiseTicks = 20;

    /// <summary>Three seconds of immunity after rising, so a shove cannot loop a target on the floor.</summary>
    public const int ProneImmunityTicks = 60;

    public const int ShoveStamina = 12;
    public const int TripStamina = 18;
    public const int DisarmStamina = 20;

    /// <summary>Shove reach is arm's length plus the weapon's own; a target further than this cannot be shoved at all.</summary>
    public const double ShoveReachM = 1.6;

    /// <summary>
    /// A shove: pushes, costs stamina, and knocks prone only when the target is neither guarding nor already down-and-immune.
    /// A guarded target is moved but keeps its feet, which is what makes guarding worth holding against a shover.
    /// </summary>
    public static ControlOutcome Shove(ControlProfile actor, in Footing target, bool targetGuarding, double distanceM)
    {
        if (actor.Stamina < ShoveStamina)
            return ControlOutcome.Refused(ControlMove.Shove, "not enough stamina");
        if (distanceM > ShoveReachM + 0.4 * actor.ReachM)
            return new ControlOutcome(ControlMove.Shove, false, ShoveStamina, target, false, "out of reach");

        bool knocked = !targetGuarding && target.CanBeKnockedDown;
        Footing after = knocked
            ? new Footing(true, ProneRiseTicks, ProneImmunityTicks)
            : target;
        return new ControlOutcome(ControlMove.Shove, true, ShoveStamina, after, false,
            knocked ? "shoved and knocked down" : "shoved off balance");
    }

    /// <summary>
    /// A trip: the guaranteed prone when it lands, refused by a guard (a guarded fighter is braced) and refused by immunity, so the
    /// counter to a trip is to guard or to have just got up. A heavier target is harder to bring down: weight buys resistance.
    /// </summary>
    public static ControlOutcome Trip(ControlProfile actor, in Footing target, bool targetGuarding, double targetWeightKg)
    {
        if (actor.Stamina < TripStamina)
            return ControlOutcome.Refused(ControlMove.Trip, "not enough stamina");
        if (targetGuarding)
            return new ControlOutcome(ControlMove.Trip, false, TripStamina, target, false, "guarded against");
        if (!target.CanBeKnockedDown)
            return new ControlOutcome(ControlMove.Trip, false, TripStamina, target, false, "still getting up");

        // A 60 kg actor brings down 80 kg unreliably and 120 kg not at all: weight is a real counter, not a stat line.
        bool landed = actor.WeightKg * 1.5 >= targetWeightKg;
        Footing after = landed ? new Footing(true, ProneRiseTicks, ProneImmunityTicks) : target;
        return new ControlOutcome(ControlMove.Trip, landed, TripStamina, after, false,
            landed ? "tripped" : "too heavy to bring down");
    }

    /// <summary>
    /// A disarm: drops the target's weapon if the actor's reach can get to it. A target with a longer weapon is harder to disarm -
    /// the reach-weapon penalty CMB-16 names - and the dropped weapon is picked up by presentation as a ground item.
    /// </summary>
    public static ControlOutcome Disarm(ControlProfile actor, int targetReachM, bool targetHoldingWeapon)
    {
        if (actor.Stamina < DisarmStamina)
            return ControlOutcome.Refused(ControlMove.Disarm, "not enough stamina");
        if (!targetHoldingWeapon)
            return new ControlOutcome(ControlMove.Disarm, false, DisarmStamina, Footing.Standing, false, "nothing to disarm");
        if (targetReachM > actor.ReachM + 1)
            return new ControlOutcome(ControlMove.Disarm, false, DisarmStamina, Footing.Standing, false,
                "their reach keeps the weapon out of yours");
        return new ControlOutcome(ControlMove.Disarm, true, DisarmStamina, Footing.Standing, true, "disarmed");
    }

    /// <summary>
    /// How a creature should read a control move against it (CMB-16's AI awareness): a disarmed creature backs off and looks for its
    /// weapon, a shoved one keeps its distance from a shover, and neither forgets it happened. Presentation maps these to whatever
    /// awareness channel it already uses for a charge.
    /// </summary>
    public static string AwarenessFor(ControlOutcome outcome) => outcome.Move switch
    {
        ControlMove.Disarm when outcome.Landed => "disarmed: recover the weapon, keep out of their reach",
        ControlMove.Shove when outcome.Target.Prone => "knocked down by a shover: rise clear, then keep distance",
        ControlMove.Trip when outcome.Landed => "tripped: rise clear, do not close on the tripper",
        ControlMove.Shove => "shoved: brace or give ground",
        _ => "unaffected",
    };
}
