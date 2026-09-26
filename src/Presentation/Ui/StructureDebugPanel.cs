// UNNAMED Presentation - the structure and navigation debugger (M7, F2)
// Godot presentation only: it reads the simulation's views and holds no gameplay state (D-11)

using System.Text;
using Godot;
using UNNAMED.Presentation.Player;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>F2's stages (M7 design §8.14): off, the structures, or the structures and the navigation grid.</summary>
public enum BuildDebugStage { Off, Structures, Navigation }

/// <summary>
/// F2's panel (M7 design §8.14), a developer's view like the quest debugger: its navigation block alone until E5 - the grid's digest and
/// tiles, every gate and its state, the movers' routes and the last one planned, and the work counts - redrawn at most four times a
/// second. The counts are read here and in tests only; nothing in the game decides by them.
/// </summary>
public partial class StructureDebugPanel : CanvasLayer
{
    private readonly Label _text = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(1060, 0) };
    private double _since;
    private NavGrid? _digestOf;
    private string _digest = "";

    /// <summary>The last route a mover planned, as the event told it; for display only.</summary>
    public RoutePlanned? LastRoute { get; set; }

    public override void _Ready()
    {
        Visible = false;
        var panel = new PanelContainer { Position = new Vector2(40, 130), CustomMinimumSize = new Vector2(1100, 0) };
        _text.AddThemeFontSizeOverride("font_size", 15);
        panel.AddChild(_text);
        AddChild(panel);
    }

    /// <summary>Which stage is shown: the structure block always, the navigation block at the second.</summary>
    public BuildDebugStage Stage { get; set; }

    public void Refresh(Simulation simulation, double delta, BuildMode? build = null, StructuresChanged? last = null, bool now = false)
    {
        if (!Visible)
            return;
        _since += delta;
        if (!now && _since < 0.25)
            return;
        _since = 0;
        var view = simulation.Navigation;
        string structures = Structures(simulation, build, last);
        if (Stage != BuildDebugStage.Navigation)
        {
            _text.Text = "STRUCTURE DEBUG   [F2] next stage\n\n" + structures;
            return;
        }
        // The digest hashes every node: worked out once per grid, not four times a second.
        if (!ReferenceEquals(view.Grid, _digestOf))
        {
            _digestOf = view.Grid;
            _digest = view.Grid.Digest();
        }
        _text.Text = Render(view, _digest, LastRoute).Replace("STRUCTURE DEBUG   [F2] next stage\n\n", "STRUCTURE DEBUG   [F2] off\n\n" + structures + "\n");
    }

    /// <summary>
    /// The structure block (M7 design §8.14): the revision and the pieces, each area's count against its room, the load audit, the last
    /// change, the ghost's last full ask with its reason and how long it took, the target, and the pieces within 12 m.
    /// </summary>
    public static string Structures(Simulation simulation, BuildMode? build, StructuresChanged? last)
    {
        var pieces = simulation.Pieces;
        var text = new StringBuilder($"STRUCTURES  revision {simulation.StructureRevision}, {pieces.Length} pieces\n");
        foreach (var area in simulation.Setup.Layout.BuildAreas)
        {
            int count = pieces.Count(p => p.XMm >= area.MinXMm && p.XMm <= area.MaxXMm && p.ZMm >= area.MinZMm && p.ZMm <= area.MaxZMm);
            text.Append($"  {area.Key}: {count}/{area.MaxPieces} pieces\n");
        }
        text.Append("  audit: ").Append(simulation.StructureAudit.IsEmpty ? "clean" : string.Join("; ", simulation.StructureAudit.Select(a => $"{a.Subject} {a.Problem}")))
            .Append('\n');
        text.Append("  last change: ").Append(last is { } c
            ? $"{c.Kind} ({c.MinXMm}, {c.MinZMm})-({c.MaxXMm}, {c.MaxZMm}), revision {c.Revision}, tick {c.Tick}" : "none").Append('\n');
        if (build is { Active: true, Asked: { } asked })
        {
            var p = asked.Preview;
            text.Append($"  ghost: {asked.DefId} at ({asked.Pose.X}, {asked.Pose.Z}) r{asked.Pose.R}: {(p.Allowed ? "allowed" : $"refused, {p.Failed}: {p.Reason}")}, " +
                        $"navigability {p.Navigability}; asked at revision {asked.Revision}; {build.PreviewMs:0.000} ms\n");
        }
        if (build is { Active: true, Target: { } target })
        {
            var slot = simulation.Setup.Building.Catalog.Find(target.DefId) is { } definition
                ? UNNAMED.Domain.Building.Lattice.SlotKey(definition.Slot, target.XMm, target.ZMm, target.Rotation) : "?";
            text.Append($"  target: {target.Id} {target.DefId} {slot} ({target.XMm}, {target.ZMm}) r{target.Rotation} owner {target.Owner} " +
                        $"health {target.HealthCurrent}/{target.HealthMax}\n");
        }
        var body = simulation.Player.Body;
        var near = pieces.Where(p => Math.Max(Math.Abs(p.XMm - body.XMm), Math.Abs(p.ZMm - body.ZMm)) <= 12_000).ToList();
        text.Append($"  within 12 m: {near.Count}\n");
        foreach (var p in near.Take(12))
            text.Append($"    {p.Id.Value[^6..]} {p.DefId} ({p.XMm}, {p.ZMm}) r{p.Rotation} {p.HealthCurrent}/{p.HealthMax}\n");
        return text.ToString();
    }

    public static string Render(NavigationView view, string digest, RoutePlanned? lastRoute = null)
    {
        var grid = view.Grid;
        var c = view.Counters;
        var text = new StringBuilder("STRUCTURE DEBUG   [F2] next stage\n\n");
        text.Append($"NAVIGATION  grid {digest[..Math.Min(digest.Length, 23)]}...  {grid.Tiles.Length} tiles, {grid.Config.NodeMm} mm nodes, ")
            .Append(string.Join(", ", grid.Config.Classes.Select(k => $"{k.Id} {k.RadiusMm} mm"))).Append('\n');
        text.Append("  gates: ").Append(view.Gates.IsEmpty ? "none" : string.Join("; ", view.Gates.Select(g => $"{g.Key} ({g.Kind}) {(g.Kind == "barrier" ? (g.Open ? "lifted" : "standing") : g.Open ? "open" : "shut")}")))
            .Append('\n');
        text.Append("  movers: ").Append(view.Movers.IsEmpty ? "none" : "").Append('\n');
        foreach (var mover in view.Movers)
        {
            var r = mover.Route;
            text.Append($"    {mover.NpcId}: {NavRoute.StatusKey(r.Status)} to ({r.GoalXMm}, {r.GoalZMm}), {r.Corners.Length} corners{(r.Partial ? " (partial)" : "")}, " +
                        $"planned at tick {r.PlannedTick}{(mover.Blocked ? ", BLOCKED" : "")}\n");
        }
        text.Append("  last route planned: ").Append(lastRoute is { } p
            ? $"{p.MoverKey} {p.Outcome} ({p.Reason}), {p.Corners} corners, {p.Expansions} expansions, tick {p.Tick}" : "none").Append('\n');
        text.Append($"  builds: {c.FullBuilds} full, {c.RectRebuilds} by rectangle; {c.TilesRestamped} tiles and {c.NodesRestamped} nodes stamped\n");
        text.Append($"  plans: {c.Plans} ({(c.PlansByOutcome.IsEmpty ? "none" : string.Join(", ", c.PlansByOutcome.Select(p => $"{p.Key} {p.Value}")))}); " +
                    $"{c.Expansions} expansions, at most {c.MaxExpansionsOneQuery} in one\n");
        text.Append($"  placement checks: {c.EditChecks}, refused {(c.EditRefusalsByRule.IsEmpty ? "none" : string.Join(", ", c.EditRefusalsByRule.Select(p => $"{p.Key} {p.Value}")))}; " +
                    $"{c.FloodNodes} nodes flooded\n");
        return text.ToString();
    }
}
