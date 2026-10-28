// UNNAMED Domain - the timed activity a character is committed to (fishing and lockpicking today; a mining swing later)
// No Godot references - this is pure C# domain logic

namespace UNNAMED.Domain;

/// <summary>What a timed activity is: one kind per commitment a body can be in, and can be interrupted out of.</summary>
public enum ActivityKind
{
    /// <summary>Nothing is running.</summary>
    None,

    /// <summary>A line is going out: the cast plays for its ticks, and the bite is set when it lands.</summary>
    FishCast,

    /// <summary>The line is in the water. Unbounded: nothing is caught by waiting, and only a reel takes anything.</summary>
    FishWaiting,

    /// <summary>A line is coming back: the reel plays for its ticks, and what was taken was taken when it began.</summary>
    FishReel,

    /// <summary>The picks are in a lock: the attempt holds for its ticks, and whether it turns is rolled when they run out.</summary>
    PickLock,
}

/// <summary>
/// One timed activity, from the tick it began to the tick it is done. <see cref="UntilTick"/> 0 means it runs until it is ended;
/// <see cref="TargetKey"/> is the thing it is worked against (a water), and <see cref="BiteTick"/> the moment within it that matters
/// (when a fish is on), 0 when the activity has none.
/// </summary>
public sealed record PlayerActivity(ActivityKind Kind, string TargetKey, long StartedTick, long UntilTick, long BiteTick);

/// <summary>
/// A lock's numbers, and nothing else: what a hand at the skill gets on a tier of lock, and where the lock's own record lives. A tier
/// is one to three (content refuses any other); a lock with no tier is no lock at all.
/// </summary>
public static class LockRules
{
    /// <summary>
    /// The chance one attempt turns: skill against the tier's own demand, so a tier 3 lock is a wall for a beginner and a delay for a
    /// master, and no hand is ever certain or hopeless.
    /// </summary>
    public static double Chance(int skillLevel,int tier) => Math.Clamp(0.38+(skillLevel-tier*12)*0.025,0.08,0.95);

    /// <summary>
    /// The world flag a picked lock is recorded in, named from the door's or container's own key: the cell delta already persists flags,
    /// so a lock that has been picked survives a save without the schema learning about locks. The flag is content - a tiered site's
    /// <c>lock_tier</c> is refused unless this flag is declared.
    /// </summary>
    public static string FlagOf(string siteKey) => $"world.lock.{siteKey}";
}
