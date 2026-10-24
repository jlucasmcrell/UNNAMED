// UNNAMED Presentation - the player's preferences kept and applied: user://client_prefs.json, the window, the mix, the keys, the picture
// Godot presentation only (D-11): how the game is drawn, heard and played on this machine; nothing here is game state or part of a save

using Godot;
using UNNAMED.Presentation.Audio;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Settings;

/// <summary>
/// The one place preferences live while the game runs (<see cref="Current"/>) and the one file they are kept in,
/// <c>user://client_prefs.json</c>. It absorbs the two loose files that came before it once, the first time it finds no file of its
/// own. Every change applies at once (PJ03 §3) - except the tier and the two start-time dials, which say so - and is written at once.
/// A harness run reads and writes nothing: it plays on the defaults, so its captures stay comparable.
/// </summary>
public static class SettingsStore
{
    public const string Path = "user://client_prefs.json";
    private const string LegacyAudio = "user://audio_volume.json", LegacyTier = "user://graphics_tier.txt";

    /// <summary>The input actions a player may move to other keys, in the order the Controls page lists them; Escape stays Escape.</summary>
    public static readonly string[] Remappable =
    {
        "move_forward", "move_back", "move_left", "move_right", "sprint", "walk", "jump", "crouch", "auto_run",
        "interact", "attack", "guard", "dodge", "use", "quick_1", "quick_2", "quick_3", "quick_4", "quick_5", "quick_6", "quick_7", "quick_8", "quick_9", "quick_10", "quick_11", "quick_12", "take_all",
        "bar_page_1", "bar_page_2", "bar_page_3", "bar_page_4", "bar_page_5", "bar_page_6", "bar_modifier", "bar_scroll_modifier",
        "gear_set_1", "gear_set_2", "gear_set_3", "gear_set_4", "gear_set_5", "gear_set_6", "gear_set_7", "gear_set_8", "gear_set_9", "gear_set_10",
        "companion_order", "first_person", "shoulder_swap", "free_cursor",
        "inventory", "character", "skills", "journal", "map", "party", "chat", "help", "saves", "quicksave", "report_problem", "hide_interface", "screenshot",
        "dialogue_skip", "reply_1", "reply_2", "reply_3", "reply_4", "reply_5", "reply_6", "reply_7", "reply_8", "reply_9",
    };

    private static readonly Dictionary<string, string[]> Defaults = new(StringComparer.Ordinal);
    private static bool _persist;
    private static Baseline? _baseline;

    public static ClientPrefs Current { get; private set; } = new ClientPrefs { Profile = new ProfilePrefs { FirstLaunchDone = true } };

    /// <summary>Whatever changed, after it was applied: the views that read a preference each frame need nothing, the rest listen here.</summary>
    public static event Action? Changed;

    /// <summary>
    /// Read the player's file (a player's run), or move the two old files into a new one, or begin with the defaults - the first-launch
    /// screen is then offered. A harness run takes the defaults and never writes.
    /// </summary>
    public static void Load(bool persist)
    {
        _persist = persist;
        if (!persist)
        {
            Current = new ClientPrefs { Profile = new ProfilePrefs { FirstLaunchDone = true } };
            return;
        }
        string path = ProjectSettings.GlobalizePath(Path);
        if (File.Exists(path))
        {
            Current = ClientPrefs.FromJson(Read(path) ?? "");
            return;
        }
        string audio = ProjectSettings.GlobalizePath(LegacyAudio), tier = ProjectSettings.GlobalizePath(LegacyTier);
        Current = ClientPrefs.Migrate(Read(audio), Read(tier));
        // Someone who set the old files has been through the start screen already: no first-launch screen for them.
        if (File.Exists(audio) || File.Exists(tier))
        {
            Current = Current with { Profile = Current.Profile with { FirstLaunchDone = true } };
            if (Write())
            {
                try
                {
                    File.Delete(audio);
                    File.Delete(tier);
                    GD.Print($"UNNAMED settings: {LegacyAudio} and {LegacyTier} moved into {Path}");
                }
                catch (IOException e)
                {
                    GD.PushWarning($"UNNAMED settings: moved into {Path}, but an old file could not be removed ({e.Message}); it is not read again");
                }
            }
        }
    }

    /// <summary>A change: kept, applied and written at once.</summary>
    public static void Set(ClientPrefs prefs)
    {
        Current = prefs.Normalised();
        Write();
        Changed?.Invoke();
    }

    /// <summary>
    /// Kept and written, with nothing to apply: a hint marked seen. Through <see cref="Set"/> every hint re-applied the keys (their
    /// bindings compare by reference), a 230 ms stall mid-fight each time one showed.
    /// </summary>
    public static void Remember(ClientPrefs prefs)
    {
        Current = prefs.Normalised();
        Write();
    }

