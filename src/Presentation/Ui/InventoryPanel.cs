// UNNAMED Presentation - the inventory, container and crafting panels (PROTOTYPE.md §4.1 UI panels: inventory, equipment, crafting)
// Godot presentation only: every button submits a command; nothing here changes state (D-11)

using Godot;
using System.Collections.Immutable;
using UNNAMED.Application;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// What the character carries and wears, and - when a container is open - what it holds, at a crafting station (M3f) what
/// the character knows to make there and from what, or with a trader (M4) their wares at their prices and what they would
/// pay for what is carried. Each row's buttons submit the same commands the headless tests do; the panel redraws from the
/// simulation's views after every item event. The Field Instrument sheet: STANDARD with the pack alone, WIDE with a context column;
/// under the pack, what the chosen item does (the owner's first playtest: "there are no stats for anything right now").
/// </summary>
public partial class InventoryPanel : CanvasLayer
{
    private const float PackWidth = 560, ContextWidth = 520;

    private readonly VBoxContainer _carried = new();
    private readonly VBoxContainer _container = new();
    private readonly VBoxContainer _context = new();
    private readonly VBoxContainer _contextHead = new();
    private readonly ScrollContainer _carriedScroll = UiKit.Scroll();
    private readonly ScrollContainer _containerScroll = UiKit.Scroll();
    private readonly HBoxContainer _totals = new();
    /// <summary>What the laden band means now, under the totals while laden or at the limit (D4).</summary>
    private readonly Label _laden = UiKit.Words(string.Empty, 16, HudStyle.Brass);
    private readonly VBoxContainer _details = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    private VBoxContainer _packColumn=null!;
    private readonly VBoxContainer _portraitColumn=new() {SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
    private readonly ScrollContainer _detailScroll=UiKit.Scroll();
    /// <summary>The bag bar: the backpack and each bag slot with how full it is.</summary>
    private readonly VBoxContainer _bags = new();
    /// <summary>The category tabs, drawn again with the pack; the search and the sort buttons beside them live as long as the panel.</summary>
    private readonly VBoxContainer _tabsRow = new();
    private readonly LineEdit _searchEdit = new() { PlaceholderText = "Search the pack", ClearButtonEnabled = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    private readonly Button _sellJunk = UiKit.Action("Sell junk", () => { }, compact: true, id: "pack/sell_junk");
    private Action? _sellJunkPressed;
    private PackTab _tab = PackTab.All;
    private string _search = string.Empty;
    private bool _searchFocused;
    private Sheet _sheet = null!;
    private GameSession _session = null!;
    private PlayerController _controller = null!;
    private HudIcons _icons = HudIcons.None;

    /// <summary>The item the details show: a carried stack's ID, or a container's or trader's entry ref.</summary>
    private string? _chosen;

    /// <summary>The container open alongside the inventory, if any.</summary>
    public string? OpenContainer { get; private set; }

    /// <summary>Put a usable item on a quick slot (0-7), or clear the slot (null); Main submits the command (WP9).</summary>
    public Action<int, string?>? Assign { get; set; }

    /// <summary>The crafting station open alongside the inventory, if any.</summary>
    public StationSite? OpenStation { get; private set; }

    /// <summary>The trader whose wares are open alongside the inventory, if any (M4).</summary>
    public string? OpenTrader { get; private set; }

    /// <summary>
    /// Where the panel was opened from - a container, a corpse, a station or a trader - kept after a searched corpse is gone, so walking
    /// away closes it all the same (the owner's M6 playtest). Null when it was opened with the inventory key.
    /// </summary>
    public (long XMm, long ZMm)? Anchor { get; private set; }

    public void Bind(GameSession session, PlayerController controller)
    {
        _session = session;
        _controller = controller;
    }

    /// <summary>
    /// Item icons from the icon manifest (the Phase-1 asset integration); an item without one keeps an empty slot. The same set gives the
    /// item card its stat icons, a fine or crude make's note its mark, and the screen its title tile.
    /// </summary>
    public void UseIcons(HudIcons icons)
    {
        _icons = icons;
        _sheet.SetIcon(icons.Binds("panel.inventory") ? icons.For("panel.inventory") : null);
    }

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "Inventory", "inventory", UiKit.Standard, scrolls: false);
        _totals.AddThemeConstantOverride("separation", 24);
        _sheet.Body.AddChild(_totals);
        _laden.Visible = false;
        _sheet.Body.AddChild(_laden);
        _bags.AddThemeConstantOverride("separation", 6);
        _sheet.Body.AddChild(_bags);
        _sheet.Body.AddChild(Toolbar());

        var columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 24);
        var pack = new VBoxContainer { CustomMinimumSize = new Vector2(PackWidth, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _packColumn=pack;
        pack.AddThemeConstantOverride("separation", 16);
        _carried.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _carried.AddThemeConstantOverride("separation", 4);
        _carriedScroll.Name="PackListViewport";
        _detailScroll.Name="ItemDetailViewport";
        _carriedScroll.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        _carriedScroll.AddChild(_carried);
        pack.AddChild(_carriedScroll);
        _details.AddThemeConstantOverride("separation", 6);
        _detailScroll.SizeFlagsVertical=Control.SizeFlags.ShrinkBegin;
        _detailScroll.AddChild(_details);
        pack.AddChild(_detailScroll);
        columns.AddChild(pack);
        _portraitColumn.AddThemeConstantOverride("separation",8);
        BuildPaperDoll(_portraitColumn);columns.AddChild(_portraitColumn);

        _context.CustomMinimumSize = new Vector2(ContextWidth, 0);
        _context.AddThemeConstantOverride("separation", 12);
        _context.AddChild(_contextHead);
        _container.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _container.AddThemeConstantOverride("separation", 4);
        _containerScroll.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        _containerScroll.AddChild(_container);
        _context.AddChild(_containerScroll);
        columns.AddChild(_context);
        _sheet.Body.AddChild(columns);
        foreach (var list in new Control[] { _carried, _container, _contextHead })
            list.MinimumSizeChanged += () => Callable.From(FitLists).CallDeferred();
        _sheet.Panel.Resized += () => Callable.From(FitLists).CallDeferred();
        _sheet.OnShown(() =>
        {
            if (UiKit.FocusedId(_sheet.Root) is null)
                UiKit.FirstFocusable(_sheet.Root)?.GrabFocus();
        });
    }

    /// <summary>
    /// The tabs (all, gear, consumables, materials, quest), the search and the four sorts, over the pack. The search box and the sort buttons are
    /// built once so typing is not interrupted by a redraw; the tabs are drawn again with the pack.
    /// </summary>
    private Control Toolbar()
    {
        var bar = new VBoxContainer();
        bar.AddThemeConstantOverride("separation", 6);
        bar.AddChild(_tabsRow);
        var find = new HBoxContainer();
        find.AddThemeConstantOverride("separation", 8);
        _searchEdit.TextChanged += text =>
        {
            _search = text;
            Refresh();
        };
        _searchEdit.FocusEntered += () => _searchFocused = true;
        _searchEdit.FocusExited += () => _searchFocused = false;
        UiKit.Id(_searchEdit, "pack/search");
        find.AddChild(_searchEdit);
        find.AddChild(UiKit.Caption("Sort"));
        foreach (var sort in Enum.GetValues<PackSort>())
        {
            var order = sort;
            string word = PackSorts.Key(order);
            find.AddChild(UiKit.Action(char.ToUpperInvariant(word[0]) + word[1..], () => Submit(new SortPackCommand(_session.Simulation!.PlayerId, order)),
                compact: true, id: $"pack/sort/{word}"));
        }
        _sellJunk.Visible = false;
        _sellJunk.Pressed += () => _sellJunkPressed?.Invoke();
        find.AddChild(_sellJunk);
        bar.AddChild(find);
        return bar;
    }

    public void Open(string? container)
    {
        OpenContainer = container;
        OpenStation = null;
        OpenTrader = null;
        Anchor = container is not null && _session.Simulation?.Containers.FirstOrDefault(c => c.Site.Key == container)?.Site is { } site
            ? (site.XMm, site.ZMm)
            : null;
        Show();
    }

    public void OpenAt(StationSite station)
    {
        OpenContainer = null;
        OpenStation = station;
        OpenTrader = null;
        Anchor = (station.XMm, station.ZMm);
        Show();
    }

    public void OpenTrade(string npcId)
    {
        OpenContainer = null;
        OpenStation = null;
        OpenTrader = npcId;
        Anchor = _session.Simulation?.Npcs.FirstOrDefault(n => n.Id == npcId) is { } npc ? (npc.Body.XMm, npc.Body.ZMm) : null;
        Show();
    }

    private void Show()
    {
        _chosen = null;
        bool was = Visible;
        Visible = true;
        Refresh();
        if (was)
            Callable.From(() => UiKit.FirstFocusable(_sheet.Root)?.GrabFocus()).CallDeferred();
    }

    public void Close()
    {
        OpenContainer = null;
        OpenStation = null;
        OpenTrader = null;
        Anchor = null;
        Visible = false;
    }

    /// <summary>Take everything the open container holds that the pack has room for (the owner's M6 playtest).</summary>
    public void TakeAll()
    {
        if (OpenContainer is { } key && _session.Simulation is { } simulation)
            Submit(new TakeAllCommand(simulation.PlayerId, key));
    }

    public void Refresh()
    {
        if (!Visible || _session.Simulation is not { } simulation)
            return;
        string? focused = UiKit.FocusedId(_sheet.Root);
        bool context = OpenContainer is not null || OpenStation is not null || OpenTrader is not null;
        _sheet.Shape(UiKit.Wide);
        _doll.Visible=!context;_portraitColumn.Visible=!context;
        var bagParent=context?_sheet.Body:_portraitColumn;
        if(_bags.GetParent()!=bagParent){_bags.Reparent(bagParent,false);bagParent.MoveChild(_bags,context?2:0);}
        var detailParent=context?_packColumn:_portraitColumn;
        if(_detailScroll.GetParent()!=detailParent)_detailScroll.Reparent(detailParent,false);
        if(!context)RefreshPaperDoll(simulation);
        _context.Visible = context;
        Fill(simulation);
        Details(simulation);
        Callable.From(FitLists).CallDeferred();
        if (_searchFocused)
        {
            Callable.From(() => _searchEdit.GrabFocus()).CallDeferred();
            return;
        }
        if (focused is not null && !UiKit.Refocus(_sheet.Root, focused))
            UiKit.FirstFocusable(_sheet.Root)?.GrabFocus();
    }

    private void Fill(Simulation simulation)
    {
        var player = simulation.Player;
        var catalog = simulation.Setup.Items.Catalog;
        UiKit.Clear(_totals);
        var carried = UiKit.Stat("Carried", $"{player.CarriedGrams / 1000.0:0.#} / {player.CarryLimitGrams / 1000.0:0.#} kg", null, 18);
        var load = UiKit.Gauge(HudStyle.Ash, 120);
        load.Set(player.CarriedGrams, player.CarryLimitGrams);
        carried.AddChild(load);
        // The laden band (D4): a chip beside the load while laden or at the limit, and what it means now under the totals.
        var laden = simulation.Setup.Items.Inventory.Laden;
        var band = LoadWords.Chip(player.CarriedGrams, player.CarryLimitGrams, laden);
        if (band is { } chip)
            carried.AddChild(UiKit.Chip(chip));
        _laden.Visible = band is not null;
        _laden.Text = LoadWords.Sentence(player.CarriedGrams, player.CarryLimitGrams, laden);
        _laden.AddThemeColorOverride("font_color", band is { Tone: WordTone.Refusal } ? HudStyle.Wound : HudStyle.Brass);
        _totals.AddChild(carried);
        var pack = simulation.Pack;
        _totals.AddChild(UiKit.Stat("Stacks", $"{pack.Used} / {pack.Capacity}", null, 18));
        _totals.AddChild(UiKit.Stat("Coin", $"{player.Currency}", HudStyle.Brass, 18));
        _totals.AddChild(UiKit.Stat("Armour", $"{player.Armor}", null, 18));

        DrawBags(simulation, pack);
        UiKit.Clear(_carried);
        var quick = simulation.QuickBar;
        if (player.Inventory.IsEmpty)
            _carried.AddChild(UiKit.EmptyState("Carrying nothing", "Your pack is empty."));
        var sections = PackModel.Sections(pack, catalog, e => Names.Of(_session, e.DefId), BagName, _tab, _search);
        bool narrowed = _tab != PackTab.All || !string.IsNullOrWhiteSpace(_search);
        foreach (var section in sections)
        {
            if (section.Used == 0 && !narrowed && sections.Length > 1)
            {
                _carried.AddChild(UiKit.Section(section.Title, $"{section.Fill} · empty"));
                continue;
            }
            _carried.AddChild(UiKit.Section(section.Title, section.Detail.Length == 0 ? section.Fill : $"{section.Detail} · {section.Fill}"));
            if (section.Rows.IsEmpty && section.Used > 0)
                _carried.AddChild(UiKit.Quiet("Nothing here matches."));
            foreach (var entry in section.Rows)
                _carried.AddChild(CarriedRow(simulation, catalog, quick, entry));
        }
        if (!pack.Unplaced.IsEmpty)
            _carried.AddChild(UiKit.Words($"{pack.Unplaced.Length} stack(s) have no slot: drop something or put a bag in a bag slot.", 16, HudStyle.Wound));
        _sellJunk.Visible = OpenTrader is not null;
        if (OpenTrader is { } seller)
        {
            int junk = player.Inventory.Count(e => e.Junk && !e.Locked && !player.Equipment.ContainsValue(e.ItemId) && SellPrice(simulation, seller, e.DefId, e.Wear, e.Quality, e.Notable) is > 0);
            _sellJunk.Text = junk > 0 ? $"Sell junk ({junk})" : "Sell junk";
            _sellJunk.Disabled = junk == 0;
            _sellJunkPressed = () => Submit(new SellJunkCommand(simulation.PlayerId, seller));
        }

        UiKit.Clear(_contextHead);
        UiKit.Clear(_container);
        if (OpenStation is { } station)
            ShowStation(simulation, station);
        else if (OpenTrader is { } npc)
            ShowWares(simulation, npc);
        else if (OpenContainer is { } open && simulation.Containers.FirstOrDefault(c => c.Site.Key == open) is { } view)
            ShowContainer(simulation, open, view);
    }

    private string BagName(string defId) => Names.Of(_session, defId);

    /// <summary>The tabs, then the bag bar: the backpack, and each of the four bag slots with its bag and how full it is, or empty.</summary>
    private void DrawBags(Simulation simulation, PackView pack)
    {
        UiKit.Clear(_tabsRow);
        var tabs = Enum.GetValues<PackTab>();
        _tabsRow.AddChild(UiKit.Segments("pack/tab", tabs.Select(PackTabs.Label).ToList(), Array.IndexOf(tabs, _tab), i =>
        {
            _tab = tabs[i];
            Refresh();
        }, compact: true));
        UiKit.Clear(_bags);
        var line = new HFlowContainer();
        line.AddThemeConstantOverride("h_separation", 10);
        line.AddThemeConstantOverride("v_separation", 6);
        line.AddChild(UiKit.Chip($"Backpack {pack.Containers[0].Stacks.Length}/{pack.Containers[0].Capacity}", HudStyle.Iron, HudStyle.Bone));
        for (int slot = 1; slot <= PackLimits.BagSlots; slot++)
        {
            int at = slot;
            var held = pack.Containers.FirstOrDefault(c => c.Bag == slot);
            var box = new HBoxContainer();
            box.AddThemeConstantOverride("separation", 4);
            box.AddChild(UiKit.Chip($"{slot}: {PackModel.SlotLine(pack, slot, BagName)}", held is null ? HudStyle.Iron : held.Stacks.Length >= held.Capacity ? HudStyle.Brass : HudStyle.Lichen,
                held is null ? HudStyle.Ash : null));
            if (held is not null)
                box.AddChild(UiKit.Action("Take out", () => Submit(new UnequipBagCommand(simulation.PlayerId, at)), compact: true, id: $"bag/{slot}/out"));
            line.AddChild(box);
        }
        _bags.AddChild(line);
    }

    /// <summary>One carried stack's row: its buttons, its marks (equipped, locked, junk) and the places it can be put or sold.</summary>
    private Control CarriedRow(Simulation simulation, ItemCatalog catalog, ImmutableArray<string?> quick, InventoryEntry entry)
    {
        var player = simulation.Player;
        var definition = catalog.Find(entry.DefId);
        var slot = player.Equipment.FirstOrDefault(kv => kv.Value == entry.ItemId);
        bool equipped = player.Equipment.ContainsValue(entry.ItemId);
        string id = entry.ItemId.Value;
        var row = ItemRow(id, entry.DefId, entry.Quality, entry.Count, "");
        if (equipped)
            row.Root.AddChild(UiKit.Chip(Names.Slot(slot.Key), HudStyle.Iron, HudStyle.Bone));
        if (entry.Locked)
            row.Root.AddChild(UiKit.Chip("Locked", HudStyle.Brass));
        if (entry.Junk)
            row.Root.AddChild(UiKit.Chip("Junk", HudStyle.Wound));
        // Durability (WP3b, D5): a worn piece says so on its row; a sound one keeps the row quiet.
        if (definition is not null && WearWords.Chip(entry.Wear, definition.DurabilityMax) is { } wear)
            row.Root.AddChild(UiKit.Chip(wear));
        bool usable = simulation.Setup.Combat.UseEffects.ContainsKey(entry.DefId);
        // A usable thing is dragged from its row onto an action bar's slot.
        if (usable)
            ActionBarsView.Source(row.Root, entry.DefId, Names.Of(_session, entry.DefId));
        else if(definition?.Slot is not null)EquipmentDragSource(row.Root,entry);
        int q = quick.IndexOf(entry.DefId);
        if (q >= 0)
            row.Root.AddChild(new HudView.KeyCap(HelpPanel.Key($"quick_{q + 1}"), 16));
        // The quick bar (WP9): a usable thing goes on its first free slot - key 1 first - or comes off it; the Skills screen moves it.
        if (usable && Assign is { } assign && !quick.IsDefault)
        {
            string defId = entry.DefId;
            int free = UNNAMED.Domain.Progression.QuickBar.FreeSlot(quick, UNNAMED.Domain.Progression.QuickBar.ItemOrder);
            row.Root.AddChild(q >= 0 ? Button(id, "Off the bar", () => assign(q, null))
                : Button(id, "To the bar", () => assign(free >= 0 ? free : 0, defId)));
        }
        if (definition?.Slot is not null)
        {
            row.Root.AddChild(Button(id, equipped ? "Unequip" : "Equip", () => Submit(equipped
                ? new UnequipCommand(simulation.PlayerId, slot.Key)
                : new EquipCommand(simulation.PlayerId, entry.ItemId))));
        }
        // A bag goes into the first empty bag slot; it adds slots and nothing else.
        if (definition?.Bag is not null)
        {
            int emptySlot = PackModel.FirstEmptySlot(simulation.Pack);
            var put = Button(id, "Equip bag", () => Submit(new EquipBagCommand(simulation.PlayerId, entry.ItemId, emptySlot)));
            put.Disabled = emptySlot == 0;
            row.Root.AddChild(put);
        }
        if (usable)
            row.Root.AddChild(Button(id, "Use", () => Submit(new UseItemCommand(simulation.PlayerId, entry.ItemId))));
        if (simulation.Setup.Magic.Teaches.ContainsKey(entry.DefId))
            row.Root.AddChild(Button(id, "Read", () => Submit(new UseItemCommand(simulation.PlayerId, entry.ItemId))));
        // Remains take nothing in: the world takes a body away with whatever lies in it.
        if (OpenContainer is { } key && !simulation.Creatures.Any(c => c.Condition == CreatureCondition.Corpse && c.CorpseKey == key))
            row.Root.AddChild(Button(id, "Put", () => Submit(new MoveItemCommand(simulation.PlayerId, entry.ItemId.Value, ItemPlace.Carried, ItemPlace.In(key), entry.Count))));
        if (OpenTrader is { } trader && SellPrice(simulation, trader, entry.DefId, entry.Wear, entry.Quality, entry.Notable) is { } each && each > 0 && !equipped)
        {
            var sellAll = Button(id, $"Sell {each * entry.Count}", () => Submit(new SellCommand(simulation.PlayerId, trader, entry.ItemId, entry.Count)));
            sellAll.Disabled = entry.Locked;
            row.Root.AddChild(sellAll);
            if (entry.Count > 1)
            {
                var sellOne = Button(id, $"Sell 1 ({each})", () => Submit(new SellCommand(simulation.PlayerId, trader, entry.ItemId, 1)));
                sellOne.Disabled = entry.Locked;
                row.Root.AddChild(sellOne);
            }
        }
        // A heritage item at a trader of another people: Sell stands disabled, saying why they will not take it (owner, 2026-10-24).
        else if (OpenTrader is { } other && definition?.Heritage is not null && !equipped && simulation.HeritageRefusal(other, entry.DefId) is { } refused)
        {
            var sell = Button(id, "Sell", () => { });
            sell.Disabled = true;
            sell.TooltipText = refused;
            row.Root.AddChild(sell);
        }
        if (definition is not { NoDrop: true })
            row.Root.AddChild(Button(id, "Drop", () => Submit(new MoveItemCommand(simulation.PlayerId, entry.ItemId.Value, ItemPlace.Carried, ItemPlace.Ground, entry.Count)),
                UiKit.Kind.Destructive));
        return row.Root;
    }

    /// <summary>
    /// What can be done to the chosen carried stack to keep the pack in order: lock it against sorting and selling, mark it as junk, split it,
    /// merge it into another stack of its kind, or move it to the next bag that takes it.
    /// </summary>
    private void Organise(Simulation simulation, InventoryEntry entry)
    {
        var player = simulation.PlayerId;
        string id = entry.ItemId.Value;
        var line = new HFlowContainer();
        line.AddThemeConstantOverride("h_separation", 8);
        line.AddThemeConstantOverride("v_separation", 6);
        line.AddChild(Button(id, entry.Locked ? "Unlock" : "Lock", () => Submit(new LockStackCommand(player, entry.ItemId, !entry.Locked))));
        var definition = simulation.Setup.Items.Catalog.Find(entry.DefId);
        // What the world will not junk - anything that cannot be sold or cannot be dropped (a quest item, a heritage keepsake) - is not offered.
        if (entry.Junk || definition is not ({ NoSell: true } or { NoDrop: true }))
            line.AddChild(Button(id, entry.Junk ? "Not junk" : "Mark as junk", () => Submit(new MarkJunkCommand(player, entry.ItemId, !entry.Junk))));
        var carried = simulation.Player.Inventory;
        if (entry.Count > 1)
            line.AddChild(Button(id, $"Split {entry.Count / 2}", () => Submit(new MoveItemCommand(player, id, ItemPlace.Carried, ItemPlace.Carried, entry.Count / 2))));
        if (definition is not null && carried.Any(e => e.ItemId != entry.ItemId && e.DefId == entry.DefId && e.Quality == entry.Quality && e.Junk == entry.Junk
                && e.Count < definition.StackMax && !simulation.Player.Equipment.ContainsValue(e.ItemId)))
            line.AddChild(Button(id, "Merge", () => Submit(new MoveItemCommand(player, id, ItemPlace.Carried, ItemPlace.Carried, entry.Count))));
        var pack = simulation.Pack;
        string? kind = definition is null ? null : PackKinds.Of(definition);
        var here = pack.Holding(entry.ItemId);
        var next = pack.Containers.Where(c => c.Bag != here?.Bag && c.Takes(kind) && c.Stacks.Length < c.Capacity)
            .OrderBy(c => c.Bag <= (here?.Bag ?? -1) ? 1 : 0).ThenBy(c => c.Bag).FirstOrDefault();
        if (next is not null && here is not null)
        {
            int free = Enumerable.Range(0, next.Capacity).First(i => next.Stacks.All(s => s.Slot != i));
            string where = next.IsBackpack ? "the backpack" : BagName(next.BagDefId!);
            line.AddChild(Button(id, $"Move to {where}", () => Submit(new PlaceStackCommand(player, entry.ItemId, next.Bag, free))));
        }
        _details.AddChild(line);
    }

    /// <summary>A stash was shown (the hint that names it waits on this).</summary>
    public Action? StashShown { get; set; }

    private void ShowContainer(Simulation simulation, string open, ContainerView view)
    {
        if (view.Site.Stash)
            StashShown?.Invoke();
        var head = UiKit.Section(view.Site.Stash ? $"Your {Names.Place(_session, open)}" : Names.Place(_session, open), $"{view.Items.Length} / {view.Site.StackSlots} stacks");
        _contextHead.AddChild(head);
        if (view.Items.Length == 0)
        {
            _container.AddChild(UiKit.Quiet("Nothing left in it."));
            return;
        }
        var all = new HBoxContainer();
        all.AddThemeConstantOverride("separation", 8);
        all.AddChild(UiKit.Action("Take all", TakeAll, UiKit.Kind.Primary, id: "take_all"));
        if (UiKit.KeyCap("take_all") is { } cap)
            all.AddChild(cap);
        _contextHead.AddChild(all);
        foreach (var item in view.Items)
        {
            var row = ItemRow("c:" + item.Ref, item.DefId, item.Quality, item.Count, "");
            row.Root.AddChild(Button("c:" + item.Ref, "Take", () => Submit(new MoveItemCommand(simulation.PlayerId, item.Ref, ItemPlace.In(open), ItemPlace.Carried, item.Count))));
            if (item.Count > 1)
                row.Root.AddChild(Button("c:" + item.Ref, "Take 1", () => Submit(new MoveItemCommand(simulation.PlayerId, item.Ref, ItemPlace.In(open), ItemPlace.Carried, 1))));
            _container.AddChild(row.Root);
        }
    }

    /// <summary>
    /// The recipes known for this station's kind: what each makes, its complexity against the smith's skill (the gap moves
    /// quality), what it takes against what is carried, and Make once everything is to hand - or, disabled, what is short.
    /// </summary>
    private void ShowStation(Simulation simulation, StationSite station)
    {
        var player = simulation.Player;
        var recipes = _controller.Recipes(station.Kind);
        _contextHead.AddChild(UiKit.Section(Names.Place(_session, station.Key)));
        var skills = new HFlowContainer();
        skills.AddThemeConstantOverride("h_separation", 8);
        foreach (string skill in recipes.Select(r => r.SkillId).Distinct())
            skills.AddChild(UiKit.Chip($"{Names.Of(_session, skill)} {ProgressionEngine.SkillLevel(player.Progression, skill)}", HudStyle.Brass));
        _contextHead.AddChild(skills);
        if (recipes.Count == 0)
            _container.AddChild(UiKit.Quiet("You know nothing to make here yet."));
        // Experimenting (WP3a): the station's seeded set, found by carrying a recipe's makings. The set's recipes stay hidden until found.
        var (found, total) = simulation.Experiments(station.Key);
        if (total > 0)
        {
            var experiment = new HBoxContainer();
            experiment.AddThemeConstantOverride("separation", 10);
            experiment.AddChild(UiKit.Chip($"Experiments found {found} of {total}", found < total ? HudStyle.Brass : HudStyle.Ash));
            var attempt = Button("x:" + station.Key, "Experiment", () => Submit(new ExperimentCommand(simulation.PlayerId, station.Key)));
            attempt.Disabled = found >= total;
            experiment.AddChild(attempt);
            _contextHead.AddChild(experiment);
        }
        // A station works from what is carried (in any bag) and from the stashes in its settlement (the quality-of-life rule: no need to fetch).
        var pack = simulation.Pack;
        var stashes = simulation.StashesAt(station);
        var stashed = stashes.SelectMany(st => st.Items).ToList();
        var worn = player.Equipment.Values.ToList();
        InputSupply Supply(RecipeInput input) => PackModel.Supply(input.ItemId, input.Count, pack, stashed, worn, c => BagName(c.BagDefId!));
        if (!stashes.IsEmpty)
            _contextHead.AddChild(UiKit.Words($"Also working from your stash here ({stashed.Sum(i => i.Count)} items).", 16, HudStyle.Brass));
        foreach (var recipe in recipes)
        {
            var row = ItemRow("r:" + recipe.Id, recipe.OutputItemId, Quality.Standard, recipe.OutputCount, "");
            row.Root.AddChild(UiKit.Chip($"Complexity {recipe.Complexity}", HudStyle.Iron, HudStyle.Ash));
            var short_ = recipe.Inputs.Where(i => !Supply(i).Enough).ToList();
            var make = Button("r:" + recipe.Id, "Make", () => Submit(new CraftCommand(simulation.PlayerId, recipe.Id)), UiKit.Kind.Primary);
            make.Disabled = short_.Count > 0;
            row.Root.AddChild(make);
            var inputs = new HFlowContainer();
            inputs.AddThemeConstantOverride("h_separation", 6);
            inputs.AddThemeConstantOverride("v_separation", 4);
            foreach (var input in recipe.Inputs)
            {
                var supply = Supply(input);
                var tone = supply.Source switch { SupplySource.Short => HudStyle.Wound, SupplySource.Carried => HudStyle.Lichen, _ => HudStyle.Brass };
                inputs.AddChild(UiKit.Chip($"{input.Count} {Names.Of(_session, input.ItemId)} · {supply.Label}", tone));
            }
            var block = new VBoxContainer();
            block.AddThemeConstantOverride("separation", 4);
            block.AddChild(row.Root);
            block.AddChild(Indent(inputs));
            if (short_.Count > 0)
                block.AddChild(Indent(UiKit.Words("Short of " + string.Join(", ", short_.Select(i => Names.Of(_session, i.ItemId))), 16, HudStyle.Muted)));
            _container.AddChild(block);
        }
    }

    /// <summary>A trader's wares at their prices, each to buy one at a time or all at once while the coin allows.</summary>
    private void ShowWares(Simulation simulation, string npcId)
    {
        if (simulation.Wares(npcId) is not { } wares)
            return;
        long coin = simulation.Player.Currency;
        _contextHead.AddChild(UiKit.Section($"{Names.Of(_session, npcId)}'s wares", $"you have {coin} coin"));
        if (wares.Wares.IsEmpty)
            _container.AddChild(UiKit.Quiet("Nothing left to sell."));
        foreach (var ware in wares.Wares)
        {
            string id = "w:" + ware.Ref;
            var row = ItemRow(id, ware.ItemId, ware.Quality, ware.Count, "");
            var price = HudStyle.Text(HudStyle.Strong, 18, HudStyle.Brass);
            price.Text = $"{ware.Price} each";
            price.VerticalAlignment = VerticalAlignment.Center;
            row.Root.AddChild(price);
            var one = Button(id, "Buy 1", () => Submit(new BuyCommand(simulation.PlayerId, npcId, ware.Ref, 1)));
            one.Disabled = ware.Price > coin;
            row.Root.AddChild(one);
            if (ware.Count > 1)
            {
                var all = Button(id, $"Buy all ({ware.Price * ware.Count})", () => Submit(new BuyCommand(simulation.PlayerId, npcId, ware.Ref, ware.Count)));
                all.Disabled = ware.Price * ware.Count > coin;
                row.Root.AddChild(all);
            }
            if (ware.Price > coin)
                row.Secondary.Text = "not enough coin";
            row.Secondary.Visible = row.Secondary.Text.Length > 0;
            _container.AddChild(row.Root);
        }
    }

    /// <summary>
    /// What the chosen item is and does, read off its definition: what it hits for, how far and how fast, the armour it gives, what using
    /// it does, what it teaches, what it weighs and is worth, what equipping it takes - and against what is worn now, better or worse.
    /// </summary>
    private void Details(Simulation simulation)
    {
        UiKit.Clear(_details);
        var (defId, quality, notable, wear) = Chosen(simulation);
        if (defId is null)
        {
            _details.AddChild(UiKit.Section("Item"));
            _details.AddChild(UiKit.Quiet("Point at or move to an item to see what it does."));
            return;
        }
        var setup = simulation.Setup;
        var item = setup.Items.Catalog.Find(defId);
        var head = UiKit.Section(Names.Item(_session, defId, quality),
            item is null ? "" : item.Heritage is { } people ? HeritageRules.PeopleWord(people) + " heritage" : Names.Words(item.Category));
        _details.AddChild(head);
        if (_chosen is not null && item?.Heritage is null && simulation.Player.Inventory.FirstOrDefault(e => e.ItemId.Value == _chosen) is { } selected)
        {
            var salvage = simulation.Salvage(selected.ItemId);
            string recovered = salvage.Materials.IsEmpty ? "no materials" : string.Join(", ", salvage.Materials.Select(m => $"{m.Count} {Names.Of(_session, m.ItemId)}"));
            string label = salvage.Refusal is not null ? "Salvage" : "Salvage: " + recovered;
            var dismantle = Button("salvage/" + selected.ItemId.Value, label, () => Submit(new SalvageCommand(simulation.PlayerId, selected.ItemId)));
            dismantle.ClipText = true;
            dismantle.Disabled = salvage.Refusal is not null;
            dismantle.TooltipText = salvage.Refusal ?? $"Destroys one item. Recovers: {recovered}.";
            _details.AddChild(dismantle);
            if (salvage.Refusal is null)
                _details.AddChild(UiKit.Quiet($"Salvage {salvage.Skill} · {salvage.RecoveryPercent}% recovery · destroys one item"));
        }

        var grid = new GridContainer { Columns = _context.Visible ? 2 : 1 };
        grid.AddThemeConstantOverride("h_separation", 24);
        grid.AddThemeConstantOverride("v_separation", 2);
        void Fact(string label, string value, Color? tone = null, string? icon = null)
        {
            var stat = UiKit.Stat(label, value, tone, 18);
            stat.CustomMinimumSize = new Vector2(250, 0);
            Control lead = icon is not null && _icons.Binds(icon) ? UiKit.Icon(_icons, icon, 24) : new Control { CustomMinimumSize = new Vector2(30, 0) };
            stat.AddChild(lead);
            stat.MoveChild(lead, 0);
            grid.AddChild(stat);
        }
        var player = simulation.Player;
        ItemDefinition? Worn(EquipSlot slot) => player.Equipment.TryGetValue(slot, out var worn)
            && player.Inventory.FirstOrDefault(e => e.ItemId == worn) is { } entry && entry.DefId != defId ? setup.Items.Catalog.Find(entry.DefId) : null;
        (ItemDefinition Definition, int Quality)? WornEntry(EquipSlot slot) => player.Equipment.TryGetValue(slot, out var worn)
            && player.Inventory.FirstOrDefault(e => e.ItemId == worn) is { } entry && entry.DefId != defId && setup.Items.Catalog.Find(entry.DefId) is { } d
                ? (d, entry.Quality) : null;
        var notes = new List<(string Text, Color Tone, string? Icon)>();
        if (item?.Weapon is { } weapon)
        {
            Fact("Damage", weapon.DamageMin == weapon.DamageMax ? $"{weapon.DamageMin}" : $"{weapon.DamageMin}–{weapon.DamageMax}", icon: "stat.damage");
            Fact("Kind", Names.Words(weapon.DamageType));
            Fact("Reach", $"{weapon.ReachMm / 1000.0:0.0#} m", icon: "stat.reach");
            if (weapon.Ranged)
                Fact("Draw", $"{weapon.DrawMs / 1000.0:0.0#} s", icon: "stat.draw");
            else if (weapon.AttackMs > 0)
                Fact("Swing", $"{weapon.AttackMs / 1000.0:0.0#} s", icon: "stat.speed");
            if (weapon.SkillId is { } skill)
                Fact("Skill", Names.Of(_session, skill), icon: skill);
            if (weapon.TwoHanded)
                notes.Add(("Two-handed: the off hand stays empty.", HudStyle.Ash, null));
            if (Worn(EquipSlot.MainHand)?.Weapon is { } held)
            {
                double delta = (weapon.DamageMin + weapon.DamageMax - held.DamageMin - held.DamageMax) / 2.0;
                string than = Names.Of(_session, Worn(EquipSlot.MainHand)!.Id);
                notes.Add((delta == 0 ? $"Hits as hard as your {than}." : $"{Math.Abs(delta):0.#} {(delta > 0 ? "more" : "less")} damage a blow than your {than}.",
                    delta > 0 ? HudStyle.Lichen : delta < 0 ? HudStyle.Wound : HudStyle.Ash, null));
            }
        }
        if (item is { ArmorValue: > 0 } || item?.Slot is not null && item.Weapon is null)
        {
            // A piece's quality moves its armour a step a step (WP3a), as the character sheet counts it.
            int armorPerStep = setup.Crafting.Constants.ArmorPerStep;
            int armour = EquipmentRules.ArmorOf(item!, quality, armorPerStep);
            Fact("Armour", $"+{armour}", icon: "stat.armour");
            Fact("Worn on", Names.Slot(item!.Slot!.Value));
            if (item.LightRadiusMm > 0)
                Fact("Light", $"{item.LightRadiusMm / 1000.0:0.#} m");
            if (item.StealthPenalty > 0)
                notes.Add(($"Seen {item.StealthPenalty * 100:0}% further off while it is equipped.", HudStyle.Wound, null));
            if (WornEntry(item.Slot!.Value) is { } other)
            {
                int delta = armour - EquipmentRules.ArmorOf(other.Definition, other.Quality, armorPerStep);
                var otherItem = other.Definition;
                notes.Add((delta == 0 ? $"As much armour as your {Names.Of(_session, otherItem.Id)}."
                    : $"{Math.Abs(delta)} {(delta > 0 ? "more" : "less")} armour than your {Names.Of(_session, otherItem.Id)}.",
                    delta > 0 ? HudStyle.Lichen : delta < 0 ? HudStyle.Wound : HudStyle.Ash, null));
            }
        }
        if (setup.Combat.UseEffects.TryGetValue(defId, out string? effectId) && setup.Combat.Effects.TryGetValue(effectId, out var effect))
            notes.Add((Uses(effect), HudStyle.Lichen, effect.HealPerTick > 0 ? "stat.heal" : null));
        if (setup.Magic.Teaches.TryGetValue(defId, out var teaches) && teaches.Length > 0)
            notes.Add(($"Reading it teaches {string.Join(", ", teaches.Select(t => Names.Of(_session, t)))}.", HudStyle.Lichen, null));
        if (item is not null)
        {
            Fact("Weight", $"{item.WeightGrams / 1000.0:0.0#} kg", icon: "stat.weight");
            // A heritage item's worth counts only among its people: shown at a trader who would pay it, nowhere else.
            if (item.Heritage is null || OpenTrader is { } trader && simulation.Offer(trader, defId) is not null)
                Fact("Worth", $"{item.ValueBase} coin", HudStyle.Brass, "stat.worth");
            foreach (var (attribute, minimum) in item.Requirements.Attributes)
            {
                int value = ProgressionEngine.AttributeValue(player.Progression, attribute, setup.Progression);
                Fact($"Needs {ProgressionKeys.Key(attribute)}", Models.ColourWords.Needs(minimum, value), value >= minimum ? HudStyle.Lichen : HudStyle.Wound);
            }
            foreach (var (skill, minimum) in item.Requirements.Skills)
            {
                int level = ProgressionEngine.SkillLevel(player.Progression, skill);
                Fact($"Needs {Names.Of(_session, skill)}", Models.ColourWords.Needs(minimum, level), level >= minimum ? HudStyle.Lichen : HudStyle.Wound);
            }
        }
        if (quality == Quality.Masterwork)
            notes.Add(("Masterwork: two steps past the common make.", HudStyle.Brass, "quality.fine"));
        else if (quality != Quality.Standard)
            notes.Add((quality > 0 ? "Fine work: better than the common make." : "Crude work: worse than the common make.", quality > 0 ? HudStyle.Lichen : HudStyle.Wound,
                quality > 0 ? "quality.fine" : "quality.crude"));
        // Durability (WP3b, D5): what is left of it, and a worn-out piece counting as crude until it is mended.
        if (item is { DurabilityMax: > 0 })
        {
            int left = Durability.Remaining(wear, item.DurabilityMax);
            Fact("Durability", $"{left} / {item.DurabilityMax}", left == 0 ? HudStyle.Wound : left * 4 <= item.DurabilityMax ? HudStyle.Brass : HudStyle.Ash);
            if (Durability.IsWornOut(wear, item.DurabilityMax))
                notes.Add(("Worn out: it counts as crude work until it is mended. It will not break.", HudStyle.Wound, null));
        }
        // Class, provenance and familiarity (WP3a, DCP:673-676).
        if (item is not null && ItemClasses.Of(item, quality) is var itemClass && itemClass != ItemClass.Ordinary)
            Fact("Class", Names.Words(ItemClasses.Key(itemClass)), HudStyle.Brass);
        if (notable is not null)
        {
            notes.Add((Provenance(notable), HudStyle.Ash, null));
            notes.Add(($"Familiarity {new string('●', notable.Familiarity)}{new string('○', NotableRecord.MaxFamiliarity - notable.Familiarity)}", HudStyle.Brass, null));
        }
        _details.AddChild(grid);
        if (_chosen is not null && simulation.Player.Inventory.FirstOrDefault(e => e.ItemId.Value == _chosen) is { } carriedEntry && !simulation.Player.Bags.Contains(carriedEntry.ItemId))
            Organise(simulation, carriedEntry);
        if (item?.Bag is { } bagSpec)
            notes.Add(($"A bag: {setup.Items.Inventory.Bags.Capacity(bagSpec, quality)} slots at this make, {(bagSpec.IsSpecialised ? "takes " + PackModel.AcceptsWords(bagSpec.Accepts) : "takes anything")}. It adds room, not carrying strength.", HudStyle.Ash, null));
        if (item?.Heritage is { } heritage)
            notes.Add(($"{HeritageRules.PeopleWord(heritage)} heritage: only the {HeritageRules.PeopleWord(heritage)} use, buy, sell or trade it, and it is never dropped.", HudStyle.Brass, null));
        foreach (var (text, tone, icon) in notes)
        {
            if (icon is null || !_icons.Binds(icon))
            {
                _details.AddChild(UiKit.Words(text, 17, tone));
                continue;
            }
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 10);
            line.AddChild(UiKit.Icon(_icons, icon, 24));
            line.AddChild(UiKit.Words(text, 17, tone));
            _details.AddChild(line);
        }
    }

