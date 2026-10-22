using UNNAMED.Domain.Pets;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

public class PlaytestFeatureGateTests
{
    [Fact]
    public void DisabledPets_HaveNoSpeciesOrAnimalStock_AndNoEncounterScaling()
    {
        using var profile = new TempProfile();
        using var session = GameSession.Boot(new GameOptions(Path.Combine(Harness.RepoRoot(), "content"), profile.Root) { EnablePets = false });
        Assert.Null(session.Setup.Pets);
        Assert.DoesNotContain(session.Setup.Items.Merchants.Values.SelectMany(m => m.Stock),
            stock => session.Setup.Items.Catalog.Get(stock.ItemId).PetCreature is not null);
        var sim = session.NewGame(Harness.Named("Release gate"), seed: 42);
        Assert.Empty(sim.Pets);
        Assert.Equal(100, Encounters.Of(10, sim.PetRules.Encounter).Percent);
    }
}
