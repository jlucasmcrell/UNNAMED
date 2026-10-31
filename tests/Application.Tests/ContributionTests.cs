// UNNAMED Application tests - public contribution accounting end to end (CMB-45)
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// CMB-45 in a real fight: the accounting is reached the way every live system is reached - the system that resolves a blow dispatches the
/// contribution, and the system that owns the death concludes the encounter. These tests fight for real and then read what the world recorded.
/// </summary>
public class ContributionTests
{
    private const string Prey = "creature.beast.rabbit";
    private static readonly (double X, double Z) Yard = (48, 126.2);

    [Fact]
    public void ARealFight_FillsTheLedger_AndTheKillConcludesTheEncounter()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, Yard, 0, new[] { (Prey, 49.4, 126.2, "guard") }, Wielding);

        var concluded = arena.Record<EncounterConcluded>();
        var prey = arena.Creature();

        // Nothing swung yet: nothing accounted for.
        Assert.Null(arena.Simulation.Contributions(prey.Key));

        arena.Fight(prey, 400);

        // The fight ended, so the encounter concluded at the same moment the creature fell.
        var outcome = Assert.Single(concluded);
        Assert.Equal("killed", outcome.Outcome);
        Assert.Equal(Prey, outcome.CreatureDefId);

        // One contributor, the whole of the credit, and it qualifies.
        var mine = Assert.Single(outcome.Shares);
        Assert.Equal(arena.Player.Value, mine.ActorId);
        Assert.Equal(1000, mine.Permille);
        Assert.True(mine.Qualified);
        Assert.Equal(arena.Player, outcome.Finisher);

        // Qualified by the region it happened in, and the ledger is spent once the encounter is over.
        Assert.StartsWith("r", outcome.RegionKey);
        Assert.Null(arena.Simulation.Contributions(prey.Key));
        Assert.Empty(arena.Simulation.Contributions());
    }

    /// <summary>The accounting is private until someone lands a blow: an encounter nobody has hit has no ledger.</summary>
    [Fact]
    public void AnEncounterNobodyHasHit_HasNoLedger()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, Yard, 0, new[] { (Prey, 49.4, 126.2, "guard") }, Wielding);

        var prey = arena.Creature();
        Assert.Null(arena.Simulation.Contributions(prey.Key));
        Assert.Empty(arena.Simulation.Contributions());
    }

    /// <summary>The fixture's character starts unarmed; a fight needs something in its hand, so it is handed a blade.</summary>
    private static PlayerRecord Wielding(PlayerRecord record)
    {
        const string Sword = "item.weapon.rusted_sword";
        var inventory = record.Inventory.Any(e => e.DefId == Sword) ? record.Inventory : record.Inventory.Add(Arena.Stack(Sword, 1));
        var item = inventory.Single(e => e.DefId == Sword).ItemId;
        return new PlayerRecord(record.Id, record.Name, record.XMm, record.YMm, record.ZMm, record.AppearanceSeed, inventory, record.Progression,
            record.FacingMdeg, record.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, item) }, record.Currency, record.Effects);
    }
}
