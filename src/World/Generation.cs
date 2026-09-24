// UNNAMED World - deterministic baseline generation (D-05, PERSISTENCE.md §1, WORLD_ARCHITECTURE.md §5)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;

namespace UNNAMED.World;

/// <summary>
/// Resource-node placement: how many nodes of one definition a cell carries. <see cref="Name"/> is the
/// rule's semantic key. Node keys and random channels are named by it - not by list position, not by
/// the definition ID - so adding, reordering or renaming anything else never re-keys a node.
/// </summary>
public sealed record NodeRule(string Name, string DefId, int MinPerCell, int MaxPerCell);

/// <summary>
/// An authored node (M3f): one node of a definition at a fixed place in a cell - a seam in a rock face, a stand of ash -
/// rather than a rule's scatter. It is baseline like any other node: keyed by its name, its harvest state saved the same
/// way (PERSISTENCE.md §5.5). Coordinates are centimetres within the cell.
/// </summary>
public sealed record FixedNode(string Name, string DefId, CellKey Cell, int XCm, int ZCm);

/// <summary>A spawn population per cell: a family with a budget (WORLD_ARCHITECTURE.md §5.5).</summary>
public sealed record PopulationRule(string Name, string FamilyDefId, int Target, int Min, int Max);

/// <summary>Terrain sampling for the cell's terrain signature, in integer millimetres.</summary>
public sealed record TerrainRule(int BaseHeightMm, int AmplitudeMm, int SamplesPerAxis);

/// <summary>
/// Placement data: the authored input to generation, and part of the worldgen fingerprint. Only
/// what generation reads belongs here. Runtime-only content (a creature's hit points, an item's price)
/// never does, which is why a balance edit cannot move the world.
/// </summary>
public sealed record GenerationProfile
{
    public GenerationProfile(IEnumerable<NodeRule> nodes, IEnumerable<PopulationRule> populations, TerrainRule terrain,
        IEnumerable<FixedNode>? fixedNodes = null)
    {
        Nodes = nodes.ToImmutableArray();
        Populations = populations.ToImmutableArray();
        Terrain = terrain;
        FixedNodes = (fixedNodes ?? Enumerable.Empty<FixedNode>()).ToImmutableArray();
        Validate();
        Digest = ComputeDigest();
    }

    public ImmutableArray<NodeRule> Nodes { get; }

    /// <summary>Authored nodes (M3f), each at its place.</summary>
    public ImmutableArray<FixedNode> FixedNodes { get; }
    public ImmutableArray<PopulationRule> Populations { get; }
    public TerrainRule Terrain { get; }

    /// <summary>Content-addressed identity of the placement data.</summary>
    public string Digest { get; }

