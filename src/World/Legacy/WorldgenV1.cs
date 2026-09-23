// UNNAMED World - worldgen 1 (M2). FROZEN.
// No Godot references - pure C#
//
// Kept only so a worldgen-1 save can be reinterpreted by the schema 1 -> 2 migration (M2b): its node
// keys are generation indices, and its baseline was seeded by the whole content_hash, so reading one
// requires regenerating exactly what M2 generated. Nothing new is generated with it. Change nothing
// here; the golden-digest test pins it.

using System.Buffers.Binary;
using System.Collections.Immutable;
using UNNAMED.Domain;

namespace UNNAMED.World.Legacy;

/// <summary>
/// M2's baseline tuple <c>(world_seed, worldgen_version, content_hash)</c>. Retired by M2b: content
/// identity is no longer an input to generation.
/// </summary>
public sealed record BaselineTuple
{
    public BaselineTuple(ulong worldSeed, int worldgenVersion, string contentHash)
    {
        if (worldgenVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(worldgenVersion), worldgenVersion, "worldgen_version starts at 1");
        if (!DigestText.IsSha256(contentHash))
            throw new ArgumentException($"content_hash must be 'sha256:<64 hex>', got '{contentHash}'", nameof(contentHash));
        WorldSeed = worldSeed;
        WorldgenVersion = worldgenVersion;
        ContentHash = contentHash;
    }

    public ulong WorldSeed { get; }
    public int WorldgenVersion { get; }
    public string ContentHash { get; }
}

/// <summary>M2's stream: xoshiro256** seeded by the tuple, cell and purpose, drawn in call order.</summary>
public sealed class DeterministicStream
{
    private ulong _s0, _s1, _s2, _s3;

    private DeterministicStream(ReadOnlySpan<byte> seed32)
    {
        _s0 = BinaryPrimitives.ReadUInt64BigEndian(seed32[..8]);
        _s1 = BinaryPrimitives.ReadUInt64BigEndian(seed32[8..16]);
        _s2 = BinaryPrimitives.ReadUInt64BigEndian(seed32[16..24]);
        _s3 = BinaryPrimitives.ReadUInt64BigEndian(seed32[24..32]);
        if ((_s0 | _s1 | _s2 | _s3) == 0)
            _s0 = 1;   // the all-zero state is xoshiro's single fixed point
    }

    public static DeterministicStream For(BaselineTuple tuple, CellKey cell, string purpose)
    {
        using var hasher = new CanonicalHasher();
        byte[] seed = hasher.Add("unnamed.stream/v1")
            .Add(tuple.WorldSeed)
            .Add(tuple.WorldgenVersion)
            .Add(tuple.ContentHash)
            .Add(cell.ToString())
            .Add(purpose)
            .FinishBytes();
        return new DeterministicStream(seed);
    }

    public ulong NextUInt64()
    {
        ulong result = RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>Uniform integer in [minInclusive, maxExclusive), without modulo bias.</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Empty range");
        ulong range = (ulong)((long)maxExclusive - minInclusive);
        ulong threshold = (0UL - range) % range;
        while (true)
        {
            ulong r = NextUInt64();
            if (r >= threshold)
                return (int)((long)minInclusive + (long)(r % range));
        }
    }

    private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));
}

/// <summary>Where a worldgen-1 node came from: the rule that generated it and its ordinal in that rule.</summary>
public sealed record NodeOriginV1(string NodeKey, NodeRule Rule, int Ordinal);

/// <summary>Worldgen 1, exactly as M2 shipped it. FROZEN.</summary>
public sealed class CellBaselineGeneratorV1
{
    public const int Version = 1;
    public const string GeneratorId = "unnamed.worldgen.cell-baseline";

    public CellBaselineGeneratorV1(GenerationProfile profile)
    {
        Profile = profile;
        using var h = new CanonicalHasher();
        WorldgenDigest = h.Add(GeneratorId).Add(Version).Add(ProfileDigest(profile))
            .Add("numeric-policy:integer-only;prng:xoshiro256starstar;hash:sha256")
            .Finish();
    }

    public GenerationProfile Profile { get; }

    /// <summary>M2's <c>worldgen_digest</c>, recorded in every schema-1 manifest.</summary>
    public string WorldgenDigest { get; }

