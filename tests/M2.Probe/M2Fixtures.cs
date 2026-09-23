using UNNAMED.Domain;
using UNNAMED.Persistence;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.M2Probe;

/// <summary>
/// Worlds shared by the M2 tests and the probe process, so a save made in one process can be checked
/// against the same world built in another. Everything here is deterministic - including the player's
/// IDs - so separately built worlds are comparable digest-for-digest.
/// </summary>
public static class M2Fixtures
{
    public const ulong Seed = 0x5C1A9E7B4D2F0083;
    public const string ContentVersion = "0.1.0";
    public const string Slot = SaveSlots.Quick;

    public static readonly string ContentHash = "sha256:" + string.Concat(Enumerable.Repeat("ab", 32));

    /// <summary>Every definition ID the fixture worlds reference: flags, creatures, items.</summary>
    public static readonly IReadOnlyList<string> DefinitionIds = new[]
    {
        "creature.beast.deer", "creature.beast.wolf_grey",
        "item.potion.heal_dangling", "item.weapon.iron_sword",
        "world.door.barn_open", "world.door.cellar_open", "world.lever.mill_gate",
    };

    /// <summary>ROADMAP M2's exit world: ten cells.</summary>
    public static readonly CellKey[] TenCells =
        Enumerable.Range(0, 10).Select(i => new CellKey(new RegionKey(0, 0), 0, i)).ToArray();

    public static GenerationProfile Profile(int wolfTarget = 5) => new(
        new[]
        {
            new NodeRule("iron_vein", "resource.ore.iron_vein", 2, 4),
            new NodeRule("silverleaf", "resource.herb.silverleaf", 0, 3),
        },
        new[]
        {
            new PopulationRule("wolves", "creature.beast.wolf_grey", wolfTarget, 2, 7),
            new PopulationRule("deer", "creature.beast.deer", 3, 1, 4),
        },
        new TerrainRule(12_000, 800, 11));

    public static CellBaselineGenerator Generator(int wolfTarget = 5) => new(Profile(wolfTarget));

    public static ContentIdentity Content(string? contentHash = null) =>
        new(ContentVersion, contentHash ?? ContentHash, DefinitionIds);

    public static LoadContext Context(Registry registry, ContentIdentity? content = null, int wolfTarget = 5) =>
        new(Generator(wolfTarget), content ?? Content(), registry);

    public static PlayerRecord Player(string name = "Aelin") => new(
        EntityId.Create(EntityKind.Character, 1_700_000_000_000, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }),
        name, 150_250, 12_000, -40_125,
        new[]
        {
            new InventoryEntry(EntityId.Create(EntityKind.Item, 1_700_000_000_001, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 1 }),
                "item.weapon.iron_sword", 1),
            new InventoryEntry(EntityId.Create(EntityKind.Item, 1_700_000_000_002, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 2 }),
                "item.potion.heal_dangling", 3),
        });

    /// <summary>The "before" world for the kill test: two cells changed.</summary>
    public static WorldDelta OldWorld(Registry registry, int wolfTarget = 5)
    {
        var world = new WorldDelta(Generator(wolfTarget), Seed, registry);
        world.SetFlag(TenCells[0], "world.door.cellar_open", 1);
        world.HarvestNode(TenCells[3], world.Baseline(TenCells[3]).Nodes[0].NodeKey, tick: 1_000);
        return world;
    }

    /// <summary>The "after" world: the old changes plus two more cells.</summary>
    public static WorldDelta NewWorld(Registry registry)
    {
        var world = OldWorld(registry);
        world.SetPopulationAlive(TenCells[5], $"pop.r_0_0.c_00_05.deer", 1);
        world.SetFlag(TenCells[7], "world.lever.mill_gate", 3);
        return world;
    }

    /// <summary>
    /// The historical-fixture world (tests/Persistence.Tests/Fixtures/README.md): the same logical world
    /// at every schema version, written by that version's own writer, with fixture content 0.1.0.
    /// </summary>
    public static class Historical
    {
        public const string ContentVersion = "0.1.0";
        public const string ContentHash = "sha256:7f522a30f46119bbe25e50c29a9db0a4ff21be31b080dc7a5eba0f225379353d";

        public static PlayerRecord Player() => new(
            M2Fixtures.Player().Id, "Aelin", 150_250, 12_000, -40_125,
            new[]
            {
                new InventoryEntry(EntityId.Create(EntityKind.Item, 1_700_000_000_001, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 1 }),
                    "item.weapon.iron_sword", 1),
                new InventoryEntry(EntityId.Create(EntityKind.Item, 1_700_000_000_002, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 2 }),
                    "item.potion.healing_draught", 3),
            });

        public static WorldDelta World(Registry registry)
        {
            var world = new WorldDelta(Generator(), Seed, registry);
            var cells = TenCells;
            world.SetFlag(cells[0], "world.door.cellar_open", 1);
            world.HarvestNode(cells[3], world.Baseline(cells[3]).Nodes[0].NodeKey, tick: 1_000);
            world.SetPopulationAlive(cells[5], "pop.r_0_0.c_00_05.deer", 1);
            world.SetFlag(cells[7], "world.lever.mill_gate", 3);
            var wolves = world.Baseline(cells[2]).Populations.Single(p => p.PopulationId.EndsWith(".wolves", StringComparison.Ordinal));
            world.KillOccupant(wolves.Slots[0].SlotKey);
            var deer = world.Baseline(cells[4]).Populations.Single(p => p.PopulationId.EndsWith(".deer", StringComparison.Ordinal));
            world.MoveOccupant(deer.Slots[1].SlotKey, 1_234, 5_678);
            return world;
        }

        public const string CurrentContentVersion = "0.2.0";
        public const string CurrentContentHash = "sha256:eb203d32f2d04344cdb2d2cd0a4c8323422c28df87ffbb178353fba62faf082f";

        /// <summary>
        /// Fixtures/content (0.2.0) as a content identity, for the probe, which does not load content
        /// files. A test pins this mirror to the real pack.
        /// </summary>
        public static ContentIdentity CurrentContent() => new(
            CurrentContentVersion, CurrentContentHash,
            new[]
            {
                "creature.beast.deer", "creature.beast.wolf_grey", "item.potion.minor_healing", "item.weapon.iron_sword",
                "world.door.cellar_open", "world.lever.mill_gate",
            },
            new Dictionary<string, string> { ["item.potion.healing_draught"] = "item.potion.minor_healing" });

        public static LoadContext Context(Registry registry) => new(Generator(), CurrentContent(), registry);

        /// <summary>Write the fixture with this build's writer.</summary>
        public static void Write(string profileRoot) =>
            new SaveStore(profileRoot).Save(SaveSlots.Quick, SaveDocuments.Capture(
                World(new Registry()), Player(), new ContentIdentity(ContentVersion, ContentHash, Array.Empty<string>()),
                5_000, playtimeSeconds: 321.5));
    }

    public static SaveDocument Document(WorldDelta world, PlayerRecord? player = null, long tick = 5_000, ContentIdentity? content = null) =>
        SaveDocuments.Capture(world, player ?? Player(), content ?? Content(), tick, playtimeSeconds: 321.5);

    /// <summary>A digest of the effective state of all ten cells - equal exactly when the worlds are equal.</summary>
    public static string WorldDigest(WorldDelta world)
    {
        using var h = new CanonicalHasher();
        foreach (var cell in TenCells)
            h.Add(world.EffectiveCellDigest(cell));
        return h.Finish();
    }
}