    private void Validate()
    {
        var nodeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in Nodes)
        {
            if (!IsName(n.Name))
                throw new ArgumentException($"Node rule name '{n.Name}' must be lowercase snake_case");
            if (!nodeNames.Add(n.Name))
                throw new ArgumentException($"Duplicate node rule name '{n.Name}'");
            if (!DefinitionId.IsValid(n.DefId))
                throw new ArgumentException($"Node rule '{n.Name}' has an invalid definition ID '{n.DefId}'");
            if (n.MinPerCell < 0 || n.MaxPerCell < n.MinPerCell || n.MaxPerCell > 99)
                throw new ArgumentException($"Node rule '{n.Name}' has an invalid range [{n.MinPerCell}, {n.MaxPerCell}]");
        }
        foreach (var f in FixedNodes)
        {
            if (!IsName(f.Name))
                throw new ArgumentException($"Authored node name '{f.Name}' must be lowercase snake_case");
            if (!nodeNames.Add(f.Name))
                throw new ArgumentException($"Authored node name '{f.Name}' is also another node's");
            if (!DefinitionId.IsValid(f.DefId))
                throw new ArgumentException($"Authored node '{f.Name}' has an invalid definition ID '{f.DefId}'");
            if (f.XCm is < 0 or >= WorldMath.CellSizeCm || f.ZCm is < 0 or >= WorldMath.CellSizeCm)
                throw new ArgumentException($"Authored node '{f.Name}' lies outside its cell's square");
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in Populations)
        {
            if (!DefinitionId.IsValid(p.FamilyDefId))
                throw new ArgumentException($"Population '{p.Name}' has an invalid family ID '{p.FamilyDefId}'");
            if (!IsName(p.Name))
                throw new ArgumentException($"Population name '{p.Name}' must be lowercase snake_case");
            if (!names.Add(p.Name))
                throw new ArgumentException($"Duplicate population name '{p.Name}'");
            if (p.Min < 0 || p.Min > p.Target || p.Target > p.Max || p.Target > 99)
                throw new ArgumentException($"Population '{p.Name}' budget must satisfy 0 <= min <= target <= max, target <= 99");
        }
        if (Terrain.SamplesPerAxis < 2 || Terrain.AmplitudeMm < 0)
            throw new ArgumentException("Terrain rule needs at least 2 samples per axis and a non-negative amplitude");
    }

    private static bool IsName(string? name) =>
        !string.IsNullOrEmpty(name) && name.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');

    private string ComputeDigest()
    {
        using var h = new CanonicalHasher();
        h.Add("unnamed.generation-profile/v2");
        h.Add(Nodes.Length);
        foreach (var n in Nodes)
            h.Add(n.Name).Add(n.DefId).Add(n.MinPerCell).Add(n.MaxPerCell);
        h.Add(Populations.Length);
        foreach (var p in Populations)
            h.Add(p.Name).Add(p.FamilyDefId).Add(p.Target).Add(p.Min).Add(p.Max);
        h.Add(Terrain.BaseHeightMm).Add(Terrain.AmplitudeMm).Add(Terrain.SamplesPerAxis);
        // Only a profile that places authored nodes digests them, so every earlier profile keeps its digest.
        if (!FixedNodes.IsEmpty)
        {
            h.Add("fixed-nodes").Add(FixedNodes.Length);
            foreach (var f in FixedNodes)
                h.Add(f.Name).Add(f.DefId).Add(f.Cell.ToString()).Add(f.XCm).Add(f.ZCm);
        }
        return h.Finish();
    }
}

/// <summary>A resource node's baseline: identity is the key, not a ULID (WORLD_ARCHITECTURE.md §5.4).</summary>
public sealed record BaselineNode(string NodeKey, string DefId, int XCm, int ZCm);

/// <summary>One population member's baseline slot (PERSISTENCE.md I-8): <c>&lt;cell_key&gt;.&lt;population_id&gt;.&lt;ordinal&gt;</c>.</summary>
public sealed record BaselineSlot(string SlotKey, string PopulationId, string FamilyDefId, int Ordinal, int XCm, int ZCm);

public sealed record BaselinePopulation(
    string PopulationId, string FamilyDefId, int Target, int Min, int Max, ImmutableArray<BaselineSlot> Slots);

/// <summary>A regenerated cell: a pure function of (generator, world seed, cell key).</summary>
public sealed record CellBaseline(
    CellKey Cell,
    string TerrainHash,
    ImmutableArray<BaselineNode> Nodes,
    ImmutableArray<BaselinePopulation> Populations)
{
    /// <summary>
    /// The cell's <c>baseline_hash</c>: a digest of everything generated for it. A saved delta records
    /// the hash of the baseline it was made against, and is applied only to a baseline with the same
    /// hash (M2b). It covers outputs only, so equal output means compatible whatever produced it.
    /// </summary>
    public string Digest
    {
        get
        {
            using var h = new CanonicalHasher();
            h.Add("unnamed.cell-baseline/v1").Add(Cell.ToString()).Add(TerrainHash);
            h.Add(Nodes.Length);
            foreach (var n in Nodes)
                h.Add(n.NodeKey).Add(n.DefId).Add(n.XCm).Add(n.ZCm);
            h.Add(Populations.Length);
            foreach (var p in Populations)
            {
                h.Add(p.PopulationId).Add(p.FamilyDefId).Add(p.Target).Add(p.Min).Add(p.Max).Add(p.Slots.Length);
                foreach (var s in p.Slots)
                    h.Add(s.SlotKey).Add(s.Ordinal).Add(s.XCm).Add(s.ZCm);
            }
            return h.Finish();
        }
    }

    public BaselineNode? FindNode(string nodeKey) => Nodes.FirstOrDefault(n => n.NodeKey == nodeKey);

    public BaselinePopulation? FindPopulation(string populationId) =>
        Populations.FirstOrDefault(p => p.PopulationId == populationId);

    public BaselineSlot? FindSlot(string slotKey) =>
        Populations.SelectMany(p => p.Slots).FirstOrDefault(s => s.SlotKey == slotKey);
}

