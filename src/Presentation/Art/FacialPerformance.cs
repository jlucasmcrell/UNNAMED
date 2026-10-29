using Godot;
using System.Text.Json;

namespace UNNAMED.Presentation.Art;

/// <summary>Original-rate acquired facial mocap, applied to the kept native channels.</summary>
public sealed class FacialPerformance
{
    public double Fps { get; init; }
    public Dictionary<string, float>[] Frames { get; init; } = [];
    public double Seconds => (Frames.Length - 1) / Fps;

    public static string Folder => Path.Combine(System.Environment.GetEnvironmentVariable("OTHERREACH_CHARACTER_ROOT")
        ?? Path.Combine(OS.HasFeature("template") ? Path.GetDirectoryName(OS.GetExecutablePath())!
            : ProjectSettings.GlobalizePath("res://"), "humgen_characters"), "facial_motion");

    public static FacialPerformance Load(string path)
    {
        var take = JsonSerializer.Deserialize<FacialPerformance>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException(path);
        if (!double.IsFinite(take.Fps) || take.Fps <= 0 || take.Frames.Length < 2
            || take.Frames.Any(frame => frame.Values.Any(v => !float.IsFinite(v) || Math.Abs(v) > 2)))
            throw new InvalidDataException("Invalid native facial performance: " + path);
        return take;
    }

    public void Sample(double seconds, Dictionary<string, float> target)
    {
        double at = Math.Clamp(seconds * Fps, 0, Frames.Length - 1);
        int before = (int)at, after = Math.Min(before + 1, Frames.Length - 1);
        foreach (var (channel, value) in Frames[before])
            if (target.ContainsKey(channel)) target[channel] = Mathf.Lerp(value, Frames[after][channel], (float)(at - before));
    }
}
