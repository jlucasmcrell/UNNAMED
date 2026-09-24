// UNNAMED World - coordinates and keys (WORLD_ARCHITECTURE.md §3)
// No Godot references - pure C#

using System.Globalization;

namespace UNNAMED.World;

/// <summary>
/// Integer helpers for the coordinate scheme. <see cref="FloorMod"/> is the single shared helper
/// WORLD_ARCHITECTURE.md §3.3 requires: C#'s <c>%</c> truncates toward zero and returns negative
/// remainders, which produce cell indices outside [0, 19] that no baseline will ever match.
/// </summary>
public static class WorldMath
{
    public const int RegionSizeMeters = 2000;
    public const int CellSizeMeters = 100;
    public const int CellsPerRegionAxis = 20;
    public const int CellSizeCm = CellSizeMeters * 100;

    /// <summary>Euclidean modulo: <c>((a % n) + n) % n</c>, always in [0, n).</summary>
    public static long FloorMod(long a, long n) => ((a % n) + n) % n;

    public static long FloorDiv(double value, double divisor) => (long)Math.Floor(value / divisor);
}

/// <summary>A region address <c>(rx, rz)</c>, keyed <c>r_&lt;rx&gt;_&lt;rz&gt;</c>.</summary>
public readonly record struct RegionKey(int Rx, int Rz) : IComparable<RegionKey>
{
    public static RegionKey OfWorld(double x, double z) =>
        new((int)WorldMath.FloorDiv(x, WorldMath.RegionSizeMeters), (int)WorldMath.FloorDiv(z, WorldMath.RegionSizeMeters));

    // Negative indices are spelled neg<abs> so keys stay filesystem- and URL-safe (§3.2).
    public override string ToString() => $"r_{Format(Rx)}_{Format(Rz)}";

    public int CompareTo(RegionKey other) => string.CompareOrdinal(ToString(), other.ToString());

    public static bool TryParse(string? value, out RegionKey key)
    {
        key = default;
        if (value is null || !value.StartsWith("r_", StringComparison.Ordinal))
            return false;
        string[] parts = value[2..].Split('_');
        if (parts.Length != 2 || !TryParseIndex(parts[0], out int rx) || !TryParseIndex(parts[1], out int rz))
            return false;
        key = new RegionKey(rx, rz);
        return key.ToString() == value;   // rejects non-canonical spellings such as r_00_0 or r_neg0_0
    }

    public static RegionKey Parse(string value) =>
        TryParse(value, out var key) ? key : throw new FormatException($"Not a region key: '{value}'");

    internal static string Format(int v) =>
        v < 0 ? "neg" + (-(long)v).ToString(CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture);

    private static bool TryParseIndex(string text, out int value)
    {
        bool negative = text.StartsWith("neg", StringComparison.Ordinal);
        string digits = negative ? text[3..] : text;
        value = 0;
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)
            || !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long magnitude))
            return false;
        long signed = negative ? -magnitude : magnitude;
        if (signed is < int.MinValue or > int.MaxValue)
            return false;
        value = (int)signed;
        return true;
    }
}

/// <summary>
/// A cell address: a region plus <c>(cx, cz)</c> in [0, 19], keyed <c>&lt;region&gt;:c_&lt;cx&gt;_&lt;cz&gt;</c>,
/// e.g. <c>r_0_0:c_07_11</c>. The canonical key is the identity every stream and delta record uses.
/// </summary>
public readonly record struct CellKey : IComparable<CellKey>
{
    public CellKey(RegionKey region, int cx, int cz)
    {
        if (cx is < 0 or >= WorldMath.CellsPerRegionAxis)
            throw new ArgumentOutOfRangeException(nameof(cx), cx, "Cell index must be in [0, 19]");
        if (cz is < 0 or >= WorldMath.CellsPerRegionAxis)
            throw new ArgumentOutOfRangeException(nameof(cz), cz, "Cell index must be in [0, 19]");
        Region = region;
        Cx = cx;
        Cz = cz;
    }

    public RegionKey Region { get; }
    public int Cx { get; }
    public int Cz { get; }

    /// <summary><c>cellOf(world) = (floormod(floor(x/100), 20), floormod(floor(z/100), 20))</c> (§3.3).</summary>
    public static CellKey OfWorld(double x, double z) => new(
        RegionKey.OfWorld(x, z),
        (int)WorldMath.FloorMod(WorldMath.FloorDiv(x, WorldMath.CellSizeMeters), WorldMath.CellsPerRegionAxis),
        (int)WorldMath.FloorMod(WorldMath.FloorDiv(z, WorldMath.CellSizeMeters), WorldMath.CellsPerRegionAxis));

    public override string ToString() => $"{Region}:c_{Cx:00}_{Cz:00}";

    public int CompareTo(CellKey other) => string.CompareOrdinal(ToString(), other.ToString());

    public static bool TryParse(string? value, out CellKey key)
    {
        key = default;
        if (value is null)
            return false;
        int colon = value.IndexOf(':');
        if (colon < 0 || !RegionKey.TryParse(value[..colon], out var region))
            return false;
        string cell = value[(colon + 1)..];
        if (cell.Length != 7 || !cell.StartsWith("c_", StringComparison.Ordinal) || cell[4] != '_'
            || !int.TryParse(cell.AsSpan(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int cx)
            || !int.TryParse(cell.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int cz)
            || cx >= WorldMath.CellsPerRegionAxis || cz >= WorldMath.CellsPerRegionAxis)
            return false;
        key = new CellKey(region, cx, cz);
        return true;
    }

    public static CellKey Parse(string value) =>
        TryParse(value, out var key) ? key : throw new FormatException($"Not a cell key: '{value}'");

    /// <summary>All 400 cells of a region, in canonical key order.</summary>
    public static IEnumerable<CellKey> AllIn(RegionKey region)
    {
        for (int cx = 0; cx < WorldMath.CellsPerRegionAxis; cx++)
        for (int cz = 0; cz < WorldMath.CellsPerRegionAxis; cz++)
            yield return new CellKey(region, cx, cz);
    }
}
