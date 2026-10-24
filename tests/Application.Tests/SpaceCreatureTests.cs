// UNNAMED Application tests - creatures belong to one space (B1)
// A ring spawn's creatures are in the ring. They must not be offered to a player standing in the Ashen Hollow, and
// with the gate off they must not exist at all. Ground and walls come from the creature's own space.

using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using Xunit;

namespace UNNAMED.Application.Tests;

public class SpaceCreatureTests
{
    private const string Ring = "space.barrowworks";

    [Fact]
    public void RingSpawns_CarryTheRingsSpace()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile, barrowworks: true);

        var ring = session.Setup.Combat.Spawns.Where(s => s.Space == SpaceId.Parse(Ring)).ToList();
        Assert.NotEmpty(ring);
        // And the region's own spawns are not the ring's.
        Assert.All(session.Setup.Combat.Spawns.Where(s => !ring.Contains(s)), s => Assert.NotEqual(SpaceId.Parse(Ring), s.Space));
    }

    [Fact]
    public void RingCreatures_AreNotSeenFromTheExterior()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile, barrowworks: true);
        session.NewGame(Harness.Named("Wanderer"), seed: 42);

        // By spawner, not by kind: the ring's spiders and husk are the frontier's own species.
        var ringSpawns = session.Setup.Combat.Spawns.Where(s => s.Space == SpaceId.Parse(Ring))
            .Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        var seen = session.Simulation!.Creatures.Select(c => c.Key[..c.Key.LastIndexOf('#')]).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(ringSpawns);
        Assert.Empty(seen.Intersect(ringSpawns));
    }

    [Fact]
    public void WithTheGateOff_NoRingCreatureIsSpawned()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        Assert.DoesNotContain(session.Setup.Combat.Spawns, s => s.Space == SpaceId.Parse(Ring));
    }
}
