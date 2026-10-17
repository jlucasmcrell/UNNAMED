// UNNAMED Presentation - the player's preferences: what Settings sets, kept in user://client_prefs.json (PERSISTENCE.md §2; PJ03 §2, §8)
// Godot-free, so the tests compile it as the game does: no game state lives here, and nothing here is part of a save (D-11)

using System.Text.Json;
using System.Text.Json.Serialization;

namespace UNNAMED.Presentation.Settings;

/// <summary>
/// Everything Settings sets, in two parts as canon scopes them (PJ03 §2): <see cref="Machine"/> (how this computer draws) and
/// <see cref="Profile"/> (how this person plays: sound, controls, camera, interface, guidance). Every value is clamped to what the
/// game can take when it is read (<see cref="Normalised"/>), so a hand-edited file never starts the game in a state it cannot draw.
/// </summary>
public sealed record ClientPrefs
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public MachinePrefs Machine { get; init; } = new();
    public ProfilePrefs Profile { get; init; } = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>The preferences in a file's text, normalised; the defaults when the text is not a preferences file.</summary>
    public static ClientPrefs FromJson(string text)
    {
        try
        {
            return (JsonSerializer.Deserialize<ClientPrefs>(text, Json) ?? new ClientPrefs()).Normalised();
        }
        catch (JsonException)
        {
            return new ClientPrefs();
        }
    }

    /// <summary>
    /// The one-time move from the two loose files this replaces: <c>audio_volume.json</c> (Master, Music, Effects from 0 to 1) and
    /// <c>graphics_tier.txt</c> (one word). Either may be absent or unreadable; what can be read is kept, the rest is the default.
    /// </summary>
    public static ClientPrefs Migrate(string? audioVolumeJson, string? graphicsTier)
    {
        var prefs = new ClientPrefs();
        if (graphicsTier?.Trim() is { } tier && Array.IndexOf(MachinePrefs.Tiers, tier) >= 0)
            prefs = prefs with { Machine = prefs.Machine with { Tier = tier } };
        if (audioVolumeJson is not null)
        {
            try
            {
                using var json = JsonDocument.Parse(audioVolumeJson);
                float Read(string name, float fallback) =>
                    json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                        ? (float)v.GetDouble() : fallback;
                var audio = prefs.Profile.Audio;
                prefs = prefs with
                {
                    Profile = prefs.Profile with
                    {
                        Audio = audio with { Master = Read("Master", audio.Master), Music = Read("Music", audio.Music), Effects = Read("Effects", audio.Effects) },
                    },
                };
            }
            catch (JsonException)
            {
            }
        }
        return prefs.Normalised();
    }

    /// <summary>Every value inside the range the game can take; an unknown choice falls back to its default.</summary>
    public ClientPrefs Normalised() => this with
    {
        Version = CurrentVersion,
        Machine = (Machine ?? new MachinePrefs()).Normalised(),
        Profile = (Profile ?? new ProfilePrefs()).Normalised(),
    };
}

public enum WindowMode { Windowed, Borderless, Fullscreen }

/// <summary>How this computer draws: the window, the frame rate, the tier and the dials over it (PJ03 §4.1).</summary>
public sealed record MachinePrefs
{
    public static readonly string[] Tiers = { "low", "medium", "high", "ultra" };
    public static readonly int[] FrameCaps = { 30, 60, 90, 120, 144, 165, 240, 0 };

    public WindowMode WindowMode { get; init; } = WindowMode.Borderless;

    /// <summary>The window's size in windowed mode, "1920x1080"; borderless and fullscreen take the screen's.</summary>
    public string Resolution { get; init; } = "1920x1080";

    /// <summary>The 3D image's share of the window's pixels (0.5-1.0); the interface is always drawn at full size.</summary>
    public float RenderScale { get; init; } = 1f;

    public bool Vsync { get; init; } = true;

    /// <summary>Frames a second at most; 0 is no ceiling.</summary>
    public int FrameCap { get; init; }

