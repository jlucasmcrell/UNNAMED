using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.World.Tests;

/// <summary>The sparse delta: diff, rebase, slot-keyed merge, and invariant validation.</summary>
public class WorldDeltaTests
{
    private const string Door = "world.door.cellar_open";

    [Fact]
    public void UntouchedWorld_CostsNothing()
    {
        var world = TestWorlds.NewWorld();
        _ = world.Baseline(TestWorlds.Home);   // generating a baseline is not a divergence

        var snapshot = world.TakeSnapshot();
        Assert.Empty(snapshot.Cells);
        Assert.Empty(snapshot.Entities);
    }

    [Fact]
    public void Flag_IsRecorded_ThenRebasedAwayWhenReset()
    {
        var world = TestWorlds.NewWorld();
        world.SetFlag(TestWorlds.Home, Door, 1);

        var cell = Assert.Single(world.TakeSnapshot().Cells);
        Assert.Equal("r_0_0:c_07_11", cell.CellKey);
        Assert.Equal(new[] { "flags" }, cell.DirtyReasons);
        Assert.Equal(1, Assert.Single(cell.Flags).Value);

        world.SetFlag(TestWorlds.Home, Door, 0);
        Assert.Empty(world.TakeSnapshot().Cells);
    }

    [Fact]
    public void Flag_MustBeAWorldFlagDefinition() =>
        Assert.Throws<ArgumentException>(() => TestWorlds.NewWorld().SetFlag(TestWorlds.Home, "door_open", 1));

    [Fact]
    public void Node_HarvestThenRegrow_RebasesAway()
    {
        var world = TestWorlds.NewWorld();
        string node = world.Baseline(TestWorlds.Home).Nodes[0].NodeKey;

        world.HarvestNode(TestWorlds.Home, node, tick: 100);
        var harvest = Assert.Single(Assert.Single(world.TakeSnapshot().Cells).HarvestedNodes);
        Assert.Equal((node, 100L, 1), (harvest.NodeKey, harvest.LastHarvestTick, harvest.HarvestSeq));

        world.RegrowNode(TestWorlds.Home, node);
        Assert.Empty(world.TakeSnapshot().Cells);
    }

    [Fact]
    public void Node_InvalidMutations_Throw()
    {
        var world = TestWorlds.NewWorld();
        string node = world.Baseline(TestWorlds.Home).Nodes[0].NodeKey;
        world.HarvestNode(TestWorlds.Home, node, 1);

        Assert.Throws<InvalidOperationException>(() => world.HarvestNode(TestWorlds.Home, "node.r_0_0:c_07_11.99", 2));
    }

    [Fact]
    public void Node_HarvestedAgain_CountsItsHarvests()
    {
        // M3f: a node with charges is worked more than once before it is spent; each harvest replaces its one record.
        var world = TestWorlds.NewWorld();
        string node = world.Baseline(TestWorlds.Home).Nodes[0].NodeKey;
        Assert.Null(world.NodeRecord(TestWorlds.Home, node));

        world.HarvestNode(TestWorlds.Home, node, 10);
        world.HarvestNode(TestWorlds.Home, node, 25);

        Assert.Equal(new NodeHarvest(node, 25, 2), world.NodeRecord(TestWorlds.Home, node));
        Assert.Single(world.TakeSnapshot().Cells.Single(c => c.CellKey == TestWorlds.Home.ToString()).HarvestedNodes);
    }

    [Fact]
    public void Population_AtTarget_IsBaseline()
    {
        var world = TestWorlds.NewWorld();
        string wolves = "pop.r_0_0.c_07_11.wolves";

        world.SetPopulationAlive(TestWorlds.Home, wolves, 3);
        Assert.Equal(3, world.GetPopulationAlive(TestWorlds.Home, wolves));
        Assert.Single(world.TakeSnapshot().Cells);

        world.SetPopulationAlive(TestWorlds.Home, wolves, 5);
        Assert.Empty(world.TakeSnapshot().Cells);
        Assert.Throws<ArgumentOutOfRangeException>(() => world.SetPopulationAlive(TestWorlds.Home, wolves, 8));
    }

