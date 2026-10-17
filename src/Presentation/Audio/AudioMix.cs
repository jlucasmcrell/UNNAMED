// UNNAMED Presentation - the mix: one bus a kind of sound under Master, at the volumes Settings sets
// Godot presentation only (D-11): how loud the game is; nothing here is game state or part of a save

using Godot;
using UNNAMED.Presentation.Settings;

namespace UNNAMED.Presentation.Audio;

/// <summary>
/// Five buses under Master (PJ03 §4.2): <see cref="Music"/> for the score, <see cref="Ambience"/> for the beds and the world's distant
/// one-shots, <see cref="Interface"/> for the menus' sounds, <see cref="Voice"/> for spoken lines, and <see cref="Effects"/> for every
/// other sound. A sound goes on the bus its manifest group names (<see cref="BusFor"/>).
/// </summary>
public static class AudioMix
{
    public const string Music = "Music";
    public const string Effects = "Effects";
    public const string Ambience = "Ambience";
    public const string Interface = "Interface";
    public const string Voice = "Voice";

    /// <summary>
    /// The buses, each sending to Master. <c>res://default_bus_layout.tres</c> carries them, in this order, so the engine starts with them
    /// (WP10 A3); this makes any the layout lacks - a layout edited away, or a harness without it.
    /// </summary>
    public static void EnsureBuses()
    {
        foreach (string name in new[] { Music, Effects, Ambience, Interface, Voice })
        {
            if (AudioServer.GetBusIndex(name) >= 0)
                continue;
            AudioServer.AddBus();
            int index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
            AudioServer.SetBusSend(index, "Master");
        }
    }

    /// <summary>The bus for a sound of a manifest group: "ambience.bed" plays on Ambience, "ui" on Interface, "voice.*" on Voice.</summary>
    public static string BusFor(string? group) => group?.Split('.')[0] switch
    {
        "ambience" => Ambience,
        "ui" => Interface,
        "voice" or "vo" or "dialogue" or "bark" => Voice,
        "music" => Music,
        _ => Effects,
    };

    public static void Apply(AudioPrefs volumes)
    {
        EnsureBuses();
        Set("Master", volumes.Master);
        Set(Music, volumes.Music);
        Set(Effects, volumes.Effects);
        Set(Ambience, volumes.Ambience);
        Set(Interface, volumes.Interface);
        Set(Voice, volumes.Voice);
    }

    private static void Set(string bus, float level)
    {
        int index = AudioServer.GetBusIndex(bus);
        AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Math.Max(0.0001f, Math.Clamp(level, 0f, 1f))));
        AudioServer.SetBusMute(index, level <= 0.001f);
    }
}
