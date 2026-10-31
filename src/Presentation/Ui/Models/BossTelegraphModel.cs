using UNNAMED.Domain.Combat;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui.Models;

public enum BossTellPattern { Sweep, Fall, Grasp, Bite, Lunge }

/// <summary>The public attack's footprint and timing, with no second set of combat numbers.</summary>
public sealed record BossTelegraphModel(string Source, BossTellPattern Pattern, bool Lane,
    float ReachM, float HalfWidthM, float ArcDegrees, float Progress, bool Active, int FacingMdeg)
{
    public static BossTelegraphModel? Of(CreatureView creature, CreatureDefinition definition,
        CombatConstants constants, long targetRadiusMm, long tick, int tickMilliseconds)
    {
        if (!creature.Alive || !definition.Tags.Contains("boss")
            || creature.Phase is not (CombatPhase.Windup or CombatPhase.Active)) return null;
        var attack = definition.Moves.FirstOrDefault(a => a.Source == creature.AttackSource);
        if (attack is null) return null;
        string source = attack.Source;
        var pattern = source.Contains("fall", StringComparison.Ordinal) || source.Contains("heavy", StringComparison.Ordinal) ? BossTellPattern.Fall
            : source.Contains("grasp", StringComparison.Ordinal) ? BossTellPattern.Grasp
            : source.Contains("lunge", StringComparison.Ordinal) || attack.IsCharge ? BossTellPattern.Lunge
            : source.Contains("bite", StringComparison.Ordinal) ? BossTellPattern.Bite : BossTellPattern.Sweep;
        float progress = creature.Phase == CombatPhase.Active ? 1
            : Math.Clamp(1f - creature.PhaseTicksLeft / (float)Math.Max(1, creature.PhaseDurationTicks), 0, 1);
        long reach = attack.ReachMm + targetRadiusMm;
        long width = definition.RadiusMm + targetRadiusMm;
        if (attack.IsCharge)
        {
            // Use the same integer step/run length as CreatureSystem.Run. Charge
            // PhaseTicksLeft is intentionally zero, so read its public start tick.
            long step = Math.Max(1, attack.ChargeSpeedMmPerSecond * tickMilliseconds / 1000);
            long runs = Math.Max(1, attack.ReachMm / step);
            long elapsed = creature.Phase == CombatPhase.Active
                ? Math.Clamp(tick - creature.AttackTick - attack.WindupTicks, 0, runs) : 0;
            width = CombatRules.ChargeContactRadiusMm(definition.RadiusMm, targetRadiusMm);
            reach = (runs - elapsed) * step + width;
        }
        else if (attack.LungeMm > 0)
        {
            float remaining = creature.Phase == CombatPhase.Active
                ? Math.Clamp(creature.PhaseTicksLeft / (float)Math.Max(1, creature.PhaseDurationTicks), 0, 1) : 1;
            reach += (long)(attack.LungeMm * remaining);
        }
        return new(source, pattern, attack.IsCharge, reach / 1000f,
            width / 1000f, constants.MeleeArcMdeg / 1000f, progress,
            creature.Phase == CombatPhase.Active,
            attack.IsCharge ? creature.ChargeFacingMdeg ?? creature.Body.FacingMdeg : creature.Body.FacingMdeg);
    }
}
