// UNNAMED Application tests - using the ring's switches actually answers the quest
// The satisfiability test proves every flag HAS a writer. This proves the writer works: a body standing at the
// prop, using it the way a key press does, sets the flag - and stands inside the place the objective reads it
// from. That last part is the whole reason the choice is a switch on the measure and not a line of Kera's: a
// world flag is per-cell, and she stands at the waystation.

using UNNAMED.Application;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit;

namespace UNNAMED.Application.Tests;

public class BarrowworksSwitchTests
{
    private const string Region = "region.barrowworks";
    private const string Quest = "quest.barrowworks.unfinished_measure";

    /// <summary>The reading props and the flag each answers, as the region declares them.</summary>
    private static readonly (string Switch, string Flag)[] Readings =
    [
        ("switch.barrowworks.notice", "world.barrowworks.withdrawal_notice_read"),
        ("switch.barrowworks.fitting_note", "world.barrowworks.fitting_note_read"),
        ("switch.barrowworks.diagram_rack", "world.barrowworks.diagram_rack_read"),
        ("switch.barrowworks.blind_ledger", "world.barrowworks.blind_store_read"),
    ];

    private static GameSession Boot(TempProfile profile) =>
        GameSession.Boot(new GameOptions(Path.Combine(Harness.RepoRoot(), "content"), profile.Root)
        {
            LockProfile = true,
            RegionId = Region,
            Barrowworks = true,
        });

    /// <summary>A switch's structure centre, in region millimetres.</summary>
    private static (long XMm, long ZMm) Centre(SwitchSite site) => site.Body switch
    {
        BoxBlocker box => (box.CenterXMm, box.CenterZMm),
        CircleBlocker circle => (circle.CenterXMm, circle.CenterZMm),
        _ => throw new Xunit.Sdk.XunitException($"switch {site.Key} stands on {site.Body.GetType().Name}, which has no centre"),
    };

    /// <summary>
    /// An arena standing on a switch's structure. The ring is opened as its own region, so its metres are placed
    /// through the hollow's frame, which is where region.barrowworks puts its origin.
    /// </summary>
    private static Arena StandingOn(GameSession session, SwitchSite site)
    {
        var (xMm, zMm) = Centre(site);
        var (ox, oz) = Harness.HollowOf(session.Setup);
        return Arena.OpenWith(session, session.Setup, (xMm / 1000.0 - ox, zMm / 1000.0 - oz), 0, []);
    }

    [Fact]
    public void EachReading_WhenUsed_SetsItsFlag()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);

        foreach (var (key, flag) in Readings)
        {
            var site = session.Setup.Layout.Switches.Single(s => s.Key == key);
            var arena = StandingOn(session, site);
            var cell = Simulation.CellOf(site);

            Assert.Equal(0, arena.Simulation.World.GetFlag(cell, flag));
            Assert.Null(arena.Submit(new InteractCommand(arena.Simulation.PlayerId, key)));
            Assert.NotEqual(0, arena.Simulation.World.GetFlag(cell, flag));
        }
    }

    /// <summary>
    /// Each switch stands in the same CELL as the place its objective reads the flag from. This is the property
    /// that matters and not a formality: a world flag is per-cell, so the same flag set in another cell reads as
    /// zero and the step is answered by nothing. It is the reason the choice is a switch on the measure rather
    /// than a line of Kera's - she stands at the waystation, a different cell entirely.
    /// </summary>
    [Fact]
    public void EachSwitch_StandsInTheCellItsObjectiveReads()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        var layout = session.Setup.Layout;

        var reads = session.Setup.Quests.Quests[Quest].Order
            .Select(o => o.Condition).OfType<WorldStateObjective>()
            .ToDictionary(c => c.FlagId, c => c.LocationId, StringComparer.Ordinal);

        foreach (var (key, flag) in Readings)
        {
            var site = layout.Switches.Single(s => s.Key == key);
            var place = layout.Locations.Single(l => l.Id == reads[flag]);
            var (xMm, zMm) = Centre(site);

            // Both are placed with the switch's own space, which is the space the flag is read in. A location is
            // region-local and carries no space of its own, so this is the only space either of them has.
            var here = CellKey.OfInterior(site.Space, xMm / 1000.0, zMm / 1000.0);
            var there = CellKey.OfInterior(site.Space, place.XMm / 1000.0, place.ZMm / 1000.0);
            Assert.Equal(there, here);
        }
    }

    /// <summary>
    /// The two answers are one structure and two switches, so a character can do either - and each sets only its
    /// own flag. One switch that set both would answer the question twice.
    /// </summary>
    [Fact]
    public void TheTwoAnswers_AreOneStructure_AndEachSetsOnlyItsOwnFlag()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);

        var preserve = session.Setup.Layout.Switches.Single(s => s.FlagId == "world.barrowworks.headworks_preserved");
        var dismantle = session.Setup.Layout.Switches.Single(s => s.FlagId == "world.barrowworks.headworks_dismantled");
        Assert.Equal("r12_headworks_measure", preserve.Body.Id);
        Assert.Equal("r12_headworks_measure", dismantle.Body.Id);

        var arena = StandingOn(session, preserve);
        Assert.Null(arena.Submit(new InteractCommand(arena.Simulation.PlayerId, preserve.Key)));

        var cell = Simulation.CellOf(preserve);
        Assert.NotEqual(0, arena.Simulation.World.GetFlag(cell, "world.barrowworks.headworks_preserved"));
        Assert.Equal(0, arena.Simulation.World.GetFlag(cell, "world.barrowworks.headworks_dismantled"));
    }

    [Fact]
    public void WithTheGateOff_ThereAreNoSwitchesToUse()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile);

        // The ring declares its switches; the starting frontier declares the Foldscar's three stones.
        Assert.DoesNotContain(session.Setup.Layout.Switches, s => s.Key.StartsWith("switch.barrowworks.", StringComparison.Ordinal));
    }
}