    /// <summary>What using a consumable does, from its effect: "Heals 24 over 8 s".</summary>
    private string Uses(Domain.Combat.EffectDefinition effect)
    {
        double seconds = effect.DurationTicks * _session.TickSeconds;
        int ticks = effect.TickIntervalTicks > 0 ? effect.DurationTicks / effect.TickIntervalTicks : 1;
        var parts = new List<string>();
        if (effect.HealPerTick > 0)
            parts.Add($"heals {effect.HealPerTick * ticks} over {seconds:0.#} s");
        if (effect.ArmorBonus != 0)
            parts.Add($"armour {effect.ArmorBonus:+#;-#} for {seconds:0.#} s");
        if (Math.Abs(effect.DamageDealtMultiplier - 1) > 0.001)
            parts.Add($"blows {(effect.DamageDealtMultiplier - 1) * 100:+0;-0}% for {seconds:0.#} s");
        if (Math.Abs(effect.StaminaRegenMultiplier - 1) > 0.001)
            parts.Add($"stamina returns {(effect.StaminaRegenMultiplier - 1) * 100:+0;-0}% faster for {seconds:0.#} s");
        string said = parts.Count == 0 ? $"{Names.Of(_session, effect.Id)} for {seconds:0.#} s" : string.Join(", ", parts);
        return "Using it: " + said + ".";
    }

