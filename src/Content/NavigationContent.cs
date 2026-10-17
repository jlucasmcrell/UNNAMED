// UNNAMED Content - navigation's tuning, and that every authored place can be reached (WP12, ported from the M7 design §3.16; D-13)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// Builds <see cref="NavConfig"/> from <c>config.navigation</c> - optional: a pack without it navigates by <see cref="NavConfig.Default"/>
/// - and lints it and the regions (NAV codes). NAV001-NAV003 and NAV007 are the configuration's own rules (a malformed file is NAV007);
/// NAV004 and NAV005 hold it to the body movement is built for; NAV006 floods every region from its spawn, with every door and barrier
/// passable, and requires every NPC's place to be reached - a body that cannot be reached cannot walk anywhere either.
/// </summary>
public static class NavigationContent
{
    public const string ConfigId = "config.navigation";

    /// <summary>How near a flooded node must be to a place a body goes to use or reach (the reach of a hand).</summary>
    public const long ReachMm = 1_600;

    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        string? file = loader.Definitions.GetValueOrDefault(ConfigId)?.SourceFile;
        NavConfig config;
        try
        {
            config = Parse(loader);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(Error("NAV007", $"{ConfigId} is malformed: {e.Message}", file));
            return errors;
        }
        if (config.Problem() is { } problem)
        {
            errors.Add(Error(problem.Code, $"{ConfigId}: {problem.Message}", file));
            return errors;
        }

        var regions = loader.GetByKind("region");
        if (regions.Count == 0)
            return errors;
        MovementRules movement;
        try
        {
            movement = WorldContent.BuildMovement(loader);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            return errors;   // the WLD codes report it
        }
        long person = config.Classes.Single(c => c.Id == "person").RadiusMm;
        if (person != movement.BodyRadiusMm)
            errors.Add(Error("NAV004", $"{ConfigId}: the person class's radius {person} mm must equal the body radius movement is built with, {movement.BodyRadiusMm} mm", file));
        if (config.AgentHeightMm != movement.StandHeightMm)
            errors.Add(Error("NAV005", $"{ConfigId}: agent_height_m {config.AgentHeightMm} mm must equal the standing height movement is built with, {movement.StandHeightMm} mm", file));

        foreach (var region in regions.Values.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            RegionLayout layout;
            try
            {
                layout = WorldContent.BuildLayout(loader, region.Id);
            }
            catch (Exception e) when (e is InvalidOperationException or FormatException or KeyNotFoundException or ArgumentException)
            {
                continue;   // the WLD codes report it
            }
            var reach = NavReach.FromSpawn(NavigationLayout.Build(layout, config), config, new NavPoint(layout.Spawn.XMm, layout.Spawn.ZMm));
            foreach (var npc in layout.Npcs.Where(n => !reach.Near(new NavPoint(n.XMm, n.ZMm), ReachMm)))
                errors.Add(Error("NAV006", $"{region.Id}: {npc.NpcId}'s place cannot be reached from the spawn", region.SourceFile));
        }
        return errors;
    }

    /// <summary>The configuration a simulation navigates by. Throws when it is malformed or breaks a rule of its own.</summary>
    public static NavConfig Build(ContentLoader loader)
    {
        var config = Parse(loader);
        return config.Problem() is { } problem ? throw new FormatException($"{ConfigId}: {problem.Code}: {problem.Message}") : config;
    }

    private static NavConfig Parse(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey(ConfigId))
            return NavConfig.Default;
        var map = Config(loader, ConfigId);
        int tickMs = WorldContent.TickMilliseconds(loader);
        var classes = List(map, "classes").Select((c, i) =>
        {
            var row = c as Dictionary<object, object> ?? throw new FormatException($"classes[{i}] must be a map");
            return new NavClass(Text(row, "id"), Mm(row, "radius_m"));
        }).ToImmutableArray();
        var limits = new NavLimits(
            WindowMarginMm: Mm(map, "window_margin_m"),
            WindowMaxMm: Mm(map, "window_max_m"),
            MaxExpansions: Int(map, "max_expansions"),
            ProbeNodes: Int(map, "probe_nodes"),
            MaxCorners: Int(map, "max_corners"),
            StartSnapMm: Mm(map, "start_snap_m"),
            GoalSnapMm: Mm(map, "goal_snap_m"),
            CornerReachMm: Mm(map, "corner_reach_m"),
            ArriveMm: Mm(map, "arrive_m"),
            OffLineToleranceMm: Mm(map, "off_line_tolerance_m"),
            ReplanMinGapTicks: ToTicks(Number(map, "replan_min_gap_s"), tickMs),
            RetryTicks: ToTicks(Number(map, "retry_s"), tickMs),
            StuckReplanTicks: ToTicks(Number(map, "stuck_replan_s"), tickMs),
            GoalMovedMm: Mm(map, "goal_moved_m"),
            BlockedViewTicks: ToTicks(Number(map, "blocked_view_s"), tickMs),
            SealLimitNodes: Int(map, "seal_limit_nodes"),
            EditWindowMarginMm: Mm(map, "edit_window_margin_m"));
        return new NavConfig(Mm(map, "node_m"), Mm(map, "margin_m"), Mm(map, "agent_height_m"), classes, limits);
    }

    internal static ValidationError Error(string code, string message, string? file) => new()
    {
        SeverityLevel = ValidationError.Severity.Error,
        Code = code,
        Message = message,
        FilePath = file ?? string.Empty,
    };
}

