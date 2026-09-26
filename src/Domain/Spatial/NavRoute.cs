// UNNAMED Domain - a mover's committed route: points, rectangles and the route itself (M7 design §3.9; D-13)
// No Godot references - pure C#, integer only

using System.Collections.Immutable;

namespace UNNAMED.Domain.Spatial;

/// <summary>A point on the ground plane, in whole millimetres.</summary>
public readonly record struct NavPoint(long XMm, long ZMm);

/// <summary>An axis-aligned rectangle, inclusive on every side, in whole millimetres.</summary>
public readonly record struct NavRect(long MinXMm, long MinZMm, long MaxXMm, long MaxZMm)
{
    public bool Meets(NavRect other) =>
        MinXMm <= other.MaxXMm && other.MinXMm <= MaxXMm && MinZMm <= other.MaxZMm && other.MinZMm <= MaxZMm;

    public NavRect Inflated(long byMm) => new(MinXMm - byMm, MinZMm - byMm, MaxXMm + byMm, MaxZMm + byMm);
}

public enum NavRouteStatus { None, Active, Unreachable }

/// <summary>
/// The route a mover has committed to (M7 design §3.9): the goal it was planned to, the corners still to walk, when it was planned,
/// and the stamp of the planning window, so a change of geometry under it is noticed. It is mover state and is saved with the body it
/// moves. It exists only through its validating factories, so an invalid route can never be built; it compares by value.
/// </summary>
public sealed record NavRoute
{
    public const int MaxCorners = 32;

    private NavRoute(NavRouteStatus status, long goalXMm, long goalZMm, ImmutableArray<NavPoint> corners, long plannedTick, ulong stamp,
        NavRect watch, bool partial)
    {
        if (ProblemOf(status, goalXMm, goalZMm, corners, plannedTick, stamp, watch, partial) is { } problem)
            throw new ArgumentException(problem);
        Status = status;
        GoalXMm = goalXMm;
        GoalZMm = goalZMm;
        Corners = corners;
        PlannedTick = plannedTick;
        Stamp = stamp;
        Watch = watch;
        Partial = partial;
    }

    public NavRouteStatus Status { get; }
    public long GoalXMm { get; }
    public long GoalZMm { get; }
    public ImmutableArray<NavPoint> Corners { get; }
    public long PlannedTick { get; }
    public ulong Stamp { get; }
    public NavRect Watch { get; }
    public bool Partial { get; }

    /// <summary>No route: every field zero.</summary>
    public static NavRoute None { get; } = new(NavRouteStatus.None, 0, 0, ImmutableArray<NavPoint>.Empty, 0, 0, default, false);

    public static NavRoute Active(NavPoint goal, ImmutableArray<NavPoint> corners, long plannedTick, ulong stamp, NavRect watch, bool partial) =>
        new(NavRouteStatus.Active, goal.XMm, goal.ZMm, corners.IsDefault ? ImmutableArray<NavPoint>.Empty : corners, plannedTick, stamp, watch, partial);

    public static NavRoute Unreachable(NavPoint goal, long plannedTick, ulong stamp, NavRect watch) =>
        new(NavRouteStatus.Unreachable, goal.XMm, goal.ZMm, ImmutableArray<NavPoint>.Empty, plannedTick, stamp, watch, false);

    /// <summary>The route with its first <paramref name="count"/> corners walked. The last corner is never dropped.</summary>
    public NavRoute Advance(int count)
    {
        if (Status != NavRouteStatus.Active)
            throw new InvalidOperationException($"Only an active route advances; this one is {StatusKey(Status)}");
        if (count < 0)
            throw new ArgumentException($"A route cannot advance by {count} corners");
        var corners = count >= Corners.Length ? ImmutableArray<NavPoint>.Empty : Corners.RemoveRange(0, count);
        return new NavRoute(Status, GoalXMm, GoalZMm, corners, PlannedTick, Stamp, Watch, Partial);
    }

    /// <summary>The persisted key of a status.</summary>
    public static string StatusKey(NavRouteStatus status) => status switch
    {
        NavRouteStatus.None => "none",
        NavRouteStatus.Active => "active",
        NavRouteStatus.Unreachable => "unreachable",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown route status"),
    };

    /// <summary>Why these fields cannot be a route; null when they can.</summary>
    public static string? ProblemOf(NavRouteStatus status, long goalXMm, long goalZMm, ImmutableArray<NavPoint> corners, long plannedTick,
        ulong stamp, NavRect watch, bool partial)
    {
        int count = corners.IsDefault ? 0 : corners.Length;
        bool watchOrdered = watch.MinXMm <= watch.MaxXMm && watch.MinZMm <= watch.MaxZMm;
        return status switch
        {
            NavRouteStatus.None when goalXMm != 0 || goalZMm != 0 || count != 0 || plannedTick != 0 || stamp != 0 || watch != default || partial =>
                "no route has a zero goal, no corners, tick 0, stamp 0, a zero watch and is not partial",
            NavRouteStatus.None => null,
            NavRouteStatus.Active when count is < 1 or > MaxCorners => $"an active route has 1 to {MaxCorners} corners, not {count}",
            NavRouteStatus.Active when plannedTick < 0 => $"a route is planned at a tick of at least 0, not {plannedTick}",
            NavRouteStatus.Active when !watchOrdered => "a route's watch rectangle has its minimum at or below its maximum",
            NavRouteStatus.Active => null,
            NavRouteStatus.Unreachable when count != 0 => "an unreachable route has no corners",
            NavRouteStatus.Unreachable when partial => "an unreachable route is not partial",
            NavRouteStatus.Unreachable when plannedTick < 0 => $"a route is planned at a tick of at least 0, not {plannedTick}",
            NavRouteStatus.Unreachable when !watchOrdered => "a route's watch rectangle has its minimum at or below its maximum",
            NavRouteStatus.Unreachable => null,
            _ => $"unknown route status {(int)status}",
        };
    }

    public string? Problem() => ProblemOf(Status, GoalXMm, GoalZMm, Corners, PlannedTick, Stamp, Watch, Partial);

    /// <summary>The route's digest terms, in their one order (M7 design §3.9).</summary>
    public void AddTo(CanonicalHasher h)
    {
        h.Add(StatusKey(Status)).Add(GoalXMm).Add(GoalZMm).Add(PlannedTick).Add(Stamp)
            .Add(Watch.MinXMm).Add(Watch.MinZMm).Add(Watch.MaxXMm).Add(Watch.MaxZMm).Add(Partial).Add(Corners.Length);
        foreach (var corner in Corners)
            h.Add(corner.XMm).Add(corner.ZMm);
    }

    public bool Equals(NavRoute? other) =>
        other is not null && Status == other.Status && GoalXMm == other.GoalXMm && GoalZMm == other.GoalZMm && PlannedTick == other.PlannedTick
        && Stamp == other.Stamp && Watch == other.Watch && Partial == other.Partial && Corners.SequenceEqual(other.Corners);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Status);
        hash.Add(GoalXMm);
        hash.Add(GoalZMm);
        hash.Add(PlannedTick);
        hash.Add(Stamp);
        hash.Add(Watch);
        hash.Add(Partial);
        foreach (var corner in Corners)
            hash.Add(corner);
        return hash.ToHashCode();
    }
}
