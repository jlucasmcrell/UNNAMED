// UNNAMED Application tests - the ring's sites exist in a FRONTIER run with the gate on (B6)
// Review 3's finding: every test and screenshot booted `--region region.barrowworks` directly, so nothing proved the
// run a player actually gets. `SimulationSetup.Interior` was read by no production code, and in a frontier run the
// simulation had 0 ring switches, doors and places and 0 tier cells - so the Custodian never left Idle and Kera's
// offer, which reads intake_hall, could never be shown.
//
// These boot `region.starting_frontier` with barrowworks=1 and assert the ring's authored sites are visible.

using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using Xunit;

namespace UNNAMED.Application.Tests;

public class CoLoadedSitesTests
{
    private const string Ring = "space.barrowworks";

    private static GameSession Frontier(TempProfile profile, bool barrowworks)
    {
        var session = Harness.Boot(profile, barrowworks: barrowworks);
        session.NewGame(Harness.Named("Wanderer"), seed: 42);
        return session;
    }

    [Fact]
    public void TheRingsSwitchesExistInAFrontierRun()
    {
        using var profile = new TempProfile();
        var session = Frontier(profile, barrowworks: true);

        var ring = session.Setup.Interior;
        Assert.NotNull(ring);
        Assert.NotEmpty(ring!.Switches);

        // Every one of the ring's switches is a switch the simulation offers - which is what made the quest
        // unplayable: with one layout none of them existed in the run a player gets.
        var offered = session.Simulation!.Switches.Select(s => s.Site.Key).ToHashSet(StringComparer.Ordinal);
        Assert.All(ring.Switches, s => Assert.Contains(s.Key, offered));
    }

    [Fact]
    public void TheRingsDoorsAndPlacesExistInAFrontierRun()
    {
        using var profile = new TempProfile();
        var session = Frontier(profile, barrowworks: true);
        var ring = session.Setup.Interior!;

        var doors = session.Simulation!.Doors.Select(d => d.Site.Key).ToHashSet(StringComparer.Ordinal);
        Assert.All(ring.Doors, d => Assert.Contains(d.Key, doors));
        Assert.NotEmpty(ring.Locations);
    }

    [Fact]
    public void TheRingsCellsHaveTiers()
    {
        using var profile = new TempProfile();
        var session = Frontier(profile, barrowworks: true);
        var ring = session.Setup.Interior!;

        // A space with no tier cells has no tiers, so a creature's AI - which keys on the tier of the cell it stands
        // in - never leaves Idle. That is exactly what the Custodian did.
        // Every cell the ring declares is a cell the run holds, and a cell in the ring is in the ring's space.
        var held = session.Simulation!.HeldCells.Select(c => c.ToString()).ToHashSet(StringComparer.Ordinal);
        Assert.All(ring.CellKeys, k => Assert.Contains(CellKey.Parse(k).ToString(), held));
        Assert.Contains(ring.CellKeys, k => CellKey.Parse(k).Space != SpaceId.Exterior);
    }

    [Fact]
    public void WithTheGateOff_NoneOfTheRingIsVisible()
    {
        using var profile = new TempProfile();
        var session = Frontier(profile, barrowworks: false);

        Assert.Null(session.Setup.Interior);
        Assert.All(session.Simulation!.Switches, s => Assert.DoesNotContain("barrowworks", s.Site.Key, StringComparison.Ordinal));
        Assert.All(session.Simulation!.Doors, d => Assert.DoesNotContain("barrowworks", d.Site.Key, StringComparison.Ordinal));
    }
}