    /// <summary>Picture brightness, 1 as drawn (0.6-1.4).</summary>
    public float Brightness { get; init; } = 1f;

    /// <summary>The quality tier; null until the player chooses one (the launcher's or the game's default then stands).</summary>
    public string? Tier { get; init; }

    public GraphicsDials Dials { get; init; } = new();

    public MachinePrefs Normalised() => this with
    {
        WindowMode = Enum.IsDefined(WindowMode) ? WindowMode : WindowMode.Borderless,
        Resolution = ParseResolution(Resolution) is not null ? Resolution : "1920x1080",
        RenderScale = Math.Clamp(float.IsFinite(RenderScale) ? RenderScale : 1f, 0.5f, 1f),
        FrameCap = Array.IndexOf(FrameCaps, FrameCap) >= 0 ? FrameCap : 0,
        Brightness = Math.Clamp(float.IsFinite(Brightness) ? Brightness : 1f, 0.6f, 1.4f),
        Tier = Tier is not null && Array.IndexOf(Tiers, Tier) >= 0 ? Tier : null,
        Dials = (Dials ?? new GraphicsDials()).Normalised(),
    };

    /// <summary>"1920x1080" as (1920, 1080); null when it is not a size a window can have.</summary>
    public static (int Width, int Height)? ParseResolution(string? text) =>
        text?.Split('x') is [var w, var h] && int.TryParse(w, out int width) && int.TryParse(h, out int height)
        && width is >= 640 and <= 16384 && height is >= 360 and <= 16384 ? (width, height) : null;
}

/// <summary>
/// The per-feature dials over the tier (the owner: "players get dials and sliders"). <see cref="Dial.Tier"/> leaves a feature as the tier
/// draws it; the others set it whatever the tier. Each is named for what the player sees.
/// </summary>
public sealed record GraphicsDials
{
    public Dial Shadows { get; init; } = Dial.Tier;
    public Dial GlobalIllumination { get; init; } = Dial.Tier;
    public Dial AmbientOcclusion { get; init; } = Dial.Tier;
    public Dial Reflections { get; init; } = Dial.Tier;
    public Dial Volumetrics { get; init; } = Dial.Tier;

    /// <summary>How many of the ground's plants are drawn, as a share of the tier's (0.25-1.5). Applies the next time the game starts.</summary>
    public float PlantDensity { get; init; } = 1f;

    /// <summary>How far models and plants keep their detail, as a share of the tier's (0.5-1.5). Applies the next time the game starts.</summary>
    public float DrawDistance { get; init; } = 1f;

    public GraphicsDials Normalised() => this with
    {
        Shadows = Enum.IsDefined(Shadows) ? Shadows : Dial.Tier,
        GlobalIllumination = Enum.IsDefined(GlobalIllumination) ? GlobalIllumination : Dial.Tier,
        AmbientOcclusion = Enum.IsDefined(AmbientOcclusion) ? AmbientOcclusion : Dial.Tier,
        Reflections = Enum.IsDefined(Reflections) ? Reflections : Dial.Tier,
        Volumetrics = Enum.IsDefined(Volumetrics) ? Volumetrics : Dial.Tier,
        PlantDensity = Math.Clamp(float.IsFinite(PlantDensity) ? PlantDensity : 1f, 0.25f, 1.5f),
        DrawDistance = Math.Clamp(float.IsFinite(DrawDistance) ? DrawDistance : 1f, 0.5f, 1.5f),
    };
}

/// <summary>A feature's dial: as the tier has it, off, low or high.</summary>
public enum Dial { Tier, Off, Low, High }

/// <summary>How this person plays: what they hear, the keys, the camera, the interface and how much the game guides them.</summary>
public sealed record ProfilePrefs
{
    public AudioPrefs Audio { get; init; } = new();
    public ControlPrefs Controls { get; init; } = new();
    public CameraPrefs Camera { get; init; } = new();
    public InterfacePrefs Interface { get; init; } = new();

