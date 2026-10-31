using UNNAMED.Domain.Combat;

namespace UNNAMED.Presentation.Art;

/// <summary>Clip time follows the one-second footing clock; it never extends the simulation's recovery.</summary>
public static class ControlMotion
{
    public static string Action(ControlMove move) => move switch
    {
        ControlMove.Shove => "shove",
        ControlMove.Trip => "trip",
        ControlMove.Disarm => "disarm",
        _ => throw new ArgumentOutOfRangeException(nameof(move)),
    };

    public static (string Clip, float Through) Prone(int ticksLeft, double alpha)
    {
        float elapsed = Math.Clamp((float)((ControlRules.ProneRiseTicks - ticksLeft + alpha) / ControlRules.ProneRiseTicks), 0, 1);
        return elapsed < .2f ? ("prone_fall", elapsed / .2f)
            : elapsed < .35f ? ("prone_idle", (elapsed - .2f) / .15f)
            : ("prone_rise", (elapsed - .35f) / .65f);
    }
}
