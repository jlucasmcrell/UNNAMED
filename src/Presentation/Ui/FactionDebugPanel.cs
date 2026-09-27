// UNNAMED Presentation - the faction debugger (M7, F6)
// Godot presentation only: it reads the simulation's views and holds no gameplay state (D-11)

using System.Globalization;
using System.Text;
using Godot;
using UNNAMED.Application;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>The words the character sees when a faction's regard changes (M7 design §8.13). The tier arrives in the event; nothing here derives one.</summary>
public static class FactionLines
{
    /// <summary>"The Survey: neutral (-100), told to Sel Arien".</summary>
    public static string Reported(ReputationChanged e, string factionName, string toldName)
    {
        string name = factionName.Length == 0 ? factionName : char.ToUpperInvariant(factionName[0]) + factionName[1..];
        int delta = e.To - e.From;
        return $"{name}: {e.TierTo} ({(delta >= 0 ? "+" : "")}{delta.ToString(CultureInfo.InvariantCulture)}), told to {toldName}";
    }
}

/// <summary>
/// F6's panel (M7 design §8.14): each faction as the character stands with it, the act log with what each faction knows of each act,
/// the service gates and what is on offer, and the last eight faction events. Read-only: the dialogue gate is observed in conversation,
/// never evaluated here (G2).
/// </summary>
public partial class FactionDebugPanel : CanvasLayer
{
    private readonly Label _text = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(1060, 0) };
    private readonly Queue<string> _recent = new();
    private double _since;

    public override void _Ready()
    {
        Visible = false;
        var panel = new PanelContainer { Position = new Vector2(40, 130), CustomMinimumSize = new Vector2(1100, 0) };
        _text.AddThemeFontSizeOverride("font_size", 15);
        panel.AddChild(_text);
        AddChild(panel);
    }

    /// <summary>A faction event, kept for display only: the last eight.</summary>
    public void Note(string line)
    {
        _recent.Enqueue(line);
        while (_recent.Count > 8)
            _recent.Dequeue();
    }

    public void Refresh(GameSession session, double delta, bool now = false)
    {
        if (!Visible || session.Simulation is not { } simulation)
            return;
        _since += delta;
        if (!now && _since < 0.25)
            return;
        _since = 0;
        _text.Text = Render(session, simulation, _recent);
    }

    public static string Render(GameSession session, Simulation simulation, IEnumerable<string> recent)
    {
        var text = new StringBuilder("FACTION DEBUG   [F6] close\n\n");
        var factions = simulation.Factions;
        string Name(string factionId) => factions.FirstOrDefault(f => f.Id == factionId)?.Name ?? factionId;
        foreach (var f in factions)
        {
            text.Append($"{f.Name}  {f.Id}  {f.Points}  {f.Tier} ({f.Level})  seat {f.SeatLocationId}  members ")
                .Append(string.Join(", ", f.Members.Select(session.DisplayName)))
                .Append(f.Relations.IsEmpty ? "" : "  " + string.Join("; ", f.Relations.Select(r => $"{Name(r.FactionId)}: {r.Attitude}")))
                .Append('\n');
        }

        var acts = simulation.Acts;
        text.Append($"\nACTS {acts.Length} / {session.Setup.Factions.LogCapacity}\n");
        foreach (var act in acts)
        {
            text.Append(CultureInfo.InvariantCulture, $" #{act.Seq} {act.Kind} {act.Subject}  {act.CellKey} ({act.XMm / 1000.0:0.0}, {act.ZMm / 1000.0:0.0}) tick {act.Tick}\n");
            foreach (var known in act.Known)
                text.Append($"    {Name(known.Knower)}  {known.Source} via {known.Via ?? "(none)"}  {known.Identity}  {(known.Delta >= 0 ? "+" : "")}{known.Delta}\n");
        }

        text.Append("\nGATES");
        foreach (var npc in session.Setup.Social.Npcs.Values.Where(n => n.MerchantId is not null))
        {
            var merchant = session.Setup.Items.Merchants[npc.MerchantId!];
            var wares = simulation.Wares(npc.Id);
            foreach (var row in merchant.Stock.Where(s => s.Requires is not null))
            {
                int onOffer = wares?.Wares.Where(w => w.ItemId == row.ItemId).Sum(w => w.Count) ?? 0;
                text.Append($"  {merchant.Id}  {row.ItemId}  requires {row.Requires!.FactionId} level >= {row.Requires.MinLevel}  -> on offer: {onOffer}");
            }
        }

        text.Append("\n\nRECENT\n");
        foreach (string line in recent)
            text.Append("  ").Append(line).Append('\n');
        return text.ToString();
    }
}
