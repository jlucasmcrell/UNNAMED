// UNNAMED Presentation - Settings: display, audio, controls, camera and interface, each applied as it changes (PJ03; the Field Instrument UI)
// Godot presentation only (D-11): every setting here is a preference of this machine or this player, kept by SettingsStore; none is game state

using Godot;
using UNNAMED.Presentation.Settings;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// One screen with five pages - Display, Audio, Controls, Camera, Interface - from the System Menu, the title screen and the first-launch
/// screen alike. A DENSE sheet: the pages on the left, the chosen page's settings on the right, each a name, one line on what it does,
/// its control and its value. Everything applies at once and is kept at once (<see cref="SettingsStore"/>); the three settings the engine
/// can only take at the next start say so in their own line. A change to the window asks to be kept, and goes back after 15 seconds
/// unanswered, with the focus on Keep (PJ02 §3.3). Every page can be put back to its defaults. Nothing here pauses anything.
/// </summary>
public partial class SettingsPanel : CanvasLayer
{
    private static readonly (string Id, string Name, string About)[] Pages =
    {
        ("display", "Display", "The window, the picture and its detail"),
        ("audio", "Audio", "How loud each kind of sound is"),
        ("controls", "Controls", "Every key, held or pressed on and off"),
        ("camera", "Camera", "Look speed, field of view, comfort"),
        ("interface", "Interface", "Size, contrast, colours, notices, guidance"),
    };

    /// <summary>Each remappable action as the Controls page names it.</summary>
    private static readonly Dictionary<string, string> ActionWords = new(StringComparer.Ordinal)
    {
        ["move_forward"] = "Move forward", ["move_back"] = "Move back", ["move_left"] = "Move left", ["move_right"] = "Move right",
        ["sprint"] = "Sprint", ["walk"] = "Walk", ["jump"] = "Jump", ["crouch"] = "Crouch", ["autorun"] = "Autorun",
        ["interact"] = "Open, take, talk, work", ["attack"] = "Attack, shoot", ["guard"] = "Guard, aim the bow", ["dodge"] = "Dodge",
        ["use"] = "Use a salve", ["quick_1"] = "Quick slot 1", ["quick_2"] = "Quick slot 2", ["quick_3"] = "Quick slot 3", ["quick_4"] = "Quick slot 4",
        ["quick_5"] = "Quick slot 5", ["quick_6"] = "Quick slot 6", ["quick_7"] = "Quick slot 7", ["quick_8"] = "Quick slot 8", ["take_all"] = "Take all",
        ["companion_order"] = "Companions: follow or wait", ["first_person"] = "First person, and back", ["shoulder_swap"] = "Change shoulder",
        ["inventory"] = "Inventory", ["character"] = "Character", ["skills"] = "Skills", ["journal"] = "Journal", ["map"] = "Map", ["help"] = "Controls and help",
        ["saves"] = "Saves", ["quicksave"] = "Save now",
        ["reply_1"] = "Answer 1", ["reply_2"] = "Answer 2", ["reply_3"] = "Answer 3", ["reply_4"] = "Answer 4", ["reply_5"] = "Answer 5",
        ["reply_6"] = "Answer 6", ["reply_7"] = "Answer 7", ["reply_8"] = "Answer 8", ["reply_9"] = "Answer 9",
    };

    private static readonly string[] Dials = { "As the tier", "Off", "Low", "High" };
    private const double KeepSeconds = 15;

    private Sheet _sheet = null!;
    private ListDetail _panes = null!;
    private readonly Dictionary<string, Button> _pageRows = new(StringComparer.Ordinal);
    private string _page = "display";

    // A window change waiting to be kept, and when it goes back.
    private MachinePrefs? _revertTo;
    private double _keepLeft;
    private Label? _keepCount;

    // A key being listened for: which action and which of its two bindings; and a conflict waiting for an answer.
    private (string Action, int Slot)? _listening;
    private ulong _listenEndedFrame;
    private (string Action, int Slot, string Binding, string[] Holders)? _conflict;

    /// <summary>Out of the screen: back to wherever it was opened from (Main closes it, or the title takes the focus back).</summary>
    public Action? Closed { get; set; }

    /// <summary>
    /// A key is being listened for, or was this frame: the key pressed is the binding, and must not also open a screen or close this one.
    /// </summary>
    public bool Capturing => _listening is not null || Engine.GetProcessFrames() <= _listenEndedFrame;

