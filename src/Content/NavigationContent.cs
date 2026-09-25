// UNNAMED Content - navigation's tuning, and that every authored place can be reached (M7 design §3.16; D-13)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// Builds <see cref="NavConfig"/> from <c>config.navigation</c> - optional: a pack without it navigates by
/// <see cref="NavConfig.Default"/> - and lints it and the regions (NAV codes). NAV001-NAV003 and NAV007 are the configuration's own
/// rules (a malformed file is NAV007); NAV004 and NAV005 hold it to the body movement is built for; NAV006 floods every region from its
/// spawn, with every door and barrier passable, and requires every authored place to be reached.
/// </summary>
public static class NavigationContent
{
    public const string ConfigId = "config.navigation";

    /// <summary>How near a flooded node must be to a place a body goes to use or reach (the reach of a hand).</summary>
    public const long ReachMm = 1_600;

    /// <summary>How near a flooded node must be to a door's approach point.</summary>
    public const long ApproachMm = 250;

    /// <summary>How far a door's approach point lies beyond each face of the leaf.</summary>
    public const long ApproachBeyondMm = 700;

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
            return errors;   // WLD008 reports it
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
            catch (InvalidOperationException)
            {
                continue;   // the WLD codes report it
            }
            foreach (string place in Unreachable(layout, config))
                errors.Add(Error("NAV006", $"{region.Id}: {place} cannot be reached from the spawn", region.SourceFile));
        }
        return errors;
    }

    /// <summary>The configuration a simulation navigates by. Throws when it is malformed or breaks a rule of its own.</summary>
    public static NavConfig Build(ContentLoader loader)
    {
        var config = Parse(loader);
        return config.Problem() is { } problem ? throw new FormatException($"{ConfigId}: {problem.Code}: {problem.Message}") : config;
    }

    /// <summary>
    /// The authored places of a region that its spawn's flood does not reach (NAV006): with every door and barrier passable, a person
    /// flood from the spawn must come within a hand's reach of every NPC's place, container, station and resource node, and of every
    /// switch's structure; within 250 mm of both approach points of every door; and, for every location, within its discovery radius
    /// of the anchor, or within a hand's reach of the footprint the anchor lies in.
    /// </summary>
    public static IReadOnlyList<string> Unreachable(RegionLayout layout, NavConfig config)
    {
        var grid = NavigationLayout.Build(layout, config);
        var reach = NavReach.FromSpawn(grid, config, new NavPoint(layout.Spawn.XMm, layout.Spawn.ZMm));
        var missing = new List<string>();
        foreach (var npc in layout.Npcs.Where(n => !reach.Near(new NavPoint(n.XMm, n.ZMm), ReachMm)))
            missing.Add($"{npc.NpcId}'s place");
        foreach (var container in layout.Containers.Where(c => !reach.Near(new NavPoint(c.XMm, c.ZMm), ReachMm)))
            missing.Add(container.Key);
        foreach (var station in layout.Stations.Where(s => !reach.Near(new NavPoint(s.XMm, s.ZMm), ReachMm)))
            missing.Add(station.Key);
        foreach (var node in layout.Nodes.Where(n => !reach.Near(new NavPoint(n.XMm, n.ZMm), ReachMm)))
            missing.Add($"node {node.Name}");
        foreach (var sw in layout.Switches.Where(s => !reach.Near(s.Body, ReachMm)))
            missing.Add(sw.Key);
        foreach (var door in layout.Doors)
        {
            foreach (var (point, side) in ApproachPoints(door.ClosedFootprint).Select((p, i) => (p, i)))
            {
                if (!reach.Near(point, ApproachMm))
                    missing.Add($"{door.Key}'s {(side == 0 ? "first" : "second")} approach ({point.XMm}, {point.ZMm})");
            }
        }
        foreach (var location in layout.Locations)
        {
            var anchor = new NavPoint(location.XMm, location.ZMm);
            bool reached = reach.Near(anchor, location.DiscoveryRadiusMm)
                || layout.Space.Blockers.Any(b => NavGeometry.Within(b, anchor.XMm, anchor.ZMm, 0) && reach.Near(b, ReachMm));
            if (!reached)
                missing.Add(location.Id);
        }
        return missing;
    }

    /// <summary>A door's two approach points: its leaf's centre moved, along its thin axis, half its thickness and 700 mm each way.</summary>
    public static ImmutableArray<NavPoint> ApproachPoints(BoxBlocker leaf)
    {
        long width = leaf.MaxXMm - leaf.MinXMm, depth = leaf.MaxZMm - leaf.MinZMm;
        long cx = (leaf.MinXMm + leaf.MaxXMm) / 2, cz = (leaf.MinZMm + leaf.MaxZMm) / 2;
        return width <= depth
            ? ImmutableArray.Create(new NavPoint(cx - (width / 2 + ApproachBeyondMm), cz), new NavPoint(cx + (width / 2 + ApproachBeyondMm), cz))
            : ImmutableArray.Create(new NavPoint(cx, cz - (depth / 2 + ApproachBeyondMm)), new NavPoint(cx, cz + (depth / 2 + ApproachBeyondMm)));
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

    private static ValidationError Error(string code, string message, string? file) => new()
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

    /// <summary>
    /// The flood from the walkable node nearest <paramref name="from"/> (within 1 m, by squared distance, then j, then i). Nothing is
    /// reached when no node there is walkable.
    /// </summary>
    public static NavReach FromSpawn(NavGrid grid, NavConfig config, NavPoint from)
    {
        int person = config.Classes.Select((c, k) => (c, k)).Single(p => p.c.Id == "person").k;
        long width = grid.NodeMaxI - grid.NodeMinI + 1, height = grid.NodeMaxJ - grid.NodeMinJ + 1;
        var reached = new bool[width * height];
        bool Walk(long i, long j) => grid.SolidFitAt(i, j) > person;

        long ci = grid.NodeOf(from.XMm), cj = grid.NodeOf(from.ZMm);
        int snap = (int)(config.Limits.StartSnapMm / config.NodeMm);
        var seed = Enumerable.Range(-snap, 2 * snap + 1)
            .SelectMany(dj => Enumerable.Range(-snap, 2 * snap + 1).Select(di => (I: ci + di, J: cj + dj)))
            .Where(n => n.I >= grid.NodeMinI && n.I <= grid.NodeMaxI && n.J >= grid.NodeMinJ && n.J <= grid.NodeMaxJ && Walk(n.I, n.J))
            .OrderBy(n => Square(grid.CentreOf(n.I) - from.XMm) + Square(grid.CentreOf(n.J) - from.ZMm)).ThenBy(n => n.J).ThenBy(n => n.I)
            .Select(n => ((long I, long J)?)n).FirstOrDefault();
        if (seed is null)
            return new NavReach(grid, reached, width, 0);

        var queue = new long[width * height];
        int head = 0, tail = 0;
        long Index(long i, long j) => (j - grid.NodeMinJ) * width + (i - grid.NodeMinI);
        reached[Index(seed.Value.I, seed.Value.J)] = true;
        queue[tail++] = Index(seed.Value.I, seed.Value.J);
        var steps = new (int Di, int Dj)[] { (1, 0), (0, 1), (-1, 0), (0, -1) };
        while (head < tail)
        {
            long idx = queue[head++];
            long i = grid.NodeMinI + idx % width, j = grid.NodeMinJ + idx / width;
            foreach (var (di, dj) in steps)
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

    /// <summary>True when a reached node's centre lies within <paramref name="distanceMm"/> of the footprint.</summary>
    public bool Near(Blocker shape, long distanceMm)
    {
        var box = NavGeometry.Aabb(shape).Inflated(distanceMm);
        for (long j = _grid.NodeOf(box.MinZMm); j <= _grid.NodeOf(box.MaxZMm); j++)
        {
            for (long i = _grid.NodeOf(box.MinXMm); i <= _grid.NodeOf(box.MaxXMm); i++)
            {
                if (Reached(i, j) && NavGeometry.Within(shape, _grid.CentreOf(i), _grid.CentreOf(j), distanceMm))
                    return true;
            }
        }
        return false;
    }

    private static long Square(long v) => v * v;
}
