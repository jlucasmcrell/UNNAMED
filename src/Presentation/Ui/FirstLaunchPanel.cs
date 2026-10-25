// UNNAMED Presentation - "Set up Otherreach": the first launch's one screen of comfort, text, picture, sound and controls (PJ02 §3.2)
// Godot presentation only (D-11): it sets the player's preferences through SettingsStore; nothing here is game state

using Godot;
using UNNAMED.Presentation.Settings;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// Offered once, before the title, the first time the game starts on a machine: one screen, not a wizard. The four comfort presets -
/// Vision, Hearing, Motion, Motor and timing - each naming what it changed, in any combination; then the few settings a stranger needs
/// before playing, in the same words as Settings, every one applied as it changes. <b>Use recommended settings</b> closes it at once;
/// everything already defaults to something sensible, and everything here is also in Settings.
/// </summary>
public partial class FirstLaunchPanel : CanvasLayer
{
    private Sheet _sheet = null!;
    private readonly Dictionary<string, string[]> _applied = new(StringComparer.Ordinal);

    /// <summary>Answered: Main shows the title.</summary>
    public Action? Done { get; set; }

    public override void _Ready()
    {
        Visible = false;
        Layer = 12;
        _sheet = UiKit.Sheet(this, "Set up Otherreach", null, UiKit.Wide);
        _sheet.Head("Set up Otherreach", null, closable: false);
        _sheet.SetHint("Everything here can be changed later in Settings.");
        _sheet.OnShown(() => UiKit.FirstFocusable(_sheet.Body)?.GrabFocus());
        VisibilityChanged += () =>
        {
            if (Visible)
                Build();
        };
    }

    private void Build()
    {
        string? focused = UiKit.FocusedId(_sheet.Root);
        UiKit.Clear(_sheet.Body);
        var answers = new HBoxContainer();
        answers.AddThemeConstantOverride("separation", 12);
        answers.AddChild(UiKit.Action("Use recommended settings", () => Finish(recommended: true), UiKit.Kind.Primary, id: "first.recommended"));
        answers.AddChild(UiKit.Action("Open the road", () => Finish(recommended: false), id: "first.done"));
        _sheet.Body.AddChild(answers);

        // The words of ONBOARDING_CONTENT §1: four settings that change how the game reads, and the one fact about it that is not what a
        // player of other games expects.
        var before = UiKit.Section("Before you go out", "Four things worth setting. The rest can wait until you need it.");
        var ui0 = SettingsStore.Current.Profile.Interface;
        float[] textSizes = { 1f, 1.25f, 1.5f };
        before.AddChild(SettingsPanel.Choice("first.before.text", "Text size", "How large words are drawn, over the interface's own size.",
            new[] { "As it comes", "Larger", "Largest" }, Array.IndexOf(textSizes, textSizes.OrderBy(s => Math.Abs(s - ui0.TextSize)).First()),
            i => Profile(p => p with { Interface = p.Interface with { TextSize = textSizes[i] } }), Redraw));
        before.AddChild(SettingsPanel.Choice("first.before.subtitles", "Subtitles", "Everything spoken, written along the bottom.",
            new[] { "On", "Off" }, ui0.Subtitles ? 0 : 1, i => Profile(p => p with { Interface = p.Interface with { Subtitles = i == 0 } })));
        var visions = Enum.GetValues<ColourVision>();
        before.AddChild(SettingsPanel.Choice("first.before.colour", "Colours", "Tell the game which colours you can tell apart, and it will lean on the others.",
            new[] { "No change", "Red and green, first kind", "Red and green, second kind", "Blue and yellow" }, Array.IndexOf(visions, ui0.ColourVision),
            i => Profile(p => p with { Interface = p.Interface with { ColourVision = visions[i] } }), Redraw));
        before.AddChild(SettingsPanel.Choice("first.before.controls", "Controls", "Keys are read by where they sit, not by the letter printed on them.",
            ControlPresets.Names.Select(ControlPresets.Label).ToArray(), Array.IndexOf(ControlPresets.Names, ControlPresets.Of(SettingsStore.Current.Profile.Controls)),
            i =>
            {
                Profile(p => p with { Controls = ControlPresets.Apply(p.Controls, ControlPresets.Names[i]) });
                SettingsStore.ApplyBindings();
            }));
        var guidance = Enum.GetValues<Guidance>();
        before.AddChild(SettingsPanel.Choice("first.before.guidance", "Guidance", "How much the game tells you as it goes.",
            new[] { "Tell me everything, once", "Only what I cannot work out", "Nothing - I will find it" }, Array.IndexOf(guidance, ui0.Guidance),
            i => Profile(p => p with { Interface = p.Interface with { Guidance = guidance[i] } })));
        before.AddChild(UiKit.Words("The world does not stop for a menu. Neither does it stop for you to read: open a sheet somewhere quiet, and keep one eye on the road.", 16, HudStyle.Brass));
        _sheet.Body.AddChild(before);

        var presets = UiKit.Section("Comfort", "any of them, together; each says what it changed");
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 8);
        foreach (string preset in ComfortPresets.Names)
        {
            var cell = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
            cell.AddThemeConstantOverride("separation", 4);
            bool on = _applied.ContainsKey(preset);
            var button = UiKit.Action(on ? $"{preset}: on" : preset, () => Apply(preset), on ? UiKit.Kind.Primary : UiKit.Kind.Normal, id: "first.preset." + preset);
            button.Alignment = HorizontalAlignment.Left;
            button.Disabled = on;
            cell.AddChild(button);
            cell.AddChild(UiKit.Words(on ? string.Join(" · ", _applied[preset]) : About(preset), 16, on ? HudStyle.Lichen : HudStyle.Muted));
            grid.AddChild(cell);
        }
        presets.AddChild(grid);
        _sheet.Body.AddChild(presets);

