using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// D19: whether a boss is actually in the room is two questions - is its spawn defined for the region, and does a creature
/// get instantiated from it where the character stands. The audit frames answered the first and not the second: the rope
/// cave draws with no spider in it. Creatures are instantiated when the world opens (<c>Arena.OpenCreatures</c> with the
/// region's own spawns kept), so this asks the runtime at that moment.
/// </summary>
public class BossPresenceTests
{
    private const string Matriarch = "creature.beast.hunting_spider_matriarch";
    private const string Tender = "creature.construct.gate_tender";
    private const string Hart = "creature.grazer.doubled_hart";

    private static Arena Open(GameSession session, (double X, double Z) player) =>
        Arena.OpenCreatures(session, session.Setup, player, 0, Array.Empty<(string, double, double, string)>(), keepSpawns: true);

    [Fact]
    public void TheRopeCave_InstantiatesTheMatriarch_NotJustItsSpawn()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.den_gorge_cave");
        Assert.Contains(session.Setup.Combat.Spawns, s => s.Key == "spawn.boss.den_gorge.matriarch");

        var arena = Open(session, (516, 763));
        Assert.Contains(arena.Simulation.Creatures, c => c.DefId == Matriarch);
    }

    [Fact]
    public void TheWaystationYard_InstantiatesTheTrialGateTender()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.starting_frontier");
        Assert.Contains(session.Setup.Combat.Spawns, s => s.Key == "spawn.boss.ashen_hollow.wayholder_trial");

        var arena = Open(session, (472, 738));
        Assert.Contains(arena.Simulation.Creatures, c => c.DefId == Tender);
    }

    [Fact]
    public void TheQuietStonesBasin_InstantiatesTheDoubledHart()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.starting_frontier");
        Assert.Contains(session.Setup.Combat.Spawns, s => s.Key == "spawn.boss.foldscar.doubled_hart");

        var arena = Open(session, (551, 643));
        Assert.Contains(arena.Simulation.Creatures, c => c.DefId == Hart);
    }
}
