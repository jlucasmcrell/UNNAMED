// UNNAMED Domain - magic: formulas, and Strain and the other rules of casting as pure functions
// (MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md "Resonance and Strain", "Unsafe casting"; PROGRESSION.md §7; M3e)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Combat;

namespace UNNAMED.Domain.Magic;

/// <summary>Where a formula's working lands: on the caster, or on the first body along the caster's facing.</summary>
public enum Targeting
{
    Self,
    Projectile,
}

/// <summary>
/// A formula (DATA_MODEL.md §4.6, <c>kind: spell</c>): a known working of one magic domain. Focus is spent when it is
/// begun and Strain taken when it is released; its cast time is the tell. What it does is data - a blow through the one
/// combat pipeline, effects put on or lifted - never code per domain.
/// </summary>
public sealed record FormulaDefinition(string Id, string DomainSkillId, int Complexity, int FocusCost, int StrainCost, int CastTicks,
    int RecoveryTicks, Targeting Targeting)
{
    /// <summary>A projectile's blow: its damage and range, and the domain skill a wounding hit trains.</summary>
    public AttackProfile? Blow { get; init; }

    /// <summary>Effects the working puts on its target (the caster, for a self formula).</summary>
    public ImmutableArray<string> Applies { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>Effects the working lifts from its target: the mending that stops a bleed.</summary>
    public ImmutableArray<string> Removes { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary>The tuning of casting (<c>config.magic</c>): the pools' return, Strain's limits, and what skill and Resonance change.</summary>
public sealed record MagicConstants
{
    public int FocusRegenPerSecond { get; init; } = 4;

    /// <summary>Focus starts to return this long after the last working.</summary>
    public int FocusRegenDelayTicks { get; init; } = 40;

    public int StrainRecoveryPerSecond { get; init; } = 3;

    /// <summary>Strain starts to ebb this long after the last working.</summary>
    public int StrainRecoveryDelayTicks { get; init; } = 60;

    /// <summary>At or above this share of tolerance the character is Strained, and the HUD says so.</summary>
    public int StrainedPercent { get; init; } = 75;

    /// <summary>Health lost for each point of Strain a working would push past tolerance.</summary>
    public int BacklashPerPoint { get; init; } = 2;

    /// <summary>How much more Strain a working costs per point of complexity above the domain skill - and less per point below.</summary>
    public int StrainPercentPerPoint { get; init; } = 3;

    /// <summary>However skilled the caster, a working costs at least this share of its Strain.</summary>
    public int MinStrainPercent { get; init; } = 50;

    /// <summary>The chance, per point of complexity above the domain skill, that a working fizzles.</summary>
    public int FizzlePercentPerPoint { get; init; } = 3;

    /// <summary>The Resonance at which a working hits as authored.</summary>
    public int ResonanceReference { get; init; } = 20;

    public double ResonanceDamagePercentPerPoint { get; init; } = 2;

    /// <summary>The body's recovery after a release, before it can act again.</summary>
    public int RecoveryTicks { get; init; } = 6;
}

/// <summary>The rules of casting. No level anywhere: skill, Resonance and Strain decide.</summary>
public static class MagicRules
{
    /// <summary>The Strain a working costs at a domain skill: more above one's skill, less below it, never under the floor.</summary>
    public static int StrainCost(FormulaDefinition formula, int skill, MagicConstants rules)
    {
        int percent = Math.Max(rules.MinStrainPercent, 100 + (formula.Complexity - skill) * rules.StrainPercentPerPoint);
        return (int)Math.Round(formula.StrainCost * percent / 100.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>The chance in percent that a working fizzles: nothing once the domain skill reaches its complexity.</summary>
    public static int FizzlePercent(FormulaDefinition formula, int skill, MagicConstants rules) =>
        Math.Clamp((formula.Complexity - skill) * rules.FizzlePercentPerPoint, 0, 100);

    /// <summary>
    /// Strain after a working, and the health it costs: nothing within tolerance; past it, the excess is paid in health
    /// and Strain stays at the limit. A clear risk rather than a lock ("Unsafe casting").
    /// </summary>
    public static (int Strain, int Backlash) Settle(int strain, int cost, int tolerance, MagicConstants rules)
    {
        int after = strain + cost;
        return after <= tolerance ? (after, 0) : (Math.Max(tolerance, 0), (after - Math.Max(tolerance, 0)) * rules.BacklashPerPoint);
    }

    public static bool Strained(int strain, int tolerance, MagicConstants rules) =>
        tolerance > 0 && strain * 100 >= tolerance * rules.StrainedPercent;

    /// <summary>How hard a working hits: Resonance above the reference strengthens it, below it weakens it.</summary>
    public static double ResonanceMultiplier(long resonance, MagicConstants rules) =>
        Math.Max(0.1, 1 + (resonance - rules.ResonanceReference) * rules.ResonanceDamagePercentPerPoint / 100.0);
}
