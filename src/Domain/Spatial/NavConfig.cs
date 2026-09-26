// UNNAMED Domain - the tuning of navigation (M7 design §3.3, §3.16; D-13)
// No Godot references - pure C#, integer only

using System.Collections.Immutable;

namespace UNNAMED.Domain.Spatial;

/// <summary>A body size navigation plans for: a class fits a node when a circle of its planning radius there overlaps nothing.</summary>
public sealed record NavClass(string Id, long RadiusMm);

/// <summary>
/// The limits of search, following and the placement check (M7 design §3.16), in millimetres and ticks. Nothing here is saved, so
/// retuning it never locks a save.
/// </summary>
public sealed record NavLimits(
    long WindowMarginMm,
    long WindowMaxMm,
    int MaxExpansions,
    int ProbeNodes,
    int MaxCorners,
    long StartSnapMm,
    long GoalSnapMm,
    long CornerReachMm,
    long ArriveMm,
    long OffLineToleranceMm,
    int ReplanMinGapTicks,
    int RetryTicks,
    int StuckReplanTicks,
    long GoalMovedMm,
    int BlockedViewTicks,
    int SealLimitNodes,
    long EditWindowMarginMm);

/// <summary>
/// Navigation's lattice (M7 design §3.3): nodes every <see cref="NodeMm"/> on global indices, one tile per 100 m cell, and the body
/// classes planned for, each at its radius plus <see cref="MarginMm"/>. Built from <c>config.navigation</c>, or <see cref="Default"/>
/// when a pack has none.
/// </summary>
public sealed record NavConfig(long NodeMm, long MarginMm, long AgentHeightMm, ImmutableArray<NavClass> Classes, NavLimits Limits)
{
    /// <summary>A node farther than this from an input's bounds is never affected by it: every planning radius is at most this.</summary>
    public const long InfluenceMm = 600;

    /// <summary>A tile is one 100 m cell.</summary>
    public const long TileMm = 100_000;

    /// <summary>The scratch is sized for this many expansions at most.</summary>
    public const int ExpansionCeiling = 65_536;

    /// <summary>A flood holds at most this many nodes.</summary>
    public const int FloodCeiling = 16_384;

    /// <summary>The widest planning window.</summary>
    public const long WindowCeilingMm = 128_000;

    /// <summary>The shipped tuning: the values of <c>content/config/navigation.yaml</c>, at 20 ticks a second.</summary>
    public static NavConfig Default { get; } = new(250, 50, 1_800, ImmutableArray.Create(new NavClass("person", 350)),
        new NavLimits(WindowMarginMm: 20_000, WindowMaxMm: 128_000, MaxExpansions: 65_536, ProbeNodes: 2_048, MaxCorners: 32,
            StartSnapMm: 1_000, GoalSnapMm: 2_000, CornerReachMm: 400, ArriveMm: 300, OffLineToleranceMm: 50, ReplanMinGapTicks: 10,
            RetryTicks: 40, StuckReplanTicks: 20, GoalMovedMm: 2_000, BlockedViewTicks: 200, SealLimitNodes: 16_384,
            EditWindowMarginMm: 32_000));

    /// <summary>Nodes along one side of a tile.</summary>
    public long TileNodes => TileMm / NodeMm;

    /// <summary>The planning radius of class <paramref name="k"/>: its body radius and the margin.</summary>
    public long RpMm(int k) => Classes[k].RadiusMm + MarginMm;

    /// <summary>The smallest class whose radius holds a body of this radius; -1 when none does.</summary>
    public int ClassFor(long radiusMm)
    {
        for (int k = 0; k < Classes.Length; k++)
        {
            if (Classes[k].RadiusMm >= radiusMm)
                return k;
        }
        return -1;
    }

