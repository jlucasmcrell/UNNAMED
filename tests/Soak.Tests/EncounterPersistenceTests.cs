using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Pets;
using UNNAMED.Persistence;
using UNNAMED.World;

namespace UNNAMED.Soak.Tests;

public class EncounterPersistenceTests
{
    private static PlayerRecord WithHound(PlayerRecord player) => player.WithPets(ImmutableArray.Create(
        new PetRecord(EntityId.NewId(EntityKind.Summon), "creature.beast.wolf_grey", "Hound", PetRole.Fighter,
            PetOrder.Follow, PetCondition.Up, player.XMm, player.ZMm, 0, 50, PetBond.Tamed,
            ImmutableArray.Create(new PetSkill(PetSkillKind.Bite, 1, 0)))));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void LoadingAgainstADifferentPartyDoesNotResizeAlreadySpawnedCreatures(bool beganWithPet, bool savedWithPet)
    {
        using var world = new SoakWorld();
        var simulation = world.New(42, change: player => beganWithPet ? WithHound(player) : player);
        var before = simulation.Creatures.Select(c => (c.Key, c.Health, c.MaxHealth, c.Level)).ToArray();
        var saved = simulation.CaptureRecord();
        saved = savedWithPet ? WithHound(saved) : saved.WithPets(ImmutableArray<PetRecord>.Empty);
        world.Store.Save(SaveSlots.Manual("party_changed"), SaveDocuments.Capture(simulation.World, saved,
            world.Session.Content, simulation.WorldTick, 0));
        var loaded = world.Load(simulation.Setup, "party_changed");
        Assert.Equal(before, loaded.Creatures.Select(c => (c.Key, c.Health, c.MaxHealth, c.Level)).ToArray());
        Assert.Equal(savedWithPet, loaded.PartyStrength > 0);
    }
}