/// <summary>
/// The frozen generation interface (ROADMAP M2). Generation changes only through a new
/// <see cref="WorldgenVersion"/>; the fingerprint and the pinned probe digests catch an edit that
/// skipped the bump, and each save's per-cell baseline hashes catch it again at load.
/// </summary>
public interface ICellBaselineGenerator
{
    string GeneratorId { get; }

    /// <summary>The human compatibility epoch. Bumped deliberately; never the only safeguard.</summary>
    int WorldgenVersion { get; }

    int RngContractVersion { get; }

    GenerationProfile Profile { get; }

    /// <summary>
    /// <c>worldgen_fingerprint</c>: computed from the generator's identity, version, RNG contract,
    /// placement data and its output on the canonical probe cells (<see cref="WorldgenFingerprint"/>).
    /// Detection metadata only: it is never an input to a random draw.
    /// </summary>
    string Fingerprint { get; }

    CellBaseline Generate(ulong worldSeed, CellKey cell);
}

/// <summary>Worldgen 2 (M2b): semantic random channels, semantic node keys.</summary>
public sealed class CellBaselineGenerator : ICellBaselineGenerator
{
    public const int Version = 2;
    public const string Id = "unnamed.worldgen.cell-baseline";

    private readonly Lazy<string> _fingerprint;

    public CellBaselineGenerator(GenerationProfile profile)
    {
        Profile = profile;
        _fingerprint = new Lazy<string>(() => WorldgenFingerprint.Compute(this));
    }

    public string GeneratorId => Id;

    public int WorldgenVersion => Version;

    public int RngContractVersion => RngContract.V2;

    public GenerationProfile Profile { get; }

    public string Fingerprint => _fingerprint.Value;

    public CellBaseline Generate(ulong worldSeed, CellKey cell) =>
        new(cell, GenerateTerrainHash(worldSeed, cell), GenerateNodes(worldSeed, cell), GeneratePopulations(worldSeed, cell));

    private string GenerateTerrainHash(ulong worldSeed, CellKey cell)
    {
        var terrain = Profile.Terrain;
        var heights = RngChannel.Open(worldSeed, cell, "terrain", "height");
        using var h = new CanonicalHasher();
        h.Add("unnamed.terrain/v2").Add(terrain.SamplesPerAxis);
        for (uint i = 0; i < (uint)(terrain.SamplesPerAxis * terrain.SamplesPerAxis); i++)
            h.Add(terrain.BaseHeightMm + heights.Int(i, -terrain.AmplitudeMm, terrain.AmplitudeMm + 1));
        return h.Finish();
    }

    private ImmutableArray<BaselineNode> GenerateNodes(ulong worldSeed, CellKey cell)
    {
        var nodes = ImmutableArray.CreateBuilder<BaselineNode>();
        foreach (var rule in Profile.Nodes)
        {
            int count = RngChannel.Open(worldSeed, cell, "resources", rule.Name + "/count")
                .Int(0, rule.MinPerCell, rule.MaxPerCell + 1);
            for (int ordinal = 0; ordinal < count; ordinal++)
            {
                // Each node slot owns its channel: the count can change without moving existing nodes.
                var place = RngChannel.Open(worldSeed, cell, "resources", rule.Name + "/" + Keys.Ordinal(ordinal));
                nodes.Add(new BaselineNode(Keys.NodeKey(cell, rule.Name, ordinal), rule.DefId,
                    place.Int(0, 0, WorldMath.CellSizeCm), place.Int(1, 0, WorldMath.CellSizeCm)));
            }
        }
        // Authored nodes stand where they were put; each is ordinal 00 of its own name.
        foreach (var f in Profile.FixedNodes.Where(f => f.Cell == cell))
            nodes.Add(new BaselineNode(Keys.NodeKey(cell, f.Name, 0), f.DefId, f.XCm, f.ZCm));
        return nodes.ToImmutable();
    }

