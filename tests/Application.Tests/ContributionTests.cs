// UNNAMED Application tests - public contribution accounting on a real kill (CMB-45)
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// CMB-45's read surface at the world's own level. The accounting's rules live in the domain and are tested there (eight deterministic
/// tests over weights, shares, ties, the qualification threshold and parts); this file holds what the world exposes to a reader.
/// </summary>
public class ContributionTests
{
    private const string Prey = "creature.beast.rabbit";
    private static readonly (double X, double Z) Yard = (48, 126.2);

    /// <summary>Before a blow lands there is no ledger: the accounting is made of what happened, not what might.</summary>
    [Fact]
    public void AnEncounterNobodyHasHit_HasNoLedger()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, Yard, 0, new[] { (Prey, 49.4, 126.2, "prey") });

        var wolf = arena.Simulation.Creatures.Single(c => c.DefId == Prey);
        Assert.Null(arena.Simulation.Contributions(wolf.Key));
        Assert.Empty(arena.Simulation.Contributions());
    }
}
