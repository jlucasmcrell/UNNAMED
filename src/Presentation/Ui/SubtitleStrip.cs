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

    private readonly Control _clip = new() { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
    private int _size = 16;
    private bool _any;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _rows.AddThemeConstantOverride("separation", 4);
        _rows.MouseFilter = MouseFilterEnum.Ignore;
        _clip.AddChild(_rows);
        AddChild(_clip);
    }

    /// <summary>How tall the band is wanted now: what the captions on screen take, never more than three lines of them; nothing when none show.</summary>
    public float WantedHeight => _any ? Math.Min(_rows.GetCombinedMinimumSize().Y, SubtitleModel.BandHeight(_size)) : 0;

    /// <summary>
    /// Stand the captions in their band (<see cref="HudRegions.Subtitles"/>): the newest line at its foot, an older one scrolling off the top
    /// when the lines are too many to fit.
    /// </summary>
    public void Place(Region band)
    {
        _clip.Position = new Vector2(band.X, band.Y);
        _clip.Size = new Vector2(band.Width, band.Height);
        _rows.Size = new Vector2(band.Width, 0);
        // A caption that cannot stand whole in the band is not drawn in part: the oldest go first.
        float used = 0;
        var captions = _rows.GetChildren().Cast<Control>().ToList();
        for (int i = captions.Count - 1; i >= 0; i--)
        {
            float height = captions[i].GetCombinedMinimumSize().Y;
            captions[i].Visible = used + height <= band.Height + 0.5f || i == captions.Count - 1;
            used += height + 4;
        }
        _rows.Size = new Vector2(band.Width, 0);
        _rows.Position = new Vector2(0, band.Height - _rows.Size.Y);
    }

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
        var ui = SettingsStore.Current.Profile.Interface;
        var lines = ui.Subtitles ? _model.Lines(_clock) : Array.Empty<Caption>();
        string signature = $"{(int)GetViewportRect().Size.Y}|{ui.SubtitleSize}|{ui.SubtitleBackground}|{HudStyle.TextScale}|{string.Join("/", lines.Select(l => SubtitleModel.Words(l)))}";
        if (signature == _shown)
            return;
        _shown = signature;
        _any = lines.Count > 0;
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        float maxWidth = Math.Min(GetViewportRect().Size.X * 0.7f, 1100f);
        foreach (var caption in lines)
        {
            int size = _size = SubtitleModel.FontSize(20, HudStyle.TextScale, ui.SubtitleSize, GetViewportRect().Size.Y);
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
