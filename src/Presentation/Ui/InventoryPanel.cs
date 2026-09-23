// UNNAMED Presentation - the inventory and container panels (PROTOTYPE.md §4.1 UI panels: inventory, equipment)
// Godot presentation only: every button submits a command; nothing here changes state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Items;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// What the character carries and wears, and - when a container is open - what it holds. Each row's buttons submit the
/// same commands the headless tests do; the panel redraws from the simulation's views after every item event.
/// </summary>
public partial class InventoryPanel : CanvasLayer
{
    private readonly VBoxContainer _carried = new();
    private readonly VBoxContainer _container = new();
    private readonly Label _header = new();
    private readonly Label _containerHeader = new();
    private readonly PanelContainer _containerPanel = new();
    private GameSession _session = null!;

    /// <summary>The container open alongside the inventory, if any.</summary>
    public string? OpenContainer { get; private set; }

    public void Bind(GameSession session) => _session = session;

    public override void _Ready()
    {
        Visible = false;
        var root = new HBoxContainer { Position = new Vector2(60, 120) };
        root.AddThemeConstantOverride("separation", 24);
        AddChild(root);
        root.AddChild(Panel("Carried", _header, _carried, new Vector2(620, 0)));
        _containerPanel.AddChild(Column(_containerHeader, _container));
        _containerPanel.CustomMinimumSize = new Vector2(460, 0);
        root.AddChild(_containerPanel);
    }

    public void Open(string? container)
    {
        OpenContainer = container;
        Visible = true;
        Refresh();
    }

    public void Close()
    {
        OpenContainer = null;
        Visible = false;
    }

    public void Refresh()
    {
        if (!Visible || _session.Simulation is not { } simulation)
            return;
        var player = simulation.Player;
        var catalog = simulation.Setup.Items.Catalog;
        _header.Text = $"Carried {player.CarriedGrams / 1000.0:0.##} / {player.CarryLimitGrams / 1000.0:0.##} kg   " +
                       $"{player.Inventory.Length} / {simulation.Setup.Items.Inventory.StackSlots} stacks   " +
                       $"Coin {player.Currency}   Armor {player.Armor}";
        Clear(_carried);
        foreach (var entry in player.Inventory.OrderBy(e => _session.DisplayName(e.DefId), StringComparer.Ordinal))
        {
            var definition = catalog.Find(entry.DefId);
            var slot = player.Equipment.FirstOrDefault(kv => kv.Value == entry.ItemId);
            bool equipped = player.Equipment.ContainsValue(entry.ItemId);
            var row = Row($"{_session.DisplayName(entry.DefId)}{(entry.Count > 1 ? $" x{entry.Count}" : "")}" +
                          (equipped ? $"   [{EquipSlots.Key(slot.Key)}]" : ""));
            if (definition?.Slot is not null)
            {
                row.AddChild(Button(equipped ? "Unequip" : "Equip", () => Submit(equipped
                    ? new UnequipCommand(simulation.PlayerId, slot.Key)
                    : new EquipCommand(simulation.PlayerId, entry.ItemId))));
            }
            if (simulation.Setup.Combat.UseEffects.ContainsKey(entry.DefId))
                row.AddChild(Button("Use", () => Submit(new UseItemCommand(simulation.PlayerId, entry.ItemId))));
            if (simulation.Setup.Magic.Teaches.ContainsKey(entry.DefId))
                row.AddChild(Button("Read", () => Submit(new UseItemCommand(simulation.PlayerId, entry.ItemId))));
            if (OpenContainer is { } key)
                row.AddChild(Button("Put", () => Submit(new MoveItemCommand(simulation.PlayerId, entry.ItemId.Value, ItemPlace.Carried, ItemPlace.In(key), entry.Count))));
            row.AddChild(Button("Drop", () => Submit(new MoveItemCommand(simulation.PlayerId, entry.ItemId.Value, ItemPlace.Carried, ItemPlace.Ground, entry.Count))));
            _carried.AddChild(row);
        }

        _containerPanel.Visible = OpenContainer is not null;
        Clear(_container);
        if (OpenContainer is not { } open || simulation.Containers.FirstOrDefault(c => c.Site.Key == open) is not { } view)
            return;
        _containerHeader.Text = $"{Main.Describe(_session, open)}   {view.Items.Length} / {view.Site.StackSlots} stacks";
        foreach (var item in view.Items)
        {
            var row = Row($"{_session.DisplayName(item.DefId)}{(item.Count > 1 ? $" x{item.Count}" : "")}");
            row.AddChild(Button("Take", () => Submit(new MoveItemCommand(simulation.PlayerId, item.Ref, ItemPlace.In(open), ItemPlace.Carried, item.Count))));
            if (item.Count > 1)
                row.AddChild(Button("Take 1", () => Submit(new MoveItemCommand(simulation.PlayerId, item.Ref, ItemPlace.In(open), ItemPlace.Carried, 1))));
            _container.AddChild(row);
        }
    }

    private void Submit(GameCommand command) => _session.Submit(command);

    private static PanelContainer Panel(string title, Label header, VBoxContainer list, Vector2 size)
    {
        var panel = new PanelContainer { CustomMinimumSize = size };
        header.Text = title;
        panel.AddChild(Column(header, list));
        return panel;
    }

    private static VBoxContainer Column(Label header, VBoxContainer list)
    {
        var column = new VBoxContainer();
        header.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(header);
        column.AddChild(list);
        return column;
    }

    private static HBoxContainer Row(string text)
    {
        var row = new HBoxContainer();
        var label = new Label { Text = text, CustomMinimumSize = new Vector2(300, 0) };
        label.AddThemeFontSizeOverride("font_size", 16);
        row.AddChild(label);
        return row;
    }

    private static Button Button(string text, Action pressed)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.Pressed += pressed;
        return button;
    }

    private static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
            child.QueueFree();
    }
}
