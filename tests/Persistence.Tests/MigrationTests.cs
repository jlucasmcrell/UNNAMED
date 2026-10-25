using UNNAMED.Domain.Social;
using System.Collections.Immutable;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
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
    private static IEnumerable<string> StepPrefixes(int from, int length) =>
        Enumerable.Range(from, SaveFormat.SchemaVersion - from).Select(v => $"schema {v} -> {v + 1}:"[..length]);


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

        Assert.Equal(SaveFormat.SchemaVersion - 2, report.Steps.Count);
        Assert.StartsWith("schema 2 -> 3:", report.Steps[0]);
        Assert.StartsWith("schema 3 -> 4:", report.Steps[1]);
        Assert.StartsWith("schema 4 -> 5:", report.Steps[2]);
        Assert.StartsWith("schema 5 -> 6:", report.Steps[3]);
        Assert.StartsWith("schema 6 -> 7:", report.Steps[4]);
        Assert.StartsWith("schema 7 -> 8:", report.Steps[5]);
        Assert.StartsWith("schema 8 -> 9:", report.Steps[6]);
        Assert.StartsWith("schema 9 -> 10:", report.Steps[7]);
        Assert.StartsWith("schema 10 -> 11:", report.Steps[8]);
        Assert.StartsWith("schema 11 -> 12:", report.Steps[9]);
        Assert.StartsWith("schema 12 -> 13:", report.Steps[10]);
        Assert.StartsWith("schema 13 -> 14:", report.Steps[11]);
        Assert.StartsWith("schema 14 -> 15:", report.Steps[12]);
        Assert.StartsWith("schema 15 -> 16:", report.Steps[13]);
        Assert.StartsWith("schema 16 -> 17:", report.Steps[14]);
        Assert.StartsWith("schema 17 -> 18:", report.Steps[15]);
        Assert.StartsWith("schema 18 -> 19:", report.Steps[16]);
        Assert.StartsWith("schema 19 -> 20:", report.Steps[17]);
        Assert.StartsWith("schema 20 -> 21:", report.Steps[18]);
        Assert.StartsWith("schema 21 -> 22:", report.Steps[19]);
        Assert.StartsWith("schema 22 -> 23:", report.Steps[20]);
        Assert.StartsWith("schema 23 -> 24:", report.Steps[21]);
        Assert.StartsWith("schema 24 -> 25:", report.Steps[22]);
        Assert.StartsWith("schema 25 -> 26:", report.Steps[23]);
        Assert.StartsWith("schema 26 -> 27:", report.Steps[24]);
        Assert.StartsWith("schema 27 -> 28:", report.Steps[25]);
        Assert.StartsWith("schema 28 -> 29:", report.Steps[26]);
        Assert.StartsWith("schema 29 -> 30:", report.Steps[27]);
        Assert.StartsWith("schema 30 -> 31:", report.Steps[28]);
        Assert.StartsWith("schema 31 -> 32:", report.Steps[29]);
        Assert.StartsWith("schema 32 -> 33:", report.Steps[30]);
        Assert.StartsWith("schema 33 -> 34:", report.Steps[31]);
        Assert.StartsWith("schema 34 -> 35:", report.Steps[32]);
        Assert.StartsWith("schema 35 -> 36:", report.Steps[33]);
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

        Assert.Equal(StepPrefixes(3, 14), report.Steps.Select(s => s[..14]));
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Player)));
        Assert.Equal(CharacterProgression.Empty.Digest, player.Progression.Digest);
        Assert.Equal(1, player.Progression.Level);
        Assert.Empty(player.Progression.Skills);
        Assert.Empty(player.Progression.Known);
        Assert.Equal(PoolState.Full, player.Progression.Pools);
        Assert.Equal(0x32599743E39279EFUL, player.AppearanceSeed);   // everything schema 3 held is kept
    }

    // ── required fields in player state, schema 4 -> 5 (M3) ─────────────────

    [Fact]
    public void Schema4To5_AddsFacingAndDiscoveries_AtTheirEmptyValues()
    {
        using var profile = Fixtures.Copy(4);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(4, 14), report.Steps.Select(s => s[..14]));
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Player)));
        Assert.Equal(0, player.FacingMdeg);
        Assert.Empty(player.Discoveries);
        Assert.Equal(3, player.Progression.Level);   // everything schema 4 held is kept
        Assert.Equal((150_250L, 12_000L, -40_125L), (player.XMm, player.YMm, player.ZMm));
    }

    // ── required fields in player and entities state, schema 5 -> 6 (M3b) ───

    [Fact]
    public void Schema5To6_AddsEquipmentCurrencyCountsAndContainers_AtTheirEmptyValues()
    {
        using var profile = Fixtures.Copy(5);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(5, 14), report.Steps.Select(s => s[..14]));
        string slot = store.SlotPath(SaveSlots.Quick);
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(slot, SaveFormat.Player)));
        Assert.Empty(player.Equipment);
        Assert.Equal(0, player.Currency);
        Assert.Equal(123_456, player.FacingMdeg);   // everything schema 5 held is kept
        var (_, created, containers, _, _) = SectionCodec.DecodeEntitySection(File.ReadAllBytes(Path.Combine(slot, SaveFormat.Entities)));
        Assert.Equal(1, Assert.Single(created).Count);
        Assert.Empty(containers);
    }

    // ── required field in player state, schema 6 -> 7 (M3c) ─────────────────

    [Fact]
    public void Schema6To7_AddsActiveEffects_AtTheirEmptyValue()
    {
        using var profile = Fixtures.Copy(6);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(6, 14), report.Steps.Select(s => s[..14]));
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Player)));
        Assert.Empty(player.Effects);
        Assert.Equal(40, player.Currency);   // everything schema 6 held is kept
        Assert.Equal(M2Fixtures.Historical.SwordId, player.Equipment[Domain.Items.EquipSlot.MainHand]);
    }

    // ── required list in the entities section, schema 7 -> 8 (M3d) ──────────

    [Fact]
    public void Schema7To8_AddsCreatureRecords_AtTheirEmptyValue()
    {
        using var profile = Fixtures.Copy(7);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(7, 14), report.Steps.Select(s => s[..14]));
        var (_, created, containers, creatures, _) = SectionCodec.DecodeEntitySection(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Entities)));
        Assert.Empty(creatures);
        Assert.Equal(3, Assert.Single(created, c => c.DefId == "item.potion.minor_healing").Count);   // everything schema 7 held is kept
        Assert.Equal(2, Assert.Single(containers).Items.Length);
    }

    // ── a required field on every item stack, schema 8 -> 9 (M3f) ──────────

    [Fact]
    public void Schema8To9_GivesEveryStackStandardQuality()
    {
        using var profile = Fixtures.Copy(8);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(8, 14), report.Steps.Select(s => s[..14]));
        string slot = store.SlotPath(SaveSlots.Quick);
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(slot, SaveFormat.Player)));
        var (_, created, containers, creatures, _) = SectionCodec.DecodeEntitySection(File.ReadAllBytes(Path.Combine(slot, SaveFormat.Entities)));
        Assert.All(player.Inventory, e => Assert.Equal(0, e.Quality));
        Assert.All(created, c => Assert.Equal(0, c.Quality));
        Assert.All(containers.SelectMany(c => c.Items), i => Assert.Equal(0, i.Quality));
        Assert.Equal(3, creatures.Length);   // everything schema 8 held is kept
        Assert.Equal(2, player.Inventory.Length);
    }

    // ── what the player's people think of them, schema 9 -> 10 (M4) ──────────

    [Fact]
    public void Schema9To10_GivesThePlayerNoRelationshipsAndNoConversations()
    {
        using var profile = Fixtures.Copy(9);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(9, 14), report.Steps.Select(s => s[..14]));
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Player)));
        Assert.Empty(player.Relationships);
        Assert.Empty(player.Conversations);
        Assert.Contains(player.Inventory, e => e.Quality == 1);   // everything schema 9 held is kept
    }

    // ── the player's quests, schema 10 -> 11 (M5) ────────────────────────────

    [Fact]
    public void Schema10To11_GivesThePlayerNoQuests()
    {
        using var profile = Fixtures.Copy(10);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(10, 14), report.Steps.Select(s => s[..14]));
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Player)));
        Assert.Empty(player.Quests);
        Assert.Equal(2, player.Relationships.Length);   // everything schema 10 held is kept
        Assert.Equal(new[] { "greet", "rumour" }, Assert.Single(player.Conversations).Heard);
    }

    // ── the player's companions, schema 11 -> 12 (M6) ────────────────────────

    [Fact]
    public void Schema11To12_GivesThePlayerNoCompanions()
    {
        using var profile = Fixtures.Copy(11);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(11, 14), report.Steps.Select(s => s[..14]));
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Player)));
        Assert.Empty(player.Companions);
        Assert.Equal(2, player.Quests.Length);   // everything schema 11 held is kept
        Assert.Equal(2, player.Relationships.Length);
    }

    // ── the player's posture, schema 12 -> 13 (the owner's M6 playtest) ─────

    [Fact]
    public void Schema12To13_StandsThePlayerOnTheGround()
    {
        using var profile = Fixtures.Copy(12);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(12, 14), report.Steps.Select(s => s[..14]));
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Player)));
        Assert.Equal(Posture.Grounded, player.Posture);
        Assert.Single(player.Companions);   // everything schema 12 held is kept
        Assert.Equal(2, player.Quests.Length);
    }

    [Fact]
    public void ASchema13PlayerWithoutAPosture_IsCorrupt_NotDefaulted()
    {
        var dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Posture = null;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("posture", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);
    }

    // ── what a creature's next ticks depend on, schema 13 -> 14 (the Phase-1 technical audit, L-06) ─────

    [Fact]
    public void Schema13To14_KeepsEveryCreature_FreeOfWhatWasNotSaved_AndLeavesNothingToHear()
    {
        using var profile = Fixtures.Copy(13);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(13, 14), report.Steps.Select(s => s[..14]));
        var (_, _, containers, creatures, noises) =
            SectionCodec.DecodeEntitySection(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Entities)));
        Assert.Equal(new[] { "spawn.fixture.den#0", "spawn.fixture.den#1", "spawn.fixture.ridge#0" }, creatures.Select(c => c.Key));
        Assert.All(creatures, c => Assert.Equal((0L, 0L, (long?)null, 0), (c.NextChargeTick, c.StaggerImmuneUntil, c.StaggeredTick, c.StaggerLastsTicks)));
        Assert.Equal((CreatureMind.Searching, 45, true), (creatures[0].Mind, creatures[0].Awareness, creatures[0].HasCalled));   // everything schema 13 held is kept
        Assert.Equal(2, containers.Length);
        Assert.Empty(noises);
    }

    [Fact]
    public void ASchema14CreatureWithoutItsContinuation_OrSectionWithoutItsNoises_IsCorrupt_NotDefaulted()
    {
        var snapshot = M2Fixtures.Historical.World(new Registry()).TakeSnapshot();
        var dto = MessagePack.MessagePackSerializer.Deserialize<EntitiesSectionDto>(
            SectionCodec.EncodeEntities(snapshot), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Creatures![0].Continuation = null;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("continuation", Assert.Throws<FormatException>(() => SectionCodec.DecodeEntitySection(bytes)).Message);

        dto = MessagePack.MessagePackSerializer.Deserialize<EntitiesSectionDto>(
            SectionCodec.EncodeEntities(snapshot), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Noises = null;
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("noises", Assert.Throws<FormatException>(() => SectionCodec.DecodeEntitySection(bytes)).Message);
    }

    // ── Waystone travel, schema 14 -> 15 (S-38) ─────────────────────────────

    [Fact]
    public void Schema14To15_KnowsNoStone_ChoosesNone_AndHasNoRoadOnItsMap()
    {
        using var profile = Fixtures.Copy(14);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(14, 14), report.Steps.Select(s => s[..14]));
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Player)));
        Assert.Empty(player.Waystones);
        Assert.Null(player.ReturnPoint);   // the region's spawn, as before Waystone travel
        Assert.Empty(player.RoadsWalked);
        Assert.Equal(new Posture(Stance.Crouched, false, 0), player.Posture);   // everything schema 14 held is kept
        Assert.Single(player.Companions);
        Assert.Equal(2, player.Quests.Length);
    }

    // ── The character creator, schema 16 -> 17 (WP2) ────────────────────────

    [Fact]
    public void Schema16To17_KeepsTheName_AndGivesTheCharacterTheGameAlwaysMade_WithNoCall()
    {
        using var profile = Fixtures.Copy(16);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(16, 14), report.Steps.Select(s => s[..14]));
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Player)));
        Assert.Equal("Aelin", player.Name);
        Assert.Equal(new CharacterIdentity(Pronouns.They, "people.veth", "body.default", null), player.Identity);
        Assert.Equal(CharacterIdentity.BeforeTheCreator, player.Identity);
        Assert.Equal("waystone.fixture.ford", player.ReturnPoint);   // everything schema 16 held is kept
        Assert.Equal(2, player.Waystones.Length);
        Assert.Equal(new Posture(Stance.Crouched, false, 0), player.Posture);
        Assert.Equal(3, player.Deeds.Length);
    }

    /// <summary>
    /// The saves the game itself wrote before the creator: every one was a "Wanderer" with no Call (both NewGame("Wanderer") calls). Such a
    /// schema-16 player, written in the frozen shape, migrates to a Wanderer with no Call - and loads as one.
    /// </summary>
    [Fact]
    public void ASchema16WandererFromTheGame_MigratesToAWandererWithNoCall()
    {
        using var profile = Fixtures.Copy(16);
        var store = new SaveStore(profile.Root);
        string slot = store.SlotPath(SaveSlots.Quick);
        var options = MessagePack.MessagePackSerializerOptions.Standard;
        var v16 = MessagePack.MessagePackSerializer.Deserialize<UNNAMED.Persistence.Sections.V16.Player>(File.ReadAllBytes(Path.Combine(slot, SaveFormat.Player)), options);
        v16.Name = "Wanderer";
        File.WriteAllBytes(Path.Combine(slot, SaveFormat.Player), MessagePack.MessagePackSerializer.Serialize(v16, options));
        // The integrity root a schema-16 writer would have written over these bytes (the sha256sum layout, §3.3).
        File.WriteAllText(Path.Combine(slot, SaveFormat.IntegrityRoot), string.Concat(SaveFormat.CheckedFiles.Select(file =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(slot, file)))).ToLowerInvariant() + "  " + file + "\n")));

        var loaded = store.Load(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(16, 14), loaded.Report.Steps.Select(s => s[..14]));
        Assert.Equal("Wanderer", loaded.Player.Name);
        Assert.Null(loaded.Player.Identity.CallId);
        Assert.Equal(CharacterIdentity.BeforeTheCreator, loaded.Player.Identity);
    }

    [Fact]
    public void ASchema17PlayerWithoutItsIdentity_IsCorrupt_NotDefaulted_AndUnknownPronounsAreRefused()
    {
        var dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Identity = null;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("identity", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);

        dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Identity!.Pronouns = "it";
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("pronouns", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);

        dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Identity!.Call = "item.weapon.iron_sword";
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("identity", Assert.Throws<ArgumentException>(() => SectionCodec.DecodePlayer(bytes)).Message);
    }

    [Fact]
    public void ASchema15PlayerWithoutItsWaystonesOrRoads_IsCorrupt_NotDefaulted_AndAReturnPointItDoesNotKnowIsRefused()
    {
        var dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Waystones = null;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("Waystone", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);

        dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.RoadsWalked = null;
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("roads", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);

        dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.ReturnPoint = "waystone.fixture.nowhere";
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("return point", Assert.Throws<ArgumentException>(() => SectionCodec.DecodePlayer(bytes)).Message);
    }

    // ── the persistent clock, schema 17 -> 18 (WP11) ────────────────────────

    [Fact]
    public void Schema17To18_SavedAtItsBuildTimestamp_WithNothingCaughtUp()
    {
        using var profile = Fixtures.Copy(17);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(17, 14), report.Steps.Select(s => s[..14]));
        var manifest = SectionCodec.DecodeManifest(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Manifest)));
        // The absence a login measures runs from when the save was taken, not from when it was migrated.
        Assert.Equal("2026-10-16T23:24:26.000Z", manifest.SavedUtc);
        Assert.Empty(manifest.AwayLog);
        Assert.Equal(0, manifest.WorldTimeAdvancedTicks);
        Assert.Equal(SaveFormat.SchemaVersion, manifest.SchemaVersion);
    }

    [Fact]
    public void ASchema17ManifestWithoutABuildTimestamp_IsRefused_AndASavedUtcThatIsNoMoment_IsCorrupt()
    {
        var document = new MigrationDocument(17, new System.Text.Json.Nodes.JsonObject { ["schema_version"] = 17 },
            new Dictionary<string, byte[]?>());
        var report = new MigrationReport("quick");
        new SchemaV17ToV18().Apply(document, new MigrationEnvironment(M2Fixtures.Generator()), report);
        Assert.Contains("build_timestamp", Assert.Single(report.Blockers));

        var manifest = SectionCodec.DecodeManifest(File.ReadAllBytes(Path.Combine(Fixtures.Save(18), SaveFormat.Manifest)));
        Assert.Throws<FormatException>(() => (manifest with { SavedUtc = "yesterday" }).SavedAt);
        Assert.Throws<FormatException>(() => (manifest with { SavedUtc = "2026-10-17T09:30:00Z" }).SavedAt);
    }

    // ── itemization, schema 18 -> 19 (WP3a) ─────────────────────────────────

    [Fact]
    public void Schema18To19_GivesNoStackANotableRecord_AndKeepsEverythingElse()
    {
        using var profile = Fixtures.Copy(18);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(18, 14), report.Steps.Select(s => s[..14]));
        string slot = store.SlotPath(SaveSlots.Quick);
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(slot, SaveFormat.Player)));
        var (_, created, containers, _, _) = SectionCodec.DecodeEntitySection(File.ReadAllBytes(Path.Combine(slot, SaveFormat.Entities)));
        Assert.All(player.Inventory, e => Assert.Null(e.Notable));
        Assert.All(created, c => Assert.Null(c.Notable));
        Assert.All(containers.SelectMany(c => c.Items), i => Assert.Null(i.Notable));
        Assert.Equal(1, player.Inventory.Single(e => e.ItemId == M2Fixtures.Historical.SwordId).Quality);   // everything schema 18 held is kept
        Assert.Equal("waystone.fixture.ford", player.ReturnPoint);
        Assert.Equal(3, player.Deeds.Length);
        Assert.Equal("call.scholar", player.Identity.CallId);
    }

    // ── the party, schema 20 -> 21 (WP15a) ───────────────────────────────────

    [Fact]
    public void Schema20To21_MakesEveryoneInThePartyACompanion_AggressiveOnTheLeash_AndGivesTheLedgerNoOne()
    {
        using var profile = Fixtures.Copy(20);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(20, 14), report.Steps.Select(s => s[..14]));
        Assert.StartsWith("schema 20 -> 21:", report.Steps[0]);
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Player)));
        var warden = Assert.Single(player.Companions);
        Assert.Equal((PartyMembership.Companion, CompanionStance.Aggressive, PartyTactics.Default, (HirelingContract?)null),
            (warden.Membership, warden.Stance, warden.Tactics, warden.Contract));
        Assert.Equal((64, 2, 3), (warden.Health, warden.StuckTicks, warden.Trail.Length));   // everything schema 20 held is kept
        Assert.Equal(PartyLedger.Empty, player.Party);
        Assert.NotNull(player.Inventory.Single(e => e.ItemId == M2Fixtures.Historical.SwordId).Notable);
    }

    [Fact]
    public void ASchema21CompanionWithoutItsMembership_OrAPlayerWithoutTheLedger_IsCorrupt()
    {
        var dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Companions![0].Membership = null;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("membership", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);

        dto.Companions![0].Membership = "hireling";
        dto.Party = null;
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("party ledger", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);

        dto.Party = new PartyLedgerDto();
        dto.Companions![0].Contract!.Role = "cook";
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("role 'cook'", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);
    }

    // ── the living economy, schema 21 -> 22 (WP14) ──────────────────────────

    [Fact]
    public void Schema21To22_HoldsNoEconomy_AndKeepsEverythingElse()
    {
        using var profile = Fixtures.Copy(21);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(21, 16), report.Steps.Select(s => s[..16]));
        string slot = store.SlotPath(SaveSlots.Quick);
        var bytes = File.ReadAllBytes(Path.Combine(slot, SaveFormat.Entities));
        var (economy, merchants) = SectionCodec.DecodeEconomy(bytes);
        Assert.Null(economy);   // the markets start at their targets when the world does
        Assert.Empty(merchants);
        var (_, created, containers, creatures, noises) = SectionCodec.DecodeEntitySection(bytes);
        Assert.Equal((2, 2, 2), (created.Length, containers.Length, noises.Length));   // everything schema 21 held is kept
        Assert.Equal(3, creatures.Length);
        Assert.NotNull(containers.Single(c => c.Key == "container.fixture_chest").Items.Single(i => i.Quality == 2).Notable);
    }

    [Fact]
    public void ASchema22SectionWithoutItsMerchants_OrWithAMarketTwice_IsCorrupt_NotDefaulted()
    {
        var snapshot = M2Fixtures.Historical.World(new Registry()).TakeSnapshot();
        var dto = MessagePack.MessagePackSerializer.Deserialize<EntitiesSectionDto>(
            SectionCodec.EncodeEntities(snapshot), MessagePack.MessagePackSerializerOptions.Standard);
        Assert.NotNull(dto.Economy);
        dto.Merchants = null;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("merchants", Assert.Throws<FormatException>(() => SectionCodec.DecodeEconomy(bytes)).Message);

        dto = MessagePack.MessagePackSerializer.Deserialize<EntitiesSectionDto>(
            SectionCodec.EncodeEntities(snapshot), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Economy!.Markets = new[] { dto.Economy.Markets[0], dto.Economy.Markets[0] };
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("twice", Assert.Throws<FormatException>(() => SectionCodec.DecodeEconomy(bytes)).Message);
    }

    // ── the quick bar, schema 22 -> 23 (WP9) ─────────────────────────────────

    [Fact]
    public void Schema22To23_GivesAnEmptyQuickBar_WithNothingCleared_AndKeepsEverythingElse()
    {
        using var profile = Fixtures.Copy(22);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(22, 16), report.Steps.Select(s => s[..16]));
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Player)));
        Assert.Equal(new string?[12], player.QuickBar);
        Assert.Empty(player.QuickBarCleared);
        Assert.NotNull(player.Inventory.Single(e => e.ItemId == M2Fixtures.Historical.SwordId).Notable);   // everything schema 22 held is kept
        Assert.Equal("call.scholar", player.Identity.CallId);
        Assert.Equal(PartyMembership.Hireling, Assert.Single(player.Companions).Membership);
        Assert.Single(player.Factions.Standing);
    }

    [Fact]
    public void ASchema23PlayerWithoutItsQuickBar_IsCorrupt_NotDefaulted_AndABadBarIsRefused()
    {
        var dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.QuickBar = null;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("quick bar", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);

        var empty = Array.Empty<string>();
        foreach (var (slots, cleared) in new[]
                 {
                     (new string?[7], empty),
                     (new string?[] { "item.potion.healing_draught", "item.potion.healing_draught", null, null, null, null, null, null }, empty),
                     (new string?[] { "recipe.alchemy.salve_minor", null, null, null, null, null, null, null }, empty),
                     (new string?[8], new[] { "recipe.alchemy.salve_minor" }),
                     (new string?[8], new[] { "item.potion.healing_draught", "item.potion.healing_draught" }),
                 })
        {
            dto.QuickBar = new QuickBarDto { Slots = slots, Cleared = cleared };
            bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
            Assert.Contains("quick bar", Assert.Throws<ArgumentException>(() => SectionCodec.DecodePlayer(bytes)).Message);
        }
    }

    // ── wear, schema 23 -> 24 (WP3b) ───────────────────────────────────────

    [Fact]
    public void Schema23To24_WearsNothing_AndKeepsEverythingElse()
    {
        using var profile = Fixtures.Copy(23);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(23, 16), report.Steps.Select(s => s[..16]));
        string slot = store.SlotPath(SaveSlots.Quick);
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(slot, SaveFormat.Player)));
        Assert.All(player.Inventory, e => Assert.Null(e.Wear));
        Assert.NotNull(player.Inventory.Single(e => e.ItemId == M2Fixtures.Historical.SwordId).Notable);   // what schema 23 held is kept
        Assert.Contains(player.QuickBar, id => id is not null);
        Assert.Single(player.QuickBarCleared);
        Assert.Equal(PartyMembership.Hireling, Assert.Single(player.Companions).Membership);
        var bytes = File.ReadAllBytes(Path.Combine(slot, SaveFormat.Entities));
        var (_, created, containers, creatures, noises) = SectionCodec.DecodeEntitySection(bytes);
        Assert.Equal((2, 2, 2, 3), (created.Length, containers.Length, noises.Length, creatures.Length));
        Assert.All(created, c => Assert.Null(c.Wear));
        Assert.All(containers.SelectMany(c => c.Items), i => Assert.Null(i.Wear));
        Assert.NotNull(containers.Single(c => c.Key == "container.fixture_chest").Items.Single(i => i.Quality == 2).Notable);
        var (economy, merchants) = SectionCodec.DecodeEconomy(bytes);
        Assert.NotNull(economy);
        Assert.Single(merchants);
    }

    [Fact]
    public void ASchema24WearOutOfRange_IsCorrupt()
    {
        var dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        Assert.NotNull(dto.Inventory[0].Wear);
        dto.Inventory[0].Wear!.Points = -1;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("invalid wear", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);

        dto.Inventory[0].Wear!.Points = 3;
        dto.Inventory[0].Wear!.MadeQuality = 3;
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("invalid wear", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);
    }

    // ── the party's sheets, schema 24 -> 25 (WP15b) ─────────────────────────

    [Fact]
    public void Schema24To25_GivesThePartyNoSheets_AndKeepsEverythingElse()
    {
        using var profile = Fixtures.Copy(24);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(24, 16), report.Steps.Select(s => s[..16]));
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Player)));
        Assert.Empty(player.PartySheets);
        var warden = Assert.Single(player.Companions);   // everything schema 24 held is kept
        Assert.Equal((PartyMembership.Hireling, 3), (warden.Membership, warden.Contract!.DaysServed));
        Assert.NotNull(player.Inventory.Single(e => e.ItemId == M2Fixtures.Historical.SwordId).Notable);
        Assert.NotNull(player.Inventory.Single(e => e.ItemId == M2Fixtures.Historical.SwordId).Wear);
        Assert.Contains(player.QuickBar, id => id is not null);
    }

    [Fact]
    public void ASchema25PlayerWithoutItsSheets_IsCorrupt_NotDefaulted()
    {
        var dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        Assert.NotNull(dto.PartySheets);
        dto.PartySheets = null;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("party sheets", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);
    }

    // ── NPC life, schema 25 -> 26 (WP12) ────────────────────────────────────

    [Fact]
    public void Schema25To26_HoldsNoLives_AndKeepsEverythingElse()
    {
        using var profile = Fixtures.Copy(25);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(25, 16), report.Steps.Select(s => s[..16]));
        var bytes = File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Entities));
        Assert.Null(SectionCodec.DecodeNpcLife(bytes));   // the NPCs begin their routines when the world starts
        var (economy, merchants) = SectionCodec.DecodeEconomy(bytes);
        Assert.Equal(3, economy!.Day);   // everything schema 25 held is kept
        Assert.Single(merchants);
        var (_, created, containers, creatures, noises) = SectionCodec.DecodeEntitySection(bytes);
        Assert.Equal((2, 2, 2, 3), (created.Length, containers.Length, noises.Length, creatures.Length));
        Assert.NotNull(containers.Single(c => c.Key == "container.fixture_chest").Items.Single(i => i.Quality == 2).Wear);
    }

    // ── the attack under way, schema 26 -> 27 (the soak's W4-SAVE-01) ──────

    [Fact]
    public void Schema26To27_HoldsNoAttack_AndKeepsEverythingElse()
    {
        using var profile = Fixtures.Copy(26);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(26, 16), report.Steps.Select(s => s[..16]));
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Player)));
        Assert.Null(player.Attack);   // a load started every attack over
        Assert.Single(player.PartySheets);   // everything schema 26 held is kept
        Assert.Equal(new UNNAMED.Domain.Items.ItemWear(40, 1), player.Inventory.Single(e => e.ItemId == M2Fixtures.Historical.SwordId).Wear);
    }

    [Fact]
    public void ASchema27AttackStrikingWhatIsNoCreature_IsRefused()
    {
        var bytes = SectionCodec.EncodePlayer(M2Fixtures.Historical.Player());
        var dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(bytes, MessagePack.MessagePackSerializerOptions.Standard);
        dto.Attack!.Struck = new[] { M2Fixtures.Historical.SwordId.Value };
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("attack", Assert.Throws<ArgumentException>(() => SectionCodec.DecodePlayer(bytes)).Message);
    }

    // ── hired lives and trips, schema 27 -> 28 (WP12b) ─────────────────────

    [Fact]
    public void Schema27To28_OnlyMovesTheVersion_AndKeepsEveryLife()
    {
        using var profile = Fixtures.Copy(27);
        var store = new SaveStore(profile.Root);
        var before = SectionCodec.DecodeNpcLife(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Entities)));
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(27, 16), report.Steps.Select(s => s[..16]));
        var after = SectionCodec.DecodeNpcLife(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Entities)));
        Assert.Equal(before!.Npcs.Keys, after!.Npcs.Keys);
        Assert.DoesNotContain(after.Npcs.Values, n => n.Condition == NpcCondition.Hired);
    }

    [Fact]
    public void AHiredLife_RoundTrips_AndAnUnknownConditionIsCorrupt()
    {
        var snapshot = M2Fixtures.Historical.World(new Registry()).TakeSnapshot();
        var bytes = SectionCodec.EncodeEntities(snapshot);
        var life = SectionCodec.DecodeNpcLife(bytes)!;
        Assert.Equal(NpcCondition.Hired, life.Npcs["routine.fixture.hunter"].Condition);
        Assert.Equal(NpcActivities.Away, life.Npcs["routine.fixture.trapper"].Activity);
        var dto = MessagePack.MessagePackSerializer.Deserialize<EntitiesSectionDto>(bytes, MessagePack.MessagePackSerializerOptions.Standard);
        dto.NpcLife!.Npcs.Single(n => n.Key == "routine.fixture.hunter").Condition = "employed";
        var bad = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("unknown condition", Assert.Throws<FormatException>(() => SectionCodec.DecodeNpcLife(bad)).Message);
    }

    [Fact]
    public void ASchema26LifeWithAMalformedRoute_OrHeldTwice_IsCorrupt_NotDefaulted()
    {
        var snapshot = M2Fixtures.Historical.World(new Registry()).TakeSnapshot();
        var dto = MessagePack.MessagePackSerializer.Deserialize<EntitiesSectionDto>(
            SectionCodec.EncodeEntities(snapshot), MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Equal(4, dto.NpcLife!.Npcs.Length);
        dto.NpcLife.Npcs[0].Route.Corners = new long[] { 1, 2, 3 };
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("route", Assert.Throws<FormatException>(() => SectionCodec.DecodeNpcLife(bytes)).Message);

        dto = MessagePack.MessagePackSerializer.Deserialize<EntitiesSectionDto>(
            SectionCodec.EncodeEntities(snapshot), MessagePack.MessagePackSerializerOptions.Standard);
        dto.NpcLife!.Npcs = new[] { dto.NpcLife.Npcs[0], dto.NpcLife.Npcs[0] };
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("twice", Assert.Throws<FormatException>(() => SectionCodec.DecodeNpcLife(bytes)).Message);
    }

    [Fact]
    public void Schema26_StoresLivesSparselyByCell_AndRoundTripsThem()
    {
        var world = M2Fixtures.Historical.World(new Registry());
        var bytes = SectionCodec.EncodeEntities(world.TakeSnapshot());
        var dto = MessagePack.MessagePackSerializer.Deserialize<EntitiesSectionDto>(bytes, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Equal(dto.NpcLife!.Npcs.OrderBy(n => n.Cell, StringComparer.Ordinal).ThenBy(n => n.Key, StringComparer.Ordinal).Select(n => n.Key),
            dto.NpcLife.Npcs.Select(n => n.Key));
        var decoded = SectionCodec.DecodeNpcLife(bytes)!;
        var h1 = new UNNAMED.Domain.CanonicalHasher();
        var h2 = new UNNAMED.Domain.CanonicalHasher();
        world.NpcLife!.AddTo(h1);
        decoded.AddTo(h2);
        Assert.Equal(h1.Finish(), h2.Finish());
    }

    [Fact]
    public void ASchema19NotableRecordOutOfRange_IsCorrupt()
    {
        var dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        Assert.NotNull(dto.Inventory[0].Notable);
        dto.Inventory[0].Notable!.Familiarity = 6;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("notable", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);

        dto.Inventory[0].Notable!.Familiarity = 1;
        dto.Inventory[0].Notable!.Provenance = "stolen";
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes));
    }

    // ── adventurers, schema 28 -> 29 (WP13) ─────────────────────────────────

    [Fact]
    public void Schema28To29_HoldsNoAdventurers_PromisesNoSlot_AndKeepsEverythingElse()
    {
        using var profile = Fixtures.Copy(28);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(28, 16), report.Steps.Select(s => s[..16]));
        var bytes = File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Entities));
        Assert.Null(SectionCodec.DecodeAdventurers(bytes));   // each settlement's slots fill when the world starts
        var life = SectionCodec.DecodeNpcLife(bytes)!;   // everything schema 28 held is kept
        Assert.Equal(80, life.Hour);
        Assert.Equal(4, life.Npcs.Count);
        Assert.Null(Assert.Single(life.Vacancies).Value.ArrivingName);
        Assert.Equal(3, SectionCodec.DecodeEconomy(bytes).Economy!.Day);
    }

    [Fact]
    public void ASchema29AdventurerWithAnUnknownCondition_OrHeldTwice_IsCorrupt_NotDefaulted()
    {
        var snapshot = M2Fixtures.Historical.World(new Registry()).TakeSnapshot();
        var dto = MessagePack.MessagePackSerializer.Deserialize<EntitiesSectionDto>(
            SectionCodec.EncodeEntities(snapshot), MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Equal(2, dto.Adventurers!.Agents.Length);
        dto.Adventurers.Agents[1].Condition = "asleep";
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("condition", Assert.Throws<FormatException>(() => SectionCodec.DecodeAdventurers(bytes)).Message);
        dto.Adventurers.Agents[1].Condition = "active";
        dto.Adventurers.Agents[1].Key = dto.Adventurers.Agents[0].Key;
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("twice", Assert.Throws<FormatException>(() => SectionCodec.DecodeAdventurers(bytes)).Message);
    }

    // ── factions, schema 19 -> 20 (WP6) ────────────────────────────────────

    [Fact]
    public void Schema19To20_GivesTheEmptyLedger_AndKeepsEverythingElse()
    {
        using var profile = Fixtures.Copy(19);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(19, 14), report.Steps.Select(s => s[..14]));
        Assert.StartsWith("schema 19 -> 20: the player gains a faction ledger", report.Steps[0]);
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Player)));
        Assert.Equal(UNNAMED.Domain.Factions.FactionLedger.Empty, player.Factions);
        Assert.NotNull(player.Inventory.Single(e => e.ItemId == M2Fixtures.Historical.SwordId).Notable);   // everything schema 19 held is kept
        Assert.Equal(3, player.Deeds.Length);
        Assert.Equal("call.scholar", player.Identity.CallId);
    }

    [Fact]
    public void ASchema20PlayerWithoutItsLedger_IsCorrupt_NotDefaulted_AndABrokenLedgerIsRefused()
    {
        PlayerDto Dto() => MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        byte[] Bytes(PlayerDto dto) => MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);

        var dto = Dto();
        dto.Factions = null;
        Assert.Contains("faction ledger", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(Bytes(dto))).Message);

        dto = Dto();
        dto.Factions!.Acts[0].Kind = "item_taken";        // named, not built
        Assert.Throws<ArgumentException>(() => SectionCodec.DecodePlayer(Bytes(dto)));
        dto = Dto();
        dto.Factions!.Knowledge[0].Act = 7;               // knows an act the log does not hold
        Assert.Throws<ArgumentException>(() => SectionCodec.DecodePlayer(Bytes(dto)));
        dto = Dto();
        dto.Factions!.Standing[0].Points = 0;             // a standing of 0 is never stored
        Assert.Throws<ArgumentException>(() => SectionCodec.DecodePlayer(Bytes(dto)));
        dto = Dto();
        dto.Factions!.NextActSeq = 1;                     // the next act is after the last
        Assert.Throws<ArgumentException>(() => SectionCodec.DecodePlayer(Bytes(dto)));
    }

    // ── deeds, schema 15 -> 16 (WP5, E-7) ───────────────────────────────────

    [Fact]
    public void Schema15To16_CountsNoDeed_AndKeepsEverythingElse()
    {
        using var profile = Fixtures.Copy(15);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(StepPrefixes(15, 14), report.Steps.Select(s => s[..14]));
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(store.SlotPath(SaveSlots.Quick), SaveFormat.Player)));
        Assert.Empty(player.Deeds);
        Assert.Equal(2, player.Waystones.Length);   // everything schema 15 held is kept
        Assert.Equal("waystone.fixture.ford", player.ReturnPoint);
        Assert.Equal(2, player.RoadsWalked.Length);
        Assert.Single(player.Companions);
    }

    [Fact]
    public void ASchema16PlayerWithoutItsDeeds_IsCorrupt_NotDefaulted_AndAnUnknownDeedIsRefused()
    {
        var dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Deeds = null;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("deed", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);

        dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Deeds![0].Deed = "stolen";
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("stolen", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);

        dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Deeds![0].Count = 0;
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("deed tally", Assert.Throws<ArgumentException>(() => SectionCodec.DecodePlayer(bytes)).Message);
    }

    [Fact]
    public void ASchema12PlayerWithoutCompanions_IsCorrupt_NotDefaulted()
    {
        var dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Companions = null;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("companions", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);

        dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Companions![0].Order = "guard";   // not an order Phase 1 gives
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("guard", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);

        dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Companions![0].TrailMm = new long[] { 1, 2, 3 };   // a trail is x, z pairs
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("pairs", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);
    }

    [Fact]
    public void ASchema11PlayerWithoutQuests_IsCorrupt_NotDefaulted()
    {
        var dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Quests = null;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("quests", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);

        dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Quests![0].Status = "abandoned";   // not a quest status Phase 1 knows
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("abandoned", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);
    }

    [Fact]
    public void ASchema10PlayerWithoutRelationships_IsCorrupt_NotDefaulted()
    {
        var dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Relationships = null;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("relationships", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);

        dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Conversations = null;
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Contains("conversations", Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message);
    }

    [Fact]
    public void ASchema9StackWithoutQuality_IsCorrupt_NotDefaulted()
    {
        var dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Inventory[0].Quality = null;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes));

        dto.Inventory[0].Quality = 3;   // not crude, standard, fine or masterwork
        bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);
        Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes));
    }

    [Fact]
    public void ASchema8EntitiesSectionWithoutCreatures_IsCorrupt_NotDefaulted()
    {
        var dto = MessagePack.MessagePackSerializer.Deserialize<EntitiesSectionDto>(
            SectionCodec.EncodeEntities(DeltaSnapshot.Empty), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Creatures = null;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);

        Assert.Throws<FormatException>(() => SectionCodec.DecodeEntitySection(bytes));
    }

    [Fact]
    public void ASchema7PlayerWithoutEffects_IsCorrupt_NotDefaulted()
    {
        var dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Effects = null;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);

        Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes));
    }

    [Fact]
    public void ASchema6EntitiesSectionWithoutContainers_IsCorrupt_NotDefaulted()
    {
        var dto = MessagePack.MessagePackSerializer.Deserialize<EntitiesSectionDto>(
            SectionCodec.EncodeEntities(DeltaSnapshot.Empty), MessagePack.MessagePackSerializerOptions.Standard);
        dto.Containers = null;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);

        Assert.Throws<FormatException>(() => SectionCodec.DecodeEntitySection(bytes));
    }

    [Fact]
    public void ASchema5PlayerWithoutFacing_IsCorrupt_NotDefaulted()
    {
        var dto = MessagePack.MessagePackSerializer.Deserialize<PlayerDto>(
            SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), MessagePack.MessagePackSerializerOptions.Standard);
        dto.FacingMdeg = null;
        var bytes = MessagePack.MessagePackSerializer.Serialize(dto, MessagePack.MessagePackSerializerOptions.Standard);

        Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes));
    }

    // ── 12.2 multi-hop ──────────────────────────────────────────────────────

    [Fact]
    public void Schema1_MigratesStepByStep_ThroughEveryVersion_ToTheCanonicalState()
    {
        var loaded = LoadFixture(1, Fixtures.Context(new Registry()));

        Assert.Equal(SaveFormat.SchemaVersion - 1, loaded.Report.Steps.Count);
        Assert.StartsWith("schema 1 -> 2:", loaded.Report.Steps[0]);
        Assert.StartsWith("schema 2 -> 3:", loaded.Report.Steps[1]);
        Assert.StartsWith("schema 3 -> 4:", loaded.Report.Steps[2]);
        Assert.StartsWith("schema 4 -> 5:", loaded.Report.Steps[3]);
        Assert.StartsWith("schema 5 -> 6:", loaded.Report.Steps[4]);
        Assert.StartsWith("schema 6 -> 7:", loaded.Report.Steps[5]);
        Assert.StartsWith("schema 7 -> 8:", loaded.Report.Steps[6]);
        Assert.StartsWith("schema 8 -> 9:", loaded.Report.Steps[7]);
        Assert.StartsWith("schema 9 -> 10:", loaded.Report.Steps[8]);
        Assert.StartsWith("schema 10 -> 11:", loaded.Report.Steps[9]);
        Assert.StartsWith("schema 11 -> 12:", loaded.Report.Steps[10]);
        Assert.StartsWith("schema 12 -> 13:", loaded.Report.Steps[11]);
        Assert.StartsWith("schema 13 -> 14:", loaded.Report.Steps[12]);
        Assert.StartsWith("schema 14 -> 15:", loaded.Report.Steps[13]);
        Assert.StartsWith("schema 15 -> 16:", loaded.Report.Steps[14]);
        Assert.StartsWith("schema 16 -> 17:", loaded.Report.Steps[15]);
        Assert.StartsWith("schema 17 -> 18:", loaded.Report.Steps[16]);
        Assert.StartsWith("schema 18 -> 19:", loaded.Report.Steps[17]);
        Assert.StartsWith("schema 19 -> 20:", loaded.Report.Steps[18]);
        Assert.StartsWith("schema 20 -> 21:", loaded.Report.Steps[19]);
        Assert.StartsWith("schema 21 -> 22:", loaded.Report.Steps[20]);
        Assert.StartsWith("schema 22 -> 23:", loaded.Report.Steps[21]);
        Assert.StartsWith("schema 23 -> 24:", loaded.Report.Steps[22]);
        Assert.StartsWith("schema 24 -> 25:", loaded.Report.Steps[23]);
        Assert.StartsWith("schema 25 -> 26:", loaded.Report.Steps[24]);
        Assert.StartsWith("schema 26 -> 27:", loaded.Report.Steps[25]);
        Assert.StartsWith("schema 27 -> 28:", loaded.Report.Steps[26]);
        Assert.StartsWith("schema 28 -> 29:", loaded.Report.Steps[27]);
        Assert.StartsWith("schema 29 -> 30:", loaded.Report.Steps[28]);
        Assert.StartsWith("schema 30 -> 31:", loaded.Report.Steps[29]);
        Assert.StartsWith("schema 31 -> 32:", loaded.Report.Steps[30]);
        Assert.StartsWith("schema 32 -> 33:", loaded.Report.Steps[31]);
        Assert.StartsWith("schema 33 -> 34:", loaded.Report.Steps[32]);
        Assert.StartsWith("schema 34 -> 35:", loaded.Report.Steps[33]);
        Assert.StartsWith("schema 35 -> 36:", loaded.Report.Steps[34]);
        Assert.StartsWith("schema 36 -> 37:", loaded.Report.Steps[35]);
        Assert.StartsWith("schema 37 -> 38:", loaded.Report.Steps[36]);
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

        Assert.Contains($"no registered migration chain from schema 1 to schema {SaveFormat.SchemaVersion}", Assert.Single(RefuseFixture(1, context).Report.Blockers));
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
            {
                if (!SaveFormat.ReservedSchemas.Contains(schema))
                    Assert.Contains(RefuseFixture(schema, Fixtures.Context(new Registry(), content)).Report.Blockers, b => b.Contains(Draught));
            }
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

    /// <summary>
    /// The writer's pack is historical: it is hashed as it was, and need not pass today's content checks, just as an
    /// old save need not have today's shape.
    /// </summary>
    [Fact]
    public void TheWritersContentIdentity_IsItsFixturePack()
    {
        var pack = new UNNAMED.Content.ContentLoader();
        pack.LoadAll(Path.Combine(Fixtures.Root, "content-0.1.6"));
        Assert.Equal(M2Fixtures.Historical.WriterContentHash, pack.ComputeContentHash());
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
