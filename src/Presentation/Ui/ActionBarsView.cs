// UNNAMED Presentation - the action bars: up to six bars of twelve slots, the main one at the bottom centre, the others shown or hidden
// above it or at the right edge. Godot presentation only: no gameplay state lives here (D-11); every change goes out as a command.

using System.Collections.Immutable;
using Godot;
using UNNAMED.Domain.Progression;
using UNNAMED.Presentation.Settings;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The bars. The main bar is the paged one (Ctrl and 1 to 6, Alt and the wheel, or its own arrows change which of the six it shows; paging is optional - further bars can be shown instead); further bars are shown
/// or hidden from its menu, stacked above it or standing at the right edge. A slot is used by its key or a click, filled by dragging from the
/// Skills screen or the inventory (or another slot), cleared by a right click, and all of it is switched off by the lock. Nothing here pauses
/// the world: the bars take the pointer only while the pointer is free, as every screen does.
/// </summary>
public sealed partial class ActionBarsView : Control
{
    public const string DragKind = "action";
    private const float Gap = 3;

    /// <summary>The one slot size, in interface pixels at 1080 lines: the main bar and every further bar, so no bar reads smaller than another.</summary>
    public const float SlotPx = 44;

    /// <summary>The icons by key; set by the HUD.</summary>
    public HudIcons Icons { get; set; } = HudIcons.None;

    /// <summary>(bar, slot, definition ID or null): put something on a slot, or clear it.</summary>
    public Action<int, int, string?>? Assign { get; set; }

    /// <summary>(bar, slot): the slot was clicked.</summary>
    public Action<int, int>? Use { get; set; }

    /// <summary>The bar the main row shows (0 to 5), asked to change.</summary>
    public Action<int>? PageTo { get; set; }

    /// <summary>The player touched the bars: opened the menu, paged, or put something on a slot (the hint that names the bars waits on this).</summary>
    public Action? Touched { get; set; }

    /// <summary>The vitals sit here, between the further bars and the main bar: the HUD puts its gauges in it.</summary>
    public VBoxContainer VitalsHost { get; } = new();

