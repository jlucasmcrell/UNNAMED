// UNNAMED Presentation - the production HUD's look: its type, its colours and the few shapes it is built from (Phase B, the HUD finish)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The production HUD's visual language, after the Player Journey design's style direction (doc 09 §9): restrained and legible, dark
/// translucent panels, clear sans-serif type, a thin iron-grey rule line and one warm focus colour - no parchment, neon or ornament.
/// Type: Barlow for reading, Barlow Condensed in small capitals for names and labels (SIL Open Font License 1.1, <c>Ui/Fonts/OFL.txt</c>).
/// Colour: soot panels, bone text, iron rules, and ember only for what asks for the player's attention - a key to press, the level,
/// the tracked quest, the formula being worked. Pool colours are the classic ones, dulled, and never carry meaning without a word.
/// </summary>
public static class HudStyle
{
    public static readonly Color Soot = new(0.045f, 0.045f, 0.042f, 0.76f);
    public static readonly Color Track = new(0.02f, 0.02f, 0.02f, 0.78f);
    public static readonly Color Iron = new(0.52f, 0.52f, 0.49f, 0.55f);
    public static readonly Color IronBright = new(0.66f, 0.65f, 0.61f, 0.9f);
    public static readonly Color Bone = new(0.93f, 0.91f, 0.86f);
    public static readonly Color Ash = new(0.76f, 0.74f, 0.69f);
    public static readonly Color Muted = new(0.67f, 0.66f, 0.62f);
    public static readonly Color Ember = new(0.93f, 0.66f, 0.33f);
    public static readonly Color Brass = new(0.83f, 0.69f, 0.45f);
    public static readonly Color Wound = new(0.94f, 0.55f, 0.48f);

    public static readonly Color HealthFill = new(0.71f, 0.2f, 0.16f);
    public static readonly Color StaminaFill = new(0.77f, 0.62f, 0.28f);
    public static readonly Color FocusFill = new(0.32f, 0.51f, 0.76f);
    public static readonly Color StrainFill = new(0.54f, 0.38f, 0.7f);
    public static readonly Color StrainedFill = new(0.84f, 0.22f, 0.24f);
    public static readonly Color XpFill = new(0.82f, 0.64f, 0.34f);

    private static Font? _body, _strong, _caps;
    private static Theme? _theme;
    private static ImageTexture? _hatch;
    private static ShaderMaterial? _iconMaterial;

    /// <summary>Barlow Medium: everything read as a sentence or a value.</summary>
    public static Font Body => _body ??= Load("Barlow-Medium.ttf");

    /// <summary>Barlow SemiBold: values and names that must stand out of a line.</summary>
    public static Font Strong => _strong ??= Load("Barlow-SemiBold.ttf");

    /// <summary>Barlow Condensed SemiBold with a little air between the letters: names, labels and keys, set in capitals.</summary>
    public static Font Caps => _caps ??= new FontVariation { BaseFont = Load("BarlowCondensed-SemiBold.ttf"), SpacingGlyph = 1 };

    /// <summary>The theme every production-HUD control hangs from: Barlow, bone on soot, a soft shadow under floating words.</summary>
    public static Theme Theme => _theme ??= BuildTheme();

    /// <summary>
    /// The font file itself when the project folder has it (a run from source, imported or not), else the imported resource (an
    /// exported build). Without either the engine's own font stands in, so a missing file never blanks the HUD.
    /// </summary>
    private static Font Load(string file)
    {
        string path = $"res://Ui/Fonts/{file}";
        FontFile? font = Godot.FileAccess.FileExists(path) ? new FontFile { Data = Godot.FileAccess.GetFileAsBytes(path) }
            : ResourceLoader.Exists(path) ? GD.Load<FontFile>(path) : null;
        if (font is null)
        {
            GD.PushWarning($"UNNAMED HUD: {path} is missing; the engine's font stands in");
            return ThemeDB.FallbackFont;
        }
        font.Antialiasing = TextServer.FontAntialiasing.Gray;
        font.Hinting = TextServer.Hinting.Light;
        font.SubpixelPositioning = TextServer.SubpixelPositioning.Auto;
        font.Fallbacks = new Godot.Collections.Array<Font> { ThemeDB.FallbackFont };
        return font;
    }

    private static Theme BuildTheme()
    {
        var theme = new Theme { DefaultFont = Body, DefaultFontSize = 17 };
        theme.SetColor("font_color", "Label", Bone);
        theme.SetColor("font_shadow_color", "Label", new Color(0, 0, 0, 0.6f));
        theme.SetConstant("shadow_offset_x", "Label", 0);
        theme.SetConstant("shadow_offset_y", "Label", 1);
        theme.SetConstant("shadow_outline_size", "Label", 2);
        theme.SetConstant("outline_size", "Label", 0);
        theme.SetConstant("line_spacing", "Label", 1);
        theme.SetStylebox("panel", "PanelContainer", PanelBox());
        return theme;
    }

