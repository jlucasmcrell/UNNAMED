// UNNAMED Presentation - the action bars: up to six bars of twelve slots, the main one at the bottom centre, the others shown or hidden
// above it or at the right edge. Godot presentation only: no gameplay state lives here (D-11); every change goes out as a command.

using System.Collections.Immutable;
using Godot;
using UNNAMED.Domain.Progression;
using UNNAMED.Presentation.Settings;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The bars. The main bar is the paged one (Shift and 1 to 6, or Alt and the wheel, change which of the six it shows); further bars are shown
/// or hidden from its menu, stacked above it or standing at the right edge. A slot is used by its key or a click, filled by dragging from the
/// Skills screen or the inventory (or another slot), cleared by a right click, and all of it is switched off by the lock. Nothing here pauses
/// the world: the bars take the pointer only while the pointer is free, as every screen does.
/// </summary>
public sealed partial class ActionBarsView : Control
{
    public const string DragKind = "action";
    private const float Gap = 5;

    /// <summary>The icons by key; set by the HUD.</summary>
    public HudIcons Icons { get; set; } = HudIcons.None;

    /// <summary>(bar, slot, definition ID or null): put something on a slot, or clear it.</summary>
    public Action<int, int, string?>? Assign { get; set; }

    /// <summary>(bar, slot): the slot was clicked.</summary>
    public Action<int, int>? Use { get; set; }

    /// <summary>The bar the main row shows (0 to 5), asked to change.</summary>
    public Action<int>? PageTo { get; set; }

    /// <summary>The vitals sit here, between the further bars and the main bar: the HUD puts its gauges in it.</summary>
    public VBoxContainer VitalsHost { get; } = new();

    private readonly VBoxContainer _dock = new();
    private readonly VBoxContainer _stack = new();
    private readonly PanelContainer _main = new();
    private readonly HBoxContainer _mainRow = new();
    private readonly HBoxContainer _side = new();
    private readonly Button _lock = new() { ToggleMode = true, Text = "Lock", FocusMode = FocusModeEnum.None };
    private readonly MenuButton _bars = new() { Text = "Bars", FocusMode = FocusModeEnum.None };
    private string _signature = string.Empty;
    private ImmutableArray<ImmutableArray<ActionSlotView>> _views = ImmutableArray<ImmutableArray<ActionSlotView>>.Empty;
    private int _shownPage;

    /// <summary>How tall the whole stack stands from the bottom edge, so the interaction prompt can keep above it.</summary>
    public float Height => _dock.Size.Y + HudView.Margin;

    /// <summary>The rectangles the bars take, for screens that keep clear of the HUD.</summary>
    public IEnumerable<Rect2> Rects()
    {
        if (_dock.IsVisibleInTree())
            yield return _dock.GetGlobalRect();
        if (_side.IsVisibleInTree() && _side.GetChildCount() > 0)
            yield return _side.GetGlobalRect();
    }

