// UNNAMED Presentation - the subtitle strip: what is being said, with who says it, along the foot of the screen
// Godot presentation only: it holds captions and nothing else (D-11); whether it shows, its size and its background are the player's settings.

using Godot;
using UNNAMED.Presentation.Settings;
using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// Captions for every line said aloud or in passing: a bark, a conversation's chatter, ambient talk. The speaker's name stands in front of the
/// words in capitals, so who speaks never rests on colour. Size follows Settings (over the text size), and the background is none (an outline
/// only), a dim band or a solid panel. It sits just above the action bars and takes no pointer.
/// </summary>
public sealed partial class SubtitleStrip : Control
{
    private readonly SubtitleModel _model = new();
    private readonly VBoxContainer _rows = new();
    private double _clock;
    private string _shown = string.Empty;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _rows.AnchorLeft = 0.5f;
        _rows.AnchorRight = 0.5f;
        _rows.AnchorTop = _rows.AnchorBottom = 1;
        _rows.GrowHorizontal = GrowDirection.Both;
        _rows.GrowVertical = GrowDirection.Begin;
        _rows.AddThemeConstantOverride("separation", 4);
        _rows.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_rows);
    }

    /// <summary>Where the strip's foot stands above the bottom edge: clear of the action bars.</summary>
    public float Foot { get; private set; } = 200;

    /// <summary>A line is being said (a bark within hearing, a spoken line): shown while subtitles are on.</summary>
    public void Say(string speaker, string text, double spokenSeconds = 0)
    {
        if (!SettingsStore.Current.Profile.Interface.Subtitles)
            return;
        _model.Say(speaker, text, _clock, spokenSeconds);
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        Foot = (GetParent()?.GetNodeOrNull<ActionBarsView>("ActionBars")?.Height ?? 160) + 8;
        var ui = SettingsStore.Current.Profile.Interface;
        var lines = ui.Subtitles ? _model.Lines(_clock) : Array.Empty<Caption>();
        string signature = $"{(int)GetViewportRect().Size.Y}|{ui.SubtitleSize}|{ui.SubtitleBackground}|{HudStyle.TextScale}|{(int)Foot}|{string.Join("/", lines.Select(l => SubtitleModel.Words(l)))}";
        _rows.OffsetBottom = -Foot;
        if (signature == _shown)
            return;
        _shown = signature;
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        float maxWidth = Math.Min(GetViewportRect().Size.X * 0.7f, 1100f);
        foreach (var caption in lines)
        {
            int size = SubtitleModel.FontSize(20, HudStyle.TextScale, ui.SubtitleSize, GetViewportRect().Size.Y);
            var row = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
            var box = new StyleBoxFlat { ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 4, ContentMarginBottom = 4 };
            box.BgColor = ui.SubtitleBackground switch
            {
                SubtitleBackground.Solid => new Color(0.04f, 0.04f, 0.045f, 1f),
                SubtitleBackground.Dim => new Color(0.04f, 0.04f, 0.045f, 0.6f),
                _ => new Color(0, 0, 0, 0),
            };
            row.AddThemeStyleboxOverride("panel", box);
            var label = new RichTextLabel
            {
                BbcodeEnabled = false, FitContent = true, ScrollActive = false, AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(Math.Min(maxWidth, 240), 0), MouseFilter = MouseFilterEnum.Ignore,
            };
            label.AddThemeFontOverride("normal_font", HudStyle.Body);
            label.AddThemeFontSizeOverride("normal_font_size", size);
            label.AddThemeColorOverride("default_color", HudStyle.Bone);
            label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.95f));
            label.AddThemeConstantOverride("outline_size", ui.SubtitleBackground == SubtitleBackground.None ? 8 : 3);
            if (caption.Speaker.Length > 0)
            {
                label.PushColor(HudStyle.Ember);
                label.PushFont(HudStyle.Strong);
                label.AddText(caption.Speaker.ToUpperInvariant() + ": ");
                label.Pop();
                label.Pop();
            }
            label.AddText(caption.Text);
            label.CustomMinimumSize = new Vector2(Math.Min(maxWidth, Math.Max(240, (caption.Speaker.Length + 2 + caption.Text.Length) * size * 0.55f)), 0);
            row.AddChild(label);
            _rows.AddChild(row);
        }
    }
}
