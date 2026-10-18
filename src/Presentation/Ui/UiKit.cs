// UNNAMED Presentation - the management screens' shared parts: the Field Instrument sheet, its rows and its states
// Godot presentation only: these build controls and hold no game state (D-11)
//
// Everything visual comes from HudStyle and HudView (the production HUD's faces, colours, key caps and gauges): this file arranges
// them at the menus' scale and adds the interactive states. It is a handful of factories, not a framework - no skinning, no
// navigation stack.

using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The Field Instrument management UI (the owner-approved Direction A): the production HUD's larger, calmer sibling. Every screen is
/// a <see cref="Sheet"/> in the management band - 1152 x 720 on the 1920 x 1080 reference canvas, centred, from y 128 - over a 35 %
/// world scrim, so the HUD stays visible and live around it.
/// </summary>
public static partial class UiKit
{
    public const float Small = 640, Standard = 880, Wide = 1152;
    public const float BandTop = 128, BandHeight = 720, MinHeight = 320, Padding = 24;
    public const float ListWidth = 368, ListGutter = 32;

    /// <summary>What a stable focus id is stored under, so a rebuilt screen can hand focus back to the same thing.</summary>
    private const string FocusKey = "ui_id";

    private static Theme? _theme;

    /// <summary>The one menu theme, built from <see cref="HudStyle"/>; set on each sheet's root because a CanvasLayer passes none down.</summary>
    public static Theme Theme => _theme ??= BuildTheme();

    private static Theme BuildTheme()
    {
        var theme = new Theme { DefaultFont = HudStyle.Body, DefaultFontSize = HudStyle.Sized(18) };
        theme.SetColor("font_color", "Label", HudStyle.Bone);
        theme.SetStylebox("panel", "PanelContainer", new StyleBoxEmpty());
        theme.SetStylebox("panel", "ScrollContainer", new StyleBoxEmpty());
        theme.SetStylebox("focus", "ScrollContainer", new StyleBoxEmpty());

        var disabled = new Color(HudStyle.Muted, 0.55f);
        foreach (string type in new[] { "Button", "OptionButton", "CheckButton" })
        {
            theme.SetFont("font", type, type == "Button" ? HudStyle.Caps : HudStyle.Body);
            theme.SetFontSize("font_size", type, HudStyle.Sized(17));
            foreach (string state in new[] { "font_color", "font_hover_color", "font_focus_color", "font_pressed_color", "font_hover_pressed_color" })
                theme.SetColor(state, type, HudStyle.Bone);
            theme.SetColor("font_disabled_color", type, disabled);
            theme.SetStylebox("normal", type, Box(HudStyle.Coal, HudStyle.Iron));
            theme.SetStylebox("hover", type, Box(HudStyle.Coal.Lightened(0.04f), HudStyle.IronBright));
            theme.SetStylebox("pressed", type, Box(new Color(HudStyle.Ember, 0.2f), HudStyle.Ember));
            theme.SetStylebox("hover_pressed", type, Box(new Color(HudStyle.Ember, 0.2f), HudStyle.Ember));
            theme.SetStylebox("disabled", type, Box(new Color(0, 0, 0, 0), new Color(HudStyle.Iron, 0.4f)));
            theme.SetStylebox("focus", type, Focus());
        }

        // One primary action a screen: ember label and border. Destructive: quiet until the pointer or the focus is on it.
        theme.SetTypeVariation("Primary", "Button");
        foreach (string state in new[] { "font_color", "font_hover_color", "font_focus_color", "font_pressed_color", "font_hover_pressed_color" })
            theme.SetColor(state, "Primary", HudStyle.Ember);
        theme.SetStylebox("normal", "Primary", Box(HudStyle.Coal, new Color(HudStyle.Ember, 0.7f)));
        theme.SetStylebox("hover", "Primary", Box(new Color(HudStyle.Ember, 0.08f), HudStyle.Ember));
        theme.SetTypeVariation("Destructive", "Button");
        theme.SetColor("font_color", "Destructive", HudStyle.Ash);
        theme.SetColor("font_hover_color", "Destructive", HudStyle.Wound);
        theme.SetColor("font_focus_color", "Destructive", HudStyle.Wound);
        theme.SetStylebox("hover", "Destructive", Box(HudStyle.Coal, HudStyle.Wound));

        // A list row: no frame of its own until it is pointed at, chosen or focused.
        theme.SetTypeVariation("ListRow", "Button");
        var clear = new Color(0, 0, 0, 0);
        theme.SetStylebox("normal", "ListRow", Box(clear, clear));
        theme.SetStylebox("hover", "ListRow", Box(HudStyle.Coal, HudStyle.IronBright));
        theme.SetStylebox("pressed", "ListRow", Box(new Color(HudStyle.Ember, 0.2f), clear));
        theme.SetStylebox("hover_pressed", "ListRow", Box(new Color(HudStyle.Ember, 0.2f), clear));
        theme.SetStylebox("disabled", "ListRow", Box(clear, clear));

        theme.SetStylebox("panel", "PopupMenu", Box(new Color(HudStyle.Soot, 0.98f), HudStyle.Iron, 4));
        theme.SetStylebox("hover", "PopupMenu", Box(HudStyle.Coal, HudStyle.Ember, 2));
        theme.SetFont("font", "PopupMenu", HudStyle.Body);
        theme.SetFontSize("font_size", "PopupMenu", HudStyle.Sized(17));
        theme.SetColor("font_color", "PopupMenu", HudStyle.Bone);
        theme.SetColor("font_hover_color", "PopupMenu", HudStyle.Ember);

        theme.SetStylebox("panel", "TooltipPanel", Box(new Color(HudStyle.Soot, 0.98f), HudStyle.IronBright, 10, 6));
        theme.SetFont("font", "TooltipLabel", HudStyle.Body);
        theme.SetFontSize("font_size", "TooltipLabel", HudStyle.Sized(16));
        theme.SetColor("font_color", "TooltipLabel", HudStyle.Bone);

        theme.SetStylebox("slider", "HSlider", Box(HudStyle.Track, HudStyle.Iron, 0, 3));
        theme.SetStylebox("grabber_area", "HSlider", Box(HudStyle.Brass, clear, 0, 3));
        theme.SetStylebox("grabber_area_highlight", "HSlider", Box(HudStyle.Brass, clear, 0, 3));
        theme.SetStylebox("focus", "HSlider", Focus());

        theme.SetStylebox("scroll", "VScrollBar", Box(HudStyle.Track, clear, 4, 0));
        theme.SetStylebox("grabber", "VScrollBar", Box(HudStyle.Iron, clear, 4, 0));
        theme.SetStylebox("grabber_highlight", "VScrollBar", Box(HudStyle.IronBright, clear, 4, 0));
        theme.SetStylebox("grabber_pressed", "VScrollBar", Box(HudStyle.Brass, clear, 4, 0));
        return theme;
    }

