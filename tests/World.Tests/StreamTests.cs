using UNNAMED.World;

namespace UNNAMED.World.Tests;

/// <summary>WORLD_ARCHITECTURE.md §3.4: streams keyed by (tuple, cell, purpose).</summary>
public class StreamTests
{
    // Reference values computed by an independent implementation (Python: canonical field hashing +
    // xoshiro256**), not by this code. They pin the algorithm, not just its self-consistency.
    [Fact]
    public void Stream_MatchesIndependentReference()
    {
        var stream = DeterministicStream.For(TestWorlds.Tuple(), TestWorlds.Home, "terrain");

        Assert.Equal(1250369279751835600UL, stream.NextUInt64());
        Assert.Equal(328551610048717457UL, stream.NextUInt64());
        Assert.Equal(16769341891200531252UL, stream.NextUInt64());
    }

    [Fact]
    public void NextInt_MatchesIndependentReference_IncludingRejectionSampling()
    {
        var stream = DeterministicStream.For(TestWorlds.Tuple(), TestWorlds.Home, "terrain");
        int[] draws = Enumerable.Range(0, 5).Select(_ => stream.NextInt(-800, 801)).ToArray();

        Assert.Equal(new[] { 141, 591, 361, -336, -86 }, draws);
    }

    [Fact]
    public void SameKey_SameSequence()
    {
        var a = DeterministicStream.For(TestWorlds.Tuple(), TestWorlds.Home, "nodes:resource.ore.iron_vein");
        var b = DeterministicStream.For(TestWorlds.Tuple(), TestWorlds.Home, "nodes:resource.ore.iron_vein");
        for (int i = 0; i < 1000; i++)
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void EveryKeyComponent_ChangesTheStream()
    {
        ulong First(BaselineTuple tuple, CellKey cell, string purpose) =>
            DeterministicStream.For(tuple, cell, purpose).NextUInt64();

        ulong reference = First(TestWorlds.Tuple(), TestWorlds.Home, "terrain");
        string otherHash = "sha256:" + string.Concat(Enumerable.Repeat("cd", 32));

        Assert.NotEqual(reference, First(TestWorlds.Tuple(), TestWorlds.Home, "nodes"));
        Assert.NotEqual(reference, First(TestWorlds.Tuple(), CellKey.Parse("r_0_0:c_07_12"), "terrain"));
        Assert.NotEqual(reference, First(TestWorlds.Tuple(otherHash), TestWorlds.Home, "terrain"));
        Assert.NotEqual(reference, First(new BaselineTuple(TestWorlds.Seed + 1, 1, TestWorlds.ContentHash), TestWorlds.Home, "terrain"));
    }

    [Fact]
    public void NextInt_StaysInRange_AndReachesBothEnds()
    {
        var stream = DeterministicStream.For(TestWorlds.Tuple(), TestWorlds.Home, "range-check");
        var seen = new HashSet<int>();
        for (int i = 0; i < 5000; i++)
        {
            int v = stream.NextInt(-3, 4);
            Assert.InRange(v, -3, 3);
            seen.Add(v);
        }
        Assert.Equal(7, seen.Count);
    }

    [Fact]
    public void SeedSpelling_RoundTrips()
    {
        Assert.Equal("0x5C1A9E7B4D2F0083", BaselineTuple.FormatSeed(TestWorlds.Seed));
        Assert.Equal(TestWorlds.Seed, BaselineTuple.ParseSeed("0x5C1A9E7B4D2F0083"));
    }
}