    public override void _Ready()
    {
        Visible = false;
        Layer = 12;
        _sheet = UiKit.Sheet(this, "Settings", null, UiKit.Wide, scrolls: false);
        _sheet.Shape(UiKit.Wide, dense: true);
        _sheet.SetHint("Every change applies at once and is kept for the next time.");
        _sheet.OnShown(() => (_pageRows.GetValueOrDefault(_page) as Control ?? UiKit.FirstFocusable(_sheet.Root))?.GrabFocus());
        VisibilityChanged += () =>
        {
            if (Visible)
                Build();
            else
                Cancel();
        };
    }

    /// <summary>Open at a page (the harnesses picture each), or where it was last.</summary>
    public void Open(string? page = null)
    {
        if (page is not null && Pages.Any(p => p.Id == page))
            _page = page;
        if (Visible)
            Build();
        Visible = true;
    }

    /// <summary>Close, as Escape does: a window change not yet kept goes back first.</summary>
    public void Close()
    {
        if (_revertTo is not null)
            Revert();
        Visible = false;
    }

    private void Cancel()
    {
        _listening = null;
        _conflict = null;
        if (_revertTo is not null)
            Revert();
    }

    public override void _Process(double delta)
    {
        if (!Visible || _revertTo is null)
            return;
        _keepLeft -= delta;
        if (_keepLeft <= 0)
        {
            Revert();
            ShowPage();
        }
        else if (_keepCount is not null)
        {
            _keepCount.Text = $"Going back in {Math.Ceiling(_keepLeft)} s";
        }
    }

    /// <summary>While a key is listened for: the next key or mouse button is the binding. Escape stops listening; Backspace clears it.</summary>
    public override void _Input(InputEvent input)
    {
        if (!Visible || _listening is not { } listening)
            return;
        if (input is InputEventKey { Pressed: true, Echo: false } key)
        {
            GetViewport().SetInputAsHandled();
            if (key.Keycode == Key.Escape || key.PhysicalKeycode == Key.Escape)
                StopListening();
            else if (key.Keycode == Key.Backspace || key.PhysicalKeycode == Key.Backspace)
                Assign(listening.Action, listening.Slot, null);
            else if (SettingsStore.Encode(key) is { } binding)
                Offer(listening.Action, listening.Slot, binding);
        }
        else if (input is InputEventMouseButton { Pressed: true } mouse && SettingsStore.Encode(mouse) is { } button)
        {
            GetViewport().SetInputAsHandled();
            Offer(listening.Action, listening.Slot, button);
        }
    }

    private void Build()
    {
        UiKit.Clear(_sheet.Body);
        _pageRows.Clear();
        _panes = UiKit.ListDetail();
        _sheet.Body.AddChild(_panes.Root);
        foreach (var (id, name, about) in Pages)
        {
            var row = UiKit.ListRow("page." + id, name, about, height: 56);
            string page = id;
            row.Button.FocusEntered += () => Choose(page);
            row.Button.Pressed += () =>
            {
                Choose(page);
                Callable.From(() => UiKit.FirstFocusable(_panes.Detail)?.GrabFocus()).CallDeferred();
            };
            _pageRows[id] = row.Button;
            _panes.List.AddChild(row.Button);
        }
        _panes.List.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var back = UiKit.Action("Back", () => Callable.From(() => Closed?.Invoke()).CallDeferred(), id: "settings.back");
        _panes.List.AddChild(back);
        ShowPage();
    }

    private void Choose(string page)
    {
        if (_page == page)
            return;
        _listening = null;
        _conflict = null;
        _page = page;
        Callable.From(ShowPage).CallDeferred();
    }

    /// <summary>The chosen page's settings, the focus handed back to the setting that had it.</summary>
    private void ShowPage()
    {
        if (!GodotObject.IsInstanceValid(_panes.Detail))
            return;
        string? focused = UiKit.FocusedId(_panes.Detail);
        foreach (var (id, row) in _pageRows)
            UiKit.Select(row, id == _page);
        UiKit.Clear(_panes.Detail);
        _keepCount = null;
        var prefs = SettingsStore.Current;
        switch (_page)
        {
            case "display":
                Display(prefs.Machine);
                break;
            case "audio":
                Audio(prefs.Profile.Audio);
                break;
            case "controls":
                Controls(prefs.Profile.Controls);
                break;
            case "camera":
                Camera(prefs.Profile.Camera);
                break;
            case "interface":
                Interface(prefs.Profile.Interface);
                break;
        }
        UiKit.Refocus(_panes.Detail, focused);
    }