    private static string? Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool Write()
    {
        if (!_persist)
            return false;
        try
        {
            string path = ProjectSettings.GlobalizePath(Path);
            File.WriteAllText(path + ".tmp", Current.ToJson());
            File.Move(path + ".tmp", path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"UNNAMED settings: {Path} could not be written ({e.Message}); the change holds until the game closes");
            return false;
        }
    }

    // ---- the keys -------------------------------------------------------------------------------------------------------------

    /// <summary>The bindings <c>DefineInput</c> made, taken once as the defaults Reset returns to.</summary>
    public static void CaptureDefaults()
    {
        foreach (string action in Remappable.Where(a => InputMap.HasAction(a)))
            Defaults[action] = InputMap.ActionGetEvents(action).Select(Encode).OfType<string>().ToArray();
    }

    public static string[] DefaultBindings(string action) => Defaults.GetValueOrDefault(action) ?? Array.Empty<string>();

    /// <summary>Each remappable action's bindings as they stand: the player's where they changed them, the defaults elsewhere.</summary>
    public static IReadOnlyList<KeyValuePair<string, string[]>> Bindings() =>
        Remappable.Where(Defaults.ContainsKey)
            .Select(a => new KeyValuePair<string, string[]>(a, Current.Profile.Controls.Bindings.GetValueOrDefault(a) ?? Defaults[a])).ToList();

    /// <summary>The input map made to match the bindings as they stand.</summary>
    public static void ApplyBindings()
    {
        foreach (var (action, bindings) in Bindings())
        {
            InputMap.ActionEraseEvents(action);
            foreach (string binding in bindings)
            {
                if (Decode(binding) is { } input)
                    InputMap.ActionAddEvent(action, input);
            }
        }
    }