    /// <summary>A flat box: a fill, a 1 px border and the HUD's 2 px corners.</summary>
    internal static StyleBoxFlat Box(Color fill, Color border, float across = 12, float down = 4) => new()
    {
        BgColor = fill,
        BorderColor = border, BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        ContentMarginLeft = across, ContentMarginRight = across, ContentMarginTop = down, ContentMarginBottom = down,
    };

    /// <summary>Keyboard focus: a 2 px ember outline drawn just outside whatever state the control is in.</summary>
    private static StyleBoxFlat Focus() => new()
    {
        DrawCenter = false,
        BorderColor = HudStyle.Ember, BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        ExpandMarginLeft = 2, ExpandMarginTop = 2, ExpandMarginRight = 2, ExpandMarginBottom = 2,
    };

    /// <summary>Selected / current: an ember wash and a 3 px ember bar at the left - never the focus outline.</summary>
    private static StyleBoxFlat SelectedBox(bool hover) => new()
    {
        BgColor = new Color(HudStyle.Ember, hover ? 0.16f : 0.12f),
        BorderColor = HudStyle.Ember, BorderWidthLeft = 3,
        CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 4,
    };

    /// <summary>The sheet itself: soot at 0.94 in a hairline iron frame, with the sheet's padding.</summary>
    internal static StyleBoxFlat SheetBox() => new()
    {
        BgColor = new Color(HudStyle.Soot, 0.94f),
        BorderColor = HudStyle.Iron, BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        ContentMarginLeft = Padding, ContentMarginRight = Padding, ContentMarginTop = 8, ContentMarginBottom = Padding,
        ShadowColor = new Color(0, 0, 0, 0.35f), ShadowSize = 12,
    };

