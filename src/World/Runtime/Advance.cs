// UNNAMED World - the abstract advance: the world going on while the character is away (WP11; canon PERSISTENCE.md T-22)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.World.Runtime;

/// <summary>
/// A system that can bring itself from one world tick to a later one without being stepped tick by tick: the offline catch-up
/// (SYSTEMS_COMPLETENESS_PLAN §2.12). Each implementation is either closed-form, or stepped at a fixed abstract step whose boundaries are
/// absolute ticks (<see cref="AbstractAdvance.Steps"/>), so that <c>Advance(a, c)</c> leaves the same state as <c>Advance(a, b)</c> then
/// <c>Advance(b, c)</c> for every <c>b</c> between them (T-22). The clock has already moved to <paramref name="toTick"/> when a system is
/// called. A system logs only what the character could know.
/// </summary>
public interface IAbstractAdvance
{
    void Advance(long fromTick, long toTick, AdvanceLog log);
}

/// <summary>One thing that happened while the character was away: what kind of thing, what it concerns, and how much.</summary>
public sealed record AdvanceLine(long Tick, string Kind, string Ref, long Amount = 1);

/// <summary>The kinds of <see cref="AdvanceLine"/> the systems write. Presentation words them; the domain does not.</summary>
public static class AdvanceKinds
{
    /// <summary>World ticks that passed; <see cref="AdvanceLine.Amount"/> is the count.</summary>
    public const string TimePassed = "time.passed";

    /// <summary>Midnights the world clock passed (the calendar's day changes); <see cref="AdvanceLine.Amount"/> is the count.</summary>
    public const string DaysPassed = "time.days";

    /// <summary>An effect on the character ran out; <see cref="AdvanceLine.Ref"/> is the effect.</summary>
    public const string EffectExpired = "effect.expired";
}

/// <summary>What one advance did that the character could know, in the order the systems wrote it (§2.12 item 7).</summary>
public sealed class AdvanceLog
{
    private readonly List<AdvanceLine> _lines = new();

    public AdvanceLog(long fromTick, long toTick)
    {
        if (toTick < fromTick)
            throw new ArgumentOutOfRangeException(nameof(toTick), toTick, "An advance never runs backwards");
        FromTick = fromTick;
        ToTick = toTick;
    }

    public long FromTick { get; }
    public long ToTick { get; }
    public long Ticks => ToTick - FromTick;

    public IReadOnlyList<AdvanceLine> Lines => _lines;

    public void Add(AdvanceLine line) => _lines.Add(line ?? throw new ArgumentNullException(nameof(line)));

    /// <summary>The last <paramref name="count"/> lines: what a save keeps of the log.</summary>
    public ImmutableArray<AdvanceLine> Tail(int count) => _lines.Skip(Math.Max(0, _lines.Count - count)).ToImmutableArray();
}

/// <summary>The world clock's reading of a tick: the day it falls in and where in that day (config.time).</summary>
public sealed record WorldClock(long TicksPerDay, long StartTicks)
{
    /// <summary>A 48-minute day at 20 Hz starting at midnight: what a setup has before content gives it one.</summary>
    public static readonly WorldClock Default = new(57_600, 0);

    /// <summary>The calendar day a tick falls in, counting from the world's first (day 0).</summary>
    public long Day(long tick) => (tick + StartTicks) / TicksPerDay;

    /// <summary>Midnights passed going from one tick to a later one.</summary>
    public long DaysCrossed(long fromTick, long toTick) => Day(toTick) - Day(fromTick);
}

/// <summary>
/// How the world treats an absence (config.time): the most of it that is honoured, in world ticks (DN1: one Reckoning), and how long after
/// a login creatures do not take notice of the character (§2.12 item 6).
/// </summary>
public sealed record OfflineRules(long CapTicks, long LoginGraceTicks)
{
    /// <summary>No catch-up and no grace: a content pack that declares neither.</summary>
    public static readonly OfflineRules None = new(0, 0);
}

/// <summary>The framework's helpers for <see cref="IAbstractAdvance"/> implementations.</summary>
public static class AbstractAdvance
{
    /// <summary>
    /// The abstract step boundaries in (<paramref name="fromTick"/>, <paramref name="toTick"/>]: the multiples of
    /// <paramref name="stepTicks"/>. They are absolute ticks, so a span split anywhere visits exactly the boundaries the whole span does -
    /// what makes a stepped system chunk-invariant.
    /// </summary>
    public static IEnumerable<long> Steps(long fromTick, long toTick, long stepTicks)
    {
        if (stepTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(stepTicks), stepTicks, "A step is at least one tick");
        for (long boundary = (fromTick / stepTicks + 1) * stepTicks; boundary <= toTick; boundary += stepTicks)
            yield return boundary;
    }

    /// <summary>Run systems in order over one span: the order is data, fixed by the caller.</summary>
    public static void Run(long fromTick, long toTick, IEnumerable<IAbstractAdvance> systems, AdvanceLog log)
    {
        foreach (var system in systems)
            system.Advance(fromTick, toTick, log);
    }
}