    /// <summary>
    /// A panel: soot, translucent, a hairline iron frame and a soft shadow that lifts it off bright ground; <paramref name="quiet"/> for
    /// what is read in passing (the combat log), a little lighter.
    /// </summary>
    public static StyleBoxFlat PanelBox(int across = 14, int down = 10, bool quiet = false) => new()
    {
        BgColor = quiet ? new Color(Soot, 0.62f) : Soot,
        BorderColor = quiet ? new Color(Iron, 0.3f) : Iron, BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        ContentMarginLeft = across, ContentMarginRight = across, ContentMarginTop = down, ContentMarginBottom = down,
        ShadowColor = new Color(0, 0, 0, 0.28f), ShadowSize = 8,
    };

    /// <summary>A small tag inside a panel ("Strained", "Crouched", a debt): a darker inset with a hairline frame.</summary>
    public static StyleBoxFlat ChipBox(Color border) => new()
    {
        BgColor = new Color(0.02f, 0.02f, 0.02f, 0.55f),
        BorderColor = border, BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 0, ContentMarginBottom = 1,
    };

    /// <summary>An icon's frame: the painted tile sits in a dark inset with an iron edge (ember while it is the one being worked).</summary>
    public static StyleBoxFlat SlotBox(bool active = false) => new()
    {
        BgColor = new Color(0.02f, 0.02f, 0.02f, 0.85f),
        BorderColor = active ? Ember : IronBright,
        BorderWidthLeft = active ? 2 : 1, BorderWidthTop = active ? 2 : 1, BorderWidthRight = active ? 2 : 1, BorderWidthBottom = active ? 2 : 1,
        CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        ContentMarginLeft = 2, ContentMarginRight = 2, ContentMarginTop = 2, ContentMarginBottom = 2,
    };

    /// <summary>A key cap: a raised dark key, its lower edge heavier, the key's name in ember.</summary>
    public static StyleBoxFlat KeyBox() => new()
    {
        BgColor = new Color(0.14f, 0.135f, 0.125f, 0.95f),
        BorderColor = new Color(0.7f, 0.68f, 0.62f, 0.85f), BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 0, ContentMarginBottom = 0,
    };

    /// <summary>A label in one of the three faces, at a size and colour; <paramref name="caps"/> sets it in capitals.</summary>
    public static Label Text(Font font, int size, Color color, bool caps = false)
    {
        var label = new Label { Uppercase = caps, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    /// <summary>The thin iron rule between a panel's parts.</summary>
    public static HSeparator Rule(int room = 8)
    {
        var rule = new HSeparator { MouseFilter = Control.MouseFilterEnum.Ignore };
        rule.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = Iron, Thickness = 1 });
        rule.AddThemeConstantOverride("separation", room);
        return rule;
    }

    /// <summary>
    /// The icon tiles are opaque paintings on backgrounds of every shade (the manifest's <c>alpha: none</c>): this darkens each tile's
    /// border into the slot and takes a little colour and glare out, so the set reads as one family inside the iron frames.
    /// </summary>
    public static ShaderMaterial IconMaterial => _iconMaterial ??= new ShaderMaterial
    {
        Shader = new Shader
        {
            Code = "shader_type canvas_item;\n" +
                   "void fragment() {\n" +
                   "  vec4 c = texture(TEXTURE, UV);\n" +
                   "  float l = dot(c.rgb, vec3(0.299, 0.587, 0.114));\n" +
                   "  c.rgb = mix(c.rgb, vec3(l), 0.15) * 0.92;\n" +
                   "  vec2 d = abs(UV - 0.5) * 2.0;\n" +
                   "  float edge = smoothstep(0.62, 1.0, max(d.x, d.y));\n" +
                   "  c.rgb = mix(c.rgb, vec3(0.03), edge * 0.8);\n" +
                   "  COLOR = c;\n" +
                   "}\n",
        },
    };

    /// <summary>An icon in its frame at a size; an empty frame when there is no icon, so rows keep their shape.</summary>
    public static PanelContainer IconSlot(Texture2D? texture, float size, bool active = false)
    {
        var slot = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        slot.AddThemeStyleboxOverride("panel", SlotBox(active));
        slot.AddChild(new TextureRect
        {
            Texture = texture,
            Material = IconMaterial,
            CustomMinimumSize = new Vector2(size, size),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        return slot;
    }

    /// <summary>The diagonal hatch laid over the Strain gauge once the character is Strained: a pattern as well as a colour (doc 09 §4).</summary>
    public static Texture2D Hatch
    {
        get
        {
            if (_hatch is not null)
                return _hatch;
            var image = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    image.SetPixel(x, y, (x + y) % 8 < 3 ? new Color(0, 0, 0, 0.42f) : new Color(0, 0, 0, 0));
            return _hatch = ImageTexture.CreateFromImage(image);
        }
    }
}