    private ImmutableArray<BaselinePopulation> GeneratePopulations(ulong worldSeed, CellKey cell)
    {
        var populations = ImmutableArray.CreateBuilder<BaselinePopulation>();
        foreach (var rule in Profile.Populations)
        {
            string populationId = Keys.PopulationId(cell, rule.Name);
            var slots = ImmutableArray.CreateBuilder<BaselineSlot>(rule.Target);
            for (int ordinal = 0; ordinal < rule.Target; ordinal++)
            {
                var place = RngChannel.Open(worldSeed, cell, "wildlife", rule.Name + "/" + Keys.Ordinal(ordinal));
                slots.Add(new BaselineSlot(Keys.SlotKey(cell, populationId, ordinal), populationId, rule.FamilyDefId, ordinal,
                    place.Int(0, 0, WorldMath.CellSizeCm), place.Int(1, 0, WorldMath.CellSizeCm)));
            }
            populations.Add(new BaselinePopulation(populationId, rule.FamilyDefId, rule.Target, rule.Min, rule.Max, slots.ToImmutable()));
        }
        return populations.ToImmutable();
    }
}

/// <summary>
/// The canonical probe set (M2b §17): a fixed seed and a fixed handful of cells whose baselines stand
/// in for a generator's behaviour. Their digests feed the fingerprint and are pinned by tests.
/// </summary>
public static class CanonicalProbes
{
    public const ulong Seed = 0x0DDB1A5E5BAD5EED;

    public static readonly ImmutableArray<CellKey> Cells = ImmutableArray.Create(
        CellKey.Parse("r_0_0:c_00_00"),
        CellKey.Parse("r_0_0:c_19_19"),
        CellKey.Parse("r_neg1_neg1:c_10_10"),
        CellKey.Parse("r_7_neg3:c_03_17"));

    public static string Digest(ICellBaselineGenerator generator)
    {
        using var h = new CanonicalHasher();
        h.Add("unnamed.worldgen-probes/v1").Add(Cells.Length);
        foreach (var cell in Cells)
            h.Add(generator.Generate(Seed, cell).Digest);
        return h.Finish();
    }
}

/// <summary>
/// <c>worldgen_fingerprint</c> (M2b §3.6). It detects generator drift: code or placement data that
/// changed output shows up here even when <c>worldgen_version</c> was not bumped, because the probe
/// digest is part of it. It is compatibility metadata, never procedural entropy.
/// </summary>
public static class WorldgenFingerprint
{
    public static string Compute(ICellBaselineGenerator generator)
    {
        using var h = new CanonicalHasher();
        return h.Add("unnamed.worldgen-fingerprint/v1")
            .Add(generator.GeneratorId)
            .Add(generator.WorldgenVersion)
            .Add(generator.RngContractVersion)
            .Add(generator.Profile.Digest)
            .Add(CanonicalProbes.Digest(generator))
            .Finish();
    }
}

/// <summary>Region-scale determinism, as RK-01's validation is written against a 2x2 km region.</summary>
public static class RegionDigest
{
    /// <summary>A digest of every cell's baseline digest, in key order.</summary>
    public static string Compute(ICellBaselineGenerator generator, ulong worldSeed, RegionKey region)
    {
        using var h = new CanonicalHasher();
        h.Add("unnamed.region-digest/v2").Add(region.ToString());
        foreach (var cell in CellKey.AllIn(region))
            h.Add(generator.Generate(worldSeed, cell).Digest);
        return h.Finish();
    }
}
