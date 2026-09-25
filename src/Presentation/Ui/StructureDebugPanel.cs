// UNNAMED Presentation - the structure and navigation debugger (M7, F2)
// Godot presentation only: it reads the simulation's views and holds no gameplay state (D-11)

using System.Text;
using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>F2's stages (M7 design §8.14): off, or the navigation grid. E5 adds the structures stage before it.</summary>
public enum BuildDebugStage { Off, Navigation }

/// <summary>
/// F2's panel (M7 design §8.14), a developer's view like the quest debugger: in E1 its navigation block alone - the grid's digest and
/// tiles, every gate and its state, the movers' routes, and the work counts - redrawn at most four times a second. The counts are read
/// here and in tests only; nothing in the game decides by them.
/// </summary>
public partial class StructureDebugPanel : CanvasLayer
{
    private readonly Label _text = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(1060, 0) };
    private double _since;
    private NavGrid? _digestOf;
    private string _digest = "";

    public override void _Ready()
    {
        Visible = false;
        var panel = new PanelContainer { Position = new Vector2(40, 130), CustomMinimumSize = new Vector2(1100, 0) };
        _text.AddThemeFontSizeOverride("font_size", 15);
        panel.AddChild(_text);
        AddChild(panel);
    }

    public void Refresh(Simulation simulation, double delta, bool now = false)
    {
        if (!Visible)
            return;
        _since += delta;
        if (!now && _since < 0.25)
            return;
        _since = 0;
        var view = simulation.Navigation;
        // The digest hashes every node: worked out once per grid, not four times a second.
        if (!ReferenceEquals(view.Grid, _digestOf))
        {
            _digestOf = view.Grid;
            _digest = view.Grid.Digest();
        }
        _text.Text = Render(view, _digest);
    }

    public static string Render(NavigationView view, string digest)
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
        text.Append($"  builds: {c.FullBuilds} full, {c.RectRebuilds} by rectangle; {c.TilesRestamped} tiles and {c.NodesRestamped} nodes stamped\n");
        text.Append($"  plans: {c.Plans} ({(c.PlansByOutcome.IsEmpty ? "none" : string.Join(", ", c.PlansByOutcome.Select(p => $"{p.Key} {p.Value}")))}); " +
                    $"{c.Expansions} expansions, at most {c.MaxExpansionsOneQuery} in one\n");
        text.Append($"  placement checks: {c.EditChecks}, refused {(c.EditRefusalsByRule.IsEmpty ? "none" : string.Join(", ", c.EditRefusalsByRule.Select(p => $"{p.Key} {p.Value}")))}; " +
                    $"{c.FloodNodes} nodes flooded\n");
        return text.ToString();
    }
}