    /// <summary>
    /// A management screen's frame on <paramref name="layer"/>: the scrim, the sheet in the band, the title bar with the screen's
    /// own key and <c>[ESC] CLOSE</c>, the ticked rule, an optional hint and the body. With <paramref name="scrolls"/> the body
    /// scrolls under the fixed title bar beyond 720; without, the screen's panes scroll themselves.
    /// </summary>
    public static Sheet Sheet(CanvasLayer layer, string title, string? ownKey, float width = Standard, bool scrolls = true) =>
        new(layer, title, ownKey, width, scrolls);

    /// <summary>A section: a brass head in condensed capitals over a rule, with an optional muted note beside it.</summary>
    public static VBoxContainer Section(string heading, string note = "", Color? tone = null)
    {
        var section = new VBoxContainer();
        section.AddThemeConstantOverride("separation", 4);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 12);
        var title = HudStyle.Text(HudStyle.Caps, 18, tone ?? HudStyle.Brass, caps: true);
        title.Text = heading;
        head.AddChild(title);
        if (note.Length > 0)
        {
            var aside = Words(note, 16, HudStyle.Muted);
            aside.VerticalAlignment = VerticalAlignment.Center;
            head.AddChild(aside);
        }
        section.AddChild(head);
        section.AddChild(HudStyle.Rule(4));
        return section;
    }

    /// <summary>A label in the menus' type: Barlow, wrapping, at a size and colour.</summary>
    public static Label Words(string text, int size = 18, Color? color = null, Font? font = null)
    {
        var label = HudStyle.Text(font ?? HudStyle.Body, size, color ?? HudStyle.Bone);
        label.Text = text;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }

    /// <summary>A muted label in condensed capitals (16): what a value is.</summary>
    public static Label Caption(string text, Color? color = null, int size = 16)
    {
        var label = HudStyle.Text(HudStyle.Caps, size, color ?? HudStyle.Muted, caps: true);
        label.Text = text;
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }

    /// <summary>A value: a muted label at the left and the value at the right, in a colour; the value label is returned to update in place.</summary>
    public static HBoxContainer Stat(string label, string value, Color? color = null, int size = 20) => Stat(label, value, out _, color, size);

    public static HBoxContainer Stat(string label, string value, out Label shown, Color? color = null, int size = 20)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var caption = Caption(label);
        caption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(caption);
        shown = HudStyle.Text(HudStyle.Strong, size, color ?? HudStyle.Bone);
        shown.Text = value;
        shown.HorizontalAlignment = HorizontalAlignment.Right;
        shown.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(shown);
        return row;
    }

    /// <summary>The HUD's own gauge, at a size; <paramref name="marks"/> parts it (4, or 10 where a value is read exactly).</summary>
    public static HudView.Gauge Gauge(Color fill, float width, float height = 8, bool quarters = true, int marks = 4) =>
        new(fill, new Vector2(width, height), quarters, marks) { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };

    /// <summary>
    /// A key's icon in the HUD's slot frame at a size; an empty frame when the key has no icon bound, so rows keep their shape. A
    /// <paramref name="ghost"/> is an empty equipment slot: its tile is already dark, and it sits a little back from what is worn.
    /// </summary>
    public static PanelContainer Icon(HudIcons icons, string? key, float size, bool ghost = false)
    {
        var slot = HudStyle.IconSlot(icons.Binds(key) ? icons.For(key) : null, size);
        slot.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        if (ghost)
        {
            var box = HudStyle.SlotBox();
            box.BorderColor = HudStyle.Iron;
            slot.AddThemeStyleboxOverride("panel", box);
        }
        return slot;
    }

    /// <summary>A state tag: condensed capitals in a hairline frame, the word always there so colour never carries it alone.</summary>
    public static PanelContainer Chip(string text, Color border, Color? color = null)
    {
        var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        chip.AddThemeStyleboxOverride("panel", HudStyle.ChipBox(border));
        var label = HudStyle.Text(HudStyle.Caps, 16, color ?? border, caps: true);
        label.Text = text;
        chip.AddChild(label);
        return chip;
    }

    /// <summary>The HUD's key cap for an input action, read live from the input map; null when the action has no key.</summary>
    public static HudView.KeyCap? KeyCap(string action)
    {
        string key = HelpPanel.Key(action);
        return key == "unbound" ? null : new HudView.KeyCap(key, 16);
    }

    /// <summary>
    /// The shared row: an icon slot or state chip at the left, the name over a secondary line in the middle, and whatever the
    /// caller adds after it (values, chips, buttons) at the right - added straight to the returned row.
    /// </summary>
    public static Row Row(Control? left, string name, string secondary = "", Color? nameColor = null, float height = 40)
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, height) };
        row.AddThemeConstantOverride("separation", 12);
        if (left is not null)
        {
            left.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(left);
        }
        var middle = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        middle.AddThemeConstantOverride("separation", 0);
        var title = HudStyle.Text(HudStyle.Strong, 18, nameColor ?? HudStyle.Bone);
        title.Text = name;
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        middle.AddChild(title);
        var line = Words(secondary, 16, HudStyle.Ash);
        line.Visible = secondary.Length > 0;
        middle.AddChild(line);
        row.AddChild(middle);
        return new Row(row, title, line, middle);
    }

    /// <summary>
    /// A selectable list row (a list + detail screen's left pane): a button that holds the shared row grammar inside it, so the
    /// whole row takes the pointer and the focus. <paramref name="left"/> sits before the name (an icon slot or a level).
    /// </summary>
    public static ListRow ListRow(string id, string name, string secondary = "", Color? nameColor = null, float height = 40, Control? left = null)
    {
        var button = new Button
        {
            ThemeTypeVariation = "ListRow",
            // The row holds its words in a free-standing box, so it grows with the player's text size itself.
            CustomMinimumSize = new Vector2(0, height * HudStyle.TextScale),
            FocusMode = Control.FocusModeEnum.All,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        Id(button, id);
        var inner = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        inner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        inner.OffsetLeft = 12;
        inner.OffsetRight = -12;
        inner.AddThemeConstantOverride("separation", 10);
        if (left is not null)
        {
            left.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            inner.AddChild(left);
        }
        var middle = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        middle.AddThemeConstantOverride("separation", 0);
        var title = HudStyle.Text(HudStyle.Strong, 18, nameColor ?? HudStyle.Bone);
        title.Text = name;
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        middle.AddChild(title);
        var line = HudStyle.Text(HudStyle.Body, 16, HudStyle.Ash);
        line.Text = secondary;
        line.Visible = secondary.Length > 0;
        line.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        middle.AddChild(line);
        inner.AddChild(middle);
        var right = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        right.AddThemeConstantOverride("separation", 8);
        inner.AddChild(right);
        button.AddChild(inner);
        return new ListRow(button, title, line, middle, right);
    }

    /// <summary>Mark a row or button selected (ember wash and left bar), or clear it.</summary>
    public static void Select(Button button, bool selected)
    {
        if (selected)
        {
            button.AddThemeStyleboxOverride("normal", SelectedBox(false));
            button.AddThemeStyleboxOverride("hover", SelectedBox(true));
            button.AddThemeStyleboxOverride("disabled", SelectedBox(false));
        }
        else
        {
            button.RemoveThemeStyleboxOverride("normal");
            button.RemoveThemeStyleboxOverride("hover");
            button.RemoveThemeStyleboxOverride("disabled");
        }
    }

    public enum Kind { Normal, Primary, Destructive }

    /// <summary>A button: 40 high (32 compact), focusable, in one of the three variants.</summary>
    public static Button Action(string text, Action pressed, Kind kind = Kind.Normal, bool compact = false, string? id = null)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(compact ? 32 : 48, compact ? 32 : 40),
            FocusMode = Control.FocusModeEnum.All,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            ThemeTypeVariation = kind switch { Kind.Primary => "Primary", Kind.Destructive => "Destructive", _ => string.Empty },
        };
        button.Pressed += pressed;
        if (id is not null)
            Id(button, id);
        return button;
    }

    /// <summary>A note with a coloured bar at its left: a refusal (wound), something that needs the player (ember), a fact (iron).</summary>
    public static PanelContainer Callout(Color tone, string text)
    {
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = HudStyle.Coal,
            BorderColor = tone, BorderWidthLeft = 3,
            CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
            ContentMarginLeft = 14, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8,
        });
        box.AddChild(Words(text, 17));
        return box;
    }

    /// <summary>An intentional empty state: what is empty, one sentence about it, and an optional hint naming a key.</summary>
    public static VBoxContainer EmptyState(string head, string sentence, string hint = "", string? hintKey = null)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        var title = HudStyle.Text(HudStyle.Caps, 18, HudStyle.Ash, caps: true);
        title.Text = head;
        column.AddChild(title);
        column.AddChild(Words(sentence, 18));
        if (hint.Length > 0)
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            if (hintKey is not null && KeyCap(hintKey) is { } cap)
                line.AddChild(cap);
            line.AddChild(Words(hint, 16, HudStyle.Muted));
            column.AddChild(line);
        }
        return column;
    }

    /// <summary>A quiet one-line note inside a section: "nothing yet", in a colour.</summary>
    public static Label Quiet(string text, Color? color = null) => Words(text, 17, color ?? HudStyle.Ash);

    /// <summary>The dense template's two panes: a 368 list, a 32 gutter with an iron hairline, and the detail, each scrolling on its own.</summary>
    public static ListDetail ListDetail()
    {
        var root = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", (int)(ListGutter / 2));
        var listScroll = Scroll();
        listScroll.CustomMinimumSize = new Vector2(ListWidth, 0);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 4);
        listScroll.AddChild(list);
        var divider = new VSeparator { CustomMinimumSize = new Vector2(1, 0) };
        divider.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = HudStyle.Iron, Thickness = 1, Vertical = true });
        var detailScroll = Scroll();
        detailScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var detail = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        detail.AddThemeConstantOverride("separation", 24);
        detailScroll.AddChild(detail);
        root.AddChild(listScroll);
        root.AddChild(divider);
        root.AddChild(detailScroll);
        return new ListDetail(root, list, detail, listScroll, detailScroll);
    }

    /// <summary>A vertical scroll region that keeps the focused control in view.</summary>
    public static ScrollContainer Scroll() => new()
    {
        HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        FollowFocus = true,
        SizeFlagsVertical = Control.SizeFlags.ExpandFill,
    };

    /// <summary>Tag a control with a stable id, so focus can be handed back to it after a rebuild.</summary>
    public static T Id<T>(T control, string id) where T : Control
    {
        control.SetMeta(FocusKey, id);
        return control;
    }

    /// <summary>The stable id of whatever inside <paramref name="root"/> has the keyboard focus, or null.</summary>
    public static string? FocusedId(Control root)
    {
        var owner = root.IsInsideTree() ? root.GetViewport().GuiGetFocusOwner() : null;
        return owner is not null && root.IsAncestorOf(owner) && owner.HasMeta(FocusKey) ? owner.GetMeta(FocusKey).AsString() : null;
    }

    /// <summary>Give the focus to the control under <paramref name="root"/> tagged <paramref name="id"/>; false when none is.</summary>
    public static bool Refocus(Control root, string? id)
    {
        if (id is null)
            return false;
        foreach (var control in Controls(root))
        {
            if (control.HasMeta(FocusKey) && control.GetMeta(FocusKey).AsString() == id && control.FocusMode != Control.FocusModeEnum.None
                && control.IsVisibleInTree() && control is not BaseButton { Disabled: true })
            {
                control.GrabFocus();
                return true;
            }
        }
        return false;
    }

    /// <summary>The first focusable, enabled control under <paramref name="root"/>, in tree order.</summary>
    public static Control? FirstFocusable(Control root) =>
        Controls(root).FirstOrDefault(c => c.FocusMode == Control.FocusModeEnum.All && c.IsVisibleInTree() && c is not BaseButton { Disabled: true });

    private static IEnumerable<Control> Controls(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Control control)
                yield return control;
            foreach (var deeper in Controls(child))
                yield return deeper;
        }
    }

    /// <summary>Remove and free every child, at once, so a rebuild never finds last frame's rows.</summary>
    public static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>The rule under a title bar: a 1 px iron line with five bezel ticks hanging from it (the compass bezel's mark).</summary>
    public sealed partial class TickRule : Control
    {
        public TickRule()
        {
            CustomMinimumSize = new Vector2(0, 6);
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            DrawRect(new Rect2(0, 0, Size.X, 1), HudStyle.Iron);
            for (int i = 0; i < 5; i++)
                DrawRect(new Rect2(i * 6, 1, 1, 4), HudStyle.Iron);
        }
    }

    /// <summary>
    /// After Settings changes the text size, contrast or colours (<see cref="HudStyle.Use"/>): the menu theme rebuilt, every sheet given it
    /// and its frame again, and every word drawn at its designed size times the new text size. Screens rebuild their rows as they open.
    /// </summary>
    public static void Restyle(Node root)
    {
        _theme = null;
        foreach (var node in root.FindChildren("*", "", true, false))
        {
            switch (node)
            {
                case SheetRoot sheet:
                    sheet.Theme = Theme;
                    sheet.Frame?.Panel.AddThemeStyleboxOverride("panel", SheetBox());
                    break;
                case Label label when label.HasMeta(HudStyle.DesignedSize):
                    label.AddThemeFontSizeOverride("font_size", HudStyle.Sized(label.GetMeta(HudStyle.DesignedSize).AsInt32()));
                    break;
                case Button button when button.HasMeta(HudStyle.DesignedSize):   // a conversation's open reply rows
                    button.AddThemeFontSizeOverride("font_size", HudStyle.Sized(button.GetMeta(HudStyle.DesignedSize).AsInt32()));
                    break;
            }
        }
    }

    /// <summary>The sheet a screen's layer holds, or null: for what Main changes on a screen from outside (Escape's word).</summary>
    public static Sheet? SheetOf(CanvasLayer layer) =>
        layer.GetChildren().OfType<SheetRoot>().FirstOrDefault()?.Frame;

    /// <summary>
    /// The sheet's root: the full-screen scrim the menu theme hangs from. While its screen is open it keeps the sheet clear of the HUD
    /// where the HUD reaches into the band (the companion strip under the plate, the tracked quest at the right), twice a second.
    /// </summary>
    public sealed partial class SheetRoot : Control
    {
        private double _since = 1;

        public Action? Watch { get; set; }

        /// <summary>The sheet this root belongs to.</summary>
        public Sheet? Frame { get; set; }

        public override void _Process(double delta)
        {
            if (!IsVisibleInTree())
            {
                _since = 1;
                return;
            }
            _since += delta;
            if (_since < 0.5)
                return;
            _since = 0;
            Watch?.Invoke();
        }
    }
}

