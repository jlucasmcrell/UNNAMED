// UNNAMED World - deterministic baseline generation (D-05, PERSISTENCE.md §1, WORLD_ARCHITECTURE.md §5)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;

namespace UNNAMED.World;

/// <summary>Resource-node placement: how many nodes of one definition a cell carries.</summary>
public sealed record NodeRule(string DefId, int MinPerCell, int MaxPerCell);

/// <summary>A spawn population per cell: a family with a budget (WORLD_ARCHITECTURE.md §5.5).</summary>
public sealed record PopulationRule(string Name, string FamilyDefId, int Target, int Min, int Max);

/// <summary>Terrain sampling for the cell's terrain signature, in integer millimetres.</summary>
public sealed record TerrainRule(int BaseHeightMm, int AmplitudeMm, int SamplesPerAxis);

/// <summary>
/// Placement data: the authored input to generation. It is part of the generation identity, so it
/// is hashed into <c>worldgen_digest</c> (PERSISTENCE.md §4.2). Rule order is significant - it fixes
/// node index assignment - and is therefore part of the digest too.
/// </summary>
public sealed record GenerationProfile
{
    public GenerationProfile(IEnumerable<NodeRule> nodes, IEnumerable<PopulationRule> populations, TerrainRule terrain)
    {
        Nodes = nodes.ToImmutableArray();
        Populations = populations.ToImmutableArray();
        Terrain = terrain;
        Validate();
        Digest = ComputeDigest();
    }

    public ImmutableArray<NodeRule> Nodes { get; }
    public ImmutableArray<PopulationRule> Populations { get; }
    public TerrainRule Terrain { get; }

    /// <summary>Content-addressed identity of the placement data.</summary>
    public string Digest { get; }

