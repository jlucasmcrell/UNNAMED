// UNNAMED Domain - the accessibility assists that change a rule rather than a picture: how wide the player's own timing windows are (GEN-10)
// No Godot references - pure C#

namespace UNNAMED.Domain.Combat;

/// <summary>
/// How wide the timing windows are (GEN-10): as the fight was tuned, or widened for a player who needs longer to answer a blow. It is the
/// player's own windows that widen - their parry and their dodge - never the creatures', so the fight's shape is unchanged.
/// </summary>
public enum TimingWindows
{
    Normal,
    Generous,
}

/// <summary>The two assists a fight reads: the parry window and the dodge's.</summary>
public static class AssistRules
{
    /// <summary>How much wider a generous window is than a normal one: half again as long.</summary>
    public const double GenerousShare = 1.5;

    /// <summary>
    /// A window in ticks, widened when the player asked for generous timing. Always at least a tick wider, because half again as long as a
    /// single tick is one and a half ticks, and a window that cannot grow is not a wider one.
    /// </summary>
    public static int WindowTicks(int normalTicks, TimingWindows windows) =>
        windows != TimingWindows.Generous || normalTicks <= 0
            ? normalTicks
            : Math.Max(normalTicks + 1, (int)Math.Ceiling(normalTicks * GenerousShare));

    /// <summary>
    /// The combat constants the player's own action is judged by: generous timing widens the dodge, whose window is also the ticks it cannot
    /// be struck through. A parry is a technique's own ticks, widened where the technique is used.
    /// </summary>
    public static CombatConstants Windowed(CombatConstants constants, TimingWindows windows) =>
        windows == TimingWindows.Generous
            ? constants with { DodgeTicks = WindowTicks(constants.DodgeTicks, windows) }
            : constants;
}
