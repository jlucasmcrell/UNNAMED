using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Building;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using static UNNAMED.Application.Tests.BuildingTests;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Navigation and building (WLD-81, RK-A2): a house built across the corner where four cells meet restamps all four cells' tiles; the
/// planner goes round its walls and in through its doorway, and only an agent that opens doors passes its shut door; an NPC whose place
/// is inside walks across the seams and in, opening the placed door on the way; and taking the house down gives back the grid it began as.
/// Coordinates are M7's, local to <see cref="BuildingTests.Origin"/>, as in <see cref="BuildingTests"/>.
/// </summary>
public class NavigationTests
{
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private static readonly string[] Corner = { "r_0_0:c_04_06", "r_0_0:c_05_06", "r_0_0:c_04_07", "r_0_0:c_05_07" };

    /// <summary>Kera's every hour spent at (103.5, 103.5): the house's north-east square, inside its walls.</summary>
    private static SimulationSetup KeraInside(SimulationSetup setup)
    {
        var routine = setup.NpcLife.Routines.Values.Single(r => r.NpcId == Kera);
        var inside = new NpcAnchor(103_500 + Ox, 103_500 + Oz, 0);
        var anchors = routine.Anchors.Keys.ToImmutableSortedDictionary(k => k, _ => inside, StringComparer.Ordinal);
        return setup with { NpcLife = setup.NpcLife with { Routines = setup.NpcLife.Routines.SetItem(routine.Id, routine with { Anchors = anchors }) } };
    }

    /// <summary>The workshop's house (four pads over the corner, a south doorway, seven walls, four roofs) and a door hung shut in it.</summary>
    private static EntityId House(Arena arena)
    {
        WorkshopStepOne(arena);
        Assert.Null(Place(arena, Door, 100_500, 99_000, 0));
        return PieceAt(arena, Door, 100_500, 99_000);
    }

    private static NavPlan Plan(Arena arena, NavAgent agent, (long X, long Z) from, (long X, long Z) to)
    {
        var nav = arena.Simulation.Navigation;
        var open = nav.Gates.ToDictionary(g => g.Key, g => g.Open, StringComparer.Ordinal);
        var query = new NavQuery(nav.Grid, g => g.GateKey is { } key && open.GetValueOrDefault(key), arena.Simulation.Setup.Navigation, new NavScratch(), null);
        return NavSearch.Plan(query, agent, new NavPoint(from.X + Ox, from.Z + Oz), new NavPoint(to.X + Ox, to.Z + Oz));
    }

    [Fact]
    public void AHouseAcrossTheFourCellCorner_RestampsEachCell_AndIsPlannedRoundAndIn()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (102.0, 102.0), timber: new[] { 20, 20, 20 });
        string bare = arena.Simulation.Navigation.Grid.Digest();
        var rebuilt = arena.Record<NavigationRebuilt>();
        var door = House(arena);

        // The pads straddle x = 500 m and z = 700 m; every cell of the corner hosts a piece, and every one of their tiles was restamped.
        var cells = Corner.Select(CellKey.Parse).ToArray();
        Assert.All(cells, c => Assert.NotEmpty(arena.Simulation.World.PiecesIn(c)));
        Assert.Equal(cells.Select(NavigationLayout.TileOf).OrderBy(t => t), rebuilt.SelectMany(r => r.Tiles).Distinct().OrderBy(t => t));
        Assert.All(rebuilt, r => Assert.Equal("placed", r.Reason));
        Assert.Equal(9, rebuilt.Count);   // the doorway, seven walls and the door; pads and roofs restamp nothing

        // From the south, outside, to the north-east square inside: round the walls and in through the doorway, for one who opens doors.
        var person = new NavAgent(session.Setup.Navigation.PersonClass, true);
        var inward = Plan(arena, person, (103_500, 95_000), (103_500, 103_500));
        Assert.Equal(NavOutcome.Found, inward.Outcome);
        Assert.Contains(inward.Corners, c => c.XMm - Ox is > 99_200 and < 101_800 && c.ZMm - Oz is > 97_000 and < 101_000);
        // One who does not open doors finds the house shut; with the door open, it finds the same way in.
        var shut = Plan(arena, person with { OpensDoors = false }, (103_500, 95_000), (103_500, 103_500));
        Assert.NotEqual(NavOutcome.Found, shut.Outcome);
        Assert.True(arena.WalkTo(100.5, 100.3));   // inside, within a hand's reach of the door
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, door.Value)));
        Assert.Equal(NavOutcome.Found, Plan(arena, person with { OpensDoors = false }, (103_500, 95_000), (103_500, 103_500)).Outcome);

        // Taken down, piece by piece, the grid is the grid it began as.
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, door.Value)));
        foreach (var piece in Pieces(arena).OrderBy(p => p.Family switch { PieceFamily.Roof => 0, PieceFamily.Door => 1, PieceFamily.Pad => 3, _ => 2 }))
            Assert.Null(Dismantle(arena, piece.Id));
        Assert.Empty(arena.Simulation.Pieces);
        Assert.Equal(bare, arena.Simulation.Navigation.Grid.Digest());
    }

    [Fact]
    public void AnNpcWhosePlaceIsInside_WalksAcrossTheSeams_AndOpensThePlacedDoor()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Builder(session, (102.0, 102.0), rules: KeraInside(session.Setup), timber: new[] { 20, 20, 20 });
        var door = House(arena);
        Assert.True(arena.WalkTo(99.9, 104.1));   // the inside's north-west corner, out of her way
        var toggled = arena.Record<DoorToggled>();
        var routes = arena.Record<RoutePlanned>();
        var kera = arena.Simulation.Npcs.Single(n => n.Id == Kera).InstanceId;

        var goal = (X: 103_500 + Ox, Z: 103_500 + Oz);
        for (int i = 0; i < 4_000 && arena.Simulation.Npcs.Single(n => n.Id == Kera).Body is var body && (body.XMm, body.ZMm) != goal; i++)
            arena.Tick();

        var at = arena.Simulation.Npcs.Single(n => n.Id == Kera).Body;
        Assert.Equal(goal, (at.XMm, at.ZMm));
        var opened = Assert.Single(toggled, t => t.DoorKey == door.Value);   // and her forge shed's own door on the way out
        Assert.Equal((kera, door.Value, true), (opened.Actor, opened.DoorKey, opened.Open));
        Assert.True(Pieces(arena).Single(p => p.Id == door).DoorOpen);   // NPCs open doors and never close them
        Assert.Contains(routes, r => r.MoverKey == Kera && r.Outcome == "found");
    }
}