    private void Validate()
    {
        foreach (var n in Nodes)
        {
            if (!DefinitionId.IsValid(n.DefId))
                throw new ArgumentException($"Node rule has an invalid definition ID '{n.DefId}'");
            if (n.MinPerCell < 0 || n.MaxPerCell < n.MinPerCell)
                throw new ArgumentException($"Node rule '{n.DefId}' has an invalid range [{n.MinPerCell}, {n.MaxPerCell}]");
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in Populations)
        {
            if (!DefinitionId.IsValid(p.FamilyDefId))
                throw new ArgumentException($"Population '{p.Name}' has an invalid family ID '{p.FamilyDefId}'");
            if (string.IsNullOrEmpty(p.Name) || !p.Name.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
                throw new ArgumentException($"Population name '{p.Name}' must be lowercase snake_case");
            if (!names.Add(p.Name))
                throw new ArgumentException($"Duplicate population name '{p.Name}'");
            if (p.Min < 0 || p.Min > p.Target || p.Target > p.Max)
                throw new ArgumentException($"Population '{p.Name}' budget must satisfy 0 <= min <= target <= max");
        }
        if (Terrain.SamplesPerAxis < 2 || Terrain.AmplitudeMm < 0)
            throw new ArgumentException("Terrain rule needs at least 2 samples per axis and a non-negative amplitude");
    }

    private string ComputeDigest()
    {
        using var h = new CanonicalHasher();
        h.Add("unnamed.generation-profile/v1");
        h.Add(Nodes.Length);
        foreach (var n in Nodes)
            h.Add(n.DefId).Add(n.MinPerCell).Add(n.MaxPerCell);
        h.Add(Populations.Length);
        foreach (var p in Populations)
            h.Add(p.Name).Add(p.FamilyDefId).Add(p.Target).Add(p.Min).Add(p.Max);
        h.Add(Terrain.BaseHeightMm).Add(Terrain.AmplitudeMm).Add(Terrain.SamplesPerAxis);
        return h.Finish();
    }
}

/// <summary>A resource node's baseline: identity is the key, not a ULID (WORLD_ARCHITECTURE.md §5.4).</summary>
public sealed record BaselineNode(string NodeKey, string DefId, int XCm, int ZCm);

/// <summary>One population member's baseline slot (PERSISTENCE.md I-8): <c>&lt;cell_key&gt;.&lt;population_id&gt;.&lt;ordinal&gt;</c>.</summary>
public sealed record BaselineSlot(string SlotKey, string PopulationId, string FamilyDefId, int Ordinal, int XCm, int ZCm);

public sealed record BaselinePopulation(
    string PopulationId, string FamilyDefId, int Target, int Min, int Max, ImmutableArray<BaselineSlot> Slots);

/// <summary>A regenerated cell. A pure function of (baseline tuple, generator, cell key).</summary>
public sealed record CellBaseline(
    CellKey Cell,
    string TerrainHash,
    ImmutableArray<BaselineNode> Nodes,
    ImmutableArray<BaselinePopulation> Populations)
{
    /// <summary>A stable digest of the whole baseline - RK-01's per-cell comparison unit.</summary>
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
/// The frozen generation interface (ROADMAP M2). An implementation is frozen per
/// <see cref="WorldgenVersion"/>: generation may change only through a new version - never by
/// editing an existing one - and the golden-digest test catches an edit that skips the bump.
/// </summary>
public interface ICellBaselineGenerator
{
    string GeneratorId { get; }

    int WorldgenVersion { get; }

    GenerationProfile Profile { get; }

    /// <summary>
    /// The authority over baseline regeneration (PERSISTENCE.md §4.2, §6.1): content-addressed over
    /// the generator identity, its version, the placement data, and the numeric policy.
    /// </summary>
    string WorldgenDigest { get; }

    CellBaseline Generate(BaselineTuple tuple, CellKey cell);
}

/// <summary>Worldgen version 1. FROZEN: change nothing here; add a version 2 generator instead.</summary>
public sealed class CellBaselineGeneratorV1 : ICellBaselineGenerator
{
    public const int Version = 1;

    public CellBaselineGeneratorV1(GenerationProfile profile)
    {
        Profile = profile;
        using var h = new CanonicalHasher();
        WorldgenDigest = h.Add(GeneratorId).Add(Version).Add(profile.Digest)
            .Add("numeric-policy:integer-only;prng:xoshiro256starstar;hash:sha256")
            .Finish();
    }

    public string GeneratorId => "unnamed.worldgen.cell-baseline";

    public int WorldgenVersion => Version;

    public GenerationProfile Profile { get; }

    public string WorldgenDigest { get; }

    public CellBaseline Generate(BaselineTuple tuple, CellKey cell)
    {
        if (tuple.WorldgenVersion != Version)
            throw new InvalidOperationException(
                $"Generator v{Version} cannot generate a baseline for worldgen_version {tuple.WorldgenVersion}");

        return new CellBaseline(cell, GenerateTerrainHash(tuple, cell), GenerateNodes(tuple, cell), GeneratePopulations(tuple, cell));
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

    private ImmutableArray<BaselineNode> GenerateNodes(BaselineTuple tuple, CellKey cell)
    {
        var nodes = ImmutableArray.CreateBuilder<BaselineNode>();
        foreach (var rule in Profile.Nodes)
        {
            var stream = DeterministicStream.For(tuple, cell, "nodes:" + rule.DefId);
            int count = stream.NextInt(rule.MinPerCell, rule.MaxPerCell + 1);
            for (int i = 0; i < count; i++)
            {
                // node_key = "node.<cell_key>.<index>", index assigned by this generator (§5.4)
                string key = $"node.{cell}.{nodes.Count}";
                nodes.Add(new BaselineNode(key, rule.DefId,
                    stream.NextInt(0, WorldMath.CellSizeCm), stream.NextInt(0, WorldMath.CellSizeCm)));
            }
        }
        return nodes.ToImmutable();
    }

    private ImmutableArray<BaselinePopulation> GeneratePopulations(BaselineTuple tuple, CellKey cell)
    {
        var populations = ImmutableArray.CreateBuilder<BaselinePopulation>();
        foreach (var rule in Profile.Populations)
        {
            // Population IDs follow WORLD_ARCHITECTURE.md §5.5: pop.<region>.c_<cx>_<cz>.<name>
            string populationId = $"pop.{cell.Region}.c_{cell.Cx:00}_{cell.Cz:00}.{rule.Name}";
            var stream = DeterministicStream.For(tuple, cell, "spawns:" + rule.Name);
            var slots = ImmutableArray.CreateBuilder<BaselineSlot>(rule.Target);
            for (int ordinal = 0; ordinal < rule.Target; ordinal++)
            {
                slots.Add(new BaselineSlot($"{cell}.{populationId}.{ordinal:00}", populationId, rule.FamilyDefId, ordinal,
                    stream.NextInt(0, WorldMath.CellSizeCm), stream.NextInt(0, WorldMath.CellSizeCm)));
            }
            populations.Add(new BaselinePopulation(populationId, rule.FamilyDefId, rule.Target, rule.Min, rule.Max, slots.ToImmutable()));
        }
        return populations.ToImmutable();
    }
}

/// <summary>Region-scale determinism, as RK-01's validation is written against a 2x2 km region.</summary>
public static class RegionDigest
{
    /// <summary>A digest of every cell's baseline digest (terrain hash + sorted nodes and spawn slots), in key order.</summary>
    public static string Compute(ICellBaselineGenerator generator, BaselineTuple tuple, RegionKey region)
    {
        using var h = new CanonicalHasher();
        h.Add("unnamed.region-digest/v1").Add(region.ToString()).Add(generator.WorldgenDigest);
        foreach (var cell in CellKey.AllIn(region))
            h.Add(generator.Generate(tuple, cell).Digest);
        return h.Finish();
    }
}