    private static void Change(Func<ClientPrefs, ClientPrefs> change) => SettingsStore.Set(change(SettingsStore.Current));

    private static void Machine(Func<MachinePrefs, MachinePrefs> change) =>
        Change(p => p with { Machine = change(p.Machine) });

    private static void Profile(Func<ProfilePrefs, ProfilePrefs> change) =>
        Change(p => p with { Profile = change(p.Profile) });

    // ---- Display ------------------------------------------------------------------------------------------------------------

    private void Display(MachinePrefs machine)
    {
        var page = Page("Display", "Window and picture");
        if (_revertTo is not null)
        {
            var ask = new VBoxContainer();
            ask.AddThemeConstantOverride("separation", 8);
            ask.AddChild(UiKit.Callout(HudStyle.Ember, "Keep this window? It goes back on its own if you do not answer."));
            var answers = new HBoxContainer();
            answers.AddThemeConstantOverride("separation", 8);
            var keep = UiKit.Action("Keep", () => Callable.From(() =>
            {
                _revertTo = null;
                ShowPage();
            }).CallDeferred(), UiKit.Kind.Primary, id: "display.keep");
            answers.AddChild(keep);
            answers.AddChild(UiKit.Action("Go back", () => Callable.From(() =>
            {
                Revert();
                ShowPage();
            }).CallDeferred(), id: "display.revert"));
            _keepCount = UiKit.Caption($"Going back in {Math.Ceiling(_keepLeft)} s", HudStyle.Ember);
            answers.AddChild(_keepCount);
            ask.AddChild(answers);
            page.AddChild(ask);
            keep.CallDeferred(Control.MethodName.GrabFocus);
        }
        var modes = new[] { WindowMode.Borderless, WindowMode.Windowed, WindowMode.Fullscreen };
        page.AddChild(Choice("display.window", "Window", "Borderless fills the screen and lets other windows over it; full screen takes the display.",
            new[] { "Borderless full screen", "Window", "Full screen" }, Array.IndexOf(modes, machine.WindowMode),
            i => Window(m => m with { WindowMode = modes[i] })));
        var sizes = SettingsStore.Resolutions();
        var size = Choice("display.resolution", "Window size", machine.WindowMode == WindowMode.Windowed
                ? "The window's size on the screen." : "Borderless and full screen take the screen's own size.",
            sizes.Select(s => s.Replace("x", " x ")).ToArray(), Math.Max(0, Array.IndexOf(sizes, machine.Resolution)),
            i => Window(m => m with { Resolution = sizes[i] }));
        Disable(size, machine.WindowMode != WindowMode.Windowed);
        page.AddChild(size);
        page.AddChild(Slider("display.scale", "Render scale", "The world drawn at fewer pixels, then sharpened up (FSR) - faster; the interface stays sharp.",
            50, 100, 5, machine.RenderScale * 100, v => $"{v:0} %", v => Machine(m => m with { RenderScale = (float)(v / 100) })));
        page.AddChild(Toggle("display.vsync", "Vertical sync", "Waits for the screen: no tearing, a little more delay.", machine.Vsync,
            on => Machine(m => m with { Vsync = on })));
        page.AddChild(Choice("display.cap", "Frame ceiling", "The most frames a second the game draws.",
            MachinePrefs.FrameCaps.Select(f => f == 0 ? "None" : $"{f} a second").ToArray(), Array.IndexOf(MachinePrefs.FrameCaps, machine.FrameCap),
            i => Machine(m => m with { FrameCap = MachinePrefs.FrameCaps[i] })));
        page.AddChild(Slider("display.brightness", "Brightness", "Lift it until the shadows under the eaves show shape, no further.",
            60, 140, 5, machine.Brightness * 100, v => $"{v:0} %", v => Machine(m => m with { Brightness = (float)(v / 100) })));

        var detail = Page("Quality and detail", "the dials override the tier");
        string current = VisualOptions.Tier;
        string chosen = machine.Tier ?? (Array.IndexOf(MachinePrefs.Tiers, current) >= 0 ? current : VisualOptions.DefaultTier);
        detail.AddChild(Choice("display.tier", "Graphics quality", chosen == current
                ? "Low, Medium, High (recommended) or Ultra. Applies the next time the game starts."
                : $"Drawing at {Title(current)} now; {Title(chosen)} from the next start.",
            MachinePrefs.Tiers.Select(t => t == "high" ? "High (recommended)" : Title(t)).ToArray(), Array.IndexOf(MachinePrefs.Tiers, chosen),
            i => Machine(m => m with { Tier = MachinePrefs.Tiers[i] }), Redraw));
        var dials = machine.Dials;
        foreach (var (id, name, about, value, with) in new (string, string, string, Dial, Func<GraphicsDials, Dial, GraphicsDials>)[]
                 {
                     ("shadows", "Shadows", "The sun's shadows: how far they reach and how fine they are.", dials.Shadows, (d, x) => d with { Shadows = x }),
                     ("gi", "Bounced light", "Light carried from lit ground into shade (global illumination).", dials.GlobalIllumination, (d, x) => d with { GlobalIllumination = x }),
                     ("ao", "Contact shade", "The darkening where things meet (ambient occlusion).", dials.AmbientOcclusion, (d, x) => d with { AmbientOcclusion = x }),
                     ("ssr", "Reflections", "Water and wet stone reflecting what is on screen.", dials.Reflections, (d, x) => d with { Reflections = x }),
                     ("fog", "Light in the air", "Volumetric fog: shafts of light, mist in the hollows.", dials.Volumetrics, (d, x) => d with { Volumetrics = x }),
                 })
        {
            detail.AddChild(Choice("display.dial." + id, name, about, Dials, (int)value, i => Machine(m => m with { Dials = with(m.Dials, (Dial)i) })));
        }
        detail.AddChild(Slider("display.plants", "Plant density", "How much grows on the ground, against the tier's. Applies the next time the game starts.",
            25, 150, 5, dials.PlantDensity * 100, v => $"{v:0} %", v => Machine(m => m with { Dials = m.Dials with { PlantDensity = (float)(v / 100) } })));
        detail.AddChild(Slider("display.distance", "Detail distance", "How far trees, rocks and plants keep their detail. Applies the next time the game starts.",
            50, 150, 5, dials.DrawDistance * 100, v => $"{v:0} %", v => Machine(m => m with { Dials = m.Dials with { DrawDistance = (float)(v / 100) } })));
        Reset("display.reset", () => Change(p => p with { Machine = new MachinePrefs() }));
    }