    /// <summary>Whether the first-launch screen has been answered (PJ02 §3.2); it is offered once.</summary>
    public bool FirstLaunchDone { get; init; }

    /// <summary>Whether a character of this profile has walked the road in to the waystation: only then is the skip offered (PJ06 §8).</summary>
    public bool RoadWalked { get; init; }

    /// <summary>The skip as last chosen, "Start at the Waystone": once offered it defaults on, and is remembered (PJ06 §8).</summary>
    public bool SkipRoad { get; init; } = true;

    public ProfilePrefs Normalised() => this with
    {
        Audio = (Audio ?? new AudioPrefs()).Normalised(),
        Controls = (Controls ?? new ControlPrefs()).Normalised(),
        Camera = (Camera ?? new CameraPrefs()).Normalised(),
        Interface = (Interface ?? new InterfacePrefs()).Normalised(),
    };
}

/// <summary>The six volumes, 0 to 1, one a bus: everything, the score, the world's sounds, its beds, the interface and voices.</summary>
public sealed record AudioPrefs
{
    public float Master { get; init; } = 1f;
    public float Music { get; init; } = 0.7f;
    public float Effects { get; init; } = 1f;
    public float Ambience { get; init; } = 1f;
    public float Interface { get; init; } = 1f;
    public float Voice { get; init; } = 1f;

    private static float Level(float value) => Math.Clamp(float.IsFinite(value) ? value : 1f, 0f, 1f);

    public AudioPrefs Normalised() => this with
    {
        Master = Level(Master), Music = Level(Music), Effects = Level(Effects), Ambience = Level(Ambience), Interface = Level(Interface), Voice = Level(Voice),
    };
}

/// <summary>
/// The keys: the player's own bindings over the defaults - only the actions they changed are kept, as each action's bindings in order,
/// "key:&lt;physical keycode&gt;" or "mouse:&lt;button&gt;" - and which actions are held and which press on, press off.
/// </summary>
public sealed record ControlPrefs
{
    /// <summary>The actions that can be held or toggled, and their defaults: crouch toggles (the owner's M6 playtest), the rest are held.</summary>
    public static readonly IReadOnlyDictionary<string, bool> Toggleable = new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        ["sprint"] = false, ["walk"] = false, ["crouch"] = true, ["guard"] = false,
    };

    public Dictionary<string, string[]> Bindings { get; init; } = new(StringComparer.Ordinal);

    /// <summary>The toggleable actions set to press on, press off; the others are held.</summary>
    public Dictionary<string, bool> Toggles { get; init; } = new(StringComparer.Ordinal);

    public bool IsToggle(string action) =>
        Toggles.TryGetValue(action, out bool on) ? on : Toggleable.GetValueOrDefault(action);

    public ControlPrefs Normalised() => this with
    {
        Bindings = (Bindings ?? new()).Where(b => b.Key.Length > 0 && b.Value is not null)
            .ToDictionary(b => b.Key, b => b.Value.Where(Binding.IsValid).Distinct(StringComparer.Ordinal).Take(Binding.PerAction).ToArray(), StringComparer.Ordinal),
        Toggles = (Toggles ?? new()).Where(t => Toggleable.ContainsKey(t.Key)).ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal),
    };
}

/// <summary>The camera: how fast it turns, which way is up, how wide it sees, and how much it may move on its own.</summary>
public sealed record CameraPrefs
{
    /// <summary>Radians a pixel of mouse travel turns the camera, at 100 %.</summary>
    public const float BaseSensitivity = 0.0025f;

    /// <summary>The look speed as a share of the base (0.2-4).</summary>
    public float Sensitivity { get; init; } = 1f;

    public bool InvertY { get; init; }

    /// <summary>The vertical field of view in degrees (60-110).</summary>
    public float Fov { get; init; } = 75f;

    /// <summary>How much of any camera shake is felt (0-1). No shake exists yet; this is the hook it will read (PJ03 §4.4).</summary>
    public float Shake { get; init; } = 1f;

