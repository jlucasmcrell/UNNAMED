// UNNAMED Domain - following a planned route (M7 design §3.7.6, §3.8; D-13)
// No Godot references - pure C#, integer only

namespace UNNAMED.Domain.Spatial;

/// <summary>What a mover does this tick along its route.</summary>
public enum NavStepKind { Walk, Arrived, OpenGate, Unreachable }

/// <summary>
/// One tick of following: the route after it (replanned or advanced), what to do, the point to walk to, the gate to open, and - on a
/// replan tick only - why it replanned, how the plan ended and what it cost.
/// </summary>
public sealed record NavStep(NavRoute Route, NavStepKind Kind, NavPoint Target, string? GateKey, string? ReplanReason, NavOutcome? Outcome, int Expansions);

/// <summary>
/// Route following (M7 design §3.8), pure: replan on the first trigger that holds (§3.7.6), hold an unreachable route, advance past
/// corners reached or seen past, arrive, open the nearest closed door in reach that stands across the way, or walk to the current
/// corner. Every condition reads only the persisted route, the mover's persisted state, the current geometry and gates, and the tick.
/// </summary>
public static class NavFollower
{
    public const string None = "none";
    public const string Retry = "retry";
    public const string GoalMoved = "goal_moved";
    public const string Geometry = "geometry";
    public const string Blocked = "blocked";
    public const string Stuck = "stuck";
    public const string PartialEnd = "partial";
    public const string OffLine = "off_line";

    /// <summary>
    /// The next step for a mover of <paramref name="agent"/>'s class at <paramref name="body"/>, heading for <paramref name="goal"/>. A
    /// closed door is opened only within <paramref name="reachMm"/> of the body, as the character's own reach is measured.
    /// </summary>
    public static NavStep Next(NavQuery q, NavAgent agent, NavRoute route, NavPoint body, NavPoint goal, int stuckTicks, long tick, long reachMm)
    {
        var grid = q.Grid;
        var limits = q.Config.Limits;
        long r = grid.Config.Classes[agent.ClassIndex].RadiusMm;

        // 1-2. Replan, or hold an unreachable route until it may retry.
        string? reason = ReplanReason(q, agent, route, body, goal, stuckTicks, tick, r);
        if (reason is null && route.Status == NavRouteStatus.Unreachable)
            return new NavStep(route, NavStepKind.Unreachable, body, null, null, null, 0);
        NavOutcome? outcome = null;
        int expansions = 0;
        if (reason is not null)
        {
            var plan = NavSearch.Plan(q, agent, body, goal);
            outcome = plan.Outcome;
            expansions = plan.Expansions;
            ulong stamp = grid.WindowStamp(plan.Window);
            route = plan.Outcome == NavOutcome.Found
                ? NavRoute.Active(goal, plan.Corners.IsEmpty ? System.Collections.Immutable.ImmutableArray.Create(goal) : plan.Corners, tick, stamp, plan.Window, plan.Partial)
                : NavRoute.Unreachable(goal, tick, stamp, plan.Window);
        }
        if (route.Status == NavRouteStatus.Unreachable)
            return new NavStep(route, NavStepKind.Unreachable, body, null, reason, outcome, expansions);

        // 3. Advance past a corner reached, or one the body can already see past - at most four, never the last.
        long cornerReach2 = limits.CornerReachMm * limits.CornerReachMm;
        for (int n = 0; n < 4 && route.Corners.Length >= 2; n++)
        {
            if (Distance2(body, route.Corners[0]) > cornerReach2 && !NavSearch.Clear(grid, agent, q.IsGateOpen, body, route.Corners[1], r))
                break;
            route = route.Advance(1);
        }

        // 4. Arrived.
        var target = route.Corners[0];
        if (route.Corners.Length == 1 && Distance2(body, target) <= limits.ArriveMm * limits.ArriveMm)
            return new NavStep(route, NavStepKind.Arrived, target, null, reason, outcome, expansions);

        // 5. A closed door across the way, within reach: the nearest by (distance to its footprint, then its corner) - geometry, never the key.
        if (agent.OpensDoors && NearestDoorAcross(grid, q.IsGateOpen, body, target, r, reachMm) is { } door)
            return new NavStep(route, NavStepKind.OpenGate, target, door, reason, outcome, expansions);

        // 6. Walk.
        return new NavStep(route, NavStepKind.Walk, target, null, reason, outcome, expansions);
    }

    /// <summary>The first replan trigger that holds, in §3.7.6's order, or null. An unreachable route not yet due for a retry holds.</summary>
    public static string? ReplanReason(NavQuery q, NavAgent agent, NavRoute route, NavPoint body, NavPoint goal, int stuckTicks, long tick, long r)
    {
        var limits = q.Config.Limits;
        if (route.Status == NavRouteStatus.None)
            return None;
        if (route.Status == NavRouteStatus.Unreachable)
            return tick - route.PlannedTick >= limits.RetryTicks ? Retry : null;
        bool gapped = tick - route.PlannedTick >= limits.ReplanMinGapTicks;
        if (gapped && Distance2(goal, new NavPoint(route.GoalXMm, route.GoalZMm)) > limits.GoalMovedMm * limits.GoalMovedMm)
            return GoalMoved;
        if (q.Grid.WindowStamp(route.Watch) != route.Stamp)
            return Geometry;
        for (int i = 0; i + 1 < route.Corners.Length; i++)
        {
            if (!NavSearch.Clear(q.Grid, agent, q.IsGateOpen, route.Corners[i], route.Corners[i + 1], r))
                return Blocked;
        }
        if (stuckTicks > 0 && stuckTicks % limits.StuckReplanTicks == 0)
            return Stuck;
        if (route.Partial && route.Corners.Length == 1 && Distance2(body, route.Corners[0]) <= limits.CornerReachMm * limits.CornerReachMm)
            return PartialEnd;
        if (gapped && !NavSearch.Clear(q.Grid, agent, q.IsGateOpen, body, route.Corners[0], r - limits.OffLineToleranceMm))
            return OffLine;
        return null;
    }

    /// <summary>The closed door that stands across the body's way to the target and lies within reach, nearest first; null when none.</summary>
    private static string? NearestDoorAcross(NavGrid grid, Func<NavInput, bool> isGateOpen, NavPoint body, NavPoint target, long r, long reachMm)
    {
        string? best = null;
        (long Distance2, long MinX, long MinZ) bestKey = (long.MaxValue, long.MaxValue, long.MaxValue);
        var seen = new HashSet<NavInput>();
        foreach (var tile in grid.Tiles)
        {
            foreach (var input in tile.Inputs)
            {
                if (input.Kind != NavInputKind.Door || isGateOpen(input) || !seen.Add(input) || input.Shape is not BoxBlocker box)
                    continue;
                if (NavGeometry.SegmentClear(body, target, r, input.Shape))
                    continue;
                long d2 = NavGeometry.DistanceSquaredTo(box, body.XMm, body.ZMm);
                if (d2 > reachMm * reachMm)
                    continue;
                var key = (d2, box.MinXMm, box.MinZMm);
                if (key.CompareTo(bestKey) < 0)
                {
                    bestKey = key;
                    best = input.GateKey;
                }
            }
        }
        return best;
    }

    private static long Distance2(NavPoint a, NavPoint b)
    {
        long dx = a.XMm - b.XMm, dz = a.ZMm - b.ZMm;
        return dx * dx + dz * dz;
    }
}
