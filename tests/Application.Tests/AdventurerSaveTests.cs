using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Social;
using UNNAMED.Persistence;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>WP13's adventurers across a save and a load (schema 29): their lives, their slots, and one in the character's hire.</summary>
public class AdventurerSaveTests
{
    private const long Day = 57_600;

    [Fact]
    public void TheirLives_SurviveASaveAndLoad_AndGoOnTheSame()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AdventurerWorldTests.AtTheInn(session, coin: 50);
        var simulation = arena.Simulation;
        simulation.Advance(4 * Day + 300);   // 07:35: they are at the inn's board, where the character stands
        // One of them is hired, with their sheet in the character's save.
        var hired = simulation.Adventurers.Where(a => a.InTown && a.At == "ashen_hollow")
            .FirstOrDefault(a => arena.Submit(new HireCommand(arena.Player, a.Key, HirelingRole.Guard, 5, AcceptsRisk: true)) is null);
        Assert.NotNull(hired);
        // Into the day, until they have gone out and their bodies have gone with them (spec 15: an empty seat says who is out).
        bool OutAndGone() => simulation.Adventurers.Any(a => !a.InTown && a.Party is null && !simulation.Npcs.Any(n => n.Id == a.Key));
        for (int hour = 0; hour < 48 && !OutAndGone(); hour++)
            simulation.Advance(Day / 24);
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("adventurers"), SaveDocuments.Capture(simulation.World, simulation.CaptureRecord(), session.Content, simulation.WorldTick, 0));
        var loaded = store.Load(SaveSlots.Manual("adventurers"), new LoadContext(session.Generator, session.Content, new Registry()));
        Assert.True(loaded.IsComplete);
        var resumed = Simulation.Start(simulation.Setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick, new EventBus());
        // Every body stands again where it stood - one out has none to stand again - and the lives go on the same (the soak's exact save and
        // load).
        Assert.True(OutAndGone());
        Assert.Equal(AdventurerWorldTests.World(simulation), AdventurerWorldTests.World(resumed));
        simulation.Advance(3 * Day);
        resumed.Advance(3 * Day);
        Assert.Equal(AdventurerWorldTests.World(simulation), AdventurerWorldTests.World(resumed));
        Assert.Equal("hireling", resumed.Adventurers.Single(a => a.Key == hired!.Key).Party);
        Assert.NotNull(resumed.PartySheet(hired!.Key));
    }
}