    /// <summary>How much of any head bob is felt (0-1). No bob exists yet; this is the hook it will read.</summary>
    public float Bob { get; init; } = 1f;

    /// <summary>Flashes held down: a burst of light reaches at most a third of its brightness (PJ03 §6.3).</summary>
    public bool ReduceFlashes { get; init; }

    public CameraPrefs Normalised() => this with
    {
        Sensitivity = Math.Clamp(float.IsFinite(Sensitivity) ? Sensitivity : 1f, 0.2f, 4f),
        Fov = Math.Clamp(float.IsFinite(Fov) ? Fov : 75f, 60f, 110f),
        Shake = Math.Clamp(float.IsFinite(Shake) ? Shake : 1f, 0f, 1f),
        Bob = Math.Clamp(float.IsFinite(Bob) ? Bob : 1f, 0f, 1f),
    };
}

public enum ColourVision { None, Protanopia, Deuteranopia, Tritanopia }

public enum Guidance { Full, Light, Off }

/// <summary>What chat shows (WP16): everything, only what is about play (offers to set out together, bounties, the system), or nothing. The members match World's ChatMode, in order.</summary>
public enum ChatSetting { On, PlayRelevantOnly, Off }

/// <summary>
/// The interface: its size, its text's size, contrast and colours, how long notices stay, subtitles (read by dialogue and voice), how
/// much the game guides (read by the hints), the hints already learned, and what chat shows.
/// </summary>
public sealed record InterfacePrefs
{
    public static readonly double[] NoticeSeconds = { 0.75, 1, 1.5, 2.5 };

    /// <summary>The whole interface's size (0.75-1.5).</summary>
    public float UiScale { get; init; } = 1f;

    /// <summary>The size of words, over the interface's (1-2).</summary>
    public float TextSize { get; init; } = 1f;

    public bool HighContrast { get; init; }
    public ColourVision ColourVision { get; init; } = ColourVision.None;

    /// <summary>How long a notice stays, as a multiple of its own time: short, normal, long, longer.</summary>
    public double NoticeDuration { get; init; } = 1;

    public bool Subtitles { get; init; } = true;
    public Guidance Guidance { get; init; } = Guidance.Full;

    /// <summary>What chat shows (WP16); on by default. Chat itself is never saved: this only filters what is shown.</summary>
    public ChatSetting Chat { get; init; } = ChatSetting.On;

    /// <summary>The hints this player has seen; "Reset hints" empties it so every hint shows again.</summary>
    public string[] SeenHints { get; init; } = Array.Empty<string>();

    public InterfacePrefs Normalised() => this with
    {
        UiScale = Math.Clamp(float.IsFinite(UiScale) ? UiScale : 1f, 0.75f, 1.5f),
        TextSize = Math.Clamp(float.IsFinite(TextSize) ? TextSize : 1f, 1f, 2f),
        ColourVision = Enum.IsDefined(ColourVision) ? ColourVision : ColourVision.None,
        NoticeDuration = Array.IndexOf(NoticeSeconds, NoticeDuration) >= 0 ? NoticeDuration : 1,
        Guidance = Enum.IsDefined(Guidance) ? Guidance : Guidance.Full,
        Chat = Enum.IsDefined(Chat) ? Chat : ChatSetting.On,
        SeenHints = (SeenHints ?? Array.Empty<string>()).Where(h => !string.IsNullOrEmpty(h)).Distinct(StringComparer.Ordinal).ToArray(),
    };
}

/// <summary>
/// The four first-launch comfort bundles (PJ02 §3.2), in any combination; each names what it changed, so nothing a preset does is hidden.
/// </summary>
public static class ComfortPresets
{
    public static readonly string[] Names = { "Vision", "Hearing", "Motion", "Motor and timing" };