    private readonly VBoxContainer _dock = new();
    private readonly VBoxContainer _stack = new();
    private readonly PanelContainer _main = new();
    private readonly HBoxContainer _mainRow = new();
    private readonly HBoxContainer _side = new();
    private readonly PopupMenu _menu = new();
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
        _dock.AddThemeConstantOverride("separation", 2);
        _dock.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_dock);

        _stack.AddThemeConstantOverride("separation", 2);
        _stack.MouseFilter = MouseFilterEnum.Ignore;
        _dock.AddChild(_stack);
        VitalsHost.MouseFilter = MouseFilterEnum.Ignore;
        _dock.AddChild(VitalsHost);

        _main.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(6, 3));
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

        AddChild(_menu);
        var popup = _menu;
        popup.HideOnCheckableItemSelection = false;
        for (int bar = 1; bar < QuickBar.Bars; bar++)
            popup.AddCheckItem($"Show bar {bar + 1}", bar);
        popup.AddSeparator();
        popup.AddCheckItem("Further bars at the right edge", 100);
        popup.IdPressed += id =>
        {
            Touched?.Invoke();
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
        string signature = $"{page}|{ui.BarsShown}|{ui.BarsAtSide}|{ui.BarsLocked}|{HudStyle.TextScale}|{(int)GetViewportRect().Size.X}|" + string.Join("/", views.Select((bar, b) =>
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
        foreach (var child in _mainRow.GetChildren())
            child.QueueFree();
        foreach (var child in _stack.GetChildren())
            child.QueueFree();
        foreach (var child in _side.GetChildren())
            child.QueueFree();

        _slot = SlotSize();
        _mainRow.AddChild(BarRow(_shownPage, vertical: false, keys: true));

        // At the bar's end, small: page up and down (the main bar's own arrows), and the lock and the bars menu as icon buttons.
        var popup = _menu;
        for (int bar = 1; bar < QuickBar.Bars; bar++)
            popup.SetItemChecked(popup.GetItemIndex(bar), (ui.BarsShown >> (bar - 1) & 1) == 1);
        popup.SetItemChecked(popup.GetItemIndex(100), ui.BarsAtSide);
        var controls = new GridContainer { Columns = 2 };
        controls.AddThemeConstantOverride("h_separation", 2);
        controls.AddThemeConstantOverride("v_separation", 2);
        float half = (_slot - 2) / 2;
        controls.AddChild(new MiniButton(MiniButton.Glyph.Up, half, $"Page the main bar up: bar {ActionBarModel.Page(_shownPage, -1) + 1} (now bar {_shownPage + 1}). Ctrl and 1 to 6, or Alt and the wheel, do the same.",
            () =>
            {
                Touched?.Invoke();
                PageTo?.Invoke(ActionBarModel.Page(_shownPage, -1));
            }));
        controls.AddChild(new MiniButton(MiniButton.Glyph.Down, half, $"Page the main bar down: bar {ActionBarModel.Page(_shownPage, 1) + 1} (now bar {_shownPage + 1}).",
            () =>
            {
                Touched?.Invoke();
                PageTo?.Invoke(ActionBarModel.Page(_shownPage, 1));
            }));
        controls.AddChild(new MiniButton(ui.BarsLocked ? MiniButton.Glyph.Locked : MiniButton.Glyph.Unlocked, half,
            ui.BarsLocked ? "The bars are locked: nothing can be dragged onto, off or between slots. Click to unlock." : "Lock the bars: nothing can be dragged onto, off or between slots.",
            () =>
            {
                var prefs = SettingsStore.Current;
                SettingsStore.Set(prefs with { Profile = prefs.Profile with { Interface = prefs.Profile.Interface with { BarsLocked = !prefs.Profile.Interface.BarsLocked } } });
            }, on: ui.BarsLocked));
        MiniButton? menu = null;
        menu = new MiniButton(MiniButton.Glyph.Menu, half, "Bars: show or hide bars 2 to 6, or stand them at the right edge.",
            () => { Touched?.Invoke(); _menu.Popup(new Rect2I((Vector2I)(menu!.GlobalPosition + new Vector2(0, -(_menu.GetContentsMinimumSize().Y + 4))), Vector2I.Zero)); });
        controls.AddChild(menu);
        _mainRow.AddChild(controls);

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
        // No title column: a tooltip names the bar.
        var frame = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Pass, TooltipText = $"Bar {bar + 1}" };
        frame.AddThemeStyleboxOverride("panel", HudStyle.PanelBox(6, 2, quiet: true));
        frame.AddChild(BarRow(bar, vertical: false, keys: false));
        return frame;
    }

    private BoxContainer BarRow(int bar, bool vertical, bool keys)
    {
        BoxContainer row = vertical ? new VBoxContainer() : new HBoxContainer();
        row.AddThemeConstantOverride("separation", (int)Gap);
        row.MouseFilter = MouseFilterEnum.Ignore;
        var slots = bar < _views.Length ? _views[bar] : ImmutableArray<ActionSlotView>.Empty;
        foreach (var view in slots)
            row.AddChild(new ActionSlot(this, view, keys, _slot));
        return row;
    }

    private float _slot = SlotPx;

    /// <summary>
    /// The slot size now: 44 px at 1080 lines, grown with the text size, and shrunk so the twelve slots and the end buttons always fit the
    /// width of the interface (an enlarged interface leaves less of it).
    /// </summary>
    private float SlotSize()
    {
        float wanted = SlotPx * Math.Max(1f, HudStyle.TextScale);
        float room = GetViewportRect().Size.X - 2 * HudView.Margin - 40;
        float fit = (room - 24) / QuickBar.Slots - Gap;
        return Math.Clamp(Math.Min(wanted, fit), 28f, wanted);
    }

    /// <summary>A small square button drawn in the HUD's iron and ember, so it needs no font glyph: a page arrow, the lock, the bars menu.</summary>
    private sealed partial class MiniButton : Control
    {
        public enum Glyph { Up, Down, Locked, Unlocked, Menu }

        private readonly Glyph _glyph;
        private readonly Action _press;
        private readonly bool _on;

        public MiniButton(Glyph glyph, float size, string tip, Action press, bool on = false)
        {
            (_glyph, _press, _on) = (glyph, press, on);
            CustomMinimumSize = new Vector2(size, size);
            TooltipText = tip;
            MouseFilter = MouseFilterEnum.Stop;
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                _press();
                AcceptEvent();
            }
        }

        public override void _Draw()
        {
            var box = new Rect2(Vector2.Zero, Size);
            bool hover = GetGlobalRect().HasPoint(GetGlobalMousePosition());
            DrawRect(box, new Color(HudStyle.Iron, hover ? 0.9f : 0.55f));
            DrawRect(box, hover ? HudStyle.Ember : HudStyle.IronBright, false, 1);
            var ink = _on ? HudStyle.Ember : HudStyle.Bone;
            float s = Size.X, m = s * 0.28f;
            switch (_glyph)
            {
                case Glyph.Up:
                    DrawColoredPolygon(new[] { new Vector2(s / 2, m), new Vector2(s - m, s - m), new Vector2(m, s - m) }, ink);
                    break;
                case Glyph.Down:
                    DrawColoredPolygon(new[] { new Vector2(m, m), new Vector2(s - m, m), new Vector2(s / 2, s - m) }, ink);
                    break;
                case Glyph.Menu:
                    for (int i = 0; i < 3; i++)
                        DrawRect(new Rect2(m, m + i * (s - 2 * m) / 2 - 1, s - 2 * m, 2), ink);
                    break;
                default:
                    // A padlock: a body, and a shackle that is shut or lifted clear.
                    DrawRect(new Rect2(m, s * 0.5f, s - 2 * m, s * 0.3f), ink);
                    var centre = new Vector2(s / 2, s * 0.5f);
                    float r = (s - 2 * m) * 0.36f;
                    if (_glyph == Glyph.Locked)
                        DrawArc(centre, r, Mathf.Pi, Mathf.Tau, 12, ink, 1.5f);
                    else
                        DrawArc(centre + new Vector2(r, -r * 0.5f), r, Mathf.Pi, Mathf.Tau, 12, ink, 1.5f);
                    break;
            }
        }

        public override void _Notification(int what)
        {
            if (what is (int)NotificationMouseEnter or (int)NotificationMouseExit)
                QueueRedraw();
        }
    }

    /// <summary>One slot: the icon framed, its key on the corner, its note under it; dimmed with a mark when it cannot be used now.</summary>
    private sealed partial class ActionSlot : Control
    {
        private readonly ActionBarsView _owner;
        private readonly ActionSlotView _view;

        /// <summary>A corner label: small, outlined so it reads over any picture, never taking the slot's own words' place.</summary>
        private static Label Corner(string text, Color ink, float size, bool top, bool right)
        {
            var label = HudStyle.Text(HudStyle.Strong, 13, ink).Say(text);
            label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
            label.AddThemeConstantOverride("outline_size", 4);
            label.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            label.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            label.CustomMinimumSize = new Vector2(size - 3, size - 1);
            label.Position = new Vector2(1, 1);
            label.MouseFilter = MouseFilterEnum.Ignore;
            return label;
        }

        public ActionSlot(ActionBarsView owner, ActionSlotView view, bool keys, float size)
        {
            _owner = owner;
            _view = view;
            // One size for every slot of every bar; nothing hangs under it: the key is at its top right, the count at its bottom right.
            CustomMinimumSize = new Vector2(size, size);
            MouseFilter = MouseFilterEnum.Stop;
            TooltipText = Tip(view, keys);
            var holder = new Control { CustomMinimumSize = new Vector2(size, size), MouseFilter = MouseFilterEnum.Ignore };
            AddChild(holder);
            // An empty slot is its bare frame; a gear set carries its place in words, since it has no picture.
            var picture = view.Empty || view.Kind == ActionSlotKind.GearSet ? null : owner.Icons.For(view.Id);
            var frame = HudStyle.IconSlot(picture, size - 2, active: view.Active);
            frame.Position = new Vector2(1, 0);
            if (view.Empty || !view.Ready)
                frame.Modulate = new Color(1, 1, 1, view.Empty ? 0.45f : 0.55f);
            holder.AddChild(frame);
            // What the slot holds, said in the middle: a gear set's place, or - with no picture bound - its name in short, so a slot is never a bare
            // frame holding something. The corner labels sit around it, never over it.
            string? centre = view.Kind == ActionSlotKind.GearSet ? view.Note.Replace("Set ", "")
                : picture is null && !view.Empty ? (view.Name.Length > 4 ? view.Name[..4] : view.Name) : null;
            if (centre is not null)
            {
                var words = HudStyle.Text(HudStyle.Caps, 16, view.Kind == ActionSlotKind.GearSet ? HudStyle.Brass : HudStyle.Ash, caps: true).Say(centre);
                words.CustomMinimumSize = new Vector2(size, size);
                words.HorizontalAlignment = HorizontalAlignment.Center;
                words.VerticalAlignment = VerticalAlignment.Center;
                words.MouseFilter = MouseFilterEnum.Ignore;
                holder.AddChild(words);
            }
            if (keys && view.Key.Length > 0)
                holder.AddChild(Corner(view.Key, HudStyle.Ember, size, top: true, right: true));
            if (view.Shape.Length > 0)
                holder.AddChild(Corner(view.CooldownSeconds > 0 ? $"{view.CooldownSeconds}s" : view.Shape, HudStyle.Ember, size, top: true, right: false));
            if (view.Kind != ActionSlotKind.GearSet && view.Note.Length > 0)
                holder.AddChild(Corner(view.Note, view.Ready ? HudStyle.Bone : HudStyle.Wound, size, top: false, right: true));
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