    /// <summary>The chosen item's definition and quality, wherever it lies; null when nothing is chosen or it is gone.</summary>
    private (string? DefId, int Quality, NotableRecord? Notable, ItemWear? Wear) Chosen(Simulation simulation)
    {
        if (_chosen is null)
            return (null, 0, null, null);
        if (_chosen.StartsWith("c:", StringComparison.Ordinal) && OpenContainer is { } open
            && simulation.Containers.FirstOrDefault(c => c.Site.Key == open)?.Items.FirstOrDefault(i => "c:" + i.Ref == _chosen) is { } inside)
            return (inside.DefId, inside.Quality, inside.Notable, inside.Wear);
        if (_chosen.StartsWith("w:", StringComparison.Ordinal) && OpenTrader is { } trader
            && simulation.Wares(trader)?.Wares.FirstOrDefault(w => "w:" + w.Ref == _chosen) is { } ware)
            return (ware.ItemId, ware.Quality, null, ware.Wear);
        if (_chosen.StartsWith("r:", StringComparison.Ordinal) && simulation.Setup.Crafting.Recipes.TryGetValue(_chosen[2..], out var recipe))
            return (recipe.OutputItemId, Quality.Standard, null, null);
        return simulation.Player.Inventory.FirstOrDefault(e => e.ItemId.Value == _chosen) is { } entry
            ? (entry.DefId, entry.Quality, entry.Notable, entry.Wear)
            : (null, 0, null, null);
    }

