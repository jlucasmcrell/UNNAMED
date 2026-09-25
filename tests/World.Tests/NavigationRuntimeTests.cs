using System.Reflection;
using UNNAMED.Domain;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.World.Tests;

/// <summary>Navigation inside the running world (M7 design §3.20.2): tiles are cells, one owner, nothing shared between two worlds.</summary>
public class NavigationRuntimeTests
{
    /// <summary>Records every event published, and never throws.</summary>
    private sealed class RecordingBus : IEventBus
    {
        public List<object> Published { get; } = new();

        public void Subscribe<T>(Action<T> handler)
        {
        }

        public void Unsubscribe<T>(Action<T> handler)
        {
        }

        public void Publish<T>(T @event) => Published.Add(@event!);
    }

    private static readonly Lazy<SimulationSetup> Hollow = new(TestWorlds.HollowSetup);

    // N-W1
    [Fact]
    public void TileKeys_AreCellKeys()
    {
        foreach (string key in Hollow.Value.Layout.CellKeys)
        {
            var cell = CellKey.Parse(key);
            var tile = NavigationLayout.TileOf(cell);
            // Every corner of the cell falls in its tile, by the same integer floor the grid uses.
            long x0 = tile.Tx * NavConfig.TileMm, z0 = tile.Tz * NavConfig.TileMm;
            foreach (var (x, z) in new[] { (x0, z0), (x0 + 99_999, z0), (x0, z0 + 99_999), (x0 + 99_999, z0 + 99_999), (x0 + 50_000, z0 + 50_000) })
            {
                Assert.Equal(cell, CellKey.OfWorld(x / 1000.0, z / 1000.0));
                Assert.Equal(tile, new NavTileKey(NavGeometry.FloorDiv(x, NavConfig.TileMm), NavGeometry.FloorDiv(z, NavConfig.TileMm)));
            }
        }
        Assert.Equal(new[] { new NavTileKey(0, 0), new NavTileKey(1, 0), new NavTileKey(0, 1), new NavTileKey(1, 1) },
            NavigationLayout.TileKeys(Hollow.Value.Layout));

        // Points on the seams belong to the eastern and northern cells, as CellKey.OfWorld has them.
        foreach (long along in new long[] { 0, 25_000, 99_999, 100_000, 150_000 })
        {
            Assert.Equal(new NavTileKey(1, NavGeometry.FloorDiv(along, NavConfig.TileMm)), NavigationLayout.TileOf(CellKey.OfWorld(100.0, along / 1000.0)));
            Assert.Equal(new NavTileKey(NavGeometry.FloorDiv(along, NavConfig.TileMm), 1), NavigationLayout.TileOf(CellKey.OfWorld(along / 1000.0, 100.0)));
        }

        // A region at negative x: its last cell is tile -1, its first tile -20.
        var negative = CellKey.Parse("r_neg1_0:c_19_00");
        Assert.Equal(new NavTileKey(-1, 0), NavigationLayout.TileOf(negative));
        Assert.Equal(negative, CellKey.OfWorld(-50.0, 50.0));
        Assert.Equal(-1, NavGeometry.FloorDiv(-50_000, NavConfig.TileMm));
        Assert.Equal(new NavTileKey(-20, 0), NavigationLayout.TileOf(CellKey.Parse("r_neg1_0:c_00_00")));
    }

    // N-W2
    [Fact]
    public void TheNavigationSlice_HasOneOwner_AndBuildingPublishesNothing()
    {
        var bus = new RecordingBus();
        var simulation = TestWorlds.HollowSimulation(Hollow.Value, bus);

        Assert.Equal(nameof(NavigationSystem), simulation.SliceOwners[StateSlice.Navigation]);
        Assert.Equal(Enum.GetValues<StateSlice>().Length, simulation.SliceOwners.Count);
        Assert.Equal(4, simulation.Navigation.Grid.Tiles.Length);
        Assert.Equal(1, simulation.Navigation.Counters.FullBuilds);
        Assert.DoesNotContain(bus.Published, e => e.GetType().Name.Contains("Navigation", StringComparison.Ordinal)
            || e.GetType().Name.Contains("Route", StringComparison.Ordinal));
    }

    // N-W3
    [Fact]
    public void TwoSimulations_ShareNoNavigationState()
    {
        var setup = Hollow.Value;
        // The second world has one more structure: a rock on the crossing, where the four cells meet.
        var rock = new CircleBlocker("rock_n_w3", 100_000, 100_000, 1_500, 2_000);
        var edited = setup with { Layout = setup.Layout with { Space = setup.Layout.Space with { Blockers = setup.Layout.Space.Blockers.Add(rock) } } };
        var a = TestWorlds.HollowSimulation(setup, new RecordingBus());
        var b = TestWorlds.HollowSimulation(edited, new RecordingBus());

        Assert.NotEqual(a.Navigation.Grid.Digest(), b.Navigation.Grid.Digest());
        var systemA = SystemOf(a);
        var systemB = SystemOf(b);
        Assert.NotSame(systemA.Scratch, systemB.Scratch);
        Assert.NotSame(systemA.Counters, systemB.Counters);

        // Work in one world is counted in that world alone, and changes nothing in either grid.
        string digestA = a.Navigation.Grid.Digest(), digestB = b.Navigation.Grid.Digest();
        Assert.True(systemA.Reachable(new NavAgent(0, true), new NavPoint(30_000, 150_000), new NavPoint(61_600, 139_600)));
        Assert.Equal(1, a.Navigation.Counters.Plans);
        Assert.Equal(0, b.Navigation.Counters.Plans);
        Assert.Equal(digestA, a.Navigation.Grid.Digest());
        Assert.Equal(digestB, b.Navigation.Grid.Digest());
    }

    private static NavigationSystem SystemOf(Simulation simulation) =>
        (NavigationSystem)typeof(Simulation).GetField("_navigation", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(simulation)!;
}
