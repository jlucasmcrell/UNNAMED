// UNNAMED Presentation - the inventory, container and crafting panels (PROTOTYPE.md §4.1 UI panels: inventory, equipment, crafting)
// Godot presentation only: every button submits a command; nothing here changes state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// What the character carries and wears, and - when a container is open - what it holds, at a crafting station (M3f) what
/// the character knows to make there and from what, or with a trader (M4) their wares at their prices and what they would
/// pay for what is carried. Each row's buttons submit the same commands the headless tests do; the panel redraws from the
/// simulation's views after every item event.
/// </summary>
public partial class InventoryPanel : CanvasLayer
{
    private readonly VBoxContainer _carried = new();
    private readonly VBoxContainer _container = new();
    private readonly Label _header = new();
    private readonly Label _containerHeader = new();
    private readonly PanelContainer _containerPanel = new();
    private GameSession _session = null!;
    private PlayerController _controller = null!;

    /// <summary>The container open alongside the inventory, if any.</summary>
    public string? OpenContainer { get; private set; }

    /// <summary>The crafting station open alongside the inventory, if any.</summary>
    public StationSite? OpenStation { get; private set; }

    /// <summary>The trader whose wares are open alongside the inventory, if any (M4).</summary>
    public string? OpenTrader { get; private set; }

    public void Bind(GameSession session, PlayerController controller)
    {
        _session = session;
        _controller = controller;
    }

    public override void _Ready()
    {
        Visible = false;
        // Below the notices at the top of the screen, so a trade's toast never covers the wares.
        var root = new HBoxContainer { Position = new Vector2(60, 250) };
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
        OpenStation = null;
        OpenTrader = null;
        Visible = true;
        Refresh();
    }

    public void OpenAt(StationSite station)
    {
        OpenContainer = null;
        OpenStation = station;
        OpenTrader = null;
        Visible = true;
        Refresh();
    }

    public void OpenTrade(string npcId)
    {
        OpenContainer = null;
        OpenStation = null;
        OpenTrader = npcId;
        Visible = true;
        Refresh();
    }

    public void Close()
    {
        OpenContainer = null;
        OpenStation = null;
        OpenTrader = null;
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
            var row = Row($"{Main.ItemName(_session, entry.DefId, entry.Quality)}{(entry.Count > 1 ? $" x{entry.Count}" : "")}" +
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
            if (OpenTrader is { } trader && SellPrice(simulation, trader, entry.DefId) is { } each && each > 0 && !equipped)
            {
                row.AddChild(Button($"Sell {each * entry.Count}", () => Submit(new SellCommand(simulation.PlayerId, trader, entry.ItemId, entry.Count))));
                if (entry.Count > 1)
                    row.AddChild(Button($"Sell 1 ({each})", () => Submit(new SellCommand(simulation.PlayerId, trader, entry.ItemId, 1))));
            }
            row.AddChild(Button("Drop", () => Submit(new MoveItemCommand(simulation.PlayerId, entry.ItemId.Value, ItemPlace.Carried, ItemPlace.Ground, entry.Count))));
            _carried.AddChild(row);
        }

        _containerPanel.Visible = OpenContainer is not null || OpenStation is not null || OpenTrader is not null;
        Clear(_container);
        if (OpenStation is { } station)
        {
            ShowStation(simulation, station);
            return;
        }
        if (OpenTrader is { } npc)
        {
            ShowWares(simulation, npc);
            return;
        }
        if (OpenContainer is not { } open || simulation.Containers.FirstOrDefault(c => c.Site.Key == open) is not { } view)
            return;
        _containerHeader.Text = $"{Main.Describe(_session, open)}   {view.Items.Length} / {view.Site.StackSlots} stacks";
        foreach (var item in view.Items)
        {
            var row = Row($"{Main.ItemName(_session, item.DefId, item.Quality)}{(item.Count > 1 ? $" x{item.Count}" : "")}");
            row.AddChild(Button("Take", () => Submit(new MoveItemCommand(simulation.PlayerId, item.Ref, ItemPlace.In(open), ItemPlace.Carried, item.Count))));
            if (item.Count > 1)
                row.AddChild(Button("Take 1", () => Submit(new MoveItemCommand(simulation.PlayerId, item.Ref, ItemPlace.In(open), ItemPlace.Carried, 1))));
            _container.AddChild(row);
        }
    }

