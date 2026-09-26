using UNNAMED.Content;
using UNNAMED.Domain;
using UNNAMED.World;
using UNNAMED.World.Legacy;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.World.Tests;

/// <summary>Shared fixtures: one placement profile and one world seed used across the suite.</summary>
internal static class TestWorlds
{
    public const ulong Seed = 0x5C1A9E7B4D2F0083;

    /// <summary>A content hash for worldgen 1, where it was a generation input. Worldgen 2 never reads one.</summary>
    public static readonly string ContentHash = "sha256:" + string.Concat(Enumerable.Repeat("ab", 32));

    public static GenerationProfile Profile(int wolfTarget = 5, IEnumerable<NodeRule>? extraNodes = null) => new(
        new[]
        {
            new NodeRule("iron_vein", "resource.ore.iron_vein", 2, 4),
            new NodeRule("silverleaf", "resource.herb.silverleaf", 0, 3),
        }.Concat(extraNodes ?? Array.Empty<NodeRule>()),
        new[]
        {
            new PopulationRule("wolves", "creature.beast.wolf_grey", wolfTarget, 2, 7),
            new PopulationRule("deer", "creature.beast.deer", 3, 1, 4),
        },
        new TerrainRule(BaseHeightMm: 12_000, AmplitudeMm: 800, SamplesPerAxis: 11));

    public static CellBaselineGenerator Generator(int wolfTarget = 5) => new(Profile(wolfTarget));

    public static WorldDelta NewWorld() => new(Generator(), Seed, new Registry());

    public static CellBaselineGeneratorV1 LegacyGenerator(int wolfTarget = 5) => new(Profile(wolfTarget));

    public static BaselineTuple LegacyTuple(string? contentHash = null) =>
        new(Seed, CellBaselineGeneratorV1.Version, contentHash ?? ContentHash);

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

    /// <summary>
    /// The rules of the game's own region, built from <c>content/</c> with the same Content builders <c>GameSession.Boot</c> uses (World.Tests
    /// references Content but not Application).
    /// </summary>
    public static SimulationSetup HollowSetup()
    {
        const string region = "region.ashen_hollow";
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(RepoRoot(), "content"));
        Assert.False(loader.HasErrors, string.Join("\n", loader.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var layout = WorldContent.BuildLayout(loader, region);
        var movement = WorldContent.BuildMovement(loader);
        return new SimulationSetup(layout, movement, ProgressionContent.BuildRules(loader), WorldContent.BuildTiers(loader), WorldContent.TickMilliseconds(loader))
        {
            Items = new ItemSetup(ItemContent.BuildCatalog(loader), ItemContent.BuildLootTables(loader),
                ItemContent.BuildInventoryRules(loader, movement.InteractReachMm), ItemContent.BuildStartingKit(loader),
                ItemContent.BuildPricing(loader), ItemContent.BuildMerchants(loader)),
            Combat = CombatContent.Build(loader, region),
            Magic = MagicContent.Build(loader),
            Crafting = CraftingContent.Build(loader),
            Social = new SocialSetup(SocialContent.BuildNpcs(loader), SocialContent.BuildDialogues(loader)) { Companions = SocialContent.BuildCompanionTuning(loader) },
            Quests = new QuestSetup(QuestContent.BuildQuests(loader)),
            Navigation = NavigationContent.Build(loader),
            Building = BuildingContent.Build(loader),
        };
    }

    /// <summary>A new character in a new world of the region, as a new game starts one.</summary>
    public static Simulation HollowSimulation(SimulationSetup setup, IEventBus events, ulong seed = Seed, Func<PlayerRecord, PlayerRecord>? change = null)
    {
        var layout = setup.Layout;
        var terrain = new TerrainRule(layout.Generation.TerrainBaseHeightMm, layout.Generation.TerrainAmplitudeMm, layout.Generation.TerrainSamplesPerAxis);
        var fixedNodes = layout.Nodes.Select(site => new FixedNode(site.Name, site.NodeDefId, CellKey.OfWorld(site.XMm / 1000.0, site.ZMm / 1000.0),
            (int)WorldMath.FloorMod(site.XMm / 10, WorldMath.CellSizeCm), (int)WorldMath.FloorMod(site.ZMm / 10, WorldMath.CellSizeCm)));
        var generator = new CellBaselineGenerator(new GenerationProfile(Array.Empty<NodeRule>(), Array.Empty<PopulationRule>(), terrain, fixedNodes));
        var id = EntityId.NewId(EntityKind.Character);
        var player = Simulation.NewCharacter(setup, id, "Tester", PlayerRecord.DerivedAppearanceSeed(id));
        player = change?.Invoke(player) ?? player;
        return Simulation.Start(setup, player, new WorldDelta(generator, seed, new Registry()), 0, events);
    }

    public static void CopyDirectory(string from, string to)
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
