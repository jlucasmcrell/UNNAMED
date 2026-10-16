// UNNAMED Presentation - Waystones you know: the stone the character stands at, where they return, and the journeys it offers (S-38)
// Godot presentation only: its buttons go through the controller's commands; nothing here holds state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.Domain.Travel;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The panel a known Waystone opens (OWNER OD-C16's wording, "Waystones you know"): a STANDARD sheet. First the stone itself - whether it
/// is the return point, and if not the one deliberate act that makes it so (binding is never a side effect of travel). Then every other
/// stone the character knows, each with what the journey costs in words before it is taken - the road's walk, and the world time that
/// passes instead - and Travel; refused journeys say why. It closes when the character walks away from the stone.
/// </summary>
public partial class WaystonePanel : CanvasLayer
{
    /// <summary>Further than this from the stone's edge, the panel closes: the stones go only from where the character stands.</summary>
    private const long KeepOpenMm = 3_000;

    private Sheet _sheet = null!;
    private GameSession _session = null!;
    private PlayerController _controller = null!;
    private HudIcons _icons = HudIcons.None;
    private string? _drawn;

    /// <summary>The stone the panel is open at, or null.</summary>
    public string? StoneKey { get; private set; }

    public void Bind(GameSession session, PlayerController controller)
    {
        _session = session;
        _controller = controller;
    }

    public void UseIcons(HudIcons icons)
    {
        _icons = icons;
        _sheet.SetIcon(icons.Binds("panel.waystones") ? icons.For("panel.waystones") : null);
        _drawn = null;
    }

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "Waystones you know", null);
        _sheet.OnShown(() =>
        {
            _drawn = null;
            Callable.From(() => UiKit.FirstFocusable(_sheet.Root)?.GrabFocus()).CallDeferred();
        });
    }

    /// <summary>Open at a stone the character knows.</summary>
    public void Open(string stoneKey)
    {
        StoneKey = stoneKey;
        _drawn = null;
        Visible = true;
    }

    public void Close()
    {
        Visible = false;
        StoneKey = null;
    }

    public override void _Process(double delta)
    {
        if (!Visible || _session?.Simulation is not { } simulation || StoneKey is not { } key)
            return;
        var here = simulation.Waystones.FirstOrDefault(w => w.Site.Key == key);
        var body = simulation.Player.Body;
        if (here is null || !here.Known || here.Site.Body.DistanceTo(body.XMm, body.ZMm) > KeepOpenMm)
        {
            Close();
            return;
        }
        var options = simulation.TravelOptions(key);
        string signature = $"{here.ReturnPoint}|{string.Join(",", simulation.Waystones.Where(w => w.ReturnPoint).Select(w => w.Site.Key))}|" +
                           string.Join(",", options.Select(o => $"{o.ToKey}:{o.Blocked}"));
        if (signature == _drawn)
            return;
        _drawn = signature;
        string? focused = UiKit.FocusedId(_sheet.Root);
        Build(simulation, here, options);
        if (!UiKit.Refocus(_sheet.Root, focused))
            UiKit.FirstFocusable(_sheet.Root)?.GrabFocus();
    }

    private void Build(Simulation simulation, WaystoneView here, IReadOnlyList<TravelOption> options)
    {
        UiKit.Clear(_sheet.Body);
        var stones = simulation.Waystones;
        string? returnTo = stones.FirstOrDefault(w => w.ReturnPoint)?.Site.Name;

        var stone = UiKit.Section(here.Site.Name, Names.Of(_session, here.Site.LocationId));
        var row = UiKit.Row(UiKit.Icon(_icons, here.ReturnPoint ? "travel.return_point" : "panel.waystones", 36), here.ReturnPoint ? "Your return point" : "Not your return point",
            here.ReturnPoint ? "When you fall, you come back here."
                : $"When you fall, you come back at {(returnTo is null ? "the Ashen Waystone" : $"the {returnTo}")}.");
        if (here.ReturnPoint)
            row.Root.AddChild(UiKit.Chip("Return point", HudStyle.Lichen));
        else
            row.Root.AddChild(UiKit.Action("Make this my return point", () => _controller.BindStone(here.Site.Key), UiKit.Kind.Primary, id: "bind"));
        stone.AddChild(row.Root);
        _sheet.Body.AddChild(stone);

        var travel = UiKit.Section("Travel from here", "you, and whoever follows you");
        if (options.Count == 0)
        {
            travel.AddChild(UiKit.EmptyState("No other stone yet", "Touch another Waystone, and it will answer here."));
        }
        else
        {
            if (options[0].Blocked is { } why)
                travel.AddChild(UiKit.Callout(HudStyle.Wound, why + "."));
            foreach (var option in options)
            {
                var to = stones.First(w => w.Site.Key == option.ToKey).Site;
                var journey = UiKit.Row(UiKit.Icon(_icons, "panel.waystones", 32), to.Name, TravelWords.Journey(option.Cost.WalkTicks, option.Cost.WorldTicks, Main.TicksPerDay));
                string toKey = to.Key;
                var go = UiKit.Action("Travel", () => _controller.TravelTo(here.Site.Key, toKey), id: $"travel/{toKey}");
                go.Disabled = option.Blocked is not null;
                journey.Root.AddChild(go);
                travel.AddChild(journey.Root);
            }
        }
        travel.AddChild(UiKit.Quiet("Companions told to wait stay where they wait.", HudStyle.Muted));
        _sheet.Body.AddChild(travel);
    }
}
