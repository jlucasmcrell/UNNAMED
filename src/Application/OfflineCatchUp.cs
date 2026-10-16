// UNNAMED Application - how much of an absence the world catches up (WP11; SYSTEMS_COMPLETENESS_PLAN §2.12, DN1)
// No Godot references - pure C#

namespace UNNAMED.Application;

/// <summary>
/// An absence in world ticks: what the wall clock asked for, and what the world honours - the request capped at one Reckoning (DN1).
/// A longer absence counts as exactly the cap.
/// </summary>
public readonly record struct CatchUp(long RequestedTicks, long HonouredTicks)
{
    public static readonly CatchUp None = new(0, 0);

    /// <summary>The absence was longer than the world catches up: "a long time has passed".</summary>
    public bool Capped => RequestedTicks > HonouredTicks;
}

/// <summary>
/// The real-time bridge's arithmetic. Pure: the wall clock is read in <see cref="GameSession"/> and passed in, so the domain only ever
/// receives a tick count (canon SYSTEMS.md: the domain never reads the wall clock).
/// </summary>
public static class OfflineCatchUp
{
    /// <summary>
    /// <c>requested = max(0, now - saved) x ticks per second</c>, and <c>honoured = min(requested, cap)</c>. A clock that reads earlier
    /// than the save - set back, or another machine's - asks for nothing.
    /// </summary>
    public static CatchUp Compute(DateTimeOffset savedUtc, DateTimeOffset nowUtc, int ticksPerSecond, long capTicks)
    {
        if (ticksPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(ticksPerSecond), ticksPerSecond, "The world ticks at least once a second");
        if (nowUtc <= savedUtc)
            return CatchUp.None;
        long elapsed = (nowUtc - savedUtc).Ticks;
        // Whole seconds and the remainder apart, so a clock decades out cannot overflow; anything past the cap is not honoured anyway.
        long seconds = elapsed / TimeSpan.TicksPerSecond;
        long requested = seconds > long.MaxValue / ticksPerSecond
            ? long.MaxValue
            : seconds * ticksPerSecond + elapsed % TimeSpan.TicksPerSecond * ticksPerSecond / TimeSpan.TicksPerSecond;
        return new CatchUp(requested, Math.Min(requested, Math.Max(0, capTicks)));
    }
}
