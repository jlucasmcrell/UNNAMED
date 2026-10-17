using System.Collections.Immutable;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Soak.Tests;

// Strong continuation requirements: run these probes before assigning a Skip reason to a finding.
public sealed class InterruptedSaveTests
{
    // W4-SAVE-01: the attack under way is saved (schema 27), so a save mid-windup loads mid-windup and the fight goes on as it would have.
    [Fact, Trait("Category", "Smoke")]
    public void MidCombatSave_ContinuesLikeUninterruptedCombat()
    {
        using var w = new SoakWorld();
        var sim = Fight(w, downed: false);
        sim.Step();
        Assert.Equal(CombatPhase.Windup, sim.Combat.Phase);
        var resumed = w.RoundTrip(sim);
        Assert.Equal((sim.Combat.Phase, sim.Combat.PhaseTicksLeft), (resumed.Combat.Phase, resumed.Combat.PhaseTicksLeft));
        for (int i = 0; i < 60; i++) { sim.Step(); resumed.Step(); }
        SoakWorld.Equal(sim, resumed, "combat continuation", replayable: true);
    }

    // W4-SAVE-02, DN15: under MMO rules a save made while downed counts as the death, so quitting can never escape one. The uninterrupted
    // branch is helped up by Tavar; the resumed one dies once, owes the usual debt and respawns at the return point, Tavar near or not.
    [Fact, Trait("Category", "Smoke")]
    public void DownedSave_LoadsAsTheDeath()
    {
        using var w = new SoakWorld();
        var sim = Fight(w, downed: true);
        for (int i = 0; i < 300 && !sim.Combat.Downed; i++) sim.Step();
        Assert.True(sim.Combat.Downed, "fixture did not reach Downed");
        var resumed = w.RoundTrip(sim);
        var died = new List<PlayerDied>();
        var respawned = new List<PlayerRespawned>();
        w.Bus(resumed).Subscribe<PlayerDied>(died.Add);
        w.Bus(resumed).Subscribe<PlayerRespawned>(respawned.Add);
        var helped = new List<PlayerRevived>();
        var continuousDied = new List<PlayerDied>();
        w.Bus(sim).Subscribe<PlayerRevived>(helped.Add);
        w.Bus(sim).Subscribe<PlayerDied>(continuousDied.Add);
        long debt = sim.Player.Progression.XpDebt;
        int window = sim.Setup.Combat.Constants.DownedTicks + sim.Setup.Combat.Constants.DeathTicks;
        for (int i = 0; i < window; i++) { sim.Step(); resumed.Step(); }

        Assert.Single(helped);
        Assert.Empty(continuousDied);
        Assert.Equal(debt, sim.Player.Progression.XpDebt);
        var death = Assert.Single(died);
        Assert.Equal(debt + death.DebtAdded, resumed.Player.Progression.XpDebt);
        Assert.True(death.DebtAdded > 0);
        var back = Assert.Single(respawned);
        Assert.Equal((back.Body.XMm, back.Body.ZMm), (resumed.Player.Body.XMm, resumed.Player.Body.ZMm));
        Assert.False(resumed.Combat.Defeated || resumed.Combat.Downed);
    }

    private static Simulation Fight(SoakWorld w, bool downed)
    {
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
        return sim;
    }
}
