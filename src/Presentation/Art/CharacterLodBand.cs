namespace UNNAMED.Presentation.Art;

/// <summary>One whole-character band, with a two-metre hysteresis on either side.</summary>
public static class CharacterLodBand
{
    /// <summary>Named faces enter their native high-resolution maps at 3 m, release beyond 4 m; dialogue holds them.</summary>
    public static bool CloseTextures(float distance, bool previous, bool focused = false) =>
        focused || distance <= (previous ? 4f : 3f);

    public static int Select(float distance, int previous, bool ultra, bool closeUp = false)
    {
        if (closeUp) return 0;
        ReadOnlySpan<float> ends = ultra ? [18f, 35f, 70f] : [12f, 25f, 50f];
        int level = Math.Clamp(previous, 0, 3);
        while (level < 3 && distance > ends[level] + 2f) level++;
        while (level > 0 && distance < ends[level - 1] - 2f) level--;
        return level;
    }
}
