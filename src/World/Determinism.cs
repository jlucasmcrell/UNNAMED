// UNNAMED World - seeds, digests and key spelling shared by every generator version
// No Godot references - pure C#

using System.Globalization;

namespace UNNAMED.World;

/// <summary>The world seed's manifest spelling, e.g. <c>0x5C1A9E7B4D2F0083</c>.</summary>
public static class WorldSeed
{
    public static string Format(ulong seed) => "0x" + seed.ToString("X16", CultureInfo.InvariantCulture);

    public static ulong Parse(string text)
    {
        if (!text.StartsWith("0x", StringComparison.Ordinal)
            || !ulong.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong seed))
            throw new FormatException($"Not a world seed: '{text}'");
        return seed;
    }
}

public static class DigestText
{
    /// <summary>True for the manifest's digest spelling: <c>sha256:&lt;64 lowercase hex&gt;</c>.</summary>
    public static bool IsSha256(string? value) =>
        value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.Ordinal)
        && value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;
}

/// <summary>
/// Key spelling for generated content, culture-invariant (WORLD_ARCHITECTURE.md §5.4, §5.5, PERSISTENCE.md I-8).
/// </summary>
public static class Keys
{
    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A two-digit ordinal: <c>00</c>..<c>99</c>.</summary>
    public static string Ordinal(int ordinal) => ordinal.ToString("00", CultureInfo.InvariantCulture);

    /// <summary><c>pop.&lt;region&gt;.c_&lt;cx&gt;_&lt;cz&gt;.&lt;name&gt;</c></summary>
    public static string PopulationId(CellKey cell, string name) =>
        $"pop.{cell.Region}.c_{Ordinal(cell.Cx)}_{Ordinal(cell.Cz)}.{name}";

    /// <summary><c>&lt;cell_key&gt;.&lt;population_id&gt;.&lt;ordinal&gt;</c></summary>
    public static string SlotKey(CellKey cell, string populationId, int ordinal) => $"{cell}.{populationId}.{Ordinal(ordinal)}";

    /// <summary><c>node.&lt;cell_key&gt;.&lt;rule name&gt;.&lt;ordinal&gt;</c>: a semantic slot, not a generation index.</summary>
    public static string NodeKey(CellKey cell, string ruleName, int ordinal) => $"node.{cell}.{ruleName}.{Ordinal(ordinal)}";
}
