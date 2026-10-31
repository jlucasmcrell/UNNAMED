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
    /// <summary>
    /// The creature a spawn places, read from that spawn's own content: D19's spawns name boss variants, and a test should not
    /// pin a def id the content is free to change into the variant it now fights as.
    /// </summary>
    private static string PlacedBy(GameSession session, string spawnKey) =>
        session.Setup.Combat.Spawns.Single(spawn => spawn.Key == spawnKey).Members[0].CreatureId;

    private const string MatriarchSpawn = "spawn.boss.den_gorge.matriarch";
    private const string TenderSpawn = "spawn.boss.ashen_hollow.wayholder_trial";
    private const string HartSpawn = "spawn.boss.foldscar.doubled_hart";

    private static Arena Open(GameSession session, (double X, double Z) player) =>
        Arena.OpenCreatures(session, session.Setup, player, 0, Array.Empty<(string, double, double, string)>(), keepSpawns: true);

    [Fact]
    public void TheRopeCave_InstantiatesTheMatriarch_NotJustItsSpawn()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.den_gorge_cave");
        Assert.Contains(session.Setup.Combat.Spawns, s => s.Key == MatriarchSpawn);
        string matriarch = PlacedBy(session, MatriarchSpawn);

        var arena = Open(session, (516, 763));
        Assert.Contains(arena.Simulation.Creatures, c => c.DefId == matriarch);
    }

    [Fact]
    public void TheWaystationYard_InstantiatesTheTrialGateTender()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.starting_frontier");
        Assert.Contains(session.Setup.Combat.Spawns, s => s.Key == TenderSpawn);
        string tender = PlacedBy(session, TenderSpawn);

        var arena = Open(session, (472, 738));
        Assert.Contains(arena.Simulation.Creatures, c => c.DefId == tender);
    }

    [Fact]
    public void TheQuietStonesBasin_InstantiatesTheDoubledHart()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.starting_frontier");
        Assert.Contains(session.Setup.Combat.Spawns, s => s.Key == HartSpawn);
        string hart = PlacedBy(session, HartSpawn);

        var arena = Open(session, (551, 643));
        Assert.Contains(arena.Simulation.Creatures, c => c.DefId == hart);
    }
}
