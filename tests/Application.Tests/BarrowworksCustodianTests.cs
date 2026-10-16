using UNNAMED.World;

namespace UNNAMED.Application.Tests;

/// <summary>
/// P5: the Ring Custodian is the creature the quest's <c>o_custodian</c> waits on, and the ring shipped without
/// one - so the quest could be offered, advanced, and never finished. This is the check that the creature is
/// really there, in the room the objective names, in the region that has it.
/// </summary>
/// <remarks>
/// Booted in the ring's own region rather than the start area's, because a spawn belongs to the region that
/// declares it: <c>CombatContent</c> carries one region's spawns at a time, so the Custodian is correctly
/// absent from a start-area run and correctly present here. Both halves are asserted.
/// </remarks>
public class BarrowworksCustodianTests
{
    private const string Custodian = "creature.construct.ring_custodian";

    [Fact]
    public void TheCustodian_StandsInTheServiceVestibule_WhichIsTheRoomTheObjectiveNames()
    {
        using var profile = new TempProfile();
        var setup = Harness.Boot(profile, "region.barrowworks", barrowworks: true).Setup;

        var spawn = setup.Combat.Spawns.Single(s => s.Key == "spawn.barrowworks.ring_custodian");
        var member = Assert.Single(spawn.Members);
        Assert.Equal(Custodian, member.CreatureId);

        // R10, the service vestibule, at the blockout's own centre for it.
        Assert.Equal(50_000L, spawn.XMm);
        Assert.Equal(84_000L, spawn.ZMm);

        // One, and it does not come back: the design reads the defeat as a durable fact, and a respawning
        // Custodian would make that fact a farm.
        Assert.Equal(0L, spawn.RespawnTicks);
    }

    [Fact]
    public void TheCustodian_HasTheFightTheEngineCanBuild_ABlowAndAChargeAndARearWeakPoint()
    {
        using var profile = new TempProfile();
        var setup = Harness.Boot(profile, "region.barrowworks", barrowworks: true).Setup;

        var custodian = setup.Combat.Creatures[Custodian];
        Assert.Equal(5, custodian.Level);
        Assert.Equal(150, custodian.MaxHealth);

        // Phase 1 builds one blow and one charge; the brief asks for three telegraphed attacks, so the third is
        // recorded in OPEN_QUESTIONS rather than smuggled in as a second blow. Both of these telegraph: a windup
        // is the tell a body reads.
        Assert.NotNull(custodian.Attack);
        Assert.NotNull(custodian.Charge);
        Assert.True(custodian.Attack.WindupTicks > 0, "the blow telegraphs");
        Assert.True(custodian.Charge!.WindupTicks > 0, "the charge telegraphs");

        // The rear weak point is the whole fight: it turns slowly, so a body that gets behind it stays there.
        // The weak point itself is the archetype's, not the definition's; what this pins is the turn rate.
        Assert.True(custodian.TurnMdegPerSecond < setup.Combat.Creatures["creature.construct.animated_armour"].TurnMdegPerSecond,
            "the Custodian turns slower than the armour it is built from - the fight is about staying behind it");
    }

    [Fact]
    public void AStartAreaRun_DoesNotCarryTheCustodian()
    {
        using var profile = new TempProfile();
        var setup = Harness.Boot(profile).Setup;

        Assert.DoesNotContain(setup.Combat.Spawns, s => s.Key == "spawn.barrowworks.ring_custodian");
    }
}
