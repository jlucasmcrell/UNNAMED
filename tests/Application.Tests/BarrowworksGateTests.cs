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
    // Integrated frontier overlay: approved NPC migration, C1 creatures, C2 inn and accepted creature batches A/B.
    // Keep the full-file guard, alongside the semantic option-off checks below; line endings are normalised.
    private const string SnapshotOverlaySha256 = "7dd917233adb821993abf3346775724c03b98e547639a16abd1241aa8562bee0"; // Accepted C1 creature bindings, C2 inn additions, Batch B rework constructs and D10 variant clips over the integrated public overlay; ring keys remain gated.

    private static string Overlay => Path.Combine(Harness.RepoRoot(), "content", "_art_bindings.json");

    private static string RingOverlay => Path.Combine(Harness.RepoRoot(), "content", "_art_bindings_barrowworks.json");

    private static string NormalisedHash(string path)
    {
        string text = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    [Fact]
    public void TheShippedOverlay_MatchesTheIntegratedFrontierSnapshot()
    {
        Assert.True(File.Exists(Overlay), $"missing {Overlay}");
        Assert.Equal(SnapshotOverlaySha256, NormalisedHash(Overlay));
    }

    /// <summary>
    /// C2 adds an upper inn roof. Preserve the default longhouse/forge groups and reject any ring groups
    /// in the shipped frontier list; the ring's larger replacement remains in its gated overlay.
    /// </summary>
    [Fact]
    public void TheShippedOverlay_PreservesFrontierRoofsAndAddsOnlyTheAcceptedInn()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Overlay, Encoding.UTF8));
        Assert.Equal(new[] { "longhouse", "forge", "ah_inn_upper" },
            doc.RootElement.GetProperty("roof_groups").EnumerateArray().Select(group => group.GetString()));
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
        for (int room = 1; room <= 13; room++)
        {
            string prefix = $"r{room:00}_wall";
            Assert.Contains(root.GetProperty("roof_groups").EnumerateArray(), group => group.GetString() == prefix);
            Assert.Equal($"bw_codex_room_{room:00}", root.GetProperty("building_walls").GetProperty(prefix).GetProperty("model").GetString());
        }
        Assert.All(root.GetProperty("terrain").EnumerateObject(),
            cell => Assert.Contains("space.barrowworks", cell.Name, StringComparison.Ordinal));
    }

    /// <summary>A room or corridor-leg binding: <c>r01_…</c> or <c>corr_…</c>, which no other region uses.</summary>
    private static bool IsRingKey(string key) =>
        key.StartsWith("corr_", StringComparison.Ordinal)
        || (key.Length > 2 && key[0] == 'r' && char.IsDigit(key[1]) && char.IsDigit(key[2]));
}
