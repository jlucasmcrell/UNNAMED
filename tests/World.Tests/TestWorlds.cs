using UNNAMED.Content;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.World.Tests;

/// <summary>Shared fixtures: one placement profile and one baseline tuple used across the suite.</summary>
internal static class TestWorlds
{
    public const ulong Seed = 0x5C1A9E7B4D2F0083;

    public static readonly string ContentHash = "sha256:" + string.Concat(Enumerable.Repeat("ab", 32));

    public static GenerationProfile Profile(int wolfTarget = 5) => new(
        new[]
        {
            new NodeRule("resource.ore.iron_vein", 2, 4),
            new NodeRule("resource.herb.silverleaf", 0, 3),
        },
        new[]
        {
            new PopulationRule("wolves", "creature.beast.wolf_grey", wolfTarget, 2, 7),
            new PopulationRule("deer", "creature.beast.deer", 3, 1, 4),
        },
        new TerrainRule(BaseHeightMm: 12_000, AmplitudeMm: 800, SamplesPerAxis: 11));

    public static BaselineTuple Tuple(string? contentHash = null) =>
        new(Seed, CellBaselineGeneratorV1.Version, contentHash ?? ContentHash);

    public static CellBaselineGeneratorV1 Generator(int wolfTarget = 5) => new(Profile(wolfTarget));

    public static WorldDelta NewWorld() => new(Generator(), Tuple(), new Registry());

    public static readonly CellKey Home = CellKey.Parse("r_0_0:c_07_11");

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "content")) && Directory.Exists(Path.Combine(dir.FullName, "src")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root");
    }

    /// <summary>The content_hash of a content directory, through the real M1b loader.</summary>
    public static string ContentHashOf(string contentRoot)
    {
        var loader = new ContentLoader();
        loader.LoadAll(contentRoot);
        Assert.True(loader.LoadedCount > 0, $"No content loaded from {contentRoot}");
        return loader.ComputeContentHash();
    }
}
