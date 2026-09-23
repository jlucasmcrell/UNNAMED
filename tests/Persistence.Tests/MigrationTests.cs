using System.Collections.Immutable;
using UNNAMED.Domain.Progression;
using UNNAMED.M2Probe;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>
/// M2b §12's required cases, each against a committed historical fixture (Fixtures/README.md) or a
/// world built for the case: definition-ID migration, content changes that do and do not touch the
/// baseline, registered transitions, generator drift, and a migration killed mid-commit.
/// </summary>
public class MigrationTests
{
    private const string Draught = "item.potion.healing_draught";
    private const string Wolf = "r_0_0:c_00_02.pop.r_0_0.c_00_02.wolves.00";

    private static LoadResult LoadFixture(int schema, LoadContext context)
    {
        using var profile = Fixtures.Copy(schema);
        return new SaveStore(profile.Root).Load(SaveSlots.Quick, context);
    }

    private static SaveCompatibilityException RefuseFixture(int schema, LoadContext context)
    {
        using var profile = Fixtures.Copy(schema);
        return Assert.Throws<SaveCompatibilityException>(() => new SaveStore(profile.Root).Load(SaveSlots.Quick, context));
    }

    /// <summary>The fixture content with some IDs removed and a different alias map.</summary>
    private static ContentIdentity ContentWith(
        IEnumerable<string>? add = null, IEnumerable<string>? remove = null,
        IReadOnlyDictionary<string, string>? aliases = null, IReadOnlyDictionary<string, string>? removed = null,
        IEnumerable<string>? discarded = null)
    {
        var fixture = Fixtures.Content();
        var ids = fixture.DefinitionIds.Except(remove ?? Array.Empty<string>()).Concat(add ?? Array.Empty<string>());
        return new ContentIdentity("0.3.0", "sha256:" + string.Concat(Enumerable.Repeat("3c", 32)), ids,
            aliases ?? ImmutableDictionary<string, string>.Empty, removed, discarded);
    }

    // ── 12.1 required field, schema 1 -> 2 ──────────────────────────────────

    [Fact]
    public void Schema1To2_AddsTheRequiredBaselineHashes_DerivedFromTheRegeneratedBaseline()
    {
        using var profile = Fixtures.Copy(1);
        var store = new SaveStore(profile.Root);
        store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        string slot = store.SlotPath(SaveSlots.Quick);
        string manifest = File.ReadAllText(Path.Combine(slot, SaveFormat.Manifest));
        Assert.DoesNotContain("worldgen_digest", manifest);
        Assert.Contains($"\"worldgen_fingerprint\": \"{M2Fixtures.Generator().Fingerprint}\"", manifest);
        Assert.Contains("\"rng_contract_version\": 2", manifest);

        var generator = M2Fixtures.Generator();
        var cells = SectionCodec.DecodeCells(File.ReadAllBytes(Path.Combine(slot, SaveFormat.Cells)));
        Assert.Equal(4, cells.Length);
        Assert.All(cells, c => Assert.Equal(generator.Generate(M2Fixtures.Seed, CellKey.Parse(c.CellKey)).Digest, c.BaselineHash));
        var entities = SectionCodec.DecodeEntities(File.ReadAllBytes(Path.Combine(slot, SaveFormat.Entities)));
        Assert.All(entities, e => Assert.Equal(
            generator.Generate(M2Fixtures.Seed, CellKey.Parse(SectionCodec.HostCell(e.SlotKey))).Digest, e.BaselineHash));
    }

    // ── 12.1 required field in player state, schema 2 -> 3 ──────────────────

