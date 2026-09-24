using UNNAMED.Persistence;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>ROADMAP M3 exit (d): cell save/load preserves world changes; and the player comes back exactly (PROTOTYPE.md §7.4).</summary>
public class SaveLoadTests
{
    private const string LonghouseFlag = "world.hollow.longhouse_door_open";

    /// <summary>
    /// Open the longhouse door, go down the road until the Foldscar is discovered, and stop facing somewhere particular - clear of
    /// every creature, whose minds a load does not keep.
    /// </summary>
    private static Simulation PlaySomething(GameSession session)
    {
        var simulation = session.NewGame("Wanderer", seed: 42);
        Assert.True(Harness.WalkPath(session, (44, 138), (54.5, 134), (53, 128)));
        session.Submit(new InteractCommand(simulation.PlayerId, "door.longhouse"));
        Assert.True(Harness.WalkPath(session, (60, 128), (100, 100), (135, 70)));
        session.Submit(new MoveCommand(simulation.PlayerId, UNNAMED.Domain.Spatial.MoveIntent.Idle(123_456)));
        Harness.Ticks(session, 3);
        return simulation;
    }

    [Fact]
    public void SaveThenLoad_RestoresEveryPersistentField()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var played = PlaySomething(session);
        session.Save(SaveSlots.Quick);
        string digest = played.StateDigest();

        var reloaded = Harness.Boot(profile);
        var result = reloaded.Load(SaveSlots.Quick);
        var simulation = reloaded.Simulation!;

        Assert.True(result.IsComplete);
        Assert.Equal(digest, simulation.StateDigest());
        Assert.Equal(played.WorldTick, simulation.WorldTick);   // not reset, not advanced by the load
        Assert.Equal(played.Player.Body, simulation.Player.Body);
        Assert.Equal(123_456, simulation.Player.Body.FacingMdeg);
        Assert.Equal(played.Player.Discoveries.AsEnumerable(), simulation.Player.Discoveries.AsEnumerable());
        Assert.Contains(simulation.Player.Discoveries, d => d.LocationId == "location.foldscar");
        Assert.Equal(played.Player.Progression.Digest, simulation.Player.Progression.Digest);
        Assert.True(simulation.Doors.Single(d => d.Site.Key == "door.longhouse").Open);
        Assert.Equal(session.PlaytimeSeconds, reloaded.PlaytimeSeconds);
    }

    [Fact]
    public void AnOpenDoor_IsSavedAsItsCellsDelta_AgainstThatCellsBaseline()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = PlaySomething(session);
        session.Save(SaveSlots.Quick);

        var cells = SectionCodec.DecodeCells(File.ReadAllBytes(Path.Combine(profile.Root, SaveSlots.Quick, SaveFormat.Cells)));
        var outpost = Assert.Single(cells);
        Assert.Equal("r_0_0:c_00_01", outpost.CellKey);
        Assert.Equal(new[] { KeyValuePair.Create(LonghouseFlag, 1L) }, outpost.Flags);
        Assert.Equal(simulation.World.Baseline(CellKey.Parse("r_0_0:c_00_01")).Digest, outpost.BaselineHash);
    }

    [Fact]
    public void ClosingTheDoorAgain_ReturnsTheCellToItsBaseline_AndNothingIsSavedForIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        Assert.True(Harness.WalkPath(session, (44, 138), (54.5, 134), (53, 128)));
        session.Submit(new InteractCommand(simulation.PlayerId, "door.longhouse"));
        Harness.Ticks(session, 1);
        session.Submit(new InteractCommand(simulation.PlayerId, "door.longhouse"));
        Harness.Ticks(session, 1);
        session.Save(SaveSlots.Quick);

        Assert.Empty(SectionCodec.DecodeCells(File.ReadAllBytes(Path.Combine(profile.Root, SaveSlots.Quick, SaveFormat.Cells))));
    }

    [Fact]
    public void ALoadedWorld_KeepsSimulatingFromWhereItWas()
    {
        using var profile = new TempProfile();
        var first = Harness.Boot(profile);
        var original = PlaySomething(first);
        first.Save(SaveSlots.Quick);

        var second = Harness.Boot(profile);
        second.Load(SaveSlots.Quick);
        var loaded = second.Simulation!;

        // The same commands from the same state give the same world, whether or not a save and load sat between.
        foreach (var session in new[] { first, second })
        {
            var simulation = session.Simulation!;
            Assert.True(Harness.WalkPath(session, (44, 138), (60, 128), (100, 100)));
            session.Submit(new MoveCommand(simulation.PlayerId, UNNAMED.Domain.Spatial.MoveIntent.Idle(90_000)));
            Harness.Ticks(session, 2);
        }
        Assert.Equal(original.StateDigest(), loaded.StateDigest());
    }
}
