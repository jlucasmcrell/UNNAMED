// UNNAMED Domain tests - NPC expeditions and sparring (GEN-06, GEN-07)
using System.Collections.Immutable;
using UNNAMED.Domain.Social;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// GEN-06 and GEN-07's rules: a party goes to a real area for days and comes back with something, and a town yard runs bouts a player can watch,
/// bet on or join. Deterministic: the same seed and the same day always give the same trip and the same bout.
/// </summary>
public class ExpeditionRulesTests
{
    private static readonly ImmutableArray<ExpeditionArea> Areas = ImmutableArray.Create(
        new ExpeditionArea("spawn.field_ruins", "the Field Ruins", DistanceDays: 1, DangerLevel: 3, DaysOfWork: 2),
        new ExpeditionArea("spawn.barrowworks", "the Barrowworks", DistanceDays: 2, DangerLevel: 5, DaysOfWork: 3),
        new ExpeditionArea("spawn.foldscar", "the Foldscar", DistanceDays: 1, DangerLevel: 9, DaysOfWork: 4));

    [Fact]
    public void APartyWorksTheHardestAreaItCanSurvive_NotTheNearestOrTheHardest()
    {
        // A level-3 party can take danger 5 with the margin; danger 9 is beyond it.
        var chosen = ExpeditionRules.Choose(Areas, level: 3);
        Assert.Equal("spawn.barrowworks", chosen!.AreaKey);

        // A level-1 party cannot reach that far: it takes the level-3 ground instead.
        Assert.Equal("spawn.field_ruins", ExpeditionRules.Choose(Areas, level: 1)!.AreaKey);

        // A party that can take anything picks the most dangerous, then the nearest of the ones that tie.
        Assert.Equal("spawn.foldscar", ExpeditionRules.Choose(Areas, level: 9)!.AreaKey);
    }

    [Fact]
    public void AnExpeditionIsAwayForDays_AndNeverLessThanTheMinimum()
    {
        var near = ExpeditionRules.Plan(Areas[0], ImmutableArray.Create("a", "b"), day: 10);
        var far = ExpeditionRules.Plan(Areas[1], ImmutableArray.Create("a", "b"), day: 10);

        Assert.True(near.ReturnDay - near.LeftDay >= ExpeditionRules.MinimumDays);
        Assert.True(far.ReturnDay - far.LeftDay >= ExpeditionRules.MinimumDays);
        Assert.True(far.ReturnDay > near.ReturnDay);   // the further, slower work keeps them out longer
        Assert.True(near.IsAway(11));
        Assert.False(near.IsAway(near.ReturnDay));
    }

    [Fact]
    public void TheSameMidnightAndArea_PlanTheSameTrip_AndTheSameSeedTheSameOutcome()
    {
        var one = ExpeditionRules.Plan(Areas[1], ImmutableArray.Create("kera", "durn"), day: 40);
        var again = ExpeditionRules.Plan(Areas[1], ImmutableArray.Create("durn", "kera"), day: 40);
        Assert.Equal(one.Seed, again.Seed);
        Assert.Equal(one.Party, again.Party);   // the party is sorted, so who was listed first does not matter

        string[] loot = { "item.material.wolf_hide", "item.material.iron_ingot", "item.material.cured_leather" };
        var trip = ExpeditionRules.Resolve(one, Areas, loot, toDay: one.ReturnDay);
        var same = ExpeditionRules.Resolve(again, Areas, loot, toDay: one.ReturnDay);
        Assert.Equal(trip.Coin, same.Coin);
        Assert.Equal(trip.Loot, same.Loot);
        Assert.Equal(trip.News, same.News);
        Assert.Equal(trip.Injured, same.Injured);
    }

    [Fact]
    public void APartyThatComesBack_BringsCoin_GoodsAndNewsAboutSomewhereReal()
    {
        var trip = ExpeditionRules.Plan(Areas[1], ImmutableArray.Create("kera", "durn", "tavar"), day: 40);
        var back = ExpeditionRules.Resolve(trip, Areas, new[] { "item.material.iron_ingot", "item.material.wolf_hide" }, trip.ReturnDay);

        Assert.True(back.Coin > 0);
        Assert.Equal("the Barrowworks", back.AreaName);
        Assert.Contains("the Barrowworks", back.News);
        Assert.Contains(trip.ReturnDay.ToString(), back.News);
        Assert.Equal(trip.Party, back.Party);
        Assert.False(back.Lost);
    }

    [Fact]
    public void AnAreaBeyondTheParty_CanLoseSomeone_AndSaysSo()
    {
        // One body, danger 9: the seed decides, but when it happens the news is plain about it.
        var trip = ExpeditionRules.Plan(Areas[2], ImmutableArray.Create("alone"), day: 3);
        var back = ExpeditionRules.Resolve(trip, Areas, Array.Empty<string>(), trip.ReturnDay);
        if (back.Lost)
        {
            Assert.Contains("not all of them came back", back.News);
            Assert.Null(back.Loot.IsDefault ? null : back.News);
        }
        else
        {
            Assert.Contains("came back from the Foldscar", back.News);
        }
    }

    [Fact]
    public void ABoutIsFairToWatch_AndABetPaysAtTheOdds()
    {
        // The odds favour the underdog: a level gap of two pays half again.
        Assert.Equal(1_500, SparRules.OddsPermille(2, 4));
        Assert.Equal(1_000, SparRules.OddsPermille(3, 3));
        Assert.Equal(125, SparRules.Payout(100, 1_250, won: true));   // stake and odds back
        Assert.Equal(25, SparRules.Payout(100, 1_250, won: true) - 100);   // a quarter over on a won bet
        Assert.Equal(0, SparRules.Payout(100, 1_250, won: false));
    }

    [Fact]
    public void ABoutBetweenEquals_IsSettledByTheSeed_AndAlwaysHasAWinnerAndALoser()
    {
        var one = SparRules.Bout("kera", 5, "durn", 5, seed: 7);
        var again = SparRules.Bout("kera", 5, "durn", 5, seed: 7);
        Assert.Equal(one, again);
        Assert.NotEqual(one.Winner, one.Loser);
        Assert.Contains(one.Winner, new[] { "kera", "durn" });
    }

    [Fact]
    public void TheHigherLevelUsuallyWins_AndAYardDoesNotMatchAHopelessPair()
    {
        int upsets = 0;
        for (ulong seed = 0; seed < 40; seed++)
        {
            var bout = SparRules.Bout("strong", 8, "weak", 1, seed);
            if (bout.Winner == "weak")
                upsets++;
        }
        Assert.True(upsets <= 4, $"the weaker fighter won {upsets} of 40 bouts");

        Assert.True(SparRules.CloseEnough(3, 5));
        Assert.False(SparRules.CloseEnough(1, 8));
    }

    [Fact]
    public void AHardBoutCanHurt_AndAGentleOneDoesNot()
    {
        Assert.True(SparRules.Hurts(new SparBout("a", "b", Margin: SparRules.HurtingMargin)));
        Assert.False(SparRules.Hurts(new SparBout("a", "b", Margin: 0)));
    }
}
