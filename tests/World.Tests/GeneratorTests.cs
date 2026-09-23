using System.Collections.Immutable;
using UNNAMED.World.Legacy;

namespace UNNAMED.World.Tests;

/// <summary>
/// Baseline generation, the RK-01 risk spike, and M2b's compatibility identities: determinism, the
/// pinned canonical probes, the fingerprint, and the separation of content identity from generation.
/// </summary>
public class GeneratorTests
{
    private static readonly RegionKey Region = new(0, 0);

    // Worldgen 2 under TestWorlds.Profile(), computed by an independent Python implementation of the
    // contract (Reference/worldgen_v2_reference.py), not by this code.
    private const string ProfileDigestV2 = "sha256:433b943180755a399b61fbccb0c2ffbe0a2e8a1bf511c8ab6d4eae990fc13b7a";
    private const string ProbeDigestV2 = "sha256:03fe74b151c90e1a427dfcf9f109dda38baca5a2a83202a42d82dc3df75b4c02";
    private const string FingerprintV2 = "sha256:9810297b131af6faa729b31e0e263905fb8b8891da2fa5b7f97127498a277900";
    private const string RegionDigestV2 = "sha256:54e13404f2c3547fa4eda98565c337fc3752516f488fb6261fa8d362964b9123";
    private const string HomeBaselineV2 = "sha256:5efc965773e5c28cf9ba37e1411495e495f1b7a03bbfcf07453a3f73db88fb44";

    /// <summary>Worldgen 1's region digest (M2), pinned: the schema 1 -> 2 migration must regenerate exactly this.</summary>
    private const string GoldenRegionDigestV1 = "sha256:cd369d84e2227558d74a01eca846d0ce2eba39b93a505a26e0815bfcff434e14";

    [Fact]
    public void RK01_RegionDigest_IsIdenticalAcrossRuns()
    {
        // PERSISTENCE.md T-02: "across processes and 100 runs". The processes half is Persistence.Tests' ME3.
        string first = RegionDigest.Compute(TestWorlds.Generator(), TestWorlds.Seed, Region);
        for (int run = 2; run <= 100; run++)
            Assert.Equal(first, RegionDigest.Compute(TestWorlds.Generator(), TestWorlds.Seed, Region));
    }

    /// <summary>
    /// The canonical probe set and everything derived from it, pinned to an independent implementation.
    /// A failure here with worldgen_version still 2 is exactly the accidental drift M2b §12.8 is about.
    /// </summary>
    [Fact]
    public void Worldgen2_IsPinned_ProbesFingerprintAndRegion()
    {
        var generator = TestWorlds.Generator();

        Assert.Equal(ProfileDigestV2, generator.Profile.Digest);
        Assert.Equal(ProbeDigestV2, CanonicalProbes.Digest(generator));
        Assert.Equal(FingerprintV2, generator.Fingerprint);
        Assert.Equal(RegionDigestV2, RegionDigest.Compute(generator, TestWorlds.Seed, Region));
        Assert.Equal(HomeBaselineV2, generator.Generate(TestWorlds.Seed, TestWorlds.Home).Digest);
    }

    /// <summary>
    /// M2b §12.5 at the generator: a balance edit changes content_hash and nothing that is generated.
    /// Content identity is no longer an input to generation, so there is no path for it to move a cell.
    /// </summary>
    [Fact]
    public void RK01_BaselineNeutralContentChange_ChangesContentHash_NotTheBaseline()
    {
        string contentRoot = Path.Combine(TestWorlds.RepoRoot(), "content");
        string edited = Path.Combine(Path.GetTempPath(), "unnamed-rk01-" + Guid.NewGuid().ToString("N"));
        try
        {
            TestWorlds.CopyDirectory(contentRoot, edited);
            string wolf = Path.Combine(edited, "creatures", "beast", "wolf_grey.yaml");
            string text = File.ReadAllText(wolf);
            Assert.Contains("hp: 30", text);
            File.WriteAllText(wolf, text.Replace("hp: 30", "hp: 31"));

            Assert.NotEqual(TestWorlds.ContentHashOf(contentRoot), TestWorlds.ContentHashOf(edited));

            // Worldgen 2 reads the placement profile and the seed, never the content hash: same world.
            Assert.Equal(RegionDigestV2, RegionDigest.Compute(TestWorlds.Generator(), TestWorlds.Seed, Region));
            Assert.Equal(FingerprintV2, TestWorlds.Generator().Fingerprint);
        }
        finally
        {
            if (Directory.Exists(edited))
                Directory.Delete(edited, recursive: true);
        }
    }

