using UNNAMED.World;

namespace UNNAMED.World.Tests;

/// <summary>
/// Baseline generation and the RK-01 risk spike (RISK_REGISTER.md): "generate a 2x2 km region twice
/// from one fixed seed and byte-compare a stable digest ... then rerun across a deliberately trivial
/// content change to prove the failure is detectable rather than silent."
/// </summary>
public class GeneratorTests
{
    private static readonly RegionKey Region = new(0, 0);

    /// <summary>
    /// Worldgen v1's digest for region r_0_0, pinned. This is the "frozen per version" guard: if it
    /// fails, generation v1 changed without a version bump - every existing save would load its delta
    /// against a different baseline. Add a new generator version instead of editing v1.
    /// </summary>
    private const string GoldenRegionDigestV1 =
        "sha256:cd369d84e2227558d74a01eca846d0ce2eba39b93a505a26e0815bfcff434e14";

    [Fact]
    public void RK01_RegionDigest_IsIdenticalAcrossRuns()
    {
        // PERSISTENCE.md T-02: "across processes and 100 runs". The processes half is Persistence.Tests' ME3.
        string first = RegionDigest.Compute(TestWorlds.Generator(), TestWorlds.Tuple(), Region);
        for (int run = 2; run <= 100; run++)
            Assert.Equal(first, RegionDigest.Compute(TestWorlds.Generator(), TestWorlds.Tuple(), Region));
    }

    [Fact]
    public void RK01_TrivialContentChange_IsDetected_NotSilent()
    {
        // Through the real content loader: one tuning value changes (wolf hp 30 -> 31).
        string contentRoot = Path.Combine(TestWorlds.RepoRoot(), "content");
        string edited = Path.Combine(Path.GetTempPath(), "unnamed-rk01-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(contentRoot, edited);
            string wolf = Path.Combine(edited, "creatures", "beast", "wolf_grey.yaml");
            string text = File.ReadAllText(wolf);
            Assert.Contains("hp: 30", text);
            File.WriteAllText(wolf, text.Replace("hp: 30", "hp: 31"));

            string originalHash = TestWorlds.ContentHashOf(contentRoot);
            string editedHash = TestWorlds.ContentHashOf(edited);
            Assert.NotEqual(originalHash, editedHash);

            string before = RegionDigest.Compute(TestWorlds.Generator(), TestWorlds.Tuple(originalHash), Region);
            string after = RegionDigest.Compute(TestWorlds.Generator(), TestWorlds.Tuple(editedHash), Region);
            Assert.NotEqual(before, after);   // RK-01 "fails if ... produces no detectable difference"
        }
        finally
        {
            if (Directory.Exists(edited))
                Directory.Delete(edited, recursive: true);
        }
    }

    [Fact]
    public void ContentHash_IgnoresLineEndings()
    {
        string contentRoot = Path.Combine(TestWorlds.RepoRoot(), "content");
        string crlf = Path.Combine(Path.GetTempPath(), "unnamed-crlf-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(contentRoot, crlf);
            foreach (string file in Directory.EnumerateFiles(crlf, "*.yaml", SearchOption.AllDirectories))
            {
                string lf = File.ReadAllText(file).Replace("\r\n", "\n");
                File.WriteAllText(file, lf.Replace("\n", "\r\n"));
            }
            Assert.Equal(TestWorlds.ContentHashOf(contentRoot), TestWorlds.ContentHashOf(crlf));
        }
        finally
        {
            if (Directory.Exists(crlf))
                Directory.Delete(crlf, recursive: true);
        }
    }

    [Fact]
    public void GoldenRegionDigest_PinsWorldgenV1()
    {
        string actual = RegionDigest.Compute(TestWorlds.Generator(), TestWorlds.Tuple(), Region);
        Assert.True(actual == GoldenRegionDigestV1,
            $"Worldgen v1 output changed. Existing saves would apply their deltas to a different baseline. " +
            $"Revert the change, or add a new worldgen version. Current digest: {actual}");
    }

    [Fact]
    public void PlacementChange_ChangesWorldgenDigest_AndBaseline()
    {
        var five = TestWorlds.Generator(wolfTarget: 5);
        var four = TestWorlds.Generator(wolfTarget: 4);

        Assert.NotEqual(five.WorldgenDigest, four.WorldgenDigest);
        Assert.NotEqual(
            RegionDigest.Compute(five, TestWorlds.Tuple(), Region),
            RegionDigest.Compute(four, TestWorlds.Tuple(), Region));
    }

    [Fact]
    public void GenerationOrder_DoesNotChangeAnyBaseline()
    {
        var cells = CellKey.AllIn(Region).Take(40).ToList();
        var forward = TestWorlds.Generator();
        var backward = TestWorlds.Generator();

        var a = cells.ToDictionary(c => c, c => forward.Generate(TestWorlds.Tuple(), c).Digest);
        var b = Enumerable.Reverse(cells).ToDictionary(c => c, c => backward.Generate(TestWorlds.Tuple(), c).Digest);

        foreach (var cell in cells)
            Assert.Equal(a[cell], b[cell]);
    }

    [Fact]
    public void Keys_FollowTheSpecFormats()
    {
        var baseline = TestWorlds.Generator().Generate(TestWorlds.Tuple(), TestWorlds.Home);

        Assert.All(baseline.Nodes.Select((n, i) => (n, i)), x => Assert.Equal($"node.r_0_0:c_07_11.{x.i}", x.n.NodeKey));
        var wolves = baseline.Populations.Single(p => p.FamilyDefId == "creature.beast.wolf_grey");
        Assert.Equal("pop.r_0_0.c_07_11.wolves", wolves.PopulationId);
        Assert.Equal("r_0_0:c_07_11.pop.r_0_0.c_07_11.wolves.00", wolves.Slots[0].SlotKey);
        Assert.Equal(5, wolves.Slots.Length);
    }

    [Fact]
    public void Generator_RefusesAnotherWorldgenVersion() =>
        Assert.Throws<InvalidOperationException>(() =>
            TestWorlds.Generator().Generate(new BaselineTuple(TestWorlds.Seed, 2, TestWorlds.ContentHash), TestWorlds.Home));

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
