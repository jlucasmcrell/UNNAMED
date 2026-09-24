namespace UNNAMED.World.Tests;

/// <summary>
/// M2b §4 / §12.6: random values are addressed by (world_seed, cell, subsystem, semantic_key,
/// sample_index), so no draw anywhere can shift another.
/// </summary>
public class StableRandomTests
{
    // Reference values from an independent implementation of the contract (Reference/worldgen_v2_reference.py).
    [Fact]
    public void Channel_MatchesIndependentReference()
    {
        var iron = RngChannel.Open(TestWorlds.Seed, TestWorlds.Home, "resources", "iron_vein/00");
        Assert.Equal(new[] { 17342686721994252624UL, 9927884132162246871UL, 2789394780321086178UL },
            new[] { iron.UInt64(0), iron.UInt64(1), iron.UInt64(2) });
        Assert.Equal(new[] { 2624, 6871 }, new[] { iron.Int(0, 0, 10_000), iron.Int(1, 0, 10_000) });

        var heights = RngChannel.Open(TestWorlds.Seed, TestWorlds.Home, "terrain", "height");
        Assert.Equal(new[] { -4, 530, -300, -172, 778 }, Enumerable.Range(0, 5).Select(i => heights.Int((uint)i, -800, 801)));
    }

    [Fact]
    public void Samples_AreAddressed_NotDrawnInSequence()
    {
        var channel = RngChannel.Open(TestWorlds.Seed, TestWorlds.Home, "wildlife", "wolves/02");
        ulong seventh = channel.UInt64(7);

        // Reading other samples first, in any order and any number of times, changes nothing.
        _ = channel.UInt64(3);
        _ = channel.UInt64(3);
        _ = channel.Int(0, 0, 100);

        Assert.Equal(seventh, channel.UInt64(7));
        Assert.Equal(seventh, RngChannel.Open(TestWorlds.Seed, TestWorlds.Home, "wildlife", "wolves/02").UInt64(7));
    }

    [Fact]
    public void EveryKeyComponent_SelectsAnotherChannel()
    {
        ulong Sample(ulong seed, CellKey cell, string subsystem, string key, uint index) =>
            RngChannel.Open(seed, cell, subsystem, key).UInt64(index);

        ulong reference = Sample(TestWorlds.Seed, TestWorlds.Home, "wildlife", "wolves/00", 0);
        Assert.NotEqual(reference, Sample(TestWorlds.Seed + 1, TestWorlds.Home, "wildlife", "wolves/00", 0));
        Assert.NotEqual(reference, Sample(TestWorlds.Seed, CellKey.Parse("r_0_0:c_07_12"), "wildlife", "wolves/00", 0));
        Assert.NotEqual(reference, Sample(TestWorlds.Seed, TestWorlds.Home, "resources", "wolves/00", 0));
        Assert.NotEqual(reference, Sample(TestWorlds.Seed, TestWorlds.Home, "wildlife", "wolves/01", 0));
        Assert.NotEqual(reference, Sample(TestWorlds.Seed, TestWorlds.Home, "wildlife", "wolves/00", 1));
    }

    [Fact]
    public void Int_StaysInRange_AndReachesBothEnds()
    {
        var channel = RngChannel.Open(TestWorlds.Seed, TestWorlds.Home, "test", "range");
        var seen = new HashSet<int>();
        for (uint i = 0; i < 5000; i++)
        {
            int v = channel.Int(i, -3, 4);
            Assert.InRange(v, -3, 3);
            seen.Add(v);
        }
        Assert.Equal(7, seen.Count);
    }

    /// <summary>
    /// The M2b §12.6 case: a new decoration subsystem adds draws to every cell. Wildlife, terrain and
    /// the existing resource nodes (mushroom-like silverleaf included) keep every value.
    /// </summary>
    [Fact]
    public void AnUnrelatedNewDraw_MovesNothingThatAlreadyExists()
    {
        var before = TestWorlds.Generator();
        var after = new CellBaselineGenerator(TestWorlds.Profile(
            extraNodes: new[] { new NodeRule("boulders", "resource.stone.granite_boulder", 1, 5) }));

        foreach (var cell in CellKey.AllIn(new RegionKey(0, 0)).Take(50))
        {
            var a = before.Generate(TestWorlds.Seed, cell);
            var b = after.Generate(TestWorlds.Seed, cell);

            Assert.Equal(a.TerrainHash, b.TerrainHash);
            Assert.Equal<BaselineNode>(a.Nodes, b.Nodes.Where(n => n.DefId != "resource.stone.granite_boulder"));
            Assert.Equal(Wildlife(a), Wildlife(b));
            Assert.Contains(b.Nodes, n => n.DefId == "resource.stone.granite_boulder");
        }
    }

    /// <summary>A rule's count can change without moving the nodes it keeps: each node slot owns its channel.</summary>
    [Fact]
    public void ChangingOneRulesCount_KeepsItsExistingNodesInPlace()
    {
        var narrow = new CellBaselineGenerator(new GenerationProfile(
            new[] { new NodeRule("iron_vein", "resource.ore.iron_vein", 1, 2) },
            Array.Empty<PopulationRule>(), new TerrainRule(0, 0, 2)));
        var wide = new CellBaselineGenerator(new GenerationProfile(
            new[] { new NodeRule("iron_vein", "resource.ore.iron_vein", 5, 9) },
            Array.Empty<PopulationRule>(), new TerrainRule(0, 0, 2)));

        foreach (var cell in CellKey.AllIn(new RegionKey(0, 0)).Take(50))
        {
            var few = narrow.Generate(TestWorlds.Seed, cell).Nodes;
            var many = wide.Generate(TestWorlds.Seed, cell).Nodes;
            Assert.Equal<BaselineNode>(few, many.Take(few.Length));
        }
    }

    private static List<(string, int, int)> Wildlife(CellBaseline baseline) =>
        baseline.Populations.SelectMany(p => p.Slots).Select(s => (s.SlotKey, s.XCm, s.ZCm)).ToList();
}
