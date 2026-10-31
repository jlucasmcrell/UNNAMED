using UNNAMED.Domain.Combat;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui.Models;

public enum BossTellPattern { Sweep, Fall, Grasp, Bite, Lunge }

/// <summary>The public attack's footprint and timing, with no second set of combat numbers.</summary>
public sealed record BossTelegraphModel(string Source, BossTellPattern Pattern, bool Lane,
    float ReachM, float HalfWidthM, float ArcDegrees, float Progress, bool Active)
{
    public static BossTelegraphModel? Of(CreatureView creature, CreatureDefinition definition,
        CombatConstants constants, long targetRadiusMm)
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
        return new(source, pattern, attack.IsCharge, (attack.ReachMm + targetRadiusMm) / 1000f,
            (definition.RadiusMm + targetRadiusMm) / 1000f, constants.MeleeArcMdeg / 1000f, progress,
            creature.Phase == CombatPhase.Active);
    }
}
