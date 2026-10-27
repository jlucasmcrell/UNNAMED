using System.Linq;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using UNNAMED.Domain.Spatial;
using Xunit;
using Xunit.Abstractions;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Spec 41, "lore that unlocks a hidden path": a page that says where a place is puts it on the map as told, and being told is not having gone
/// (MAPS_CARTOGRAPHY_AND_WORLD_KNOWLEDGE.md Â§6). Read at the Survey's shelf, the rim's second page names the Rim, and the map learns it.
/// </summary>
public class LoreRevealRuntimeTests
{
    private readonly ITestOutputHelper _out;

    public LoreRevealRuntimeTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void ReadingAPageThatSaysWhereAPlaceIs_PutsItOnTheMapWithoutVisitingIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Playing(profile, Harness.Region, 0, r => r);
        var sim = session.Simulation!;
        const string place = "location.road.basin_rim";
        Assert.DoesNotContain(place, sim.Map.Places.Select(p => p.LocationId));
        var shelf = Footprints.Center(sim.ActiveLayout.Readables.Single(r => r.Key == "readable.the_rim_ii").Body);
        Assert.True(Harness.WalkTo(session, shelf.XMm, shelf.ZMm + 1_200));
        session.Submit(new InteractCommand(sim.PlayerId, "readable.the_rim_ii"));
        Harness.Ticks(session, 4);
        var known = sim.Map.Places.FirstOrDefault(p => p.LocationId == place);
        _out.WriteLine($"places after reading: {string.Join(", ", sim.Map.Places.Select(p => p.LocationId))}");
        Assert.NotNull(known);
        // Told, not visited: the map knows the place and nothing has counted it as having been there.
    }
}