    [Fact]
    public void KillingAnOccupant_PromotesItWithARegistryId()
    {
        var registry = new Registry();
        var world = new WorldDelta(TestWorlds.Generator(), TestWorlds.Seed, registry);
        string slot = FirstWolfSlot(world);

        EntityId id = world.KillOccupant(slot);

        Assert.Equal(EntityKind.Creature, id.Kind);
        Assert.True(registry.Exists(id));
        Assert.False(world.Occupant(slot).Alive);
        var record = Assert.Single(world.TakeSnapshot().Entities);
        Assert.Equal((id, slot, false, (int?)null), (record.InstanceId, record.SlotKey, record.Alive, record.XCm));
        Assert.Equal(EntityDeltaRecord.DirtyAlive, record.DirtyMask);
    }

    [Fact]
    public void RestoringAnOccupant_RetiresItsRecordAndIdentity()
    {
        var registry = new Registry();
        var world = new WorldDelta(TestWorlds.Generator(), TestWorlds.Seed, registry);
        string slot = FirstWolfSlot(world);
        EntityId id = world.KillOccupant(slot);

        world.RestoreOccupant(slot);

        Assert.Empty(world.TakeSnapshot().Entities);
        Assert.False(registry.Exists(id));
        Assert.Null(world.Occupant(slot).InstanceId);
    }

    [Fact]
    public void RoundTrip_PreservesEveryTouchedCell()
    {
        var world = TestWorlds.NewWorld();
        var other = CellKey.Parse("r_0_0:c_03_04");
        world.SetFlag(TestWorlds.Home, Door, 7);
        world.HarvestNode(other, world.Baseline(other).Nodes[0].NodeKey, 55);
        world.SetPopulationAlive(other, "pop.r_0_0.c_03_04.deer", 1);
        world.KillOccupant(FirstWolfSlot(world));
        world.MoveOccupant($"{other}.pop.r_0_0.c_03_04.wolves.02", 1234, 5678);

        var snapshot = world.TakeSnapshot();
        var restored = WorldDelta.FromSnapshot(TestWorlds.Generator(), TestWorlds.Seed, new Registry(), snapshot, out var rejected);

        Assert.Empty(rejected);
        foreach (var cell in new[] { TestWorlds.Home, other })
            Assert.Equal(world.EffectiveCellDigest(cell), restored.EffectiveCellDigest(cell));
    }

    [Fact]
    public void KilledCreature_DoesNotResurrect_OnLoad()
    {
        // RK-P10: without a slot-keyed merge the spawner re-creates the member the delta says is dead.
        var world = TestWorlds.NewWorld();
        string slot = FirstWolfSlot(world);
        EntityId id = world.KillOccupant(slot);

        var restored = WorldDelta.FromSnapshot(TestWorlds.Generator(), TestWorlds.Seed, new Registry(), world.TakeSnapshot(), out _);

        var occupant = restored.Occupant(slot);
        Assert.False(occupant.Alive);
        Assert.Equal(id, occupant.InstanceId);   // one occupant: the persisted individual, not a fresh spawn
    }

    [Fact]
    public void EntityOutsideAnyCellRecord_IsStillApplied()
    {
        // §5.6 cross-cell divergence: an entity whose host cell is pristine has no cell record.
        var world = TestWorlds.NewWorld();
        world.MoveOccupant(FirstWolfSlot(world), 10, 20);
        var snapshot = world.TakeSnapshot();
        Assert.Empty(snapshot.Cells);

        var restored = WorldDelta.FromSnapshot(TestWorlds.Generator(), TestWorlds.Seed, new Registry(), snapshot, out _);
        Assert.Equal((10, 20), (restored.Occupant(FirstWolfSlot(world)).XCm, restored.Occupant(FirstWolfSlot(world)).ZCm));
    }