    /// <summary>A notable instance's provenance line (DCP:673-676): how it came to be held, from what, and by whose hand when made.</summary>
    private string Provenance(NotableRecord notable) => notable.Provenance switch
    {
        ProvenanceKind.Crafted => $"Made by {notable.Maker} ({Names.Of(_session, notable.SourceRef)}), day {Day(notable.Tick)}.",
        ProvenanceKind.Found => $"Found in {Names.Place(_session, notable.SourceRef)}, day {Day(notable.Tick)}.",
        ProvenanceKind.Bought => $"Bought from {Names.Place(_session, notable.SourceRef)}, day {Day(notable.Tick)}.",
        ProvenanceKind.Given => $"Given: {Names.Of(_session, notable.SourceRef)}, day {Day(notable.Tick)}.",
        _ => "Carried from the start.",
    };

    private long Day(long tick) => _session.Simulation is { } simulation ? tick / simulation.Setup.Progression.TicksPerWorldDay + 1 : 1;

    /// <summary>Choose an item for the details, as the pointer or the focus reaches its row.</summary>
    private void Choose(string id)
    {
        if (id == _chosen || _session.Simulation is not { } simulation)
            return;
        _chosen = id;
        Details(simulation);
    }

    /// <summary>What the trader pays for one now: their town's price, less what it remembers the character selling it (WP14) and what the wear costs (WP3b); null when they do not buy it.</summary>
    private static long? SellPrice(Simulation simulation, string npcId, string defId, ItemWear? wear, int quality, NotableRecord? notable) =>
        simulation.Offer(npcId, defId, wear, quality, notable);

