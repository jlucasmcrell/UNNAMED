using UNNAMED.Domain;
using UNNAMED.M2Probe;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>
/// PERSISTENCE.md §9: T-01 (round trip, "no field silently defaulted"), T-03 (delta minimality and
/// rebase) and T-18 (slot merge), at the file level. World.Tests covers the same rules in memory.
/// </summary>
public class RoundTripTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 30, 45, TimeSpan.Zero);

    private readonly TempProfile _profile = new();
    private readonly SaveStore _store;

    public RoundTripTests() => _store = new SaveStore(_profile.Root, clock: () => Now);

    public void Dispose() => _profile.Dispose();

    private byte[] Section(string slot, string file) => File.ReadAllBytes(Path.Combine(_store.SlotPath(slot), file));

    private static BaselinePopulation Wolves(WorldDelta world, CellKey cell) =>
        world.Baseline(cell).Populations.Single(p => p.FamilyDefId == "creature.beast.wolf_grey");

    [Fact]
    public void T01_PlayerAndManifest_RoundTripEveryField()
    {
        var player = M2Fixtures.Player("Brannoc");
        var world = M2Fixtures.NewWorld(new Registry());
        _store.Save(M2Fixtures.Slot, SaveDocuments.Capture(world, player, "0.1.0", worldTick: 98_765,
            playtimeSeconds: 4_321.25, worldTimeAdvancedTicks: 1_234));

        var loaded = _store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry()));

        Assert.Equal(player.Digest, loaded.Player.Digest);
        Assert.Equal(player.Id, loaded.Player.Id);
        Assert.Equal((player.Name, player.XMm, player.YMm, player.ZMm), (loaded.Player.Name, loaded.Player.XMm, loaded.Player.YMm, loaded.Player.ZMm));
        Assert.Equal<InventoryEntry>(player.Inventory, loaded.Player.Inventory);   // as sequences: ImmutableArray equality is by reference

        var manifest = loaded.Manifest;
        Assert.Equal(SaveFormat.Current, manifest.SaveFormatVersion);
        Assert.Equal(SaveFormat.SchemaVersion, manifest.SchemaVersion);
        Assert.Equal("0.1.0", manifest.ContentVersion);
        Assert.Equal(M2Fixtures.ContentHash, manifest.ContentHash);
        Assert.Equal(CellBaselineGeneratorV1.Version, manifest.WorldgenVersion);
        Assert.Equal(M2Fixtures.Generator().WorldgenDigest, manifest.WorldgenDigest);
        Assert.Equal("0x5C1A9E7B4D2F0083", manifest.WorldSeed);
        Assert.Equal(98_765, manifest.WorldTick);
        Assert.Equal(1_234, manifest.WorldTimeAdvancedTicks);
        Assert.Equal(4_321.25, manifest.PlaytimeSeconds);
        Assert.Equal("2026-09-23T12:30:45Z", manifest.BuildTimestamp);
        Assert.Null(manifest.CommandLogSha256);
        Assert.Empty(manifest.Flags.QuarantinedSections);
    }

    [Fact]
    public void T01_T18_EntityDivergence_RoundTrips_AndTheDeadStayDead()
    {
        var world = M2Fixtures.NewWorld(new Registry());
        var cell = M2Fixtures.TenCells[2];
        var wolves = Wolves(world, cell);
        EntityId killed = world.KillOccupant(wolves.Slots[0].SlotKey);
        EntityId moved = world.MoveOccupant(wolves.Slots[1].SlotKey, 1_234, 5_678);
        world.SetPopulationAlive(cell, wolves.PopulationId, wolves.Target - 1);
        _store.Save(M2Fixtures.Slot, M2Fixtures.Document(world));

        var registry = new Registry();
        var loaded = _store.Load(M2Fixtures.Slot, M2Fixtures.Context(registry));

        Assert.True(loaded.IsComplete);
        var dead = loaded.World.Occupant(wolves.Slots[0].SlotKey);
        Assert.False(dead.Alive);                    // T-18: a killed generic NPC does not resurrect
        Assert.Equal(killed, dead.InstanceId);       // D-04: the ULID survives the round trip
        Assert.Equal(new OccupantView(wolves.Slots[1].SlotKey, moved, true, 1_234, 5_678), loaded.World.Occupant(wolves.Slots[1].SlotKey));
        Assert.Equal(wolves.Target - 1, loaded.World.GetPopulationAlive(cell, wolves.PopulationId));
        Assert.True(registry.Exists(killed));        // D-10: re-registered under the saved IDs
        Assert.True(registry.Exists(moved));
        Assert.Equal(M2Fixtures.WorldDigest(world), M2Fixtures.WorldDigest(loaded.World));

        // Load then save reproduces every section byte for byte: nothing was defaulted on the way in.
        _store.Save(SaveSlots.Manual("resaved"), M2Fixtures.Document(loaded.World));
        foreach (string file in new[] { SaveFormat.Cells, SaveFormat.Entities, SaveFormat.Player })
            Assert.Equal(Section(M2Fixtures.Slot, file), Section("manual_resaved", file));
    }

    [Fact]
    public void T03a_SavingTwiceWithoutMutation_YieldsIdenticalSections()
    {
        var world = M2Fixtures.NewWorld(new Registry());
        world.KillOccupant(Wolves(world, M2Fixtures.TenCells[4]).Slots[2].SlotKey);

        _store.Save(SaveSlots.Manual("first"), M2Fixtures.Document(world));
        _store.Save(SaveSlots.Manual("second"), M2Fixtures.Document(world));

        foreach (string file in new[] { SaveFormat.Cells, SaveFormat.Entities, SaveFormat.Player })
            Assert.Equal(Section("manual_first", file), Section("manual_second", file));
    }

    [Fact]
    public void T03b_MutateThenRevert_ShrinksBackToByteIdentical()
    {
        var registry = new Registry();
        var world = M2Fixtures.OldWorld(registry);
        _store.Save(SaveSlots.Manual("before"), M2Fixtures.Document(world));

        var cell = M2Fixtures.TenCells[8];
        string node = world.Baseline(cell).Nodes[0].NodeKey;
        var wolves = Wolves(world, cell);
        var wanderer = wolves.Slots[1];
        world.SetFlag(cell, "world.door.barn_open", 1);
        world.HarvestNode(cell, node, tick: 2_000);
        world.SetPopulationAlive(cell, wolves.PopulationId, wolves.Target - 1);
        world.KillOccupant(wolves.Slots[0].SlotKey);
        world.MoveOccupant(wanderer.SlotKey, wanderer.XCm + 50, wanderer.ZCm);
        _store.Save(SaveSlots.Manual("during"), M2Fixtures.Document(world));

        world.SetFlag(cell, "world.door.barn_open", 0);
        world.RegrowNode(cell, node);
        world.SetPopulationAlive(cell, wolves.PopulationId, wolves.Target);
        world.RestoreOccupant(wolves.Slots[0].SlotKey);
        world.MoveOccupant(wanderer.SlotKey, wanderer.XCm, wanderer.ZCm);   // walked back: no explicit restore
        _store.Save(SaveSlots.Manual("after"), M2Fixtures.Document(world));

        Assert.True(Section("manual_during", SaveFormat.Cells).Length > Section("manual_before", SaveFormat.Cells).Length);
        Assert.Equal(2, SectionCodec.DecodeEntities(Section("manual_during", SaveFormat.Entities)).Length);

        Assert.Equal(Section("manual_before", SaveFormat.Cells), Section("manual_after", SaveFormat.Cells));
        Assert.Equal(Section("manual_before", SaveFormat.Entities), Section("manual_after", SaveFormat.Entities));
        Assert.Empty(registry.GetAllEntities());     // the rebased occupants' identities retired too
    }

    [Fact]
    public void SaveBytes_AreTheSameUnderEveryCulture()
    {
        Cultures.Run("tr-TR", () => _store.Save(SaveSlots.Manual("turkish"), M2Fixtures.Document(M2Fixtures.NewWorld(new Registry()))));
        Cultures.Run("de-DE", () => _store.Save(SaveSlots.Manual("german"), M2Fixtures.Document(M2Fixtures.NewWorld(new Registry()))));

        foreach (string file in SaveFormat.CheckedFiles.Append(SaveFormat.IntegrityRoot))
            Assert.Equal(Section("manual_turkish", file), Section("manual_german", file));

        Cultures.Run("th-TH", () =>
        {
            var loaded = _store.Load(SaveSlots.Manual("turkish"), M2Fixtures.Context(new Registry()));
            Assert.True(loaded.IsComplete);
            Assert.Equal(321.5, loaded.Manifest.PlaytimeSeconds);
            Assert.Equal(M2Fixtures.WorldDigest(M2Fixtures.NewWorld(new Registry())), M2Fixtures.WorldDigest(loaded.World));
        });
    }

    [Fact]
    public void T03_AnUntouchedWorld_SavesEmptySections()
    {
        var world = new WorldDelta(M2Fixtures.Generator(), M2Fixtures.Tuple(), new Registry());
        foreach (var cell in M2Fixtures.TenCells)
            _ = world.EffectiveCellDigest(cell);

        _store.Save(M2Fixtures.Slot, M2Fixtures.Document(world));

        Assert.Empty(SectionCodec.DecodeCells(Section(M2Fixtures.Slot, SaveFormat.Cells)));
        Assert.Empty(SectionCodec.DecodeEntities(Section(M2Fixtures.Slot, SaveFormat.Entities)));
    }
}
