// UNNAMED Application tests - a switch's flag lands in the cell the quest reads it from (review B2)
// These used to compare the switch's cell with itself: both sides were computed from `site.Space`, which nothing
// ever set, so the test passed while every reading in the ring was unsatisfiable. The write now goes through the
// real interaction path and the expected cell comes from the objective's own location in the layout's space -
// which is what the quest's CellOfLocation resolves to. On the old code these fail, because the write went to an
// exterior cell and the read to a space-qualified one.

using UNNAMED.Application;
using System.Collections.Immutable;
using UNNAMED.Domain.Spatial;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;
using UNNAMED.Domain.Quests;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit;

namespace UNNAMED.Application.Tests;

public class BarrowworksSwitchTests
{
    [Theory]
    [InlineData("headworks_preserve", "headworks_dismantle", 0)]
    [InlineData("headworks_dismantle", "headworks_preserve", 2)]
    public void APreparedChoice_ConsumesOneWedgeAndPaysAtMostOnce(string first, string other, int ingots)
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        var site = session.Setup.Layout.Switches.Single(s => s.Key == "switch.barrowworks."+first);
        var arena = StandingOn(session, site);
        var snapshot = arena.Simulation.World.TakeSnapshot();
        var cells = snapshot.Cells.ToDictionary(c => c.CellKey, StringComparer.Ordinal);
        foreach (var requirements in site.Requirements.GroupBy(r => QuestCell(session, r.LocationId)))
        {
            string key = requirements.Key.ToString();
            var original = cells.GetValueOrDefault(key) ?? new CellDeltaRecord(key, arena.Simulation.World.Baseline(requirements.Key).Digest,
                ImmutableArray<string>.Empty, ImmutableArray<KeyValuePair<string,long>>.Empty,
                ImmutableArray<NodeHarvest>.Empty, ImmutableArray<KeyValuePair<string,int>>.Empty);
            var flags = original.Flags.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
            foreach (var requirement in requirements) flags[requirement.FlagId] = 1;
            cells[key] = original with { Flags = flags.OrderBy(f => f.Key, StringComparer.Ordinal).ToImmutableArray() };
        }
        var world = WorldDelta.FromSnapshot(session.Generator, arena.Simulation.World.WorldSeed, new Registry(),
            snapshot with { Cells = cells.Values.OrderBy(c => c.CellKey, StringComparer.Ordinal).ToImmutableArray() }, out var rejected);
        Assert.Empty(rejected);
        var record = arena.Simulation.CaptureRecord().WithInventory(arena.Simulation.CaptureRecord().Inventory.Append(Arena.Stack("item.tool.fitting_wedge", 2)))
            .WithSpace(session.Setup.Layout.SpaceId.ToString(), null);
        var bus = new EventBus();
        var simulation = Simulation.Start(session.Setup, record, world, arena.Simulation.WorldTick, bus);
        string? refusal = null;
        bus.Subscribe<CommandRejected>(e => refusal = e.Reason);
        simulation.Enqueue(new InteractCommand(simulation.PlayerId, site.Key));
        simulation.DrainCommands();
        Assert.Null(refusal);
        Assert.Equal(1, simulation.Player.Inventory.Where(i => i.DefId == "item.tool.fitting_wedge").Sum(i => i.Count));
        Assert.Equal(ingots, simulation.Player.Inventory.Where(i => i.DefId == "item.material.iron_ingot").Sum(i => i.Count));
        Assert.Equal(ingots == 0, simulation.Barriers.Single(b => b.Site.Key == "barrier.barrowworks.measure").Standing);
        simulation.Enqueue(new InteractCommand(simulation.PlayerId, site.Key));
        simulation.DrainCommands();
        Assert.Contains("already", refusal);
        Assert.Equal(1, simulation.Player.Inventory.Where(i => i.DefId == "item.tool.fitting_wedge").Sum(i => i.Count));
        var alternative = session.Setup.Layout.Switches.Single(s => s.Key == "switch.barrowworks."+other);
        Assert.True(simulation.World.GetFlag(Simulation.CellOf(session.Setup.Layout.SpaceId, site), site.FlagId) == 1);
        Assert.Equal(0, simulation.World.GetFlag(Simulation.CellOf(session.Setup.Layout.SpaceId, alternative), alternative.FlagId));
        var after = simulation.CaptureRecord();
        var (ax, az) = Centre(alternative);
        var otherRecord = new PlayerRecord(after.Id, after.Name, ax, 0, az, after.AppearanceSeed, after.Inventory, after.Progression,
            after.FacingMdeg, after.Discoveries, after.Equipment, after.Currency, after.Effects, after.Relationships, after.Conversations,
            after.Quests, after.Companions) { Factions = after.Factions, SpaceId = after.SpaceId };
        world = WorldDelta.FromSnapshot(session.Generator, world.WorldSeed, new Registry(), world.TakeSnapshot(), out rejected);
        Assert.Empty(rejected);
        simulation = Simulation.Start(session.Setup, otherRecord, world, simulation.WorldTick, bus);
        refusal = null;
        simulation.Enqueue(new InteractCommand(simulation.PlayerId, alternative.Key));
        simulation.DrainCommands();
        Assert.Equal("That choice has already been made.", refusal);
        Assert.Equal(1, simulation.Player.Inventory.Where(i => i.DefId == "item.tool.fitting_wedge").Sum(i => i.Count));
        Assert.Equal(ingots, simulation.Player.Inventory.Where(i => i.DefId == "item.material.iron_ingot").Sum(i => i.Count));
    }
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
    /// The cell the quest reads a flag from: the objective's own place, in the space the layout stands in, which
    /// is what <c>QuestSystem.CellOfLocation</c> resolves. Written out rather than reached through the private
    /// system, and deliberately NOT derived from the switch - deriving it from the switch is what made the old
    /// test compare a thing with itself.
    /// </summary>
    private static CellKey QuestCell(GameSession session, string locationId)
    {
        var place = session.Setup.Layout.Locations.Single(l => l.Id == locationId);
        return CellKey.OfInterior(session.Setup.Layout.SpaceId, place.XMm / 1000.0, place.ZMm / 1000.0);
    }

    /// <summary>The place each reading's objective watches, from the quest's own definitions.</summary>
    private static Dictionary<string, string> ObjectivePlaces(GameSession session) =>
        session.Setup.Quests.Quests[Quest].Order
            .Select(o => o.Condition).OfType<WorldStateObjective>()
            .ToDictionary(c => c.FlagId, c => c.LocationId, StringComparer.Ordinal);

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
    public void EachReading_WhenUsed_SetsItsFlagWhereTheQuestReadsIt()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        var places = ObjectivePlaces(session);

        foreach (var (key, flag) in Readings)
        {
            var site = session.Setup.Layout.Switches.Single(s => s.Key == key);
            var arena = StandingOn(session, site);
            var read = QuestCell(session, places[flag]);

            Assert.Equal(0, arena.Simulation.World.GetFlag(read, flag));
            Assert.Null(arena.Submit(new InteractCommand(arena.Simulation.PlayerId, key)));
            Assert.NotEqual(0, arena.Simulation.World.GetFlag(read, flag));
        }
    }

    /// <summary>
    /// The symptom, stated directly: a ring switch's flag cell is space-qualified. The old code wrote to
    /// <c>r_1_1:c_00_00</c> - real exterior ground - because `SwitchSite.Space` defaulted to the exterior and
    /// nothing set it, while the quest read <c>space.barrowworks/…</c>.
    /// </summary>
    [Fact]
    public void TheRingsFlagCells_AreInTheRingsSpace_NotOnExteriorGround()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        var space = session.Setup.Layout.SpaceId;

        Assert.False(space.IsExterior);
        foreach (var site in session.Setup.Layout.Switches)
        {
            var cell = Simulation.CellOf(space, site);
            Assert.Equal(space, cell.Space);
        }

        foreach (var door in session.Setup.Layout.Doors)
            Assert.Equal(space, Simulation.CellOf(space, door).Space);
    }

    /// <summary>
    /// The two answers are one structure and two switches, so a character can do either - and each sets only its
    /// own flag. One switch that set both would answer the question twice.
    /// </summary>
    [Fact]
    public void TheTwoAnswers_HaveDistinctControls_AndCannotBeMadeWithoutTheResearch()
    {
        using var profile = new TempProfile();
        using var session = Boot(profile);
        var places = ObjectivePlaces(session);
        string preserved = "world.barrowworks.headworks_preserved";
        string dismantled = "world.barrowworks.headworks_dismantled";

        var preserve = session.Setup.Layout.Switches.Single(s => s.FlagId == preserved);
        var dismantle = session.Setup.Layout.Switches.Single(s => s.FlagId == dismantled);
        Assert.Equal("r12_preserve_control", preserve.Body.Id);
        Assert.Equal("r12_dismantle_control", dismantle.Body.Id);
        Assert.Contains(dismantled, preserve.Excludes);
        Assert.Contains(preserved, dismantle.Excludes);
        Assert.Equal(new SiteItem("item.tool.fitting_wedge", 1), preserve.Consume);
        Assert.Equal(preserve.Consume, dismantle.Consume);
        Assert.Equal(new SiteItem("item.material.iron_ingot", 2), dismantle.Reward);

        var arena = StandingOn(session, preserve);
        Assert.Equal(preserve.LockedText, arena.Submit(new InteractCommand(arena.Simulation.PlayerId, preserve.Key)));

        Assert.Equal(0, arena.Simulation.World.GetFlag(QuestCell(session, places[preserved]), preserved));
        Assert.Equal(0, arena.Simulation.World.GetFlag(QuestCell(session, places[dismantled]), dismantled));
    }

    /// <summary>
    /// The other side of the same coin: an ordinary exterior switch is where it always was. The fix resolves a
    /// site's cell in the LAYOUT's space, and the exterior is both the default and <c>OfWorld</c>, so no authored
    /// exterior site moves cell.
    /// </summary>
    [Fact]
    public void AnExteriorSwitch_KeepsTheCellItHasAlwaysHad()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile);
        var layout = session.Setup.Layout;

        Assert.True(layout.SpaceId.IsExterior);
        foreach (var site in layout.Switches)
        {
            var (xMm, zMm) = Centre(site);
            Assert.Equal(CellKey.OfWorld(xMm / 1000.0, zMm / 1000.0), Simulation.CellOf(layout.SpaceId, site));
        }
    }
}
