// UNNAMED Presentation - the loot window: what a chest, a body or a pile holds, beside it, with the world and the HUD still in view
// Godot presentation only: every button submits a command; nothing here changes state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// A small window at the right edge for a container being searched: its stacks with Take on each, Take all and Close. The full
/// inventory opens only on its own key. Drawn again after every item event; closed by <see cref="Main"/> when the body walks out of reach.
/// </summary>
public partial class LootPanel : CanvasLayer
{
    private const float Width = 360;

    private readonly VBoxContainer _rows = new();
    private readonly VBoxContainer _head = new();
    private GameSession _session = null!;

    public string? OpenContainer { get; private set; }

    public (long XMm, long ZMm)? Anchor { get; private set; }

    public void Bind(GameSession session) => _session = session;

    public override void _Ready()
    {
        Visible = false;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiKit.Theme };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(Width, 0), MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", HudStyle.PanelBox());
        panel.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        panel.GrowHorizontal = Control.GrowDirection.Begin;
        panel.GrowVertical = Control.GrowDirection.Both;
        panel.OffsetLeft = -(Width + 32);
        panel.OffsetRight = -32;
        root.AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        panel.AddChild(column);
        column.AddChild(_head);
        var scroll = UiKit.Scroll();
        scroll.CustomMinimumSize = new Vector2(0, 0);
        _rows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _rows.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_rows);
        column.AddChild(scroll);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        buttons.AddChild(UiKit.Action("Take all", TakeAll, UiKit.Kind.Primary, id: "loot/take_all"));
        if (UiKit.KeyCap("take_all") is { } cap)
            buttons.AddChild(cap);
        buttons.AddChild(UiKit.Action("Close", Close, id: "loot/close"));
        column.AddChild(buttons);
    }

    public void Open(string container)
    {
        OpenContainer = container;
        Anchor = _session.Simulation?.Containers.FirstOrDefault(c => c.Site.Key == container)?.Site is { } site ? (site.XMm, site.ZMm) : null;
        Visible = true;
        Refresh();
    }

    public void Close()
    {
        OpenContainer = null;
        Anchor = null;
        Visible = false;
    }

    public void TakeAll()
    {
        if (OpenContainer is { } key && _session.Simulation is { } simulation)
            _session.Submit(new TakeAllCommand(simulation.PlayerId, key));
    }

    public void Refresh()
    {
        if (!Visible || OpenContainer is not { } key || _session.Simulation is not { } simulation)
            return;
        UiKit.Clear(_head);
        UiKit.Clear(_rows);
        if (simulation.Containers.FirstOrDefault(c => c.Site.Key == key) is not { } view)
            return;
        _head.AddChild(UiKit.Section(Names.Place(_session, key), $"{view.Items.Length} / {view.Site.StackSlots} stacks"));
        if (view.Items.Length == 0)
            _rows.AddChild(UiKit.Quiet("Nothing left in it."));
        foreach (var item in view.Items)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var name = UiKit.Words($"{Names.Item(_session, item.DefId, item.Quality)}{(item.Count > 1 ? $" x{item.Count}" : "")}", 18);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(name);
            row.AddChild(UiKit.Action("Take", () => _session.Submit(new MoveItemCommand(simulation.PlayerId, item.Ref, ItemPlace.In(key), ItemPlace.Carried, item.Count)),
                compact: true, id: $"loot/{item.Ref}"));
            _rows.AddChild(row);
        }
    }
}