        var prefs = SettingsStore.Current;
        var picture = UiKit.Section("Text and picture");
        float[] scales = { 0.75f, 0.9f, 1f, 1.1f, 1.25f, 1.5f, 1.75f, 2f };
        picture.AddChild(SettingsPanel.Choice("first.scale", "Interface size", "Everything on screen but the world. Set for this window's size.",
            scales.Select(s => $"{s * 100:0} %").ToArray(), Array.IndexOf(scales, scales.OrderBy(s => Math.Abs(s - prefs.Profile.Interface.UiScale)).First()),
            i => Profile(p => p with { Interface = p.Interface with { UiScale = scales[i] } }), Redraw));
        float[] sizes = { 1f, 1.25f, 1.5f, 1.75f, 2f };
        picture.AddChild(SettingsPanel.Choice("first.text", "Text size", "Words alone, over the interface's size.",
            sizes.Select(s => $"{s * 100:0} %").ToArray(), Array.IndexOf(sizes, sizes.OrderBy(s => Math.Abs(s - prefs.Profile.Interface.TextSize)).First()),
            i => Profile(p => p with { Interface = p.Interface with { TextSize = sizes[i] } }), Redraw));
        picture.AddChild(SettingsPanel.Slider("first.brightness", "Brightness", "Lift it until the shadows under the eaves show shape, no further.",
            60, 140, 5, prefs.Machine.Brightness * 100, v => $"{v:0} %",
            v => SettingsStore.Set(SettingsStore.Current with { Machine = SettingsStore.Current.Machine with { Brightness = (float)(v / 100) } })));
        _sheet.Body.AddChild(picture);

        var play = UiKit.Section("Sound, camera and controls");
        play.AddChild(SettingsPanel.Slider("first.master", "Master volume", "Everything together; each kind of sound has its own in Settings.",
            0, 100, 5, prefs.Profile.Audio.Master * 100, v => $"{v:0} %", v => Profile(p => p with { Audio = p.Audio with { Master = (float)(v / 100) } })));
        play.AddChild(SettingsPanel.Slider("first.sensitivity", "Look sensitivity", "How far the camera turns for the mouse's travel.",
            20, 400, 5, prefs.Profile.Camera.Sensitivity * 100, v => $"{v:0} %", v => Profile(p => p with { Camera = p.Camera with { Sensitivity = (float)(v / 100) } })));
        play.AddChild(SettingsPanel.Toggle("first.invert", "Invert vertical look", "Mouse forward looks down.", prefs.Profile.Camera.InvertY,
            on => Profile(p => p with { Camera = p.Camera with { InvertY = on } })));
        play.AddChild(SettingsPanel.Slider("first.fov", "Field of view", "How wide the view is, top to bottom.",
            60, 110, 1, prefs.Profile.Camera.Fov, v => $"{v:0}°", v => Profile(p => p with { Camera = p.Camera with { Fov = (float)v } })));
        foreach (var (action, words) in new[] { ("sprint", "Sprint"), ("guard", "Guard and aim") })
        {
            play.AddChild(SettingsPanel.Choice("first.toggle." + action, words, "Held down, or pressed once to start and again to stop.",
                new[] { "Hold", "On or off" }, prefs.Profile.Controls.IsToggle(action) ? 1 : 0,
                i => Profile(p => p with
                {
                    Controls = p.Controls with { Toggles = new Dictionary<string, bool>(p.Controls.Toggles, StringComparer.Ordinal) { [action] = i == 1 } },
                })));
        }
        _sheet.Body.AddChild(play);
        UiKit.Refocus(_sheet.Root, focused);
    }

    private static string About(string preset) => preset switch
    {
        "Vision" => "Larger text, high contrast, subtitles.",
        "Hearing" => "Subtitles, notices that stay longer.",
        "Motion" => "No camera shake or head bob, fewer flashes.",
        _ => "Press on, press off instead of holding; notices that stay.",
    };

    private void Apply(string preset)
    {
        var (prefs, changed) = ComfortPresets.Apply(SettingsStore.Current, preset);
        _applied[preset] = changed;
        SettingsStore.Set(prefs);
        Redraw();
    }

    private static void Profile(Func<ProfilePrefs, ProfilePrefs> change) =>
        SettingsStore.Set(SettingsStore.Current with { Profile = change(SettingsStore.Current.Profile) });

    private void Redraw() => Callable.From(Build).CallDeferred();

    /// <summary>Answered: with the recommended settings (every default), or with what was set here.</summary>
    private void Finish(bool recommended) => Callable.From(() =>
    {
        if (recommended)
            SettingsStore.Set(new ClientPrefs
            {
                // The opening has just been seen through: every default, but not as if it had not.
                Profile = new ProfilePrefs { Interface = new InterfacePrefs { UiScale = SettingsStore.RecommendedUiScale() }, OpeningSeen = SettingsStore.Current.Profile.OpeningSeen },
            });
        Profile(p => p with { FirstLaunchDone = true });
        Visible = false;
        Done?.Invoke();
    }).CallDeferred();
}