    public static (ClientPrefs Prefs, string[] Changed) Apply(ClientPrefs prefs, string preset)
    {
        var p = prefs.Profile;
        switch (preset)
        {
            case "Vision":
                return (prefs with { Profile = p with { Interface = p.Interface with { TextSize = Math.Max(p.Interface.TextSize, 1.25f), HighContrast = true, Subtitles = true } } },
                    new[] { "Text size 125 %", "High contrast on", "Subtitles on" });
            case "Hearing":
                return (prefs with { Profile = p with { Interface = p.Interface with { Subtitles = true, NoticeDuration = Math.Max(p.Interface.NoticeDuration, 1.5) } } },
                    new[] { "Subtitles on", "Notices stay longer" });
            case "Motion":
                return (prefs with { Profile = p with { Camera = p.Camera with { Shake = 0, Bob = 0, ReduceFlashes = true } } },
                    new[] { "Camera shake off", "Head bob off", "Flashes reduced" });
            case "Motor and timing":
                var toggles = new Dictionary<string, bool>(p.Controls.Toggles, StringComparer.Ordinal)
                    { ["sprint"] = true, ["walk"] = true, ["crouch"] = true, ["guard"] = true };
                return (prefs with { Profile = p with { Controls = p.Controls with { Toggles = toggles }, Interface = p.Interface with { NoticeDuration = 2.5 } } },
                    new[] { "Sprint, walk, crouch and guard press on, press off", "Notices stay longest" });
            default:
                throw new ArgumentException($"No comfort preset '{preset}'", nameof(preset));
        }
    }
}

/// <summary>
/// A binding as the preferences file keeps it - "key:&lt;physical keycode&gt;" or "mouse:&lt;button&gt;" - and the conflict check over a
/// whole map. A conflict is one binding on two actions that are live at the same time: the number keys answer in a conversation and
/// use quick slots outside one, which is two contexts and no conflict (PJ03 §7).
/// </summary>
public static class Binding
{
    public const int PerAction = 2;

    public static bool IsValid(string? binding) =>
        binding?.Split(':') is [var kind, var code] && kind is "key" or "mouse" && long.TryParse(code, out long n) && n > 0;

    /// <summary>The contexts an action is live in: a conversation's replies only in one; Escape in both; everything else in play.</summary>
    public static IEnumerable<string> Contexts(string action) => action switch
    {
        _ when action.StartsWith("reply_", StringComparison.Ordinal) => new[] { "conversation" },
        "release_mouse" => new[] { "play", "conversation" },
        _ => new[] { "play" },
    };

    /// <summary>Every binding held by more than one action in a context, with those actions in the map's order.</summary>
    public static IReadOnlyList<(string Binding, string[] Actions)> Conflicts(IEnumerable<KeyValuePair<string, string[]>> map)
    {
        var holders = new Dictionary<(string Context, string Binding), List<string>>();
        var order = new List<(string, string)>();
        foreach (var (action, bindings) in map)
        {
            foreach (string context in Contexts(action))
            {
                foreach (string binding in bindings.Distinct(StringComparer.Ordinal))
                {
                    if (!holders.TryGetValue((context, binding), out var actions))
                    {
                        holders[(context, binding)] = actions = new List<string>();
                        order.Add((context, binding));
                    }
                    if (!actions.Contains(action))
                        actions.Add(action);
                }
            }
        }
        var found = new List<(string, string[])>();
        foreach (var key in order)
        {
            var actions = holders[key];
            if (actions.Count > 1 && !found.Any(f => f.Item1 == key.Item2 && f.Item2.SequenceEqual(actions)))
                found.Add((key.Item2, actions.ToArray()));
        }
        return found;
    }

    /// <summary>The actions other than <paramref name="action"/> that already hold <paramref name="binding"/> in a context it shares with them.</summary>
    public static string[] Holders(IEnumerable<KeyValuePair<string, string[]>> map, string action, string binding)
    {
        var mine = Contexts(action).ToHashSet(StringComparer.Ordinal);
        return map.Where(m => m.Key != action && m.Value.Contains(binding) && Contexts(m.Key).Any(mine.Contains)).Select(m => m.Key).ToArray();
    }
}
