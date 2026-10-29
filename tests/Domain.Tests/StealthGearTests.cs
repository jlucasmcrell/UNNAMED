// UNNAMED Domain - D23 S7: the kit's numbers are canon's numbers, and no piece of it makes a body unseeable

using UNNAMED.Domain.Combat;
using Xunit;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// S7 of D23 (canon/STEALTH.md section 10). Every factor a piece of stealth kit carries is a multiplier on a physical reading, with the price canon
/// names beside it; the one rule that must never break is that none of them reaches zero.
/// </summary>
public class StealthGearTests
{
    [Fact]
    public void ACloakIsWorthAQuarterStandingStill_AndSixTenthsMoving()
    {
        double still = StealthGear.Visibility([StealthGear.Cloak], moving: false);
        double moving = StealthGear.Visibility([StealthGear.Cloak], moving: true);

        Assert.Equal(0.25, still, 3);
        Assert.Equal(0.60, moving, 3);
        Assert.True(moving > still, "the shimmer gives a moving body away: moving cannot be quieter than standing");
    }

    [Fact]
    public void NoKit_MakesABodyUnseeable()
    {
        // Canon's hard rule (STEALTH.md 10): no item and no stack of them reaches zero visibility.
        string[] everything = [StealthGear.Cloak, StealthGear.ShadowStuff, StealthGear.Muffled, StealthGear.Silenced];
        Assert.True(StealthGear.Visibility(everything, moving: false) > 0, "a cloak and shadow-stuff still leave a body visible");
        Assert.True(StealthGear.Visibility(everything, moving: true) > 0);

        // And the noise side: a dampener and a silencer quiet a step and a shot, they do not silence them.
        Assert.True(StealthGear.Noise(everything) > 0);
        Assert.True(StealthGear.ShotNoise(everything) > 0);
        Assert.True(StealthGear.Scent(everything) > 0);
    }

    [Fact]
    public void TheDampenerQuietsAStepToAFifth_AndASilencerToFourTenths()
    {
        Assert.Equal(0.20, StealthGear.Noise([StealthGear.Muffled]), 3);
        Assert.Equal(0.40, StealthGear.ShotNoise([StealthGear.Silenced]), 3);
        Assert.Equal(0.50, StealthGear.Scent([StealthGear.ScentMasked]), 3);

        // Wearing nothing changes nothing: the tables must not leak a factor onto a bare character.
        Assert.Equal(1.0, StealthGear.Visibility([], moving: true), 3);
        Assert.Equal(1.0, StealthGear.Noise([]), 3);
    }

    [Fact]
    public void TheSilencerBelongsToTheMarksmanFamily()
    {
        // S7's wiring point to D21: the silencer goes on the marksman family, and the boots and cloak on armour.
        Assert.Equal("marksman", StealthGear.FamilyOf("item.tool.silencer"));
        Assert.Equal("armor", StealthGear.FamilyOf("item.armor.cloak_optical"));
        Assert.Equal("", StealthGear.FamilyOf("item.material.iron_ingot"));
    }
}