/// <summary>The parts of a shared row the caller fills in.</summary>
public sealed record Row(HBoxContainer Root, Label Name, Label Secondary, VBoxContainer Middle);

/// <summary>The parts of a selectable list row.</summary>
public sealed record ListRow(Button Button, Label Name, Label Secondary, VBoxContainer Middle, HBoxContainer Right);

/// <summary>The dense template's panes.</summary>
public sealed record ListDetail(HBoxContainer Root, VBoxContainer List, VBoxContainer Detail, ScrollContainer ListScroll, ScrollContainer DetailScroll);

/// <summary>
/// One management screen's frame. It holds only the nodes it made; what is drawn in the body is the screen's.
/// </summary>
public sealed class Sheet
{
    private readonly VBoxContainer _head = new();
    private readonly HBoxContainer _keys = new();
    private readonly PanelContainer _icon = HudStyle.IconSlot(null, 32);
    private readonly CanvasLayer _layer;
    private string? _ownKey;
    private bool _closable = true;
    private string _closeWord = "Close";
    private bool _dense;
    private float _width;
    private float _top = UiKit.BandTop;
    private float _bottom = UiKit.BandTop + UiKit.BandHeight;
    private Tween? _opening;

    /// <summary>Full screen: the scrim, and the control the menu theme hangs from.</summary>
    public Control Root { get; }

