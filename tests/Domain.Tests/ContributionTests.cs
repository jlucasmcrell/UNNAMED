// UNNAMED Domain tests - public contribution accounting for a shared fight (CMB-45)
using System.Collections.Immutable;
using UNNAMED.Domain.Combat;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// CMB-45's rules: who did what, part by part, and how the credit divides when the encounter ends. Deterministic: the same blows always
/// produce the same shares, down to the rounding remainder.
/// </summary>
public class ContributionTests
{
    [Fact]
    public void ABlowOnAWeakPoint_CountsTwice()
    {
        Assert.Equal(20, ContributionRules.Weigh(20, weakPoint: false));
        Assert.Equal(40, ContributionRules.Weigh(20, weakPoint: true));
        Assert.Equal(0, ContributionRules.Weigh(0, weakPoint: true));
    }

    [Fact]
    public void TheSharesAlwaysAddUpToAWhole()
    {
        var shares = ContributionRules.Split(new[] { ("player", 1), ("companion", 2) });
        Assert.Equal(1000, shares.Sum(s => s.Permille));
        Assert.Equal(667, shares[0].Permille);
        Assert.Equal(333, shares[1].Permille);

        // Three equal contributors cannot divide evenly; the remainder goes to the first, so the total is still a whole.
        var three = ContributionRules.Split(new[] { ("a", 1), ("b", 1), ("c", 1) });
        Assert.Equal(1000, three.Sum(s => s.Permille));
        Assert.Equal(334, three[0].Permille);
    }

    [Fact]
    public void ATieIsBrokenByActorId_NotByLuck()
    {
        var shares = ContributionRules.Split(new[] { ("zebra", 5), ("ant", 5) });
        Assert.Equal("ant", shares[0].ActorId);
        var again = ContributionRules.Split(new[] { ("ant", 5), ("zebra", 5) });
        Assert.Equal(again[0].ActorId, shares[0].ActorId);
    }

    [Fact]
    public void AnAssistUnderFivePerCent_DoesNotQualify()
    {
        var shares = ContributionRules.Split(new[] { ("player", 99), ("adventurer", 1) });
        var assist = shares.Single(s => s.ActorId == "adventurer");
        Assert.False(assist.Qualified);
        Assert.True(shares.Single(s => s.ActorId == "player").Qualified);
    }

    [Fact]
    public void TheLedgerKeepsPartsApart_AndRollsThemUpPerAttacker()
    {
        var ledger = new ContributionLedger("spawn.test.boss");
        ledger.Landed("player", BodyRegion.Torso, 10, weakPoint: false, tick: 5);
        ledger.Landed("player", BodyRegion.Torso, 10, weakPoint: false, tick: 9);
        ledger.Landed("player", BodyRegion.Head, 5, weakPoint: true, tick: 11);
        ledger.Landed("adventurer", BodyRegion.Torso, 25, weakPoint: false, tick: 12);

        Assert.Equal(3, ledger.Entries.Length);
        Assert.Equal(55, ledger.Total);
        var shares = ledger.Shares();
        Assert.Equal(2, shares.Length);
        Assert.Equal(1000, shares.Sum(s => s.Permille));
        Assert.Equal("player", shares[0].ActorId);
        Assert.Equal(546, shares[0].Permille);
        Assert.Equal(454, shares[1].Permille);
    }

    [Fact]
    public void ABlowThatDidNoHarm_CountsForNothing()
    {
        var ledger = new ContributionLedger("spawn.test.boss");
        ledger.Landed("player", BodyRegion.Limbs, 0, weakPoint: true, tick: 1);
        Assert.Empty(ledger.Entries);
        Assert.Empty(ledger.Shares());
        Assert.Null(ledger.Leading);
    }

    [Fact]
    public void TheLeadingAttackerIsTheOneTheEncounterWeighedMost()
    {
        var ledger = new ContributionLedger("spawn.test.boss");
        ledger.Landed("player", BodyRegion.Torso, 30, weakPoint: false, tick: 1);
        ledger.Landed("adventurer", BodyRegion.Torso, 10, weakPoint: false, tick: 2);
        Assert.Equal("player", ledger.Leading);
    }

    [Fact]
    public void APartIsNamedFromTheRegionABlowLandedOn()
    {
        Assert.Equal(ContributionParts.Head, ContributionParts.Of(BodyRegion.Head));
        Assert.Equal(ContributionParts.Torso, ContributionParts.Of(BodyRegion.Torso));
        Assert.Equal(ContributionParts.Limbs, ContributionParts.Of(BodyRegion.Limbs));
    }
}
