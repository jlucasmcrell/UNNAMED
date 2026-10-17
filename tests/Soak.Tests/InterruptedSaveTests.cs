using System.Collections.Immutable;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Soak.Tests;

// Strong continuation requirements: run these probes before assigning a Skip reason to a finding.
public sealed class InterruptedSaveTests
{
    [Fact(Skip = "W4-SAVE-01: attack action is not persisted; save during Windup reloads Idle. StateDump deliberately excludes Phase, so digest alone misses this continuation mismatch."), Trait("Category", "KnownBug")]
    public void MidCombatSave_ContinuesLikeUninterruptedCombat() => Probe(false);

    [Fact(Skip = "W4-SAVE-02: CaptureRecord resolves Downed immediately to helped-up; continuous branch stays Downed for 600 ticks. Strong continuation equivalence is unmet."), Trait("Category", "KnownBug")]
    public void DownedSave_ContinuesLikeUninterruptedDowned() => Probe(true);

    private static void Probe(bool downed)
    {
        using var w = new SoakWorld();
        long x = 470_000, z = 750_000;
        var setup = w.Session.Setup with { Combat = w.Session.Setup.Combat with {
            Spawns = ImmutableArray.Create(new SpawnSite("spawn.soak.wolf", x, z + 1900, 0,
                ImmutableArray.Create(new SpawnMember("creature.beast.wolf_grey", "pack_hunter")))) } };
        var sim = w.New(630, setup, r => {
            r = SoakWorld.Place(r, setup, x, z);
            if (!downed) return r;
            return r.WithProgression(r.Progression with { Pools = r.Progression.Pools with { Health = 3 } })
                .WithCompanions(new[] { new CompanionRecord("npc.ashen_hollow.tavar_orr", CompanionOrder.Wait,
                    CompanionCondition.Up, x, z - 1500, 0, 100) });
        });
        Assert.Null(w.Command(sim, new AttackCommand(sim.PlayerId)));
        if (downed)
        {
            for (int i = 0; i < 300 && !sim.Combat.Downed; i++) sim.Step();
            Assert.True(sim.Combat.Downed, "fixture did not reach Downed");
        }
        else sim.Step();
        var resumed = w.RoundTrip(sim);
        if (!downed) Assert.Equal(sim.Combat.Phase, resumed.Combat.Phase);
        for (int i = 0; i < 60; i++) { sim.Step(); resumed.Step(); }
        SoakWorld.Equal(sim, resumed, downed ? "Downed continuation" : "combat continuation", replayable: true);
    }
}