    private static bool Locked => SettingsStore.Current.Profile.Interface.BarsLocked;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _dock.AnchorLeft = _dock.AnchorRight = 0.5f;
        _dock.AnchorTop = _dock.AnchorBottom = 1;
        _dock.OffsetBottom = -HudView.Margin;
        _dock.GrowHorizontal = GrowDirection.Both;
        _dock.GrowVertical = GrowDirection.Begin;
        _dock.AddThemeConstantOverride("separation", 4);
        _dock.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_dock);

        _stack.AddThemeConstantOverride("separation", 3);
        _stack.MouseFilter = MouseFilterEnum.Ignore;
        _dock.AddChild(_stack);
        VitalsHost.MouseFilter = MouseFilterEnum.Ignore;
        _dock.AddChild(VitalsHost);

        _main.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(10, 6));
        _mainRow.AddThemeConstantOverride("separation", (int)Gap);
        _main.AddChild(_mainRow);
        _dock.AddChild(_main);

        _side.AnchorLeft = _side.AnchorRight = 1;
        // Above the combat log and below the tracked quest and the notices, which stand at the top right.
        _side.AnchorTop = _side.AnchorBottom = 1;
        _side.OffsetRight = -HudView.Margin;
        _side.OffsetBottom = -230;
        _side.GrowHorizontal = GrowDirection.Begin;
        _side.GrowVertical = GrowDirection.Begin;
        _side.AddThemeConstantOverride("separation", 3);
        _side.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_side);

        _lock.TooltipText = "Lock the bars: nothing can be dragged onto, off or between slots";
        _lock.Toggled += on =>
        {
            var prefs = SettingsStore.Current;
            if (prefs.Profile.Interface.BarsLocked != on)
                SettingsStore.Set(prefs with { Profile = prefs.Profile with { Interface = prefs.Profile.Interface with { BarsLocked = on } } });
        };
        var popup = _bars.GetPopup();
        popup.HideOnCheckableItemSelection = false;
        for (int bar = 1; bar < QuickBar.Bars; bar++)
            popup.AddCheckItem($"Show bar {bar + 1}", bar);
        popup.AddSeparator();
        popup.AddCheckItem("Further bars at the right edge", 100);
        popup.IdPressed += id =>
        {
            var prefs = SettingsStore.Current;
            var ui = prefs.Profile.Interface;
            ui = id == 100 ? ui with { BarsAtSide = !ui.BarsAtSide } : ui with { BarsShown = ui.BarsShown ^ (1 << (int)(id - 1)) };
            SettingsStore.Set(prefs with { Profile = prefs.Profile with { Interface = ui } });
        };
        SettingsStore.Changed += OnSettings;
        TreeExiting += () => SettingsStore.Changed -= OnSettings;
    }

    private void OnSettings() => _signature = string.Empty;

    /// <summary>
    /// The bars as the world has them. The main row shows bar <paramref name="page"/>; the further bars the preferences show come after it.
    /// Rebuilt only when something a slot shows changed.
    /// </summary>
    public void Set(ImmutableArray<ImmutableArray<ActionSlotView>> views, int page)
    {
        var ui = SettingsStore.Current.Profile.Interface;
        string signature = $"{page}|{ui.BarsShown}|{ui.BarsAtSide}|{ui.BarsLocked}|{HudStyle.TextScale}|" + string.Join("/", views.Select((bar, b) =>
            b == page || b > 0 && (ui.BarsShown >> (b - 1) & 1) == 1
                ? string.Join(",", bar.Select(s => $"{s.Id}:{s.Key}:{s.Note}:{s.Ready}:{s.CooldownSeconds}:{s.Active}:{s.Name}"))
                : ""));
        if (signature == _signature)
            return;
        _signature = signature;
        _views = views;
        _shownPage = page;
        Rebuild(ui);
    }

    private void Rebuild(InterfacePrefs ui)
    {
        // The two controls outlive a rebuild: they come off the row before it is freed.
        _lock.GetParent()?.RemoveChild(_lock);
        _bars.GetParent()?.RemoveChild(_bars);
        foreach (var child in _mainRow.GetChildren())
            child.QueueFree();
        foreach (var child in _stack.GetChildren())
            child.QueueFree();
        foreach (var child in _side.GetChildren())
            child.QueueFree();

        // The paging and the two menus, at the left of the main row.
        var controls = new VBoxContainer { CustomMinimumSize = new Vector2(64, 0) };
        controls.AddThemeConstantOverride("separation", 2);
        var pager = new HBoxContainer();
        pager.AddThemeConstantOverride("separation", 2);
        var prev = new Button { Text = "‹", FocusMode = FocusModeEnum.None, TooltipText = "Previous bar (Alt and the wheel)" };
        prev.Pressed += () => PageTo?.Invoke(ActionBarModel.Page(_shownPage, -1));
        var next = new Button { Text = "›", FocusMode = FocusModeEnum.None, TooltipText = "Next bar (Alt and the wheel)" };
        next.Pressed += () => PageTo?.Invoke(ActionBarModel.Page(_shownPage, 1));
        var page = HudStyle.Text(HudStyle.Caps, 16, HudStyle.Ember, caps: true).Say($"Bar {_shownPage + 1}");
        page.HorizontalAlignment = HorizontalAlignment.Center;
        page.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        pager.AddChild(prev);
        pager.AddChild(page);
        pager.AddChild(next);
        controls.AddChild(pager);
        _lock.SetPressedNoSignal(ui.BarsLocked);
        _lock.Text = ui.BarsLocked ? "Locked" : "Lock";
        controls.AddChild(_lock);
        var popup = _bars.GetPopup();
        for (int bar = 1; bar < QuickBar.Bars; bar++)
            popup.SetItemChecked(popup.GetItemIndex(bar), (ui.BarsShown >> (bar - 1) & 1) == 1);
        popup.SetItemChecked(popup.GetItemIndex(100), ui.BarsAtSide);
        controls.AddChild(_bars);
        _mainRow.AddChild(controls);
        _mainRow.AddChild(BarRow(_shownPage, vertical: false, keys: true));

        for (int bar = QuickBar.Bars - 1; bar >= 1; bar--)
        {
            if ((ui.BarsShown >> (bar - 1) & 1) == 0)
                continue;
            if (ui.BarsAtSide)
                _side.AddChild(BarRow(bar, vertical: true, keys: false));
            else
                _stack.AddChild(BarFrame(bar));
        }
        _side.Visible = ui.BarsAtSide;
    }

    private Control BarFrame(int bar)
    {
        var frame = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        frame.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(10, 4, quiet: true));
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", (int)Gap);
        var tag = HudStyle.Text(HudStyle.Caps, 16, HudStyle.Muted, caps: true).Say($"Bar {bar + 1}");
        tag.CustomMinimumSize = new Vector2(64, 0);
        tag.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(tag);
        row.AddChild(BarRow(bar, vertical: false, keys: false));
        frame.AddChild(row);
        return frame;
    }

    private BoxContainer BarRow(int bar, bool vertical, bool keys)
    {
        BoxContainer row = vertical ? new VBoxContainer() : new HBoxContainer();
        row.AddThemeConstantOverride("separation", (int)Gap);
        row.MouseFilter = MouseFilterEnum.Ignore;
        var slots = bar < _views.Length ? _views[bar] : ImmutableArray<ActionSlotView>.Empty;
        foreach (var view in slots)
            row.AddChild(new ActionSlot(this, view, keys, compact: vertical));
        return row;
    }

    /// <summary>One slot: the icon framed, its key on the corner, its note under it; dimmed with a mark when it cannot be used now.</summary>
    private sealed partial class ActionSlot : Control
    {
        private readonly ActionBarsView _owner;
        private readonly ActionSlotView _view;

        public ActionSlot(ActionBarsView owner, ActionSlotView view, bool keys, bool compact = false)
        {
            _owner = owner;
            _view = view;
            float size = (compact ? 30 : 38) * Math.Max(1f, HudStyle.TextScale);
            CustomMinimumSize = new Vector2(size + 8, size + (compact ? 6 : 22 * HudStyle.TextScale));
            MouseFilter = MouseFilterEnum.Stop;
            TooltipText = Tip(view, keys);
            var unit = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            unit.AddThemeConstantOverride("separation", 0);
            AddChild(unit);
            var holder = new Control { CustomMinimumSize = new Vector2(size + 4, size + 4), MouseFilter = MouseFilterEnum.Ignore };
            // An empty slot is its bare frame; a gear set carries its place in words, since it has no picture.
            var picture = view.Empty || view.Kind == ActionSlotKind.GearSet ? null : owner.Icons.For(view.Id);
            var frame = HudStyle.IconSlot(picture, size, active: view.Active);
            frame.Position = new Vector2(2, 0);
            if (view.Empty || !view.Ready)
                frame.Modulate = new Color(1, 1, 1, view.Empty ? 0.45f : 0.55f);
            holder.AddChild(frame);
            if (view.Kind == ActionSlotKind.GearSet)
            {
                var set = HudStyle.Text(HudStyle.Caps, 16, HudStyle.Brass, caps: true).Say(view.Note.Replace("Set ", ""));
                set.Position = new Vector2(2, 0);
                set.CustomMinimumSize = new Vector2(size, size);
                set.HorizontalAlignment = HorizontalAlignment.Center;
                set.VerticalAlignment = VerticalAlignment.Center;
                holder.AddChild(set);
            }
            // No picture bound to it: its name in short stands in, so a slot is never a bare frame holding something.
            if (picture is null && !view.Empty && view.Kind != ActionSlotKind.GearSet)
            {
                var short_ = HudStyle.Text(HudStyle.Caps, 16, HudStyle.Ash, caps: true).Say(view.Name.Length > 4 ? view.Name[..4] : view.Name);
                short_.Position = new Vector2(2, 0);
                short_.CustomMinimumSize = new Vector2(size, size);
                short_.HorizontalAlignment = HorizontalAlignment.Center;
                short_.VerticalAlignment = VerticalAlignment.Center;
                holder.AddChild(short_);
            }
            if (keys && view.Key.Length > 0)
                holder.AddChild(new HudView.KeyCap(view.Key, 16) { Position = new Vector2(-4, size - 18) });
            if (view.Shape.Length > 0)
            {
                var shape = HudStyle.Text(HudStyle.Strong, 18, HudStyle.Ember).Say(view.CooldownSeconds > 0 ? $"{view.CooldownSeconds}s" : view.Shape);
                shape.Position = new Vector2(size - 14, -2);
                holder.AddChild(shape);
            }
            unit.AddChild(holder);
            var note = HudStyle.Text(HudStyle.Caps, 16, view.Ready ? HudStyle.Muted : HudStyle.Wound, caps: true).Say(compact || view.Kind == ActionSlotKind.GearSet ? "" : view.Note);
            note.Visible = !compact;
            note.HorizontalAlignment = HorizontalAlignment.Center;
            note.CustomMinimumSize = new Vector2(size + 4, 0);
            unit.AddChild(note);
        }

        private static string Tip(ActionSlotView view, bool keys)
        {
            if (view.Empty)
                return Locked ? "Empty. The bars are locked." : "Empty. Drag something here from Skills or the inventory.";
            string tip = view.Name;
            if (keys && view.Key.Length > 0)
                tip += $"  [{view.Key}]";
            tip += $"\n{(view.Word.Length > 0 ? view.Word : "Ready")}{(view.Reason is { } why ? $" - {why}" : "")}";
            if (!Locked)
                tip += "\nRight click to clear";
            return tip;
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is not InputEventMouseButton { Pressed: true } click)
                return;
            if (click.ButtonIndex == MouseButton.Left && !_view.Empty)
                _owner.Use?.Invoke(_view.Bar, _view.Slot);
            else if (click.ButtonIndex == MouseButton.Right && !_view.Empty && !Locked)
                _owner.Assign?.Invoke(_view.Bar, _view.Slot, null);
            AcceptEvent();
        }

        public override Variant _GetDragData(Vector2 atPosition)
        {
            if (_view.Empty || Locked)
                return default;
            SetDragPreview(HudStyle.Text(HudStyle.Strong, 18, HudStyle.Bone).Say(_view.Name));
            return new Godot.Collections.Dictionary { ["kind"] = DragKind, ["id"] = _view.Id, ["bar"] = _view.Bar, ["slot"] = _view.Slot };
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data) => !Locked && Payload(data) is not null;

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            if (Payload(data) is not { } id)
                return;
            var dropped = data.AsGodotDictionary();
            _owner.Assign?.Invoke(_view.Bar, _view.Slot, id);
            // Taken from another slot: what was here goes there, so a drag between slots moves rather than copies.
            if (dropped.ContainsKey("bar") && dropped.ContainsKey("slot"))
            {
                int fromBar = dropped["bar"].AsInt32(), fromSlot = dropped["slot"].AsInt32();
                if (fromBar != _view.Bar || fromSlot != _view.Slot)
                    _owner.Assign?.Invoke(fromBar, fromSlot, _view.Empty ? null : _view.Id);
            }
        }
    }

    /// <summary>Whether something may be dragged onto a slot: an item, formula or technique, never the other things an ID can name.</summary>
    public static bool CanDrag(string id) => QuickBar.CanHold(id);

    /// <summary>What a drag carries, if it is something a slot may hold.</summary>
    private static string? Payload(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary)
            return null;
        var dictionary = data.AsGodotDictionary();
        return dictionary.TryGetValue("kind", out var kind) && kind.AsString() == DragKind && dictionary.TryGetValue("id", out var id) && QuickBar.CanHold(id.AsString())
            ? id.AsString() : null;
    }

    /// <summary>
    /// Make a row something that can be dragged onto a bar: <paramref name="row"/> carries <paramref name="id"/> (an item, formula, technique
    /// or gear-set switch) while the bars are unlocked.
    /// </summary>
    public static void Source(Control row, string id, string name)
    {
        row.MouseFilter = Control.MouseFilterEnum.Pass;
        row.SetDragForwarding(Callable.From<Vector2, Variant>(_ =>
        {
            if (Locked)
                return default;
            row.SetDragPreview(HudStyle.Text(HudStyle.Strong, 18, HudStyle.Bone).Say(name));
            return new Godot.Collections.Dictionary { ["kind"] = DragKind, ["id"] = id };
        }), default, default);
    }
}
