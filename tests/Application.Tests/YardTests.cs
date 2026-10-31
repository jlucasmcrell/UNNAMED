using UNNAMED.Domain.Social;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// GEN-07 in the world: a settlement's yard offers only bouts its adventurers can actually fight, the same day runs the same bout, and a bet on a
/// bout that does not exist is refused rather than paid. The fixture's own settlement may not hold a matched pair, so what is asserted here is the
/// yard's behaviour, not the fixture's cast.
/// </summary>
public class YardTests
{
    [Fact]
    public void AYardNeverOffersABoutItsAdventurersCannotFight()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (60.3, 140.3), 0, Array.Empty<(string, double, double, string)>());
        var sim = arena.Simulation;

        foreach (string settlement in sim.Adventurers.Select(a => a.Settlement).Distinct())
        {
            foreach (var bout in sim.Yard(settlement))
            {
                Assert.True(SparRules.CloseEnough(bout.FirstLevel, bout.SecondLevel));
                Assert.NotEqual(bout.FirstKey, bout.SecondKey);
                Assert.Equal(settlement, bout.SettlementId);
            }
        }
    }

    [Fact]
    public void ARunBout_IsTheSameBoutOnTheSameDay_AndAlwaysHasAWinner()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (60.3, 140.3), 0, Array.Empty<(string, double, double, string)>());
        var sim = arena.Simulation;

        // A bout the yard would call: two bodies two levels apart in the same yard.
        var bout = new YardBout("settlement.test", "npc.a", "Ada", 4, "npc.b", "Berrin", 3, SparRules.OddsPermille(4, 3), Open: true);
        var once = sim.RunBout(bout, day: 12);
        var again = sim.RunBout(bout, day: 12);
        var tomorrow = sim.RunBout(bout, day: 13);

        Assert.Equal(once.WinnerKey, again.WinnerKey);
        Assert.Equal(once.Margin, again.Margin);
        Assert.NotEqual(once.WinnerKey, once.LoserKey);
        Assert.Contains(once.WinnerKey, new[] { "npc.a", "npc.b" });

        // A different day is a different bout: the yard is not a fixed script.
        Assert.True(tomorrow.WinnerKey != once.WinnerKey || tomorrow.Margin != once.Margin);
    }

    [Fact]
    public void ABackedBoutThatDoesNotExist_IsRefused_AndTakesNothingFromThePurse()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (60.3, 140.3), 0, Array.Empty<(string, double, double, string)>());
        long before = arena.Simulation.Player.Currency;

        var refusal = arena.Submit(new BetOnBoutCommand(arena.Player, "settlement.nowhere", "npc.nobody", 5));
        Assert.Equal("settlement.nowhere has no bout to back", refusal);
        Assert.Equal(before, arena.Simulation.Player.Currency);
    }
}