    /// <summary>A window change: applied, then kept only when the player says so within 15 seconds.</summary>
    private void Window(Func<MachinePrefs, MachinePrefs> change)
    {
        _revertTo ??= SettingsStore.Current.Machine;
        _keepLeft = KeepSeconds;
        Machine(change);
        Callable.From(ShowPage).CallDeferred();
    }

    private void Revert()
    {
        if (_revertTo is not { } before)
            return;
        _revertTo = null;
        var now = SettingsStore.Current.Machine;
        Machine(_ => now with { WindowMode = before.WindowMode, Resolution = before.Resolution });
    }

    private static string Title(string tier) => char.ToUpperInvariant(tier[0]) + tier[1..];

    // ---- Audio --------------------------------------------------------------------------------------------------------------

    private void Audio(AudioPrefs audio)
    {
        var page = Page("Audio", "heard as you move them");
        foreach (var (id, name, about, value, with) in new (string, string, string, float, Func<AudioPrefs, float, AudioPrefs>)[]
                 {
                     ("master", "Master", "Everything together.", audio.Master, (a, x) => a with { Master = x }),
                     ("music", "Music", "The score. At zero the world's own sounds go on.", audio.Music, (a, x) => a with { Music = x }),
                     ("effects", "Effects", "Steps, blows, creatures, work at the bench.", audio.Effects, (a, x) => a with { Effects = x }),
                     ("ambience", "Ambience", "Wind, water, the settlement, far-off sounds.", audio.Ambience, (a, x) => a with { Ambience = x }),
                     ("interface", "Interface", "The menus' own sounds.", audio.Interface, (a, x) => a with { Interface = x }),
                     ("voice", "Voices", "Spoken lines, where there are any.", audio.Voice, (a, x) => a with { Voice = x }),
                 })
        {
            page.AddChild(Slider("audio." + id, name, about, 0, 100, 5, value * 100, v => $"{v:0} %",
                v => Profile(p => p with { Audio = with(p.Audio, (float)(v / 100)) })));
        }
        Reset("audio.reset", () => Profile(p => p with { Audio = new AudioPrefs() }));
    }

