namespace UNNAMED.Presentation.Art;

/// <summary>One whole-character band, with a two-metre hysteresis on either side.</summary>
public static class CharacterLodBand
{
    /// <summary>A lining is a separate surface of its garment's band, not an always-visible part.</summary>
    public static (string PartName, int Level)? Part(string name)
    {
        int at = name.LastIndexOf("_LOD", StringComparison.Ordinal);
        if (at < 0) return null;
        int end = at + 4;
        while (end < name.Length && char.IsAsciiDigit(name[end])) end++;
        if (!int.TryParse(name.AsSpan(at + 4, end - at - 4), out int level)) return null;
        string suffix = name[end..];
        return suffix is "" or "_lining" ? (name[..at] + suffix, level) : null;
    }

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
