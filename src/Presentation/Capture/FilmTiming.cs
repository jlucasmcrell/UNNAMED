namespace UNNAMED.Presentation.Capture;

/// <summary>Sample the render clock, never the world's 20 Hz tick or an old plan's delivery rate.</summary>
public static class FilmTiming
{
    public static int RenderFps(IReadOnlyList<string> arguments)
    {
        for (int i = 0; i + 1 < arguments.Count; i++)
            if (arguments[i] == "--fixed-fps" && int.TryParse(arguments[i + 1], out int fps) && fps is >= 1 and <= 240)
                return fps;
        throw new ArgumentException("Marketing films require --fixed-fps 60 (before --).");
    }

    public static int Frames(double seconds, int fps) => double.IsFinite(seconds) && seconds > 0 && fps is >= 1 and <= 240
        ? checked((int)Math.Ceiling(seconds * fps))
        : throw new ArgumentOutOfRangeException(nameof(seconds));

    public static float Progress(int frame, int frames) => frames <= 1 ? 0 : (float)Math.Clamp(frame / (double)(frames - 1), 0, 1);
}