    /// <summary>
    /// The coupling M2b removed, pinned where it still lives: worldgen 1 seeded every draw with the whole
    /// content_hash, so any content edit moved every cell. The migration relies on reproducing it.
    /// </summary>
    [Fact]
    public void LegacyWorldgen1_WasSeededByTheWholeContentHash()
    {
        string otherHash = "sha256:" + string.Concat(Enumerable.Repeat("cd", 32));
        var legacy = TestWorlds.LegacyGenerator();

        Assert.Equal(GoldenRegionDigestV1, legacy.RegionDigest(TestWorlds.LegacyTuple(), Region));
        Assert.NotEqual(GoldenRegionDigestV1, legacy.RegionDigest(TestWorlds.LegacyTuple(otherHash), Region));
    }

    [Fact]
    public void ContentHash_IgnoresLineEndings()
    {
        string contentRoot = Path.Combine(TestWorlds.RepoRoot(), "content");
        string crlf = Path.Combine(Path.GetTempPath(), "unnamed-crlf-" + Guid.NewGuid().ToString("N"));
        try
        {
            TestWorlds.CopyDirectory(contentRoot, crlf);
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

    /// <summary>
    /// M2b §12.7 / class C at the generator: an intentional placement change changes the fingerprint and
    /// the cells it affects, and only what it affects - other channels keep their values.
    /// </summary>
    [Fact]
    public void BaselineAffectingChange_ChangesTheFingerprint_AndOnlyTheAffectedOutput()
    {
        var five = TestWorlds.Generator(wolfTarget: 5);
        var four = TestWorlds.Generator(wolfTarget: 4);
        Assert.NotEqual(five.Fingerprint, four.Fingerprint);

        foreach (var cell in CellKey.AllIn(Region).Take(25))
        {
            var before = five.Generate(TestWorlds.Seed, cell);
            var after = four.Generate(TestWorlds.Seed, cell);
            Assert.NotEqual(before.Digest, after.Digest);   // the cell's baseline hash changed: saved deltas will see it
            Assert.Equal(before.TerrainHash, after.TerrainHash);
            Assert.Equal<BaselineNode>(before.Nodes, after.Nodes);
            Assert.Equal(Slots(before, "deer"), Slots(after, "deer"));
            Assert.Equal(Slots(before, "wolves").Take(4), Slots(after, "wolves"));   // the four remaining wolves did not move
        }
    }

    /// <summary>M2b §12.8: drift without a version bump is caught by the fingerprint and the pinned probes.</summary>
    [Fact]
    public void AccidentalGeneratorDrift_IsDetected_WithoutAVersionBump()
    {
        var honest = TestWorlds.Generator();
        var drifted = new DriftedGenerator(honest);

        Assert.Equal(honest.WorldgenVersion, drifted.WorldgenVersion);   // nobody bumped the epoch
        Assert.Equal(honest.RngContractVersion, drifted.RngContractVersion);
        Assert.NotEqual(honest.Fingerprint, drifted.Fingerprint);
        Assert.NotEqual(ProbeDigestV2, CanonicalProbes.Digest(drifted));
        Assert.NotEqual(honest.Generate(TestWorlds.Seed, TestWorlds.Home).Digest, drifted.Generate(TestWorlds.Seed, TestWorlds.Home).Digest);
    }

    [Fact]
    public void GenerationOrder_DoesNotChangeAnyBaseline()
    {
        var cells = CellKey.AllIn(Region).Take(40).ToList();
        var forward = TestWorlds.Generator();
        var backward = TestWorlds.Generator();

        var a = cells.ToDictionary(c => c, c => forward.Generate(TestWorlds.Seed, c).Digest);
        var b = Enumerable.Reverse(cells).ToDictionary(c => c, c => backward.Generate(TestWorlds.Seed, c).Digest);

        foreach (var cell in cells)
            Assert.Equal(a[cell], b[cell]);
    }

    [Fact]
    public void Keys_AreSemantic_NotGenerationIndices()
    {
        var baseline = TestWorlds.Generator().Generate(TestWorlds.Seed, TestWorlds.Home);

        Assert.Equal(
            new[]
            {
                "node.r_0_0:c_07_11.iron_vein.00", "node.r_0_0:c_07_11.iron_vein.01", "node.r_0_0:c_07_11.iron_vein.02",
                "node.r_0_0:c_07_11.iron_vein.03", "node.r_0_0:c_07_11.silverleaf.00",
            },
            baseline.Nodes.Select(n => n.NodeKey));
        var wolves = baseline.Populations.Single(p => p.FamilyDefId == "creature.beast.wolf_grey");
        Assert.Equal("pop.r_0_0.c_07_11.wolves", wolves.PopulationId);
        Assert.Equal("r_0_0:c_07_11.pop.r_0_0.c_07_11.wolves.00", wolves.Slots[0].SlotKey);
        Assert.Equal(5, wolves.Slots.Length);
    }

    [Fact]
    public void LegacyWorldgen1_KeysNodesByGenerationIndex_AndMapsThemBack()
    {
        var legacy = TestWorlds.LegacyGenerator();
        var baseline = legacy.Generate(TestWorlds.LegacyTuple(), TestWorlds.Home);
        var origins = legacy.NodeOrigins(TestWorlds.LegacyTuple(), TestWorlds.Home);

        Assert.All(baseline.Nodes.Select((n, i) => (n, i)), x => Assert.Equal($"node.r_0_0:c_07_11.{x.i}", x.n.NodeKey));
        Assert.Equal(baseline.Nodes.Select(n => n.NodeKey), origins.Select(o => o.NodeKey));
        Assert.Equal(baseline.Nodes.Select(n => n.DefId), origins.Select(o => o.Rule.DefId));
        // Ordinals restart per rule: the index-to-identity map the 1 -> 2 migration uses.
        Assert.All(origins.GroupBy(o => o.Rule.Name), g => Assert.Equal(Enumerable.Range(0, g.Count()), g.Select(o => o.Ordinal)));
    }

    private static IEnumerable<(string, int, int)> Slots(CellBaseline baseline, string population) =>
        baseline.Populations.Single(p => p.PopulationId.EndsWith("." + population, StringComparison.Ordinal))
            .Slots.Select(s => (s.SlotKey, s.XCm, s.ZCm)).ToList();

    /// <summary>A generator edit that forgot to bump the version: every iron vein moved by one centimetre.</summary>
    private sealed class DriftedGenerator : ICellBaselineGenerator
    {
        private readonly ICellBaselineGenerator _inner;
        private readonly Lazy<string> _fingerprint;

        public DriftedGenerator(ICellBaselineGenerator inner)
        {
            _inner = inner;
            _fingerprint = new Lazy<string>(() => WorldgenFingerprint.Compute(this));
        }

        public string GeneratorId => _inner.GeneratorId;
        public int WorldgenVersion => _inner.WorldgenVersion;
        public int RngContractVersion => _inner.RngContractVersion;
        public GenerationProfile Profile => _inner.Profile;
        public string Fingerprint => _fingerprint.Value;

        public CellBaseline Generate(ulong worldSeed, CellKey cell)
        {
            var baseline = _inner.Generate(worldSeed, cell);
            return baseline with
            {
                Nodes = baseline.Nodes.Select(n => n.DefId == "resource.ore.iron_vein" ? n with { XCm = (n.XCm + 1) % WorldMath.CellSizeCm } : n)
                    .ToImmutableArray(),
            };
        }
    }
}