    private void Submit(GameCommand command) => _session.Submit(command);

    /// <summary>
    /// The pack's list stands as tall as its rows, up to what the band leaves under the totals and over the details; past that it
    /// scrolls. Measured once the new rows have their sizes.
    /// </summary>
    private void FitLists()
    {
        // Totals, bag slots and filters have variable height. Measure their actual
        // laid-out space instead of subtracting a fixed chrome estimate.
        float chrome=_carriedScroll.GlobalPosition.Y-_sheet.Panel.GlobalPosition.Y;
        float room=Math.Max(0,_sheet.Room-chrome-UiKit.Padding);
        // Reserve the same detail strip for every item. Hover never changes the list's height or scroll.
        bool beside=!_context.Visible;
        float detailHeight=beside?100:Math.Clamp(room*.35f,140,240);
        _detailScroll.CustomMinimumSize=new Vector2(0,detailHeight);
        Fit(_carriedScroll, _carried, beside?room:room-detailHeight-16);
        Fit(_containerScroll, _container, room - _contextHead.GetCombinedMinimumSize().Y - 12);
    }

    /// <summary>A list's scroll stands as tall as its rows, up to <paramref name="most"/>; past that it scrolls.</summary>
    private static void Fit(ScrollContainer scroll, VBoxContainer list, float most) =>
        scroll.CustomMinimumSize = new Vector2(0, Math.Max(80, Math.Min(list.GetCombinedMinimumSize().Y, most)));