    /// <summary>A key or button pressed as the preferences keep it; null for what cannot be bound (the wheel zooms, Escape closes).</summary>
    public static string? Encode(InputEvent input) => input switch
    {
        InputEventKey key when (key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode) is var code && code != Key.None && code != Key.Escape
            => $"key:{(long)code}",
        InputEventMouseButton mouse when mouse.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight or MouseButton.None)
            => $"mouse:{(long)mouse.ButtonIndex}",
        _ => null,
    };

    public static InputEvent? Decode(string binding) => binding.Split(':') is [var kind, var text] && long.TryParse(text, out long code)
        ? kind switch
        {
            "key" => new InputEventKey { PhysicalKeycode = (Key)code },
            "mouse" => new InputEventMouseButton { ButtonIndex = (MouseButton)code },
            _ => null,
        }
        : null;

    /// <summary>A binding as this keyboard prints it.</summary>
    public static string Describe(string binding) => Decode(binding) is { } input ? HelpPanel.KeyName(input) : binding;

    // ---- the window, the picture and the sound --------------------------------------------------------------------------------

    /// <summary>
    /// What a player's run is drawn with before the world is built: the tier chosen in Settings over the launcher's <c>--visual tier=</c>
    /// (the launcher's is only its default), and the two dials that are read as the world is built.
    /// </summary>
    public static string? StartVisuals(string? spec)
    {
        VisualOptions.PlantScale = Current.Machine.Dials.PlantDensity;
        VisualOptions.ReachScale = Current.Machine.Dials.DrawDistance;
        if (spec is null)
            return spec;
        var parts = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (Current.Machine.Tier is not null)
            return string.Join(",", parts.Where(part => !part.StartsWith("tier=", StringComparison.Ordinal)));
        // No tier chosen yet: the launcher's, but no higher than the card's memory carries (public playtest gate B4/C3).
        int asked = Array.IndexOf(MachinePrefs.Tiers, parts.FirstOrDefault(part => part.StartsWith("tier=", StringComparison.Ordinal))?[5..]);
        int fits = Array.IndexOf(MachinePrefs.Tiers, TierForMemory(GraphicsMemoryGb()));
        if (asked <= fits)
            return spec;
        GD.Print($"UNNAMED tier: {MachinePrefs.Tiers[fits]} for this card's memory (the launcher asked {MachinePrefs.Tiers[asked]}); Settings can change it");
        return string.Join(",", parts.Select(part => part.StartsWith("tier=", StringComparison.Ordinal) ? "tier=" + MachinePrefs.Tiers[fits] : part));
    }

    /// <summary>The highest tier measured to fit a card of <paramref name="gb"/> GB (the --perf route, 2026-10-24); null memory caps nothing.</summary>
    public static string TierForMemory(double? gb) => gb switch
    {
        null => "ultra",
        < 6 => "low",
        < 10 => "medium",
        < 14 => "high",
        _ => "ultra",
    };

    /// <summary>
    /// The largest graphics card's dedicated memory, from the display adapters' registry keys (Godot reports the adapter's name, not its
    /// memory); null off Windows or when no adapter says.
    /// </summary>
    private static double? GraphicsMemoryGb()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        using var adapters = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (adapters is null)
            return null;
        long most = 0;
        foreach (string name in adapters.GetSubKeyNames().Where(n => n.All(char.IsAsciiDigit)))
        {
            using var adapter = adapters.OpenSubKey(name);
            if (adapter?.GetValue("HardwareInformation.qwMemorySize") is long bytes)
                most = Math.Max(most, bytes);
        }
        return most > 0 ? most / 1073741824.0 : null;
    }

    /// <summary>The window as the player keeps it: its mode, its size when windowed, VSync and the frame ceiling.</summary>
    public static void ApplyWindow()
    {
        var machine = Current.Machine;
        if (DisplayServer.GetName() == "headless")
            return;
        var mode = machine.WindowMode switch
        {
            WindowMode.Windowed => DisplayServer.WindowMode.Windowed,
            WindowMode.Fullscreen => DisplayServer.WindowMode.ExclusiveFullscreen,
            _ => DisplayServer.WindowMode.Fullscreen,
        };
        if (DisplayServer.WindowGetMode() != mode)
            DisplayServer.WindowSetMode(mode);
        if (mode == DisplayServer.WindowMode.Windowed && MachinePrefs.ParseResolution(machine.Resolution) is var (w, h))
        {
            var screen = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
            var size = new Vector2I(Math.Min(w, screen.Size.X), Math.Min(h, screen.Size.Y));
            if (DisplayServer.WindowGetSize() != size)
            {
                DisplayServer.WindowSetSize(size);
                DisplayServer.WindowSetPosition(screen.Position + (screen.Size - size) / 2);
            }
        }
        DisplayServer.WindowSetVsyncMode(machine.Vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = machine.FrameCap;
    }

    /// <summary>The sizes a window can be on this screen, from 1280 x 720 up to the screen's own, as "WxH".</summary>
    public static string[] Resolutions()
    {
        var screen = DisplayServer.GetName() == "headless" ? new Vector2I(3840, 2160) : DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen());
        var sizes = new[] { (1280, 720), (1366, 768), (1600, 900), (1920, 1080), (2560, 1440), (3200, 1800), (3840, 2160) }
            .Where(s => s.Item1 <= screen.X && s.Item2 <= screen.Y).Select(s => $"{s.Item1}x{s.Item2}").ToList();
        string own = $"{screen.X}x{screen.Y}";
        if (!sizes.Contains(own))
            sizes.Add(own);
        return sizes.ToArray();
    }

    public static void ApplyAudio() => AudioMix.Apply(Current.Profile.Audio);

    /// <summary>
    /// The interface size a first launch starts at: the interface is drawn for a 1080-line screen and scaled with the window, so on a
    /// window under 900 lines its 16 px floor falls to about 11 px - a quarter larger brings it back to 13.
    /// </summary>
    public static float RecommendedUiScale() =>
        DisplayServer.GetName() != "headless" && DisplayServer.WindowGetSize().Y < 900 ? 1.25f : 1f;

    /// <summary>The interface's size, its text's size, contrast and colours: the HUD and every screen restyled.</summary>
    public static void ApplyInterface(SceneTree tree)
    {
        var ui = Current.Profile.Interface;
        tree.Root.ContentScaleFactor = ui.UiScale;
        HudStyle.Use(ui.TextSize, ui.HighContrast, ui.ColourVision);
        UiKit.Restyle(tree.Root);
    }

    /// <summary>
    /// The picture: render scale and brightness, and each graphics dial over the world's environment and its sun. The tier's own values
    /// are taken the first time, so a dial set back to "As the tier" returns to them.
    /// </summary>
    public static void ApplyPicture(Viewport viewport)
    {
        var machine = Current.Machine;
        viewport.Scaling3DScale = machine.RenderScale;
        viewport.Scaling3DMode = machine.RenderScale < 0.999f && machine.Upscaler == Upscaler.Fsr ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        VisualOptions.ApplyAntiAliasing(viewport, machine.AntiAliasing switch
        {
            AntiAliasing.Off => "off", AntiAliasing.Fxaa => "fxaa", AntiAliasing.Taa => "taa",
            AntiAliasing.Msaa2X => "msaa2", AntiAliasing.Msaa4X => "msaa", AntiAliasing.Msaa4XTaa => "msaa_taa",
            _ => VisualOptions.AntiAliasing,
        });
        if (viewport.FindWorld3D()?.Environment is not { } environment)
            return;
        var sun = viewport.GetTree().Root.FindChildren("*", "DirectionalLight3D", true, false).OfType<DirectionalLight3D>().FirstOrDefault(l => l.Visible);
        if (_baseline is null || !ReferenceEquals(_baseline.Environment, environment))
            _baseline = new Baseline(environment, environment.SdfgiEnabled, environment.SdfgiCascades, environment.SsilEnabled, environment.SsaoEnabled,
                environment.SsrEnabled, environment.SsrMaxSteps, environment.VolumetricFogEnabled, sun?.ShadowEnabled ?? true, sun?.DirectionalShadowMaxDistance ?? 100f);
        var b = _baseline;
        var dials = machine.Dials;
        // The server-wide settings back to the project's and the tier's, so a dial set back to "As the tier" leaves nothing behind.
        RenderingServer.DirectionalShadowAtlasSetSize(ProjectSettings.GetSetting("rendering/lights_and_shadows/directional_shadow/size").AsInt32(), true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(
            (RenderingServer.ShadowQuality)ProjectSettings.GetSetting("rendering/lights_and_shadows/directional_shadow/soft_shadow_filter_quality").AsInt32());
        RenderingServer.EnvironmentSetSsaoQuality((RenderingServer.EnvironmentSsaoQuality)ProjectSettings.GetSetting("rendering/environment/ssao/quality").AsInt32(),
            true, 0.5f, 2, 50, 300);
        RenderingServer.EnvironmentSetVolumetricFogVolumeSize(ProjectSettings.GetSetting("rendering/environment/volumetric_fog/volume_size").AsInt32(),
            ProjectSettings.GetSetting("rendering/environment/volumetric_fog/volume_depth").AsInt32());
        Greybox.RenderTiers.ApplyGlobal();
        environment.AdjustmentEnabled = Math.Abs(machine.Brightness - 1f) > 0.001f;
        environment.AdjustmentBrightness = machine.Brightness;

        (environment.SdfgiEnabled, environment.SdfgiCascades, environment.SsilEnabled) = dials.GlobalIllumination switch
        {
            Dial.Off => (false, b.SdfgiCascades, false),
            Dial.Low => (true, 4, false),
            Dial.High => (true, 6, true),
            _ => (b.Sdfgi, b.SdfgiCascades, b.Ssil),
        };
        environment.SsaoEnabled = dials.AmbientOcclusion switch { Dial.Off => false, Dial.Tier => b.Ssao, _ => true };
        if (dials.AmbientOcclusion is Dial.Low or Dial.High)
            RenderingServer.EnvironmentSetSsaoQuality(dials.AmbientOcclusion == Dial.Low ? RenderingServer.EnvironmentSsaoQuality.Low : RenderingServer.EnvironmentSsaoQuality.High,
                true, 0.5f, 2, 50, 300);
        (environment.SsrEnabled, environment.SsrMaxSteps) = dials.Reflections switch
        {
            Dial.Off => (false, b.SsrSteps),
            Dial.Low => (true, 32),
            Dial.High => (true, 64),
            _ => (b.Ssr, b.SsrSteps),
        };
        environment.VolumetricFogEnabled = dials.Volumetrics switch { Dial.Off => false, Dial.Tier => b.Volumetrics, _ => true };
        if (dials.Volumetrics is Dial.Low or Dial.High)
            RenderingServer.EnvironmentSetVolumetricFogVolumeSize(dials.Volumetrics == Dial.Low ? 64 : 128, dials.Volumetrics == Dial.Low ? 128 : 256);
        if (sun is not null)
        {
            sun.ShadowEnabled = dials.Shadows switch { Dial.Off => false, Dial.Tier => b.Shadows, _ => true };
            sun.DirectionalShadowMaxDistance = dials.Shadows switch { Dial.Low => Math.Min(b.ShadowDistance, 60f), Dial.High => Math.Max(b.ShadowDistance, 150f), _ => b.ShadowDistance };
            if (dials.Shadows is Dial.Low or Dial.High)
            {
                RenderingServer.DirectionalShadowAtlasSetSize(dials.Shadows == Dial.Low ? 2048 : 8192, true);
                RenderingServer.DirectionalSoftShadowFilterSetQuality(dials.Shadows == Dial.Low ? RenderingServer.ShadowQuality.SoftLow : RenderingServer.ShadowQuality.SoftHigh);
            }
        }
    }

    /// <summary>The environment's and the sun's values as the tier built them, before any dial.</summary>
    private sealed record Baseline(Godot.Environment Environment, bool Sdfgi, int SdfgiCascades, bool Ssil, bool Ssao, bool Ssr, int SsrSteps, bool Volumetrics,
        bool Shadows, float ShadowDistance);
}