    // ---- Controls -----------------------------------------------------------------------------------------------------------

    private void Controls(ControlPrefs controls)
    {
        var page = Page("Controls", "choose a key's box, then press the new key");
        page.AddChild(UiKit.Words("Escape stops listening; Backspace clears the box. Escape itself always opens the menu and closes screens.", 16, HudStyle.Muted));
        var bindings = SettingsStore.Bindings();
        if (_conflict is { } waiting)
        {
            string holders = string.Join(" and ", waiting.Holders.Select(Words));
            string? old = bindings.First(b => b.Key == waiting.Action).Value.ElementAtOrDefault(waiting.Slot) is { } was ? SettingsStore.Describe(was) : null;
            var ask = new VBoxContainer();
            ask.AddThemeConstantOverride("separation", 8);
            ask.AddChild(UiKit.Callout(HudStyle.Wound,
                $"{SettingsStore.Describe(waiting.Binding)} is already on {holders}. Both cannot answer it at once."));
            var answers = new HBoxContainer();
            answers.AddThemeConstantOverride("separation", 8);
            var swap = UiKit.Action(old is null ? $"Move it to {Words(waiting.Action)}" : $"Swap: {holders} takes {old}", () => Resolve(swap: true), UiKit.Kind.Primary, id: "controls.swap");
            answers.AddChild(swap);
            if (old is not null)
                answers.AddChild(UiKit.Action($"Take it, leave {holders} without it", () => Resolve(swap: false), id: "controls.take"));
            answers.AddChild(UiKit.Action("Cancel", () => Callable.From(() =>
            {
                _conflict = null;
                ShowPage();
            }).CallDeferred(), id: "controls.cancel"));
            ask.AddChild(answers);
            page.AddChild(ask);
            swap.CallDeferred(Control.MethodName.GrabFocus);
        }
        foreach (var (binding, actions) in Binding.Conflicts(bindings))
            page.AddChild(UiKit.Callout(HudStyle.Wound, $"{SettingsStore.Describe(binding)} is on {string.Join(" and ", actions.Select(Words))}."));

        foreach (var (action, keys) in bindings)
        {
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 44) };
            row.AddThemeConstantOverride("separation", 12);
            var name = UiKit.Words(Words(action), 18);
            name.CustomMinimumSize = new Vector2(220, 0);
            name.SizeFlagsHorizontal = Control.SizeFlags.Fill;
            name.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(name);
            for (int slot = 0; slot < Binding.PerAction; slot++)
            {
                bool listening = _listening is { } l && l.Action == action && l.Slot == slot;
                string text = listening ? "Press a key" : keys.ElementAtOrDefault(slot) is { } bound ? SettingsStore.Describe(bound) : "-";
                int chosen = slot;
                string id = action;
                var box = UiKit.Action(text, () => Listen(id, chosen), listening ? UiKit.Kind.Primary : UiKit.Kind.Normal, compact: true, id: $"key.{action}.{slot}");
                box.CustomMinimumSize = new Vector2(136, 32);
                box.TooltipText = "Choose, then press the new key";
                row.AddChild(box);
            }
            if (ControlPrefs.Toggleable.ContainsKey(action))
            {
                var mode = new OptionButton { CustomMinimumSize = new Vector2(136, 32), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
                mode.AddItem("Hold");
                mode.AddItem("On or off");
                mode.Selected = controls.IsToggle(action) ? 1 : 0;
                string id = action;
                mode.ItemSelected += i => Profile(p => p with
                {
                    Controls = p.Controls with { Toggles = new Dictionary<string, bool>(p.Controls.Toggles, StringComparer.Ordinal) { [id] = i == 1 } },
                });
                row.AddChild(UiKit.Id(mode, $"toggle.{action}"));
            }
            page.AddChild(row);
        }
        Reset("controls.reset", () => Profile(p => p with { Controls = new ControlPrefs() }), "Put every key back");
    }

    private static string Words(string action) => ActionWords.GetValueOrDefault(action, action);

    private void Listen(string action, int slot)
    {
        _conflict = null;
        _listening = (action, slot);
        Callable.From(ShowPage).CallDeferred();
    }

    private void StopListening()
    {
        _listening = null;
        _listenEndedFrame = Engine.GetProcessFrames() + 1;
        Callable.From(ShowPage).CallDeferred();
    }

    /// <summary>A key pressed for a box: taken when nothing else live at the same time has it; otherwise the conflict is asked about.</summary>
    private void Offer(string action, int slot, string binding)
    {
        var holders = Binding.Holders(SettingsStore.Bindings(), action, binding);
        if (holders.Length == 0)
        {
            Assign(action, slot, binding);
            return;
        }
        _conflict = (action, slot, binding, holders);
        StopListening();
    }

    /// <summary>The conflict answered: swapped (the others take this action's old key there) or taken (the others lose it).</summary>
    private void Resolve(bool swap)
    {
        if (_conflict is not { } c)
            return;
        _conflict = null;
        var map = SettingsStore.Bindings().ToDictionary(b => b.Key, b => b.Value.ToList(), StringComparer.Ordinal);
        string? old = map[c.Action].ElementAtOrDefault(c.Slot);
        foreach (string holder in c.Holders)
        {
            var keys = map[holder];
            int at = keys.IndexOf(c.Binding);
            if (swap && old is not null && !keys.Contains(old))
                keys[at] = old;
            else
                keys.RemoveAt(at);
        }
        Put(map[c.Action], c.Slot, c.Binding);
        Keep(map);
        Callable.From(ShowPage).CallDeferred();
    }

    private void Assign(string action, int slot, string? binding)
    {
        var map = SettingsStore.Bindings().ToDictionary(b => b.Key, b => b.Value.ToList(), StringComparer.Ordinal);
        Put(map[action], slot, binding);
        Keep(map);
        StopListening();
    }

    private static void Put(List<string> keys, int slot, string? binding)
    {
        if (binding is null)
        {
            if (slot < keys.Count)
                keys.RemoveAt(slot);
            return;
        }
        keys.Remove(binding);
        if (slot < keys.Count)
            keys[slot] = binding;
        else
            keys.Add(binding);
    }

    /// <summary>The map kept as the player's own bindings: only the actions that differ from their defaults.</summary>
    private static void Keep(Dictionary<string, List<string>> map) => Profile(p => p with
    {
        Controls = p.Controls with
        {
            Bindings = map.Where(m => !m.Value.SequenceEqual(SettingsStore.DefaultBindings(m.Key)))
                .ToDictionary(m => m.Key, m => m.Value.ToArray(), StringComparer.Ordinal),
        },
    });

    // ---- Camera -------------------------------------------------------------------------------------------------------------

    private void Camera(CameraPrefs camera)
    {
        var page = Page("Camera", "looking and comfort");
        page.AddChild(Slider("camera.sensitivity", "Look sensitivity", "How far the camera turns for the mouse's travel.",
            20, 400, 5, camera.Sensitivity * 100, v => $"{v:0} %", v => Profile(p => p with { Camera = p.Camera with { Sensitivity = (float)(v / 100) } })));
        page.AddChild(Toggle("camera.invert", "Invert vertical look", "Mouse forward looks down.", camera.InvertY,
            on => Profile(p => p with { Camera = p.Camera with { InvertY = on } })));
        page.AddChild(Slider("camera.fov", "Field of view", "How wide the view is, top to bottom. Wider shows more and moves faster.",
            60, 110, 1, camera.Fov, v => $"{v:0}°", v => Profile(p => p with { Camera = p.Camera with { Fov = (float)v } })));
        var comfort = Page("Comfort", "every camera effect has a zero");
        comfort.AddChild(Slider("camera.shake", "Camera shake", "How much any shake is felt. Nothing shakes the camera yet; this is kept for when it does.",
            0, 100, 5, camera.Shake * 100, v => $"{v:0} %", v => Profile(p => p with { Camera = p.Camera with { Shake = (float)(v / 100) } })));
        comfort.AddChild(Slider("camera.bob", "Head bob", "How much the view sways with the step. Nothing sways it yet; this is kept for when it does.",
            0, 100, 5, camera.Bob * 100, v => $"{v:0} %", v => Profile(p => p with { Camera = p.Camera with { Bob = (float)(v / 100) } })));
        comfort.AddChild(Toggle("camera.flashes", "Reduce flashes", "A burst of light from a working or an impact reaches a third of its brightness.",
            camera.ReduceFlashes, on => Profile(p => p with { Camera = p.Camera with { ReduceFlashes = on } })));
        Reset("camera.reset", () => Profile(p => p with { Camera = new CameraPrefs() }));
    }

    // ---- Interface ----------------------------------------------------------------------------------------------------------

    private void Interface(InterfacePrefs ui)
    {
        var page = Page("Interface", "size, contrast and colour");
        float[] scales = { 0.75f, 0.9f, 1f, 1.1f, 1.25f, 1.5f };
        page.AddChild(Choice("interface.scale", "Interface size", "Everything on screen but the world: the HUD and every screen.",
            scales.Select(s => $"{s * 100:0} %").ToArray(), Nearest(scales, ui.UiScale),
            i => Profile(p => p with { Interface = p.Interface with { UiScale = scales[i] } }), Redraw));
        float[] sizes = { 1f, 1.25f, 1.5f, 1.75f, 2f };
        page.AddChild(Choice("interface.text", "Text size", "Words alone, over the interface's size. The HUD and every screen take it at once.",
            sizes.Select(s => $"{s * 100:0} %").ToArray(), Nearest(sizes, ui.TextSize),
            i => Profile(p => p with { Interface = p.Interface with { TextSize = sizes[i] } }), Redraw));
        page.AddChild(Toggle("interface.contrast", "High contrast", "Solid panels and brighter quiet text. Screens take it as they open; the HUD at the next start.",
            ui.HighContrast, on => Profile(p => p with { Interface = p.Interface with { HighContrast = on } }), Redraw));
        var visions = Enum.GetValues<ColourVision>();
        page.AddChild(Choice("interface.colour", "Colour vision", "Harm and aid are the only things told apart by colour; each always carries its word too.",
            new[] { "As designed", "Red-weak (protanopia)", "Green-weak (deuteranopia)", "Blue-weak (tritanopia)" }, Array.IndexOf(visions, ui.ColourVision),
            i => Profile(p => p with { Interface = p.Interface with { ColourVision = visions[i] } }), Redraw));
        page.AddChild(Swatches());

        var reading = Page("Notices and guidance");
        reading.AddChild(Choice("interface.notices", "Notices stay", "How long a notice at the side of the screen stays up.",
            new[] { "Short", "Normal", "Long", "Longer" }, Array.IndexOf(InterfacePrefs.NoticeSeconds, ui.NoticeDuration),
            i => Profile(p => p with { Interface = p.Interface with { NoticeDuration = InterfacePrefs.NoticeSeconds[i] } })));
        reading.AddChild(Toggle("interface.subtitles", "Subtitles", "Spoken lines shown as words, with who is speaking.", ui.Subtitles,
            on => Profile(p => p with { Interface = p.Interface with { Subtitles = on } })));
        var guidance = Enum.GetValues<Guidance>();
        reading.AddChild(Choice("interface.guidance", "Guidance", "Full shows every hint; Light only the first of each kind; Off none.",
            new[] { "Full", "Light", "Off" }, Array.IndexOf(guidance, ui.Guidance),
            i => Profile(p => p with { Interface = p.Interface with { Guidance = guidance[i] } })));
        var hints = UiKit.Action(ui.SeenHints.Length == 0 ? "No hints learned yet" : $"Show my {ui.SeenHints.Length} learned hints again", () =>
        {
            Profile(p => p with { Interface = p.Interface with { SeenHints = Array.Empty<string>() } });
            Callable.From(ShowPage).CallDeferred();
        }, id: "interface.hints");
        hints.Disabled = ui.SeenHints.Length == 0;
        reading.AddChild(Line("interface.hints", "Reset hints", "Every hint shows again, as on a first game.", hints));
        Reset("interface.reset", () => Profile(p => p with { Interface = new InterfacePrefs { SeenHints = p.Interface.SeenHints } }));
    }

    /// <summary>Harm and aid as the HUD will draw them, so the choice is seen, not described.</summary>
    private static Control Swatches()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(336, 0) });
        row.AddChild(UiKit.Chip("Harm", HudStyle.Wound));
        row.AddChild(UiKit.Chip("Aid", HudStyle.Lichen));
        row.AddChild(UiKit.Chip("Ready", HudStyle.Lichen));
        row.AddChild(UiKit.Chip("Refused", HudStyle.Wound));
        return row;
    }

    private static int Nearest(float[] values, float value) =>
        Array.IndexOf(values, values.OrderBy(v => Math.Abs(v - value)).First());

    // ---- the parts every page is built from ---------------------------------------------------------------------------------

    private VBoxContainer Page(string heading, string note = "")
    {
        var section = UiKit.Section(heading, note);
        section.AddThemeConstantOverride("separation", 6);
        _panes.Detail.AddChild(section);
        return section;
    }

    /// <summary>One setting: its name over one line on what it does at the left, its control, and its value where the control does not show it.</summary>
    internal static HBoxContainer Line(string id, string name, string about, Control control, Label? value = null)
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 56) };
        row.AddThemeConstantOverride("separation", 16);
        var words = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        words.AddThemeConstantOverride("separation", 0);
        words.AddChild(UiKit.Words(name, 18));
        if (about.Length > 0)
            words.AddChild(UiKit.Words(about, 16, HudStyle.Muted));
        row.AddChild(words);
        control.FocusMode = Control.FocusModeEnum.All;
        // A switch is as wide as its word; every other control takes the row's room.
        control.SizeFlagsHorizontal = control is CheckButton ? Control.SizeFlags.ShrinkBegin : Control.SizeFlags.ExpandFill;
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        control.CustomMinimumSize = new Vector2(control is CheckButton ? 120 : 0, control is HSlider ? 24 : 40);
        UiKit.Id(control, id);
        row.AddChild(control);
        if (value is not null)
        {
            value.CustomMinimumSize = new Vector2(72, 0);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            value.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(value);
        }
        return row;
    }

    internal static HBoxContainer Slider(string id, string name, string about, double min, double max, double step, double value, Func<double, string> shown,
        Action<double> set)
    {
        var slider = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value };
        var label = HudStyle.Text(HudStyle.Strong, 18, HudStyle.Bone);
        label.Text = shown(value);
        slider.ValueChanged += v =>
        {
            label.Text = shown(v);
            set(v);
        };
        return Line(id, name, about, slider, label);
    }

    /// <summary>
    /// The whole screen drawn again after the change has been handled: for a setting that changes how the screen itself reads (the tier's
    /// line, the interface's size and its words), with the focus handed back.
    /// </summary>
    private void Redraw() => Callable.From(() =>
    {
        string? focused = UiKit.FocusedId(_sheet.Root);
        Build();
        UiKit.Refocus(_sheet.Root, focused);
    }).CallDeferred();

    internal static HBoxContainer Choice(string id, string name, string about, string[] items, int selected, Action<int> set, Action? after = null)
    {
        var choice = new OptionButton();
        foreach (string item in items)
            choice.AddItem(item);
        choice.Selected = Math.Max(0, selected);
        choice.ItemSelected += i =>
        {
            set((int)i);
            after?.Invoke();
        };
        return Line(id, name, about, choice);
    }

    internal static HBoxContainer Toggle(string id, string name, string about, bool on, Action<bool> set, Action? after = null)
    {
        var check = new CheckButton { ButtonPressed = on, Text = on ? "On" : "Off" };
        check.Toggled += value =>
        {
            check.Text = value ? "On" : "Off";
            set(value);
            after?.Invoke();
        };
        return Line(id, name, about, check);
    }

    private static void Disable(HBoxContainer line, bool disabled)
    {
        foreach (var child in line.GetChildren())
        {
            if (child is BaseButton button)
            {
                button.Disabled = disabled;
                button.FocusMode = disabled ? Control.FocusModeEnum.None : Control.FocusModeEnum.All;
            }
        }
    }

    /// <summary>A page's own Reset, quiet, at its foot.</summary>
    private void Reset(string id, Action reset, string words = "Put this page back to its defaults")
    {
        var button = UiKit.Action(words, () =>
        {
            reset();
            Callable.From(ShowPage).CallDeferred();
        }, UiKit.Kind.Destructive, id: id);
        button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _panes.Detail.AddChild(button);
    }
}
