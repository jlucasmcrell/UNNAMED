// UNNAMED Domain - guidance: how often hints may come, and when to offer help to a player who is stuck (1I, SYS-166)
// No Godot references - pure C#, so the budget and the stuck rule are testable without the engine

using System.Collections.Immutable;

namespace UNNAMED.Domain.Guidance;

/// <summary>
/// The budget hints are shown within. A hint is worth reading only if it is not competing with another, so they are spaced out
/// and capped in any stretch of play - the owner's complaint about tutorial spam was never about their words, it was about the
/// pace. Times are seconds on the session's clock.
/// </summary>
public static class HintPacing
{
    /// <summary>The least time between two hints.</summary>
    public const double MinGapSeconds = 12;

    /// <summary>The stretch the cap counts over.</summary>
    public const double WindowSeconds = 150;

    /// <summary>How many hints may show inside one stretch.</summary>
    public const int PerWindow = 3;

    /// <summary>
    /// Whether a hint may show at <paramref name="now"/>: the last one is old enough and the stretch's budget is not spent.
    /// <paramref name="shown"/> is every hint's time, oldest first.
    /// </summary>
    public static bool Allowed(double now, IReadOnlyList<double> shown)
    {
        if (shown.Count == 0)
            return true;
        if (now - shown[^1] < MinGapSeconds)
            return false;
        return shown.Count(at => now - at < WindowSeconds) < PerWindow;
    }
}

/// <summary>What the stuck rule looks at: the clock, when the player last made progress, and what they are in the middle of.</summary>
public readonly record struct StuckFrame(double Now, double LastProgress, bool PromptStanding, bool InConversationOrDowned);

/// <summary>
/// Stuck help (SYS-166): a player who has made no progress for a while gets one hint that may not be tied to a moment they have
/// not reached. Offered rarely - twice a session at most, minutes apart - and never while words are on screen or the player is
/// down, when a hint would be one more thing in the way. A prompt standing unfollowed for long enough counts as stuck sooner.
/// </summary>
public static class StuckHelp
{
    /// <summary>No progress for this long is stuck.</summary>
    public const double IdleSeconds = 75;

    /// <summary>The same, while a prompt the player has not followed stands in front of them.</summary>
    public const double PromptIdleSeconds = 30;

    /// <summary>The least time between two stuck offers.</summary>
    public const double GapSeconds = 240;

    /// <summary>How many a session offers.</summary>
    public const int MaxPerSession = 2;

    /// <summary>Whether to offer stuck help now; <paramref name="offered"/> is every offer's time, oldest first.</summary>
    public static bool Offer(StuckFrame frame, IReadOnlyList<double> offered)
    {
        if (offered.Count >= MaxPerSession)
            return false;
        if (frame.InConversationOrDowned)
            return false;
        double idle = frame.Now - frame.LastProgress;
        if (idle < (frame.PromptStanding ? PromptIdleSeconds : IdleSeconds))
            return false;
        return offered.Count == 0 || frame.Now - offered[^1] >= GapSeconds;
    }
}

/// <summary>
/// The hints a stuck player is offered: those waiting on no particular moment (<see cref="HintTriggers.Stuck"/>), then any unseen
/// hint at all - because the help a stuck player needs is often the one whose moment never came round.
/// </summary>
public static class StuckHints
{
    public static ImmutableArray<HintDefinition> Candidates(IEnumerable<HintDefinition> hints, IReadOnlySet<string> seen) =>
        hints.Where(hint => !seen.Contains(hint.Id))
            .OrderBy(hint => hint.Trigger == HintTriggers.Stuck ? 0 : 1)
            .ThenBy(hint => hint.Order)
            .ThenBy(hint => hint.Id, StringComparer.Ordinal)
            .ToImmutableArray();
}