    /// <summary>The sheet in the band.</summary>
    public PanelContainer Panel { get; }

    public Label Title { get; }

    public Label Hint { get; }

    /// <summary>Where the screen draws.</summary>
    public VBoxContainer Body { get; }

    /// <summary>The body's scroll region, or null when the screen's panes scroll themselves.</summary>
    public ScrollContainer? Scroll { get; }

    /// <summary>The room the sheet has once it keeps clear of the HUD (the band's height, less what hangs into it from above or rises from below).</summary>
    public float Room => _bottom - _top;

    internal Sheet(CanvasLayer layer, string title, string? ownKey, float width, bool scrolls)
    {
        _layer = layer;
        _ownKey = ownKey;
        var root = new UiKit.SheetRoot { Theme = UiKit.Theme, MouseFilter = Control.MouseFilterEnum.Stop, Watch = Place, Frame = this };
        Root = root;
        Root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var scrim = new ColorRect { Color = new Color(0, 0, 0, 0.35f), MouseFilter = Control.MouseFilterEnum.Ignore };
        scrim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Root.AddChild(scrim);

        Panel = new PanelContainer { AnchorLeft = 0.5f, AnchorRight = 0.5f, GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.End, MouseFilter = Control.MouseFilterEnum.Stop };
        Panel.AddThemeStyleboxOverride("panel", UiKit.SheetBox());
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 16);