    /// <summary>M2's placement-data digest. Node rule names did not exist then, so they are not in it.</summary>
    public static string ProfileDigest(GenerationProfile profile)
    {
        using var h = new CanonicalHasher();
        h.Add("unnamed.generation-profile/v1");
        h.Add(profile.Nodes.Length);
        foreach (var n in profile.Nodes)
            h.Add(n.DefId).Add(n.MinPerCell).Add(n.MaxPerCell);
        h.Add(profile.Populations.Length);
        foreach (var p in profile.Populations)
            h.Add(p.Name).Add(p.FamilyDefId).Add(p.Target).Add(p.Min).Add(p.Max);
        h.Add(profile.Terrain.BaseHeightMm).Add(profile.Terrain.AmplitudeMm).Add(profile.Terrain.SamplesPerAxis);
        return h.Finish();
    }

    public CellBaseline Generate(BaselineTuple tuple, CellKey cell)
    {
        if (tuple.WorldgenVersion != Version)
            throw new InvalidOperationException(
                $"Generator v{Version} cannot generate a baseline for worldgen_version {tuple.WorldgenVersion}");

        var nodes = GenerateNodes(tuple, cell).Select(o => o.Node).ToImmutableArray();
        return new CellBaseline(cell, GenerateTerrainHash(tuple, cell), nodes, GeneratePopulations(tuple, cell));
    }

    /// <summary>For each worldgen-1 node key in the cell, the rule and ordinal that produced it.</summary>
    public ImmutableArray<NodeOriginV1> NodeOrigins(BaselineTuple tuple, CellKey cell) =>
        GenerateNodes(tuple, cell).Select(o => new NodeOriginV1(o.Node.NodeKey, o.Rule, o.Ordinal)).ToImmutableArray();

    /// <summary>M2's region digest (the RK-01 golden value is pinned against it).</summary>
    public string RegionDigest(BaselineTuple tuple, RegionKey region)
    {
        using var h = new CanonicalHasher();
        h.Add("unnamed.region-digest/v1").Add(region.ToString()).Add(WorldgenDigest);
        foreach (var cell in CellKey.AllIn(region))
            h.Add(Generate(tuple, cell).Digest);
        return h.Finish();
    }

    private string GenerateTerrainHash(BaselineTuple tuple, CellKey cell)
    {
        var terrain = Profile.Terrain;
        var stream = DeterministicStream.For(tuple, cell, "terrain");
        using var h = new CanonicalHasher();
        h.Add("unnamed.terrain/v1").Add(terrain.SamplesPerAxis);
        for (int i = 0; i < terrain.SamplesPerAxis * terrain.SamplesPerAxis; i++)
            h.Add(terrain.BaseHeightMm + stream.NextInt(-terrain.AmplitudeMm, terrain.AmplitudeMm + 1));
        return h.Finish();
    }

    private List<(BaselineNode Node, NodeRule Rule, int Ordinal)> GenerateNodes(BaselineTuple tuple, CellKey cell)
    {
        var nodes = new List<(BaselineNode, NodeRule, int)>();
        foreach (var rule in Profile.Nodes)
        {
            var stream = DeterministicStream.For(tuple, cell, "nodes:" + rule.DefId);
            int count = stream.NextInt(rule.MinPerCell, rule.MaxPerCell + 1);
            for (int i = 0; i < count; i++)
            {
                // node_key = "node.<cell_key>.<index>", index assigned by this generator
                string key = $"node.{cell}.{Keys.Number(nodes.Count)}";
                nodes.Add((new BaselineNode(key, rule.DefId,
                    stream.NextInt(0, WorldMath.CellSizeCm), stream.NextInt(0, WorldMath.CellSizeCm)), rule, i));
            }
        }
        return nodes;
    }

    private ImmutableArray<BaselinePopulation> GeneratePopulations(BaselineTuple tuple, CellKey cell)
    {
        var populations = ImmutableArray.CreateBuilder<BaselinePopulation>();
        foreach (var rule in Profile.Populations)
        {
            string populationId = Keys.PopulationId(cell, rule.Name);
            var stream = DeterministicStream.For(tuple, cell, "spawns:" + rule.Name);
            var slots = ImmutableArray.CreateBuilder<BaselineSlot>(rule.Target);
            for (int ordinal = 0; ordinal < rule.Target; ordinal++)
            {
                slots.Add(new BaselineSlot(Keys.SlotKey(cell, populationId, ordinal), populationId, rule.FamilyDefId, ordinal,
                    stream.NextInt(0, WorldMath.CellSizeCm), stream.NextInt(0, WorldMath.CellSizeCm)));
            }
            populations.Add(new BaselinePopulation(populationId, rule.FamilyDefId, rule.Target, rule.Min, rule.Max, slots.ToImmutable()));
        }
        return populations.ToImmutable();
    }
}