/// <summary>
/// The nodes a person reaches from a point by a 4-connected flood over the whole grid, with every door and barrier passable: the content
/// lint's view of what an authored region lets a body get to.
/// </summary>
public sealed class NavReach
{
    private static readonly ImmutableArray<(int Di, int Dj)> Steps = ImmutableArray.Create((1, 0), (0, 1), (-1, 0), (0, -1));

    private readonly NavGrid _grid;
    private readonly bool[] _reached;
    private readonly long _width;

    private NavReach(NavGrid grid, bool[] reached, long width, int count)
    {
        _grid = grid;
        _reached = reached;
        _width = width;
        Count = count;
    }

    /// <summary>How many nodes the flood reached.</summary>
    public int Count { get; }

    /// <summary>The flood from the walkable node nearest <paramref name="from"/> within the start snap. Nothing is reached when none is walkable.</summary>
    public static NavReach FromSpawn(NavGrid grid, NavConfig config, NavPoint from)
    {
        int person = config.Classes.Select((c, k) => (c, k)).Single(p => p.c.Id == "person").k;
        long width = grid.NodeMaxI - grid.NodeMinI + 1, height = grid.NodeMaxJ - grid.NodeMinJ + 1;
        var reached = new bool[width * height];
        bool Walk(long i, long j) => grid.SolidFitAt(i, j) > person;
        long Index(long i, long j) => (j - grid.NodeMinJ) * width + (i - grid.NodeMinI);

        long ci = grid.NodeOf(from.XMm), cj = grid.NodeOf(from.ZMm);
        int snap = (int)(config.Limits.StartSnapMm / config.NodeMm);
        (long I, long J)? seed = null;
        long best = long.MaxValue;
        for (long j = cj - snap; j <= cj + snap; j++)
        {
            for (long i = ci - snap; i <= ci + snap; i++)
            {
                long d2 = Square(grid.CentreOf(i) - from.XMm) + Square(grid.CentreOf(j) - from.ZMm);
                if (d2 < best && i >= grid.NodeMinI && i <= grid.NodeMaxI && j >= grid.NodeMinJ && j <= grid.NodeMaxJ && Walk(i, j))
                    (seed, best) = ((i, j), d2);
            }
        }
        if (seed is null)
            return new NavReach(grid, reached, width, 0);

        var queue = new long[width * height];
        int head = 0, tail = 0;
        reached[Index(seed.Value.I, seed.Value.J)] = true;
        queue[tail++] = Index(seed.Value.I, seed.Value.J);
        while (head < tail)
        {
            long idx = queue[head++];
            long i = grid.NodeMinI + idx % width, j = grid.NodeMinJ + idx / width;
            foreach (var (di, dj) in Steps)
            {
                long ni = i + di, nj = j + dj;
                if (ni < grid.NodeMinI || ni > grid.NodeMaxI || nj < grid.NodeMinJ || nj > grid.NodeMaxJ)
                    continue;
                long n = Index(ni, nj);
                if (reached[n] || !Walk(ni, nj))
                    continue;
                reached[n] = true;
                queue[tail++] = n;
            }
        }
        return new NavReach(grid, reached, width, tail);
    }

    public bool Reached(long i, long j) =>
        i >= _grid.NodeMinI && i <= _grid.NodeMaxI && j >= _grid.NodeMinJ && j <= _grid.NodeMaxJ
        && _reached[(j - _grid.NodeMinJ) * _width + (i - _grid.NodeMinI)];

    /// <summary>True when a reached node's centre lies within <paramref name="distanceMm"/> of the point.</summary>
    public bool Near(NavPoint p, long distanceMm)
    {
        for (long j = _grid.NodeOf(p.ZMm - distanceMm); j <= _grid.NodeOf(p.ZMm + distanceMm); j++)
        {
            for (long i = _grid.NodeOf(p.XMm - distanceMm); i <= _grid.NodeOf(p.XMm + distanceMm); i++)
            {
                if (Reached(i, j) && Square(_grid.CentreOf(i) - p.XMm) + Square(_grid.CentreOf(j) - p.ZMm) <= Square(distanceMm))
                    return true;
            }
        }
        return false;
    }

    private static long Square(long v) => v * v;
}
