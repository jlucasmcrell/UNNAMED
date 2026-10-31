// UNNAMED Application tests - public contribution accounting on a real kill (CMB-45)
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.World.Runtime;

using UNNAMED.World;

namespace UNNAMED.Application.Tests;

/// <summary>
/// CMB-45's read surface at the world's own level. The accounting's rules live in the domain and are tested there (eight deterministic
/// tests over weights, shares, ties, the qualification threshold and parts); this file holds what the world exposes to a reader.
/// </summary>
public class ContributionTests
{
    private const string Prey = "creature.beast.rabbit";
    private static readonly (double X, double Z) Yard = (48, 126.2);


    /// <summary>The fixture's character starts unarmed; a fight needs something in its hand, so it is handed a blade.</summary>
    private static PlayerRecord Wielding(PlayerRecord record)
    {
        const string Sword = "item.weapon.rusted_sword";
        var inventory = record.Inventory.Any(e => e.DefId == Sword) ? record.Inventory : record.Inventory.Add(Arena.Stack(Sword, 1));
        var item = inventory.Single(e => e.DefId == Sword).ItemId;
        return new PlayerRecord(record.Id, record.Name, record.XMm, record.YMm, record.ZMm, record.AppearanceSeed, inventory, record.Progression,
            record.FacingMdeg, record.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, item) }, record.Currency, record.Effects);
    }

    /// <summary>
    /// Real blows land in a real fight: the shared Arena.Fight helper closes the distance and swings, and the world publishes the hits. This
    /// is the ground the accounting stands on. It does not yet prove the ledger: see README.md in this branch for that gap.
    /// </summary>
    [Fact]
    public void RealBlows_Land_AndTheWorldPublishesThem()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, Yard, 0, new[] { (Prey, 49.4, 126.2, "guard") }, Wielding);

        var prey = arena.Creature();
        var hits = arena.Record<HitResolved>();

        arena.Fight(prey, 120);

        Assert.Contains(hits, h => h.Attacker == arena.Player && h.Damage > 0);
    }

    /// <summary>Before a blow lands there is no ledger: the accounting is made of what happened, not what might.</summary>
    [Fact]
    public void AnEncounterNobodyHasHit_HasNoLedger()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, Yard, 0, new[] { (Prey, 49.4, 126.2, "guard") });

        var wolf = arena.Simulation.Creatures.Single(c => c.DefId == Prey);
        Assert.Null(arena.Simulation.Contributions(wolf.Key));
        Assert.Empty(arena.Simulation.Contributions());
    }
}
