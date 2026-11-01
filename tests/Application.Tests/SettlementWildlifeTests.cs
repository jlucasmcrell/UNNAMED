using System.Collections.Immutable;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

[Collection("Simulation timing budgets")]
public class SettlementWildlifeTests
{
    [Theory]
    [InlineData("region.starting_frontier", 455, 745)]
    [InlineData("region.starting_frontier", 972, 670)]
    [InlineData("region.otherhome", 1612, 782)]
    [InlineData("region.sedge_levels", 5000, 1080)]
    public void ADayOfTicksKeepsWildlifeOutsideEverySettlement(string region, double x, double z)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile, region);
        var footprint = session.Setup.Layout.Locations.Select(l => l.SettlementFootprint).OfType<Blocker>()
            .Single(t => t.DistanceTo(x * 1000, z * 1000) == 0);
        var push = footprint.Separation(x * 1000, z * 1000, 2_000)!.Value;
        long outsideX = (long)(x * 1000 + push.Dx), outsideZ = (long)(z * 1000 + push.Dz);
        // Keep a live patrol at each town edge, including towns whose ordinary wildlife is outside tier A.
        var probe = new SpawnSite("spawn.test.settlement_edge", outsideX, outsideZ, 0,
            ImmutableArray.Create(new SpawnMember(Arena.Wolf, "roamer")))
        { Route = ImmutableArray.Create((outsideX, outsideZ), ((long)(x * 1000), (long)(z * 1000))) };
        var setup = session.Setup with { Combat = session.Setup.Combat with { Spawns = session.Setup.Combat.Spawns.Add(probe) } };
        var arena = Arena.OpenCreatures(session, setup, (x, z), 0, Array.Empty<(string, double, double, string)>(),
            keepSpawns: true, origin: Harness.Region);
        var towns = session.Setup.AllLayouts.SelectMany(l => l.Locations).Select(l => l.SettlementFootprint).OfType<Blocker>().ToArray();
        Assert.NotEmpty(towns);
        Assert.Contains(arena.Simulation.Creatures, c => c.Alive && c.Temperament == Temperament.Hostile);
        var initial = arena.Simulation.Creatures.ToDictionary(c => c.Key, c => c.Body);
        bool moved = false;
        for (long tick = 0; tick <= session.Setup.Clock.TicksPerDay; tick++)
        {
            foreach (var creature in arena.Simulation.Creatures)
            {
                bool wildlife = session.Setup.Combat.Creatures[creature.DefId].Family is "beast" or "grazer"
                    && creature.RoleId != "yard_fowl";
                if (!creature.Alive || !wildlife) continue;
                long radius = session.Setup.Combat.Creatures[creature.DefId].RadiusMm;
                foreach (var town in towns)
                    if (town.Separation(creature.Body.XMm, creature.Body.ZMm, radius) is not null)
                        Assert.Fail($"{creature.Key} at {creature.Body} overlaps {town.Id} on tick {tick}");
                moved |= creature.Body != initial[creature.Key];
            }
            if (tick < session.Setup.Clock.TicksPerDay) arena.Tick();
        }
        Assert.True(moved, "The soak must exercise live movement, not only offscreen creatures at home.");
    }

    [Fact]
    public void APatrolCannotCrossSettledGroundEvenWhenBothWaypointsAreOutside()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile, "region.starting_frontier");
        var town = new BoxBlocker("test.settlement", 700_000, 700_000, 710_000, 710_000, 0);
        var flat = new WalkSpace(400_000, 600_000, 1100_000, 800_000,
            new TerrainGrid(400_000, 600_000, 700_000, 2, 2, new long[] { 0, 0, 0, 0 }), ImmutableArray<Blocker>.Empty);
        var layout = session.Setup.Layout with { Space = flat, Locations = ImmutableArray.Create(
            new LocationSite("test.settlement", 705_000, 705_000, 10_000, 0) { SettlementFootprint = town }) };
        var spawn = new SpawnSite("spawn.test.patrol", 698_000, 705_000, 0,
            ImmutableArray.Create(new SpawnMember(Arena.Wolf, "roamer")))
        { Route = ImmutableArray.Create((698_000L, 705_000L), (712_000L, 705_000L)) };
        var setup = session.Setup with { Layout = layout, Combat = session.Setup.Combat with { Spawns = ImmutableArray.Create(spawn) } };
        var arena = Arena.OpenCreatures(session, setup, (705, 650), 0, Array.Empty<(string, double, double, string)>(),
            keepSpawns: true, origin: Harness.Region);
        var open = setup with { Layout = layout with { Locations = layout.Locations.Select(l => l with { SettlementFootprint = null }).ToImmutableArray() } };
        var control = Arena.OpenCreatures(session, open, (705, 650), 0, Array.Empty<(string, double, double, string)>(),
            keepSpawns: true, origin: Harness.Region);
        bool crossedWithoutExclusion = false;
        for (int tick = 0; tick < 1200; tick++)
        {
            arena.Tick();
            var wolf = arena.Simulation.Creatures.Single();
            Assert.Null(town.Separation(wolf.Body.XMm, wolf.Body.ZMm, setup.Combat.Creatures[Arena.Wolf].RadiusMm));
            control.Tick();
            var unprotected = control.Simulation.Creatures.Single();
            crossedWithoutExclusion |= town.DistanceTo(unprotected.Body.XMm, unprotected.Body.ZMm) == 0;
        }
        Assert.True(crossedWithoutExclusion, "The same patrol must cross this ground when the settlement exclusion is absent.");
    }

    [Fact]
    public void ALoadedWolfAlreadyInTheMarketReturnsToItsSafeHome()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile, "region.otherhome");
        var spawn = new SpawnSite("spawn.test.old_wolf", 1612_000, 782_000, 0,
            ImmutableArray.Create(new SpawnMember(Arena.Wolf, "hunter")));
        var setup = session.Setup with { Combat = session.Setup.Combat with { Spawns = ImmutableArray.Create(spawn) } };
        var oldSetup = setup with { Layout = setup.Layout with {
            Locations = setup.Layout.Locations.Select(l => l with { SettlementFootprint = null }).ToImmutableArray() } };
        var arena = Arena.OpenCreatures(session, oldSetup, (1612, 770), 0, Array.Empty<(string, double, double, string)>(),
            keepSpawns: true, origin: Harness.Region);
        arena.Tick(2);
        var wolf = arena.Simulation.Creatures.Single();
        Assert.Contains(setup.Layout.Locations, l => l.SettlementFootprint?.DistanceTo(wolf.Body.XMm, wolf.Body.ZMm) == 0);
        var save = SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content, arena.Simulation.WorldTick, 0);
        var store = new SaveStore(profile.Root);
        string slot = SaveSlots.Manual("wolf_in_town");
        store.Save(slot, save);
        var loaded = store.Load(slot, new LoadContext(session.Generator, session.Content, new UNNAMED.EntityRegistry.EntityRegistry()));
        var resumed = Arena.Resume(setup, loaded);
        Assert.DoesNotContain(resumed.Simulation.Creatures, c => setup.Layout.Locations.Any(l =>
            l.SettlementFootprint?.Separation(c.Body.XMm, c.Body.ZMm, setup.Combat.Creatures[c.DefId].RadiusMm) is not null));
    }
}