    /// <summary>The first lint this configuration breaks (NAV001-NAV003, NAV007), with its code; null when it breaks none.</summary>
    public (string Code, string Message)? Problem()
    {
        if (NodeMm < 50 || TileMm % NodeMm != 0 || NodeMm % 2 != 0)
            return ("NAV001", $"node_m must divide 100 m exactly, be a whole even number of millimetres and be at least 50 mm; it is {NodeMm} mm");
        if (Classes.IsDefaultOrEmpty)
            return ("NAV002", "at least one class is needed, and one of them is 'person'");
        if (Classes.Length > byte.MaxValue)
            return ("NAV002", $"at most {byte.MaxValue} classes fit a node's count");
        if (Classes.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != Classes.Length)
            return ("NAV002", "two classes share an id");
        for (int k = 0; k < Classes.Length; k++)
        {
            if (Classes[k].RadiusMm <= 0 || (k > 0 && Classes[k].RadiusMm <= Classes[k - 1].RadiusMm))
                return ("NAV002", "class radii must be positive and strictly ascending");
            if (Classes[k].RadiusMm + MarginMm > InfluenceMm)
                return ("NAV002", $"class {Classes[k].Id}'s planning radius {Classes[k].RadiusMm + MarginMm} mm exceeds the {InfluenceMm} mm influence radius");
        }
        if (!Classes.Any(c => c.Id == "person"))
            return ("NAV002", "a class named 'person' is needed");
        if (MarginMm < 0)
            return ("NAV003", $"margin_m must not be negative; it is {MarginMm} mm");
        foreach (var c in Classes)
        {
            long rp = c.RadiusMm + MarginMm;
            if (2 * rp * rp < 2 * c.RadiusMm * c.RadiusMm + NodeMm * NodeMm)
                return ("NAV003", $"class {c.Id}: (r + margin)^2 must be at least r^2 + node^2 / 2, so every lattice edge is walkable by the body");
        }
        var l = Limits;
        if (l.WindowMaxMm > WindowCeilingMm)
            return ("NAV007", $"window_max_m must be at most {WindowCeilingMm / 1000} m");
        if (2 * l.WindowMarginMm + l.StartSnapMm + l.GoalSnapMm >= l.WindowMaxMm)
            return ("NAV007", "2 x window_margin_m + start_snap_m + goal_snap_m must be below window_max_m");
        if (l.MaxExpansions is < 1 or > ExpansionCeiling)
            return ("NAV007", $"max_expansions must be in 1..{ExpansionCeiling}");
        if (l.ProbeNodes < 1 || l.ProbeNodes > l.SealLimitNodes || l.SealLimitNodes > FloodCeiling)
            return ("NAV007", $"1 <= probe_nodes <= seal_limit_nodes <= {FloodCeiling} must hold");
        if (l.MaxCorners is < 1 or > NavRoute.MaxCorners)
            return ("NAV007", $"max_corners must be in 1..{NavRoute.MaxCorners}");
        if (AgentHeightMm <= 0 || l.WindowMarginMm <= 0 || l.WindowMaxMm <= 0 || l.StartSnapMm <= 0 || l.GoalSnapMm <= 0 || l.CornerReachMm <= 0
            || l.ArriveMm <= 0 || l.OffLineToleranceMm <= 0 || l.GoalMovedMm <= 0 || l.EditWindowMarginMm <= 0)
            return ("NAV007", "every distance must be greater than 0");
        if (l.ReplanMinGapTicks < 1 || l.RetryTicks < 1 || l.StuckReplanTicks < 1 || l.BlockedViewTicks < 1)
            return ("NAV007", "every time must be at least one tick");
        return null;
    }

    /// <summary>What a grid's bytes depend on besides its inputs: the lattice, the influence radius, the classes and the agent height.</summary>
    public string Digest()
    {
        using var h = new CanonicalHasher();
        h.Add("unnamed.nav-config/v1").Add(NodeMm).Add(MarginMm).Add(InfluenceMm).Add(AgentHeightMm).Add(Classes.Length);
        foreach (var c in Classes)
            h.Add(c.Id).Add(c.RadiusMm);
        return h.Finish();
    }
}
