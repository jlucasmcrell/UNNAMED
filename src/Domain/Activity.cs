// UNNAMED Domain - the timed activity a character is committed to (fishing today; lockpicking and a mining swing later)
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
}

/// <summary>
/// One timed activity, from the tick it began to the tick it is done. <see cref="UntilTick"/> 0 means it runs until it is ended;
/// <see cref="TargetKey"/> is the thing it is worked against (a water), and <see cref="BiteTick"/> the moment within it that matters
/// (when a fish is on), 0 when the activity has none.
/// </summary>
public sealed record PlayerActivity(ActivityKind Kind, string TargetKey, long StartedTick, long UntilTick, long BiteTick);
