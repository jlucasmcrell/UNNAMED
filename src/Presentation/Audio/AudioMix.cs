// UNNAMED Presentation - the mix the player sets: master, music and effects volumes, kept for the next launch
// Godot presentation only (D-11): how loud the game is; nothing here is game state or part of a save

using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Audio;

/// <summary>
/// Two buses under Master - <see cref="Music"/> for the score and <see cref="Effects"/> for every other sound - and the three volumes
/// (0..1) the Sound screen sets, kept in <c>user://audio_volume.json</c>. Harness runs read the defaults, never the player's file.
/// </summary>
public static class AudioMix
{
    public const string ConfigPath = "user://audio_volume.json";
    public const string Music = "Music";
    public const string Effects = "Effects";

    public sealed record Volumes(float Master = 1f, float Music = 0.7f, float Effects = 1f);

    /// <summary>The Music and Effects buses, made once per engine run, both sending to Master.</summary>
    public static void EnsureBuses()
    {
        foreach (string name in new[] { Music, Effects })
        {
            if (AudioServer.GetBusIndex(name) >= 0)
                continue;
            AudioServer.AddBus();
            int index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
            AudioServer.SetBusSend(index, "Master");
        }
    }

    public static Volumes Saved()
    {
        string path = ProjectSettings.GlobalizePath(ConfigPath);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Volumes>(File.ReadAllText(path)) ?? new Volumes() : new Volumes();
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return new Volumes();
        }
    }

    public static void Save(Volumes volumes)
    {
        try
        {
            File.WriteAllText(ProjectSettings.GlobalizePath(ConfigPath), JsonSerializer.Serialize(volumes));
        }
        catch (IOException e)
        {
            GD.PushWarning($"UNNAMED audio: the volumes could not be saved ({e.Message})");
        }
    }

    public static void Apply(Volumes volumes)
    {
        EnsureBuses();
        Set("Master", volumes.Master);
        Set(Music, volumes.Music);
        Set(Effects, volumes.Effects);
    }

    private static void Set(string bus, float level)
    {
        int index = AudioServer.GetBusIndex(bus);
        AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Math.Max(0.0001f, Math.Clamp(level, 0f, 1f))));
        AudioServer.SetBusMute(index, level <= 0.001f);
    }
}
