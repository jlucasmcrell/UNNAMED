// UNNAMED Presentation - the inventory, container and crafting panels (PROTOTYPE.md §4.1 UI panels: inventory, equipment, crafting)
// Godot presentation only: every button submits a command; nothing here changes state (D-11)

using Godot;
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
    private readonly VBoxContainer _details = new();
    private Sheet _sheet = null!;
    private GameSession _session = null!;
    private PlayerController _controller = null!;
    private HudIcons _icons = HudIcons.None;

    /// <summary>The item the details show: a carried stack's ID, or a container's or trader's entry ref.</summary>
    private string? _chosen;

    /// <summary>The container open alongside the inventory, if any.</summary>
    public string? OpenContainer { get; private set; }

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

    /// <summary>Item icons from the icon manifest (the Phase-1 asset integration); an item without one keeps an empty slot.</summary>
    public void UseIcons(HudIcons icons) => _icons = icons;

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "Inventory", "inventory", UiKit.Standard, scrolls: false);
        _totals.AddThemeConstantOverride("separation", 24);
        _sheet.Body.AddChild(_totals);

        var columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 24);
        var pack = new VBoxContainer { CustomMinimumSize = new Vector2(PackWidth, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        pack.AddThemeConstantOverride("separation", 16);
        _carried.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _carried.AddThemeConstantOverride("separation", 4);
        _carriedScroll.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        _carriedScroll.AddChild(_carried);
        pack.AddChild(_carriedScroll);
        _details.AddThemeConstantOverride("separation", 6);
        pack.AddChild(_details);
        columns.AddChild(pack);

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
        foreach (var list in new Control[] { _carried, _container, _details, _contextHead })
            list.MinimumSizeChanged += () => Callable.From(FitLists).CallDeferred();
        _sheet.OnShown(() =>
        {
            if (UiKit.FocusedId(_sheet.Root) is null)
                UiKit.FirstFocusable(_sheet.Root)?.GrabFocus();
        });
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
        _sheet.Shape(context ? UiKit.Wide : UiKit.Standard);
        _context.Visible = context;
        Fill(simulation);
        Details(simulation);
        Callable.From(FitLists).CallDeferred();
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
        _totals.AddChild(carried);
        _totals.AddChild(UiKit.Stat("Stacks", $"{player.Inventory.Length} / {simulation.Setup.Items.Inventory.StackSlots}", null, 18));
        _totals.AddChild(UiKit.Stat("Coin", $"{player.Currency}", HudStyle.Brass, 18));
        _totals.AddChild(UiKit.Stat("Armour", $"{player.Armor}", null, 18));

        UiKit.Clear(_carried);
        var quick = _controller.QuickItems().Select(q => q.DefId).ToList();
        var worn = player.Inventory.Where(e => player.Equipment.ContainsValue(e.ItemId));
        var rest = player.Inventory.Where(e => !player.Equipment.ContainsValue(e.ItemId))
            .OrderBy(e => Names.Of(_session, e.DefId), StringComparer.Ordinal);
        if (player.Inventory.IsEmpty)
            _carried.AddChild(UiKit.EmptyState("Carrying nothing", "Your pack is empty."));
        foreach (var entry in worn.Concat(rest))
        {
            var definition = catalog.Find(entry.DefId);
            var slot = player.Equipment.FirstOrDefault(kv => kv.Value == entry.ItemId);
            bool equipped = player.Equipment.ContainsValue(entry.ItemId);
            string id = entry.ItemId.Value;
            var row = ItemRow(id, entry.DefId, entry.Quality, entry.Count, equipped ? $"worn · {Names.Slot(slot.Key)}" : "");
            if (quick.IndexOf(entry.DefId) is var q and >= 0)
                row.Root.AddChild(new HudView.KeyCap(HelpPanel.Key($"quick_{q + 1}"), 15));
            if (definition?.Slot is not null)
            {
                row.Root.AddChild(Button(id, equipped ? "Unequip" : "Equip", () => Submit(equipped
                    ? new UnequipCommand(simulation.PlayerId, slot.Key)
                    : new EquipCommand(simulation.PlayerId, entry.ItemId))));
            }
            if (simulation.Setup.Combat.UseEffects.ContainsKey(entry.DefId))
                row.Root.AddChild(Button(id, "Use", () => Submit(new UseItemCommand(simulation.PlayerId, entry.ItemId))));
            if (simulation.Setup.Magic.Teaches.ContainsKey(entry.DefId))
                row.Root.AddChild(Button(id, "Read", () => Submit(new UseItemCommand(simulation.PlayerId, entry.ItemId))));
            // Remains take nothing in: the world takes a body away with whatever lies in it.
            if (OpenContainer is { } key && !simulation.Creatures.Any(c => c.Condition == CreatureCondition.Corpse && c.CorpseKey == key))
                row.Root.AddChild(Button(id, "Put", () => Submit(new MoveItemCommand(simulation.PlayerId, entry.ItemId.Value, ItemPlace.Carried, ItemPlace.In(key), entry.Count))));
            if (OpenTrader is { } trader && SellPrice(simulation, trader, entry.DefId) is { } each && each > 0 && !equipped)
            {
                row.Root.AddChild(Button(id, $"Sell {each * entry.Count}", () => Submit(new SellCommand(simulation.PlayerId, trader, entry.ItemId, entry.Count))));
                if (entry.Count > 1)
                    row.Root.AddChild(Button(id, $"Sell 1 ({each})", () => Submit(new SellCommand(simulation.PlayerId, trader, entry.ItemId, 1))));
            }
            row.Root.AddChild(Button(id, "Drop", () => Submit(new MoveItemCommand(simulation.PlayerId, entry.ItemId.Value, ItemPlace.Carried, ItemPlace.Ground, entry.Count)),
                UiKit.Kind.Destructive));
            _carried.AddChild(row.Root);
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

    private void ShowContainer(Simulation simulation, string open, ContainerView view)
    {
        var head = UiKit.Section(Names.Place(_session, open), $"{view.Items.Length} / {view.Site.StackSlots} stacks");
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
        int Carried(string defId) => player.Inventory.Where(e => e.DefId == defId && !player.Equipment.ContainsValue(e.ItemId)).Sum(e => e.Count);
        foreach (var recipe in recipes)
        {
            var row = ItemRow("r:" + recipe.Id, recipe.OutputItemId, Quality.Standard, recipe.OutputCount, "");
            row.Root.AddChild(UiKit.Chip($"Complexity {recipe.Complexity}", HudStyle.Iron, HudStyle.Ash));
            var short_ = recipe.Inputs.Where(i => Carried(i.ItemId) < i.Count).ToList();
            var make = Button("r:" + recipe.Id, "Make", () => Submit(new CraftCommand(simulation.PlayerId, recipe.Id)), UiKit.Kind.Primary);
            make.Disabled = short_.Count > 0;
            row.Root.AddChild(make);
            var inputs = new HFlowContainer();
            inputs.AddThemeConstantOverride("h_separation", 6);
            inputs.AddThemeConstantOverride("v_separation", 4);
            foreach (var input in recipe.Inputs)
            {
                int have = Carried(input.ItemId);
                var tone = have >= input.Count ? HudStyle.Lichen : HudStyle.Wound;
                inputs.AddChild(UiKit.Chip($"{input.Count} {Names.Of(_session, input.ItemId)} · have {have}", tone));
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
        var (defId, quality) = Chosen(simulation);
        if (defId is null)
        {
            _details.AddChild(UiKit.Section("Item"));
            _details.AddChild(UiKit.Quiet("Point at or move to an item to see what it does."));
            return;
        }
        var setup = simulation.Setup;
        var item = setup.Items.Catalog.Find(defId);
        var head = UiKit.Section(Names.Item(_session, defId, quality), item is null ? "" : Names.Words(item.Category));
        _details.AddChild(head);
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 24);
        grid.AddThemeConstantOverride("v_separation", 2);
        void Fact(string label, string value, Color? tone = null)
        {
            var stat = UiKit.Stat(label, value, tone, 18);
            stat.CustomMinimumSize = new Vector2(250, 0);
            grid.AddChild(stat);
        }
        var player = simulation.Player;
        ItemDefinition? Worn(EquipSlot slot) => player.Equipment.TryGetValue(slot, out var worn)
            && player.Inventory.FirstOrDefault(e => e.ItemId == worn) is { } entry && entry.DefId != defId ? setup.Items.Catalog.Find(entry.DefId) : null;
        var notes = new List<(string Text, Color Tone)>();
        if (item?.Weapon is { } weapon)
        {
            Fact("Damage", weapon.DamageMin == weapon.DamageMax ? $"{weapon.DamageMin}" : $"{weapon.DamageMin}–{weapon.DamageMax}");
            Fact("Kind", Names.Words(weapon.DamageType));
            Fact("Reach", $"{weapon.ReachMm / 1000.0:0.0#} m");
            if (weapon.Ranged)
                Fact("Draw", $"{weapon.DrawMs / 1000.0:0.0#} s");
            else if (weapon.AttackMs > 0)
                Fact("Swing", $"{weapon.AttackMs / 1000.0:0.0#} s");
            if (weapon.SkillId is { } skill)
                Fact("Skill", Names.Of(_session, skill));
            if (weapon.TwoHanded)
                notes.Add(("Two-handed: the off hand stays empty.", HudStyle.Ash));
            if (Worn(EquipSlot.MainHand)?.Weapon is { } held)
            {
                double delta = (weapon.DamageMin + weapon.DamageMax - held.DamageMin - held.DamageMax) / 2.0;
                string than = Names.Of(_session, Worn(EquipSlot.MainHand)!.Id);
                notes.Add((delta == 0 ? $"Hits as hard as your {than}." : $"{Math.Abs(delta):0.#} {(delta > 0 ? "more" : "less")} damage a blow than your {than}.",
                    delta > 0 ? HudStyle.Lichen : delta < 0 ? HudStyle.Wound : HudStyle.Ash));
            }
        }
        if (item is { ArmorValue: > 0 } || item?.Slot is not null && item.Weapon is null)
        {
            Fact("Armour", $"+{item!.ArmorValue}");
            Fact("Worn on", Names.Slot(item.Slot!.Value));
            if (Worn(item.Slot!.Value) is { } other)
            {
                int delta = item.ArmorValue - other.ArmorValue;
                notes.Add((delta == 0 ? $"As much armour as your {Names.Of(_session, other.Id)}."
                    : $"{Math.Abs(delta)} {(delta > 0 ? "more" : "less")} armour than your {Names.Of(_session, other.Id)}.",
                    delta > 0 ? HudStyle.Lichen : delta < 0 ? HudStyle.Wound : HudStyle.Ash));
            }
        }
        if (setup.Combat.UseEffects.TryGetValue(defId, out string? effectId) && setup.Combat.Effects.TryGetValue(effectId, out var effect))
            notes.Add((Uses(effect), HudStyle.Lichen));
        if (setup.Magic.Teaches.TryGetValue(defId, out var teaches) && teaches.Length > 0)
            notes.Add(($"Reading it teaches {string.Join(", ", teaches.Select(t => Names.Of(_session, t)))}.", HudStyle.Lichen));
        if (item is not null)
        {
            Fact("Weight", $"{item.WeightGrams / 1000.0:0.0#} kg");
            Fact("Worth", $"{item.ValueBase} coin", HudStyle.Brass);
            foreach (var (attribute, minimum) in item.Requirements.Attributes)
            {
                int value = ProgressionEngine.AttributeValue(player.Progression, attribute, setup.Progression);
                Fact($"Needs {ProgressionKeys.Key(attribute)}", $"{minimum}", value >= minimum ? HudStyle.Lichen : HudStyle.Wound);
            }
            foreach (var (skill, minimum) in item.Requirements.Skills)
            {
                int level = ProgressionEngine.SkillLevel(player.Progression, skill);
                Fact($"Needs {Names.Of(_session, skill)}", $"{minimum}", level >= minimum ? HudStyle.Lichen : HudStyle.Wound);
            }
        }
        if (quality != Quality.Standard)
            notes.Add((quality > 0 ? "Fine work: better than the common make." : "Crude work: worse than the common make.", quality > 0 ? HudStyle.Lichen : HudStyle.Wound));
        _details.AddChild(grid);
        foreach (var (text, tone) in notes)
            _details.AddChild(UiKit.Words(text, 17, tone));
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
    private (string? DefId, int Quality) Chosen(Simulation simulation)
    {
        if (_chosen is null)
            return (null, 0);
        if (_chosen.StartsWith("c:", StringComparison.Ordinal) && OpenContainer is { } open
            && simulation.Containers.FirstOrDefault(c => c.Site.Key == open)?.Items.FirstOrDefault(i => "c:" + i.Ref == _chosen) is { } inside)
            return (inside.DefId, inside.Quality);
        if (_chosen.StartsWith("w:", StringComparison.Ordinal) && OpenTrader is { } trader
            && simulation.Wares(trader)?.Wares.FirstOrDefault(w => "w:" + w.Ref == _chosen) is { } ware)
            return (ware.ItemId, ware.Quality);
        if (_chosen.StartsWith("r:", StringComparison.Ordinal) && simulation.Setup.Crafting.Recipes.TryGetValue(_chosen[2..], out var recipe))
            return (recipe.OutputItemId, Quality.Standard);
        return simulation.Player.Inventory.FirstOrDefault(e => e.ItemId.Value == _chosen) is { } entry ? (entry.DefId, entry.Quality) : (null, 0);
    }

    /// <summary>Choose an item for the details, as the pointer or the focus reaches its row.</summary>
    private void Choose(string id)
    {
        if (id == _chosen || _session.Simulation is not { } simulation)
            return;
        _chosen = id;
        Details(simulation);
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

    /// <summary>
    /// The pack's list stands as tall as its rows, up to what the band leaves under the totals and over the details; past that it
    /// scrolls. Measured once the new rows have their sizes.
    /// </summary>
    private void FitLists()
    {
        float room = _sheet.Room - 150;
        Fit(_carriedScroll, _carried, room - _details.GetCombinedMinimumSize().Y - 16);
        Fit(_containerScroll, _container, room - _contextHead.GetCombinedMinimumSize().Y - 12);
    }

    /// <summary>A list's scroll stands as tall as its rows, up to <paramref name="most"/>; past that it scrolls.</summary>
    private static void Fit(ScrollContainer scroll, VBoxContainer list, float most) =>
        scroll.CustomMinimumSize = new Vector2(0, Math.Max(80, Math.Min(list.GetCombinedMinimumSize().Y, most)));

    /// <summary>An item's row: its icon in the HUD's slot frame, its name and count over a secondary line; the pointer chooses it.</summary>
    private Row ItemRow(string id, string defId, int quality, int count, string secondary)
    {
        var row = UiKit.Row(HudStyle.IconSlot(_icons.Binds(defId) ? _icons.For(defId) : null, 32), Names.Item(_session, defId, quality) +
            (count > 1 ? $"  ×{count}" : ""), secondary);
        row.Root.MouseFilter = Control.MouseFilterEnum.Pass;
        row.Root.MouseEntered += () => Choose(id);
        return row;
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