    [Fact]
    public void InvalidRecords_AreRejectedWithReasons_AndValidOnesStillLoad()
    {
        var world = TestWorlds.NewWorld();
        string wolf = FirstWolfSlot(world);
        world.SetFlag(TestWorlds.Home, Door, 1);
        var good = world.TakeSnapshot();

        string Baseline(string cell) => TestWorlds.Generator().Generate(TestWorlds.Seed, CellKey.Parse(cell)).Digest;
        var badCell = new CellDeltaRecord("r_0_0:c_01_01", Baseline("r_0_0:c_01_01"), ImmutableArray.Create("nodes"),
            ImmutableArray<KeyValuePair<string, long>>.Empty,
            ImmutableArray.Create(new NodeHarvest("node.r_0_0:c_01_01.99", 1, 1)),
            ImmutableArray<KeyValuePair<string, int>>.Empty);
        var id = EntityId.NewId(EntityKind.Creature);
        var wrongFamily = new EntityDeltaRecord(id, wolf, 0, "creature.beast.deer", false, null, null, Baseline("r_0_0:c_07_11"));
        var noSuchSlot = new EntityDeltaRecord(EntityId.NewId(EntityKind.Creature), "r_0_0:c_07_11.pop.r_0_0.c_07_11.wolves.42", 0,
            "creature.beast.wolf_grey", false, null, null);

        var snapshot = new DeltaSnapshot(good.Cells.Add(badCell), ImmutableArray.Create(wrongFamily, noSuchSlot));
        var restored = WorldDelta.FromSnapshot(TestWorlds.Generator(), TestWorlds.Seed, new Registry(), snapshot, out var rejected);

        Assert.Equal(3, rejected.Length);
        Assert.Contains(rejected, r => r.Key == "r_0_0:c_01_01" && r.Reason.Contains("does not exist"));
        Assert.Contains(rejected, r => r.Key == wolf && r.Reason.Contains("does not match"));
        Assert.Contains(rejected, r => r.Reason.Contains("slot does not exist"));
        Assert.Equal(1, restored.GetFlag(TestWorlds.Home, Door));   // the valid record still applied
    }

    [Fact]
    public void EverySnapshotRecord_NamesTheBaselineItWasMadeAgainst()
    {
        var world = TestWorlds.NewWorld();
        world.SetFlag(TestWorlds.Home, Door, 1);
        world.KillOccupant(FirstWolfSlot(world));

        var snapshot = world.TakeSnapshot();

        string home = world.Baseline(TestWorlds.Home).Digest;
        Assert.Equal(home, Assert.Single(snapshot.Cells).BaselineHash);
        Assert.Equal(home, Assert.Single(snapshot.Entities).BaselineHash);
    }

    /// <summary>
    /// M2b's last line of defence: whatever the loader decided, a record whose baseline_hash is not the
    /// regenerated baseline is rejected with a reason, never applied to the wrong baseline.
    /// </summary>
    [Fact]
    public void ARecordMadeAgainstAnotherBaseline_IsRejected_NeverApplied()
    {
        var world = TestWorlds.NewWorld();
        world.SetFlag(TestWorlds.Home, Door, 1);
        string wolf = FirstWolfSlot(world);
        world.KillOccupant(wolf);
        var snapshot = world.TakeSnapshot();

        // The same records, loaded under a generator whose baseline for this cell differs.
        var restored = WorldDelta.FromSnapshot(TestWorlds.Generator(wolfTarget: 4), TestWorlds.Seed, new Registry(), snapshot, out var rejected);

        Assert.Equal(2, rejected.Length);
        Assert.All(rejected, r => Assert.Contains("baseline_hash", r.Reason));
        Assert.Equal(0, restored.GetFlag(TestWorlds.Home, Door));
        Assert.True(restored.Occupant(wolf).Alive);
    }

    private static string FirstWolfSlot(WorldDelta world) =>
        world.Baseline(TestWorlds.Home).Populations.Single(p => p.FamilyDefId == "creature.beast.wolf_grey").Slots[0].SlotKey;
}
