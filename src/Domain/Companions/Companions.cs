// UNNAMED Domain - companions: the order a companion follows and how they stand (SYSTEMS.md S-25; PROTOTYPE.md C16, §6.2; M6)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Companions;

/// <summary>The orders a Phase-1 companion takes (content bible §9: follow and wait - no tactical menu).</summary>
public enum CompanionOrder
{
    Follow,
    Wait,
}

/// <summary>Up, or downed and waiting for a hand (PROTOTYPE.md §6.2: death and revive).</summary>
public enum CompanionCondition
{
    Up,
    Downed,
}

/// <summary>What makes a named NPC a possible companion (M6): the body they fight with - their health, their weapon, their armor.</summary>
public sealed record CompanionProfile(int MaxHealth, string WeaponId, ImmutableSortedDictionary<BodyRegion, int> Armor);

/// <summary>
/// How a companion behaves (<c>config.companion</c>, M6). Distances are millimetres on the ground plane and times are ticks. The
/// behaviour is deliberately plain (ROADMAP.md M6: "boringly reliable, not clever").
/// </summary>
/// <param name="FollowNearMm">A following companion stands still this close to the character.</param>
/// <param name="RunBeyondMm">Farther than this a companion runs; nearer, walks.</param>
/// <param name="SprintBeyondMm">Farther than this a companion sprints.</param>
/// <param name="CatchUpBeyondMm">Farther than this a companion catches up at once: put down on the character's trail near them.</param>
/// <param name="SnagTicks">Making no headway for this long, a companion catches up the same way (C16: no snag lasts 15 s).</param>
/// <param name="TrailStepMm">A following companion walks the character's trail: a mark every this far the character goes.</param>
/// <param name="TrailLength">The trail keeps at most this many marks.</param>
/// <param name="FightRadiusMm">A following companion fights a creature engaged with the character within this of the companion...</param>
/// <param name="LeashMm">...and within this of the character.</param>
/// <param name="GuardRadiusMm">A waiting companion fights only what comes this close.</param>
/// <param name="AttackPauseTicks">A breath between a companion's blows.</param>
/// <param name="ReviveWindowTicks">Downed this long with no hand, a companion falls, and comes back at the Ashen Waystone.</param>
/// <param name="RevivePercent">Helped up, a companion stands with this share of their health.</param>
/// <param name="RegenDelayTicks">Out of a fight this long, a companion mends...</param>
/// <param name="RegenPerSecond">...this much health a second.</param>
public sealed record CompanionTuning(
    long FollowNearMm,
    long RunBeyondMm,
    long SprintBeyondMm,
    long CatchUpBeyondMm,
    int SnagTicks,
    long TrailStepMm,
    int TrailLength,
    long FightRadiusMm,
    long LeashMm,
    long GuardRadiusMm,
    int AttackPauseTicks,
    int ReviveWindowTicks,
    int RevivePercent,
    int RegenDelayTicks,
    int RegenPerSecond)
{
    /// <summary>A problem with the numbers, or null: the distances must nest, and every time and share be sensible.</summary>
    public string? Problem() =>
        !(0 < FollowNearMm && FollowNearMm < RunBeyondMm && RunBeyondMm < SprintBeyondMm && SprintBeyondMm < CatchUpBeyondMm)
            ? "the follow distances nest: 0 < follow_near < run_beyond < sprint_beyond < catch_up_beyond"
        : SnagTicks <= 0 || TrailStepMm <= 0 || TrailLength < 2 ? "the snag time, the trail step and the trail length are positive (at least two marks)"
        : FightRadiusMm <= 0 || LeashMm < FightRadiusMm || GuardRadiusMm <= 0 ? "the fight radius and the guard radius are positive, and the leash is at least the fight radius"
        : AttackPauseTicks < 0 || ReviveWindowTicks <= 0 || RevivePercent is < 1 or > 100 || RegenDelayTicks < 0 || RegenPerSecond < 0
            ? "the pause, the revive window, the revive share (1-100) and the mending are sensible"
        : null;
}

/// <summary>A companion's pure rules: how fast to go to keep up, and when to give up walking and catch up.</summary>
public static class CompanionRules
{
    /// <summary>Following at this distance: stand (null), walk, run or sprint.</summary>
    public static Gait? FollowGait(double distanceMm, CompanionTuning tuning) =>
        distanceMm <= tuning.FollowNearMm ? null
        : distanceMm > tuning.SprintBeyondMm ? Gait.Sprint
        : distanceMm > tuning.RunBeyondMm ? Gait.Run
        : Gait.Walk;

    /// <summary>Why a companion catches up, or null: too far behind, or snagged too long.</summary>
    public static string? CatchUp(double distanceMm, int stuckTicks, CompanionTuning tuning) =>
        distanceMm > tuning.CatchUpBeyondMm ? "distance" : stuckTicks >= tuning.SnagTicks ? "snag" : null;

    /// <summary>How a companion is, in words rather than numbers (content bible §19): healthy, wounded, critical or downed.</summary>
    public static string Condition(CompanionCondition condition, int health, int maxHealth) =>
        condition == CompanionCondition.Downed ? "downed"
        : health * 100 >= maxHealth * 75 ? "healthy"
        : health * 100 >= maxHealth * 30 ? "wounded"
        : "critical";
}

/// <summary>Saved and displayed names are stable snake_case keys, never enum ordinals.</summary>
public static class CompanionKeys
{
    public static string Key(CompanionOrder order) => order switch
    {
        CompanionOrder.Follow => "follow",
        CompanionOrder.Wait => "wait",
        _ => throw new ArgumentOutOfRangeException(nameof(order)),
    };

    public static string Key(CompanionCondition condition) => condition switch
    {
        CompanionCondition.Up => "up",
        CompanionCondition.Downed => "downed",
        _ => throw new ArgumentOutOfRangeException(nameof(condition)),
    };

    public static CompanionOrder? ParseOrder(string key) => key switch
    {
        "follow" => CompanionOrder.Follow,
        "wait" => CompanionOrder.Wait,
        _ => null,
    };

    public static CompanionCondition? ParseCondition(string key) => key switch
    {
        "up" => CompanionCondition.Up,
        "downed" => CompanionCondition.Downed,
        _ => null,
    };
}