    [Fact]
    public void Schema2To3_AddsTheRequiredAppearanceSeed_DerivedFromTheUlid()
    {
        using var profile = Fixtures.Copy(2);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(2, report.Steps.Count);
        Assert.StartsWith("schema 2 -> 3:", report.Steps[0]);
        Assert.StartsWith("schema 3 -> 4:", report.Steps[1]);
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Player)));
        Assert.Equal(0x32599743E39279EFUL, player.AppearanceSeed);   // Reference/worldgen_v2_reference.py
        Assert.Equal(PlayerRecord.DerivedAppearanceSeed(player.Id), player.AppearanceSeed);
    }

    // ── required record in player state, schema 3 -> 4 (M2c) ────────────────

    [Fact]
    public void Schema3To4_AddsTheProgressionRecord_AtItsEmptyValue()
    {
        using var profile = Fixtures.Copy(3);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.StartsWith("schema 3 -> 4:", Assert.Single(report.Steps));
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Player)));
        Assert.Equal(CharacterProgression.Empty.Digest, player.Progression.Digest);
        Assert.Equal(1, player.Progression.Level);
        Assert.Empty(player.Progression.Skills);
        Assert.Empty(player.Progression.Known);
        Assert.Equal(PoolState.Full, player.Progression.Pools);
        Assert.Equal(0x32599743E39279EFUL, player.AppearanceSeed);   // everything schema 3 held is kept
    }

    // ── 12.2 multi-hop ──────────────────────────────────────────────────────

    [Fact]
    public void Schema1_MigratesStepByStep_ThroughEveryVersion_ToTheCanonicalState()
    {
        var loaded = LoadFixture(1, Fixtures.Context(new Registry()));

        Assert.Equal(3, loaded.Report.Steps.Count);
        Assert.StartsWith("schema 1 -> 2:", loaded.Report.Steps[0]);
        Assert.StartsWith("schema 2 -> 3:", loaded.Report.Steps[1]);
        Assert.StartsWith("schema 3 -> 4:", loaded.Report.Steps[2]);
        Assert.Equal(File.ReadAllText(Fixtures.Expected(1)).Replace("\r\n", "\n"), CanonicalState.Render(loaded));
    }

    [Fact]
    public void TheProductionChain_HasOneStepPerVersion_InOrder()
    {
        Assert.Equal(
            Enumerable.Range(SaveFormat.OldestSupportedSchema, SaveFormat.SchemaVersion - SaveFormat.OldestSupportedSchema),
            SchemaMigrations.Production.Select(m => m.From));
        Assert.All(SchemaMigrations.Production, m => Assert.Equal(m.From + 1, m.To));
    }

    [Fact]
    public void AGapInTheChain_IsRefused_NotSkipped()
    {
        var context = Fixtures.Context(new Registry()) with { Migrations = ImmutableArray.Create<SchemaMigration>(new SchemaV1ToV2()) };

        Assert.Contains("no registered migration chain from schema 1 to schema 4", Assert.Single(RefuseFixture(1, context).Report.Blockers));
    }

    // ── created persistent instances (schema 3) ─────────────────────────────

    [Fact]
    public void ACreatedInstance_RoundTrips_WithItsIdentity()
    {
        using var profile = new TempProfile();
        var store = new SaveStore(profile.Root);
        var world = M2Fixtures.OldWorld(new Registry());
        var id = world.PlaceCreated(M2Fixtures.TenCells[6], "item.weapon.iron_sword", 9_999, 0);
        store.Save(M2Fixtures.Slot, M2Fixtures.Document(world));

        var registry = new Registry();
        var loaded = store.Load(M2Fixtures.Slot, M2Fixtures.Context(registry));

        var created = Assert.Single(loaded.World.CreatedIn(M2Fixtures.TenCells[6]));
        Assert.Equal((id, "item.weapon.iron_sword", 9_999, 0), (created.InstanceId, created.DefId, created.XCm, created.ZCm));
        Assert.True(registry.Exists(id));
        Assert.Equal(M2Fixtures.WorldDigest(world), M2Fixtures.WorldDigest(loaded.World));
    }

    [Fact]
    public void ACreatedInstancesDefinition_ResolvesLikeEveryOtherReference()
    {
        var content = ContentWith(add: new[] { "item.weapon.iron_blade" }, remove: new[] { "item.weapon.iron_sword" },
            aliases: new Dictionary<string, string>
            {
                [Draught] = "item.potion.minor_healing",
                ["item.weapon.iron_sword"] = "item.weapon.iron_blade",
            });

        var loaded = LoadFixture(3, Fixtures.Context(new Registry(), content));

        Assert.Equal("item.weapon.iron_blade", Assert.Single(loaded.World.CreatedIn(CellKey.Parse("r_0_0:c_00_06"))).DefId);
        Assert.Contains(loaded.Player.Inventory, e => e.DefId == "item.weapon.iron_blade");
        Assert.Contains("item.weapon.iron_sword -> item.weapon.iron_blade x2", loaded.Report.Aliases);
    }

    [Fact]
    public void ACreatedInstance_IsProvenAgainstItsHostCell_AndRebasedOnlyByATransition()
    {
        var trimmed = new LoadContext(M2Fixtures.Generator(wolfTarget: 4), Fixtures.Content(), new Registry());

        var refused = RefuseFixture(3, trimmed);
        Assert.Contains(refused.Report.CellsMismatched, c => c.StartsWith("r_0_0:c_00_06:", StringComparison.Ordinal));

        var loaded = LoadFixture(3, trimmed with { Transitions = ImmutableArray.Create(WolvesTrimmed(dropVanished: false)) });
        var created = Assert.Single(loaded.World.CreatedIn(CellKey.Parse("r_0_0:c_00_06")));
        Assert.Equal((500, 600), (created.XCm, created.ZCm));
        Assert.Equal(7, loaded.Report.CellsRebased.Count);
    }

    // ── 12.3 rename ─────────────────────────────────────────────────────────

    [Fact]
    public void ARenamedDefinition_ResolvesThroughTheAliasMap()
    {
        var content = ContentWith(
            add: new[] { "world.door.root_cellar_open" }, remove: new[] { "world.door.cellar_open" },
            aliases: new Dictionary<string, string>
            {
                [Draught] = "item.potion.minor_healing",
                ["world.door.cellar_open"] = "world.door.root_cellar_open",
            });

        var loaded = LoadFixture(2, Fixtures.Context(new Registry(), content));

        Assert.Equal(1, loaded.World.GetFlag(CellKey.Parse("r_0_0:c_00_00"), "world.door.root_cellar_open"));
        Assert.Equal(0, loaded.World.GetFlag(CellKey.Parse("r_0_0:c_00_00"), "world.door.cellar_open"));
        Assert.Contains("world.door.cellar_open -> world.door.root_cellar_open", loaded.Report.Aliases);
        Assert.True(loaded.IsComplete);
    }

    [Fact]
    public void ARenameWithoutAMap_FailsTheLoad_NamingTheId()
    {
        var refused = RefuseFixture(2, Fixtures.Context(new Registry(), ContentWith(aliases: new Dictionary<string, string>())));

        var blocker = Assert.Single(refused.Report.Blockers);
        Assert.Contains($"unresolved definition ID '{Draught}'", blocker);
        Assert.Contains("player inventory", blocker);
    }

    // ── definition IDs inside the progression record (schema 4, M2c) ────────

    [Fact]
    public void TheProgressionRecord_GoesThroughTheDefinitionPass_RenamesAndRemovals()
    {
        // The v4 fixture knows spell.ember.firebolt (renamed by the pack) and a recipe this content discards.
        var content = ContentWith(remove: new[] { "recipe.alchemy.salve_minor" }, aliases: Fixtures.Content().Aliases,
            discarded: new[] { "recipe.alchemy.salve_minor" });

        var loaded = LoadFixture(4, Fixtures.Context(new Registry(), content));

        var progression = loaded.Player.Progression;
        Assert.True(ProgressionEngine.Knows(progression, "spell.ember.bolt"));
        Assert.False(ProgressionEngine.Knows(progression, "recipe.alchemy.salve_minor"));
        Assert.DoesNotContain("recipe.alchemy.salve_minor", progression.NoveltyFirsts);
        Assert.Contains(loaded.Report.Loss, l => l.StartsWith("player known technique: 'recipe.alchemy.salve_minor'", StringComparison.Ordinal));
        Assert.Contains(loaded.Report.Loss, l => l.StartsWith("player novelty record: 'recipe.alchemy.salve_minor'", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnresolvedSkill_FailsTheLoad_NamingIt()
    {
        var content = ContentWith(remove: new[] { "skill.one_hand_blade" }, aliases: Fixtures.Content().Aliases);

        var refused = RefuseFixture(4, Fixtures.Context(new Registry(), content));

        Assert.Contains(refused.Report.Blockers, b => b.Contains("unresolved definition ID 'skill.one_hand_blade' (player skill)"));
    }

    /// <summary>
    /// "Without alias map: CI fails." The committed fixtures load against the fixture content pack, so
    /// deleting its alias entry - the mistake of renaming a definition without a map - fails CI.
    /// </summary>
    [Fact]
    public void RemovingTheAliasFromTheContentPack_FailsTheFixtureLoads()
    {
        string pack = Path.Combine(Path.GetTempPath(), "unnamed-pack-" + Guid.NewGuid().ToString("N"));
        try
        {
            Fixtures.CopyDirectory(Fixtures.ContentRoot, pack);
            File.Delete(Path.Combine(pack, "_aliases.yaml"));
            var content = Fixtures.Content(pack);

            for (int schema = SaveFormat.OldestSupportedSchema; schema <= SaveFormat.SchemaVersion; schema++)
                Assert.Contains(RefuseFixture(schema, Fixtures.Context(new Registry(), content)).Report.Blockers, b => b.Contains(Draught));
        }
        finally
        {
            Directory.Delete(pack, recursive: true);
        }
    }

    // ── 12.4 removal ────────────────────────────────────────────────────────

    [Fact]
    public void ARemovalWithAReplacement_ConvertsTheReference()
    {
        var content = ContentWith(add: new[] { "item.potion.greater_healing" }, remove: new[] { "item.potion.minor_healing" },
            removed: new Dictionary<string, string> { [Draught] = "item.potion.greater_healing" });

        var loaded = LoadFixture(2, Fixtures.Context(new Registry(), content));

        Assert.Contains(loaded.Player.Inventory, e => e.DefId == "item.potion.greater_healing" && e.Count == 3);
        Assert.Contains($"{Draught} -> item.potion.greater_healing (removed, replaced)", loaded.Report.Removals);
        Assert.True(loaded.IsComplete);
    }

    [Fact]
    public void ARemovalWithNoReplacement_DropsTheReference_AsReportedLoss()
    {
        var content = ContentWith(remove: new[] { "item.potion.minor_healing", "world.lever.mill_gate" },
            discarded: new[] { Draught, "world.lever.mill_gate" });

        var loaded = LoadFixture(2, Fixtures.Context(new Registry(), content));

        Assert.DoesNotContain(loaded.Player.Inventory, e => e.DefId.StartsWith("item.potion.", StringComparison.Ordinal));
        Assert.Equal(0, loaded.World.GetFlag(CellKey.Parse("r_0_0:c_00_07"), "world.lever.mill_gate"));
        Assert.Contains(loaded.Report.Loss, l => l.Contains(Draught) && l.Contains("dropped"));
        Assert.Contains(loaded.Report.Loss, l => l.Contains("world.lever.mill_gate"));
        Assert.False(loaded.IsComplete);   // declared, and still reported: never a silent drop
    }

    [Fact]
    public void ARemovalWithNoDisposition_FailsTheLoad() =>
        Assert.Contains(RefuseFixture(2, Fixtures.Context(new Registry(), ContentWith(remove: new[] { "item.potion.minor_healing" })))
            .Report.Blockers, b => b.Contains(Draught));

    // ── 12.5 baseline-neutral content change ────────────────────────────────

    [Fact]
    public void ABalanceEdit_ChangesContentHash_AndTheSaveLoadsWithoutReshuffle()
    {
        string pack = Path.Combine(Path.GetTempPath(), "unnamed-pack-" + Guid.NewGuid().ToString("N"));
        try
        {
            Fixtures.CopyDirectory(Fixtures.ContentRoot, pack);
            string wolf = Path.Combine(pack, "creatures", "beast", "wolf_grey.yaml");
            File.AppendAllText(wolf, "stats:\n  hp: 31\n");   // a runtime balance value: no generator reads it
            var edited = Fixtures.Content(pack);
            Assert.NotEqual(Fixtures.Content().Hash, edited.Hash);

            var loaded = LoadFixture(2, Fixtures.Context(new Registry(), edited));

            Assert.True(loaded.Report.ContentChanged);
            Assert.Equal(6, loaded.Report.CellsMatched);
            Assert.Empty(loaded.Report.CellsRebased);
            Assert.Equal(File.ReadAllText(Fixtures.Expected(2)).Replace("\r\n", "\n"), CanonicalState.Render(loaded));
        }
        finally
        {
            Directory.Delete(pack, recursive: true);
        }
    }

    // ── 12.7 baseline-affecting change, 12.8 drift, and never a silent delta ─

    [Fact]
    public void ABaselineAffectingChange_IsDetectedPerCell_AndRefused()
    {
        var context = new LoadContext(M2Fixtures.Generator(wolfTarget: 4), Fixtures.Content(), new Registry());

        var refused = RefuseFixture(2, context);

        Assert.Equal(6, refused.Report.CellsMismatched.Count);   // every changed cell has a wolf population
        Assert.True(refused.Report.FingerprintChanged);
        Assert.Contains(refused.Report.Blockers, b => b.Contains("Their deltas are not applied to a different baseline"));
    }

    [Fact]
    public void GeneratorDrift_WithTheSameVersion_IsCaughtByTheBaselineHashes()
    {
        var drifted = new DriftedGenerator(M2Fixtures.Generator());
        Assert.Equal(CellBaselineGenerator.Version, drifted.WorldgenVersion);

        var refused = RefuseFixture(2, new LoadContext(drifted, Fixtures.Content(), new Registry()));

        Assert.Contains(refused.Report.Warnings, w => w.Contains("worldgen_fingerprint differs"));
        Assert.Equal(6, refused.Report.CellsMismatched.Count);
        Assert.Equal(MigrationResult.Blocked, refused.Report.Result);
    }

    // ── 12.7 / §7: a registered transition ──────────────────────────────────

    private static BaselineTransition WolvesTrimmed(bool dropVanished) =>
        new("wolves trimmed from 5 to 4 per cell", M2Fixtures.Generator().Fingerprint, M2Fixtures.Generator(wolfTarget: 4).Fingerprint, dropVanished);

    [Fact]
    public void ARegisteredTransition_RebasesTheChangedCells_ByStableIdentity()
    {
        var context = new LoadContext(M2Fixtures.Generator(wolfTarget: 4), Fixtures.Content(), new Registry())
        {
            Transitions = ImmutableArray.Create(WolvesTrimmed(dropVanished: false)),
        };

        var loaded = LoadFixture(2, context);

        Assert.Equal(6, loaded.Report.CellsRebased.Count);
        Assert.Empty(loaded.Report.Loss);
        Assert.False(loaded.World.Occupant(Wolf).Alive);   // slot 00 exists in both baselines: carried
        Assert.True(loaded.World.IsHarvested(CellKey.Parse("r_0_0:c_00_03"), "node.r_0_0:c_00_03.iron_vein.00"));
        var deer = loaded.World.Occupant("r_0_0:c_00_04.pop.r_0_0.c_00_04.deer.01");
        Assert.Equal((1_234, 5_678), (deer.XCm, deer.ZCm));
    }

    [Fact]
    public void ATransition_NeverReaimsARecordWhoseTargetVanished()
    {
        // A save that killed the fifth wolf, which the trimmed baseline no longer generates.
        using var profile = new TempProfile();
        var store = new SaveStore(profile.Root);
        var world = M2Fixtures.OldWorld(new Registry());
        string fifth = $"{M2Fixtures.TenCells[2]}.pop.r_0_0.c_00_02.wolves.04";
        world.KillOccupant(fifth);
        store.Save(M2Fixtures.Slot, M2Fixtures.Document(world));
        LoadContext Trimmed(bool drop) => M2Fixtures.Context(new Registry(), wolfTarget: 4) with
        {
            Transitions = ImmutableArray.Create(WolvesTrimmed(drop)),
        };

        var refused = Assert.Throws<SaveCompatibilityException>(() => store.Load(M2Fixtures.Slot, Trimmed(drop: false)));
        Assert.Contains(refused.Report.Blockers, b => b.Contains(fifth) && b.Contains("does not allow dropping"));

        var loaded = store.Load(M2Fixtures.Slot, Trimmed(drop: true));
        Assert.Contains(loaded.Report.Loss, l => l.Contains(fifth));
        Assert.False(loaded.IsComplete);
    }

    // ── 12.9 crash safety ───────────────────────────────────────────────────

    /// <summary>
    /// A migration is committed by the same §7.1 sequence as any save, so a real kill at any step leaves
    /// the original or the migrated save - and either one loads to the same current state.
    /// </summary>
    [Theory]
    [InlineData(SaveStep.StagingWritten)]
    [InlineData(SaveStep.IntegrityRootWritten)]
    [InlineData(SaveStep.PreviousMovedToTrash)]
    [InlineData(SaveStep.StagingPromoted)]
    [InlineData(SaveStep.Verified)]
    [InlineData(SaveStep.Rotated)]
    public void AMigrationKilledAtAnyStep_LeavesTheOriginalOrTheMigratedSave(SaveStep killAt)
    {
        using var profile = Fixtures.Copy(1);

        var killed = Probe.Run("migrate", profile.Root, killAt.ToString());
        Assert.NotEqual(0, killed.ExitCode);
        Assert.Equal($"KILL {killAt}", killed.Output);

        var store = new SaveStore(profile.Root);
        store.RecoverInterruptedCommits();
        var loaded = store.Load(SaveSlots.Quick, Fixtures.Context(new Registry()));

        int schema = loaded.Report.SourceSchema!.Value;
        Assert.True(schema is 1 || schema == SaveFormat.SchemaVersion, $"After a kill at {killAt} the slot is schema {schema}");
        Assert.Equal(File.ReadAllText(Fixtures.Expected(1)).Replace("\r\n", "\n"), CanonicalState.Render(loaded));
        Assert.Empty(Directory.EnumerateDirectories(profile.Root, ".staging-*"));
        Assert.Empty(Directory.EnumerateDirectories(profile.Root, ".trash-*"));
        foreach (string original in store.PreMigrationBackups(SaveSlots.Quick))
            Assert.Null(SaveStore.VerifyIntegrity(original));   // a pre-migration copy, if any, is complete
    }

    [Fact]
    public void TheProbesContentMirror_IsTheFixtureContentPack()
    {
        var mirror = M2Fixtures.Historical.CurrentContent();
        var pack = Fixtures.Content();

        Assert.Equal(pack.Hash, mirror.Hash);
        Assert.Equal(pack.DefinitionIds.OrderBy(i => i, StringComparer.Ordinal), mirror.DefinitionIds.OrderBy(i => i, StringComparer.Ordinal));
        Assert.Equal(pack.Aliases, mirror.Aliases);
        Assert.Equal(WorldgenProfileDigest(), M2Fixtures.Profile().Digest);
    }

    private static string WorldgenProfileDigest() => SaveTool.WorldgenProfileFile.Read(Fixtures.Profile).Digest;

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