        _head.AddThemeConstantOverride("separation", 8);
        var bar = new HBoxContainer { CustomMinimumSize = new Vector2(0, 56) };
        bar.AddThemeConstantOverride("separation", 12);
        // The screen's icon before its title, the same tile as its System Menu row: the hand-over from menu to screen reads as one.
        _icon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _icon.Visible = false;
        bar.AddChild(_icon);
        Title = HudStyle.Text(HudStyle.Caps, 30, HudStyle.Bone, caps: true);
        Title.Text = title;
        Title.VerticalAlignment = VerticalAlignment.Center;
        Title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Title.SizeFlagsVertical = Control.SizeFlags.Fill;
        bar.AddChild(Title);
        _keys.AddThemeConstantOverride("separation", 8);
        _keys.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        bar.AddChild(_keys);
        _head.AddChild(bar);
        _head.AddChild(new UiKit.TickRule());
        Hint = UiKit.Words(string.Empty, 16, HudStyle.Muted);
        Hint.Visible = false;
        _head.AddChild(Hint);
        column.AddChild(_head);

        Body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        Body.AddThemeConstantOverride("separation", 24);
        if (scrolls)
        {
            Scroll = UiKit.Scroll();
            Scroll.AddChild(Body);
            column.AddChild(Scroll);
            Body.MinimumSizeChanged += Refit;
            Body.Resized += Refit;
        }
        else
        {
            column.AddChild(Body);
        }
        Panel.AddChild(column);
        Root.AddChild(Panel);
        layer.AddChild(Root);
        Shape(width);
        Keys();
        layer.VisibilityChanged += () =>
        {
            if (!layer.Visible)
            {
                Hud()?.StepAside(false);
                return;
            }
            Keys();
            Place();
            Open();
        };
    }

    /// <summary>Change the template: the width, and whether the sheet is the dense 720-high one.</summary>
    public void Shape(float width, bool dense = false)
    {
        _width = width;
        _dense = dense;
        Place();
    }

    /// <summary>
    /// Change the title, the screen's own key, or whether <c>[ESC] CLOSE</c> is offered (the start screen has nothing to close to) and
    /// what Escape is called there (the System Menu's is CONTINUE).
    /// </summary>
    public void Head(string title, string? ownKey, bool closable = true, string closeWord = "Close")
    {
        Title.Text = title;
        _ownKey = ownKey;
        _closable = closable;
        _closeWord = closeWord;
        Keys();
    }

    /// <summary>The screen's icon in the title bar; null hides it.</summary>
    public void SetIcon(Texture2D? icon)
    {
        ((TextureRect)_icon.GetChild(0)).Texture = icon;
        _icon.Visible = icon is not null;
    }

    /// <summary>What Escape is called in the title bar: CLOSE, or BACK when Escape returns to the System Menu that opened the screen.</summary>
    public void CloseWord(string word)
    {
        if (word == _closeWord)
            return;
        _closeWord = word;
        Keys();
    }

    /// <summary>The optional hint under the rule; empty hides it.</summary>
    public void SetHint(string text)
    {
        Hint.Text = text;
        Hint.Visible = text.Length > 0;
    }

    /// <summary>Run <paramref name="shown"/> each time the screen opens, after it has laid out: where default focus is given.</summary>
    public void OnShown(Action shown) =>
        _layer.VisibilityChanged += () =>
        {
            if (_layer.Visible)
                Callable.From(shown).CallDeferred();
        };

    private void Keys()
    {
        UiKit.Clear(_keys);
        if (_ownKey is not null && UiKit.KeyCap(_ownKey) is { } own)
            _keys.AddChild(own);
        if (!_closable || UiKit.KeyCap("release_mouse") is not { } escape)
            return;
        _keys.AddChild(escape);
        _keys.AddChild(UiKit.Caption(_closeWord));
    }

    /// <summary>The 120 ms fade and 8 px rise of a sheet coming up (02 §12); nothing else on a sheet moves but the live values.</summary>
    private void Open()
    {
        if (!Root.IsInsideTree() || DisplayServer.GetName() == "headless")
            return;
        _opening?.Kill();
        Panel.Modulate = new Color(1, 1, 1, 0);
        Panel.OffsetTop = _top + 8;
        _opening = Root.CreateTween().SetParallel();
        _opening.TweenProperty(Panel, "modulate:a", 1f, 0.12);
        _opening.TweenProperty(Panel, "offset_top", _top, 0.12).SetEase(Tween.EaseType.Out);
    }

    /// <summary>
    /// The sheet in the band (x 384-1536, y 128-848 on the reference canvas, centred), kept clear of any HUD panel that reaches into it:
    /// it starts below what hangs in from above, ends above what rises from below, and narrows, centred, around what stands beside it.
    /// With nothing in the way it is the band.
    /// </summary>
    private void Place()
    {
        if (!GodotObject.IsInstanceValid(Panel))
            return;
        // The band on the reference canvas; on a smaller one (a larger interface scale) it rises, and then shortens, to fit 16 clear of the
        // foot - keeping its top below the HUD's plate for as long as the canvas allows.
        var canvas = Root.IsInsideTree() && Root.Size.X > 100 ? Root.Size : new Vector2(1920, 1080);
        float width = Math.Min(_width, canvas.X - 32), top = Math.Min(UiKit.BandTop, Math.Max(16, canvas.Y - UiKit.BandHeight - 16));
        float bottom = Math.Min(top + UiKit.BandHeight, canvas.Y - 16);
        if (Hud() is { } hud)
        {
            // Enlarged (a canvas under the reference's 1080 lines, or larger text), the HUD steps aside rather than the sheet shrinking round it.
            hud.StepAside(hud.Crowded);
            float centre = Root.Size.X / 2;
            foreach (var rect in hud.Intrusions())
            {
                var band = new Rect2(centre - width / 2, top, width, bottom - top);
                if (!rect.Intersects(band))
                    continue;
                // Whichever costs the sheet less: start below a panel that only just hangs into the band (the companion strip), end
                // above one that rises into its foot (the formulas and pools, the combat log), or narrow, centred, beside one that
                // reaches down it (the tracked quest).
                float below = rect.End.Y + 12 - top;
                float above = bottom - (rect.Position.Y - 12);
                float narrowed = Math.Max(UiKit.Small, 2 * Math.Max(centre - rect.End.X, rect.Position.X - centre) - 24);
                if (below <= 64)
                    top += below;
                else if (rect.Position.Y > top + UiKit.MinHeight)
                    bottom -= above;
                else
                    width = Math.Min(width, narrowed);
            }
            width = Math.Min(width, Math.Min(_width, canvas.X - 32));
        }
        Panel.OffsetLeft = -width / 2;
        Panel.OffsetRight = width / 2;
        if (_opening is null || !_opening.IsRunning())
            Panel.OffsetTop = top;
        Panel.OffsetBottom = top;
        bool moved = Math.Abs(top - _top) > 0.5f || Math.Abs(bottom - _bottom) > 0.5f;
        _top = top;
        _bottom = bottom;
        Panel.CustomMinimumSize = new Vector2(width, _dense ? Room : UiKit.MinHeight);
        if (moved || Scroll is not null)
            Refit();
    }

    /// <summary>The production HUD while it shows, or null.</summary>
    private HudView? Hud() =>
        Root.IsInsideTree() && Root.GetTree().Root.FindChild("HudView", true, false) is HudView { Visible: true } hud && hud.IsVisibleInTree() ? hud : null;

    private void Refit()
    {
        if (Scroll is null)
            return;
        Callable.From(Fit).CallDeferred();
    }

    /// <summary>Hug the content, up to the room in the band: the body scrolls beyond that, under the fixed title bar.</summary>
    private void Fit()
    {
        if (Scroll is null || !GodotObject.IsInstanceValid(Scroll))
            return;
        if (_dense)
        {
            Scroll.CustomMinimumSize = Vector2.Zero;
            return;
        }
        float chrome = 8 + UiKit.Padding + _head.GetCombinedMinimumSize().Y + 16;
        float wanted = Math.Min(Body.GetCombinedMinimumSize().Y, Room - chrome);
        if (Math.Abs(Scroll.CustomMinimumSize.Y - wanted) > 0.5f)
            Scroll.CustomMinimumSize = new Vector2(0, wanted);
    }
}