    /// <summary>An item's row: its icon in the HUD's slot frame, its name and count over a secondary line; the pointer chooses it.</summary>
    private Row ItemRow(string id, string defId, int quality, int count, string secondary)
    {
        var row = UiKit.Row(HudStyle.IconSlot(_icons.Binds(defId) ? _icons.For(defId) : null, 32), Names.Item(_session, defId, quality) +
            (count > 1 ? $"  ×{count}" : ""), secondary,height:36);
        row.Root.MouseFilter = Control.MouseFilterEnum.Pass;
        row.Root.MouseEntered += () => Choose(id);
        row.Root.SetMeta("item_definition", defId);
        row.Root.SetMeta("item_reference", id);
        return row;
    }

    public void CraftGlint(string itemId, int quality)
    {
        foreach (var row in FindChildren("*", nameof(Control), true, false).OfType<Control>()
                     .Where(c => c.HasMeta("item_definition") && c.GetMeta("item_definition").AsString() == itemId))
        {
            float peak = Settings.SettingsStore.Current.Profile.Camera.ReduceFlashes ? 1.08f : 1.2f + Math.Clamp(quality,0,2) * .1f;
            row.Modulate = new Color(peak, peak, 1);
            row.CreateTween().TweenProperty(row, "modulate", Colors.White, 1.2).SetTrans(Tween.TransitionType.Sine);
        }
    }

    /// <summary>A compact row button that chooses its row's item when it takes the focus.</summary>
    private Button Button(string id, string text, Action pressed, UiKit.Kind kind = UiKit.Kind.Normal)
    {
        var button = UiKit.Action(text, pressed, kind, compact: true, id: $"{id}/{text}");
        button.FocusEntered += () => Choose(id);
        button.MouseEntered += () => Choose(id);
        return button;
    }

    private static MarginContainer Indent(Control content)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 48);
        margin.AddChild(content);
        return margin;
    }
}
