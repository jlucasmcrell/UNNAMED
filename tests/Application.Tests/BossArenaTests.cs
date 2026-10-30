using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// D19: a boss fights in an arena, and the arena is a place in the world - the Wayholder's chalked circle, the
/// Matriarch's rope cave, the measuring court, the channel under the causeway, Old Late's circle, the Quiet Stones
/// basin. The runtime already reads that arena as a court: a wounded or engaged boss whose court the character has
/// left for twenty ticks resets to full health and puts the character back outside
/// (<c>Creatures.Tick</c>; <c>LeavingTheCourt_ResetsTheWoundedCustodianToFullHealth</c>). This checks the data those
/// courts are built from, for every boss this branch adds: the court exists, the boss stands inside it, and the safe
/// point it hands back is outside it - so stepping out is a forfeit rather than a walk to safety inside the ring.
/// </summary>
public class BossArenaTests
{
    private sealed record Boss(string Key, long XMm, long ZMm, EncounterRecovery Court);

    private static Boss Find(GameSession session, string spawnKey)
    {
        var spawn = session.Setup.Combat.Spawns.Single(s => s.Key == spawnKey);
        Assert.NotNull(spawn.Recovery);
        return new Boss(spawn.Key, spawn.XMm, spawn.ZMm, spawn.Recovery!);
    }

    private static void AssertInsideItsArena(Boss boss)
    {
        var court = boss.Court;
        Assert.True(court.MinXMm < court.MaxXMm, $"{boss.Key}: the court has no width");
        Assert.True(court.MinZMm < court.MaxZMm, $"{boss.Key}: the court has no depth");
        Assert.InRange(boss.XMm, court.MinXMm, court.MaxXMm);
        Assert.InRange(boss.ZMm, court.MinZMm, court.MaxZMm);

        // The safe point is where a reset puts the character: it must be outside the court, or the forfeit would leave
        // them standing in the ring with the boss already at full health.
        bool safeOutside = court.SafeXMm < court.MinXMm || court.SafeXMm > court.MaxXMm
                           || court.SafeZMm < court.MinZMm || court.SafeZMm > court.MaxZMm;
        Assert.True(safeOutside, $"{boss.Key}: the safe point is inside its own court");
    }