    /// <summary>
    /// The recipes known for this station's kind: what each makes, its complexity against the smith's skill (the gap moves
    /// quality), what it takes against what is carried, and a Make button once everything is to hand.
    /// </summary>
    private void ShowStation(Simulation simulation, StationSite station)
    {
        var player = simulation.Player;
        var recipes = _controller.Recipes(station.Kind);
        _containerHeader.Text = $"{Main.Describe(station.Key)}   " + string.Join("   ", recipes.Select(r => r.SkillId).Distinct()
            .Select(skill => $"{_session.DisplayName(skill)} {ProgressionEngine.SkillLevel(player.Progression, skill)}"));
        if (recipes.Count == 0)
            _container.AddChild(Row("You know nothing to make here"));
        int Carried(string defId) => player.Inventory.Where(e => e.DefId == defId && !player.Equipment.ContainsValue(e.ItemId)).Sum(e => e.Count);
        foreach (var recipe in recipes)
        {
            string needs = string.Join(", ", recipe.Inputs.Select(i => $"{i.Count} {_session.DisplayName(i.ItemId)} ({Carried(i.ItemId)} carried)"));
            var row = Row($"{_session.DisplayName(recipe.OutputItemId)}{(recipe.OutputCount > 1 ? $" x{recipe.OutputCount}" : "")}   complexity {recipe.Complexity}\n" +
                          $"  from {needs}");
            var make = Button("Make", () => Submit(new CraftCommand(simulation.PlayerId, recipe.Id)));
            make.Disabled = recipe.Inputs.Any(i => Carried(i.ItemId) < i.Count);
            row.AddChild(make);
            _container.AddChild(row);
        }
    }

    /// <summary>A trader's wares at their prices, each to buy one at a time or all at once while the coin allows.</summary>
    private void ShowWares(Simulation simulation, string npcId)
    {
        if (simulation.Wares(npcId) is not { } wares)
            return;
        long coin = simulation.Player.Currency;
        _containerHeader.Text = $"{_session.DisplayName(npcId)}'s wares   Coin {coin}";
        if (wares.Wares.IsEmpty)
            _container.AddChild(Row("Nothing left to sell"));
        foreach (var ware in wares.Wares)
        {
            var row = Row($"{Main.ItemName(_session, ware.ItemId, ware.Quality)}{(ware.Count > 1 ? $" x{ware.Count}" : "")}   {ware.Price} each");
            var one = Button("Buy 1", () => Submit(new BuyCommand(simulation.PlayerId, npcId, ware.Ref, 1)));
            one.Disabled = ware.Price > coin;
            row.AddChild(one);
            if (ware.Count > 1)
            {
                var all = Button($"Buy all ({ware.Price * ware.Count})", () => Submit(new BuyCommand(simulation.PlayerId, npcId, ware.Ref, ware.Count)));
                all.Disabled = ware.Price * ware.Count > coin;
                row.AddChild(all);
            }
            _container.AddChild(row);
        }
    }

    /// <summary>What the open trader would pay for one of these, or null when they do not buy it.</summary>
    private static long? SellPrice(Simulation simulation, string npcId, string defId)
    {
        var items = simulation.Setup.Items;
        return simulation.Wares(npcId) is { } wares && items.Merchants.TryGetValue(wares.MerchantId, out var merchant) && items.Catalog.Find(defId) is { } item
            ? items.Pricing.SellPrice(item, merchant)
            : null;
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
