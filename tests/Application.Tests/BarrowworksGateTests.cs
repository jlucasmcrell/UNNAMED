// UNNAMED Application tests - the gate leaves an ordinary launch alone (review B4)
// The ring's bindings lived in Cairnwold's overlay (content/_art_bindings.json), which the shipped PLAY.bat
// passes on EVERY launch. `ArtBindings` replaces a top-level `roof_groups` rather than merging it, so the ring's
// 29 groups became the only ones there were and the longhouse's and forge's roofs stopped being built with the
// option off. Nothing failed: no test looked at presentation with the gate off. These do.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace UNNAMED.Application.Tests;

public class BarrowworksGateTests
{
    // The snapshot's content/_art_bindings.json, line endings normalised: bytes this branch may not change.
    // A hash rather than a copy, so the guard costs nothing to carry and any edit at all fails it.
    private const string SnapshotOverlaySha256 = "abab28ffd0be4e05634fb0e9b074ed51591b83f0cc29962605f4662c3baa5e36";

    private static string Overlay => Path.Combine(Harness.RepoRoot(), "content", "_art_bindings.json");

    private static string RingOverlay => Path.Combine(Harness.RepoRoot(), "content", "_art_bindings_barrowworks.json");

    private static string NormalisedHash(string path)
    {
        string text = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    [Fact]
    public void TheShippedOverlay_IsByteForByteTheSnapshots()
    {
        Assert.True(File.Exists(Overlay), $"missing {Overlay}");
        Assert.Equal(SnapshotOverlaySha256, NormalisedHash(Overlay));
    }

    /// <summary>
    /// The key that leaked. <c>Roofs</c> reads <c>_bindings.RoofGroups</c>, whose default is the longhouse and the
    /// forge, so any overlay carrying this key takes both away from every other region - correctly populated or not.
    /// </summary>
    [Fact]
    public void TheShippedOverlay_DeclaresNoRoofGroups()
    {
        Assert.DoesNotContain("\"roof_groups\"", File.ReadAllText(Overlay, Encoding.UTF8), StringComparison.Ordinal);
    }

    /// <summary>Checked by key as well as by hash: a hash says something changed, this says what.</summary>
    [Fact]
    public void TheShippedOverlay_HoldsNothingOfTheRings()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Overlay, Encoding.UTF8));
        var root = doc.RootElement;

        foreach (string section in new[] { "structures", "building_walls" })
        {
            if (!root.TryGetProperty(section, out var map))
                continue;
            foreach (var entry in map.EnumerateObject())
                Assert.False(IsRingKey(entry.Name), $"{section} still holds the ring's {entry.Name}");
        }

        Assert.All(root.GetProperty("terrain").EnumerateObject(),
            cell => Assert.DoesNotContain("space.barrowworks", cell.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void TheRingHasItsOwnOverlay_HoldingWhatTheShippedOneGaveUp()
    {
        Assert.True(File.Exists(RingOverlay), $"missing {RingOverlay}");
        using var doc = JsonDocument.Parse(File.ReadAllText(RingOverlay, Encoding.UTF8));
        var root = doc.RootElement;

        Assert.True(root.GetProperty("structures").EnumerateObject().Count() >= 125);
        Assert.Equal(29, root.GetProperty("roof_groups").GetArrayLength());
        Assert.Equal(29, root.GetProperty("building_walls").EnumerateObject().Count());
        Assert.All(root.GetProperty("terrain").EnumerateObject(),
            cell => Assert.Contains("space.barrowworks", cell.Name, StringComparison.Ordinal));
    }

    /// <summary>A room or corridor-leg binding: <c>r01_…</c> or <c>corr_…</c>, which no other region uses.</summary>
    private static bool IsRingKey(string key) =>
        key.StartsWith("corr_", StringComparison.Ordinal)
        || (key.Length > 2 && key[0] == 'r' && char.IsDigit(key[1]) && char.IsDigit(key[2]));
}