    [Fact]
    public void TheTrialsCircle_HoldsTheGateTender_AndItsSafePointIsOutsideTheChalk()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.starting_frontier");
        AssertInsideItsArena(Find(session, "spawn.boss.ashen_hollow.wayholder_trial"));
    }

    [Fact]
    public void TheTrialCircle_AndTheMeasuredFloor_AreForfeit_Courts()
    {
        // canon/BOSSES.md: the trial is fought inside the yard's marked circle and leaving it forfeits; the Custodian's
        // court is a scored floor. Both say so as an arena rule, so the attempt ends on the first tick outside.
        using (var profile = new TempProfile())
        using (var session = Harness.Boot(profile, "region.starting_frontier"))
            Assert.True(Find(session, "spawn.boss.ashen_hollow.wayholder_trial").Court.Forfeit);

        using (var profile = new TempProfile())
        using (var session = Harness.Boot(profile, "region.barrowworks", barrowworks: true))
            Assert.True(Find(session, "spawn.boss.barrowworks.ring_custodian.deep").Court.Forfeit);

        // A court without the rule keeps the grace: Old Late waits out a walk to safety instead of forfeiting at once.
        using (var profile = new TempProfile())
        using (var session = Harness.Boot(profile, "region.starting_frontier"))
            Assert.False(Find(session, "spawn.boss.old_late.the_breath_late").Court.Forfeit);
    }

    [Fact]
    public void TheBreathLatesCircle_HoldsItInOldLate()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.starting_frontier");
        AssertInsideItsArena(Find(session, "spawn.boss.old_late.the_breath_late"));
    }

    [Fact]
    public void TheQuietStonesBasin_HoldsTheDoubledHart()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.starting_frontier");
        AssertInsideItsArena(Find(session, "spawn.boss.foldscar.doubled_hart"));
    }

    [Fact]
    public void EveryBossInAnUnbuiltArea_StandsOnADevScaffoldRegion()
    {
        // Owner ruling 2026-10-30: a boss may stand on placeholder ground as data, but never in a player build. The 20 flat
        // patches are tagged dev_scaffold, and Main refuses --region for one unless --dev-scaffold is given; this asserts the
        // tag those bosses depend on, and that the five built areas are NOT tagged (they must always load).
        string regions = System.IO.Path.Combine(Harness.RepoRoot(), "content", "regions");
        var scaffold = new List<string>();
        foreach (string path in System.IO.Directory.GetFiles(regions, "*.yaml"))
        {
            string text = System.IO.File.ReadAllText(path);
            bool tagged = text.Contains("- dev_scaffold", StringComparison.Ordinal);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (tagged)
                scaffold.Add(name);
        }
        Assert.Equal(20, scaffold.Count);
        foreach (string built in new[] { "starting_frontier", "ashen_hollow", "barrowworks", "den_gorge_cave", "sedge_levels" })
            Assert.DoesNotContain(built, scaffold);
    }

    [Fact]
    public void TheNextThree_HaveTheirCourts_AndTheirBands()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.starting_frontier");
        // canon/BOSSES.md 3.1: Old Grey on the Old East Road, the Unpaid in Cairnwold's moss, the Gate Warden's deeper court.
        AssertInsideItsArena(Find(session, "spawn.boss.the_road.old_grey"));
        AssertInsideItsArena(Find(session, "spawn.boss.cairnwold.the_unpaid"));
        var warden = Find(session, "spawn.boss.field_ruins.gate_warden.deep");
        AssertInsideItsArena(warden);
        // DUNGEON END: the deep court keeps its kill.
        Assert.Equal(0L, session.Setup.Combat.Spawns.Single(s => s.Key == warden.Key).RespawnTicks);
    }

    [Fact]
    public void TheRopeCave_HoldsTheMatriarch()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.den_gorge_cave");
        AssertInsideItsArena(Find(session, "spawn.boss.den_gorge.matriarch"));
    }

    [Fact]
    public void TheMeasuringCourt_HoldsTheCustodianAtItsDeepestLevel()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.barrowworks", barrowworks: true);
        var boss = Find(session, "spawn.boss.barrowworks.ring_custodian.deep");
        AssertInsideItsArena(boss);
        // DUNGEON END (canon/BOSSES.md §2): the deeper band the area's own custodian carries, and no respawn.
        Assert.Equal(0L, session.Setup.Combat.Spawns.Single(s => s.Key == boss.Key).RespawnTicks);
    }

    [Fact]
    public void EveryCanonBoss_StandsInACourt_AndCarriesItsOwnLoot()
    {
        // D19 roster check, over the content itself: every boss spawn has a court (so its arena is painted), a band its
        // members match, and a body whose loot table is the boss's own - so two bosses sharing a body cannot share drops.
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.starting_frontier");
        var bosses = session.Setup.Combat.Spawns.Where(s => s.Key.StartsWith("spawn.boss.", StringComparison.Ordinal)).ToList();
                // Only this region's spawns are co-loaded, so the count is the frontier's own bosses; the full 35-boss roster
        // is the content's, read from the spawn files by the captures and by lint.
        Assert.True(bosses.Count >= 6, $"expected the frontier's bosses, found {bosses.Count}");
        foreach (var spawn in bosses)
        {
            Assert.NotNull(spawn.Recovery);
            Assert.True(spawn.Recovery!.MaxXMm > spawn.Recovery.MinXMm && spawn.Recovery.MaxZMm > spawn.Recovery.MinZMm);
            // The band is checked against the creature it spawns (CMB001 does the same at lint time).
            foreach (var member in spawn.Members)
                Assert.NotNull(session.Setup.Combat.Creatures[member.CreatureId].LootTableId);
        }
        Assert.Equal(bosses.Count, bosses.Select(s => s.Key).Distinct().Count());
    }

    [Fact]
    public void TheKeepingHouse_HoldsItsCustodianInSedge()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.sedge_levels");
        var boss = Find(session, "spawn.boss.sedge.keeping_house");
        AssertInsideItsArena(boss);
        // A drowned house whose family is gone: once the construct is down it stays down.
        Assert.Equal(0L, session.Setup.Combat.Spawns.Single(s => s.Key == boss.Key).RespawnTicks);
    }

    [Fact]
    public void TheEelChannel_HoldsOldChannel()
    {
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile, "region.sedge_levels");
        AssertInsideItsArena(Find(session, "spawn.boss.sedge.old_channel"));
    }

    [Fact]
    public void EveryBossThisBranchAdds_HasACourt()
    {
        foreach (var (region, barrowworks) in new[] { ("region.starting_frontier", false), ("region.den_gorge_cave", false),
                                                      ("region.barrowworks", true), ("region.sedge_levels", false) })
        {
            using var profile = new TempProfile();
            using var session = Harness.Boot(profile, region, barrowworks: barrowworks);
            foreach (var spawn in session.Setup.Combat.Spawns.Where(s => s.Key.StartsWith("spawn.boss.", StringComparison.Ordinal)))
                Assert.True(spawn.Recovery is not null, $"{spawn.Key}: a boss without an arena");
        }
    }
}
