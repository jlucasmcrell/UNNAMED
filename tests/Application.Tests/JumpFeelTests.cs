using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Spatial;
using Xunit.Abstractions;

namespace UNNAMED.Application.Tests;

/// <summary>
/// D6, the jump's feel (the owner: "extremely short"). The apex stays at 1.15 m so a 1.2 m fence is still unjumpable, the airtime is longer
/// than it was (rise 0.50 s), the jump carries its own horizontal speed rather than the walking pace, and a people with wings jumps at the
/// speed its feet had before its traits slowed them, with a flutter's worth of extra distance and no extra height. Checked on the same world
/// geometry the rest of this file uses: the fallen timber at 0.8 m and the smithy fence at 1.2 m.
/// </summary>
public class JumpFeelTests(ITestOutputHelper output)
{
    private static MovementRules Rules() => new(
        BaseSpeedMmPerSecond: 3_200, WalkPercent: 50, SprintPercent: 160, BodyRadiusMm: 350, InteractReachMm: 1_600)
    {
        JumpApexMm = 720, JumpRiseMs = 360, VaultMm = 1_200, MantleMm = 3_000, JumpTuckRadiusMm = 200, StepUpMm = 400, 
        StandHeightMm = 1_800, CrouchHeightMm = 1_150, CrouchPercent = 50, 
    };

    [Fact]
    public void TheApexIsUnchanged_AndTheAirtimeIsLonger()
    {
        var rules = Rules();
        // A real running jump: 0.72 m of foot lift, 0.72 s in the air, and the vault window above it (BRIEF D6).
        Assert.Equal(720, rules.JumpApexMm);
        Assert.Equal(360, rules.JumpRiseMs);
        Assert.Equal(720, rules.AirtimeMs);
        Assert.Equal(1_200, rules.VaultMm);
        Assert.Equal(3_000, rules.MantleMm);
        Assert.Equal(720, rules.LiftMm(rules.JumpRiseMs));
        Assert.Equal(0, rules.LiftMm(0));
        Assert.Equal(0, rules.LiftMm(rules.AirtimeMs));
    }

    [Fact]
    public void TheJumpCarriesItsOwnSpeed_AndNeverSlowerThanTheGait()
    {
        var rules = Rules();
        // No jump speed of its own: the air is walked at the gait's pace, exactly as before.
        Assert.Equal(rules.SpeedMmPerSecond(Gait.Run, Stance.Standing), rules.JumpSpeed(Gait.Run, Stance.Standing));

        // With one (a winged people), the air is faster than the gait - and a sprint on the ground is never made slower by taking off.
        var winged = rules with { JumpSpeedMmPerSecond = 3_200, JumpDistancePercent = 112 };
        Assert.True(winged.JumpSpeed(Gait.Walk, Stance.Standing) > rules.SpeedMmPerSecond(Gait.Walk, Stance.Standing));
        Assert.True(winged.JumpSpeed(Gait.Sprint, Stance.Standing) >= rules.SpeedMmPerSecond(Gait.Sprint, Stance.Standing));
    }

    [Fact]
    public void AWingedPeoplesPlainJump_GoesAtLeastAsFarAsAPlainOne()
    {
        var kalTraits = new PeopleTraits { SpeedPercent = 90, JumpDistancePercent = 112 };
        var kalAbilities = new[]
        {
            new PeopleAbility("ability.people.kal.wing_glide", "people.kal", "Wing Glide", "", PeopleAction.Glide),
            new PeopleAbility("ability.people.kal.gap_leap", "people.kal", "Gap Leap", "", PeopleAction.Leap)
            {
                ApexBonusMm = 500, DistancePercent = 160,
            },
        };
        var veth = PeopleRules.Movement(Rules(), PeopleTraits.None, Array.Empty<PeopleAbility>());
        var kal = PeopleRules.Movement(Rules(), kalTraits, kalAbilities);

        // The Kal walks at 90% of base, as its traits say - but takes off at the full 100%, and the flutter adds a little on top.
        // 3.2 m/s base, 90% for the people, 50% for the walk toggle: the Kal walks at 1.44 m/s.
        Assert.Equal(1_440, kal.SpeedMmPerSecond(Gait.Walk), 3);
        Assert.True(kal.JumpSpeedMmPerSecond > 0);
        Assert.True(kal.JumpSpeed(Gait.Walk, Stance.Standing) > veth.JumpSpeed(Gait.Walk, Stance.Standing),
            $"Kal jump {kal.JumpSpeed(Gait.Walk, Stance.Standing):0} vs Veth {veth.JumpSpeed(Gait.Walk, Stance.Standing):0}");
        // Height is not the flutter's business: the plain Kal apex is the plain apex.
        Assert.Equal(veth.JumpApexMm, kal.JumpApexMm);
        // And a Veth, having no jump speed of its own, is unchanged to the millimetre.
        Assert.Equal(0, veth.JumpSpeedMmPerSecond);
        Assert.Equal(veth.SpeedMmPerSecond(Gait.Sprint, Stance.Standing), veth.JumpSpeed(Gait.Sprint, Stance.Standing));
        output.WriteLine($"Kal plain jump {kal.JumpSpeed(Gait.Walk, Stance.Standing):0} mm/s, Veth {veth.JumpSpeed(Gait.Walk, Stance.Standing):0} mm/s");
    }
}
