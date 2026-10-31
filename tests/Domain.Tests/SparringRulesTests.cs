using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Social;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// GEN-07's rules: a bout is the world's own fight at training weight, stopped at a health floor so nobody dies, and it leaves both fighters
/// better at their skill and thinking more of each other - more when the bout was close. A ground drills on its own schedule.
/// </summary>
public class SparringRulesTests
{
    private static readonly SparSite Ground = new("spar.test", "settlement.test", "the test yard", 0, 0, 0,
        FromHour: 17, ToHour: 19, EveryDays: 2, AnimationId: "ANIM.TEACH_DEMONSTRATE");

    private static SparFighter Fighter(string key, int level) =>
        new(key, key, level, SparRules.DrillBlow, SparRules.Defense(level), default!);

    /// <summary>The same draw the world keys a bout with: a run of samples in [0, 1).</summary>
    private static Func<uint, double> Rolls(ulong seed)
    {
        ulong hash = seed;
        return _ =>
        {
            hash = (hash ^ (hash >> 33)) * 0xFF51AFD7ED558CCDUL;
            return (hash >> 11) * (1.0 / 9007199254740992.0);
        };
    }

    [Fact]
    public void ABoutStopsAtTheFloor_AndNobodyGoesBelowIt()
    {
        var first = Fighter("npc.a", 4);
        var second = Fighter("npc.b", 4);
        var bout = SparRules.Fight(Ground, first, second, tick: 100, new CombatConstants(), Rolls(7));

        int floor = SparRules.MaxHealth(4) * SparRules.HealthFloorPercent / 100;
        Assert.True(bout.FirstHealth >= floor, $"the first was taken to {bout.FirstHealth}, below the floor {floor}");
        Assert.True(bout.SecondHealth >= floor, $"the second was taken to {bout.SecondHealth}, below the floor {floor}");
        Assert.True(bout.Rounds >= 1 && bout.Rounds <= SparRules.ExchangeCap);
        // Somebody came off the better of it, and the loser is the one left at the floor (or the lower of the two when the cap was reached).
        Assert.NotEqual(bout.FirstHealth, bout.SecondHealth);   // a bout of equals is broken by the exchange it lands last
        Assert.Contains(bout.WinnerKey, new[] { first.Key, second.Key });
        Assert.NotEqual(bout.WinnerKey, bout.LoserKey);
    }

    [Fact]
    public void ALevelApartIsFought_AndAWideGapIsNot()
    {
        int betterWon = 0, bouts = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var bout = SparRules.Fight(Ground, Fighter("npc.a", 5), Fighter("npc.b", 4), tick: 100, new CombatConstants(), Rolls(seed));
            bouts++;
            if (bout.WinnerKey == "npc.a")
                betterWon++;
        }
        // A level apart is a real bout: the better fighter usually wins, and the other one sometimes does.
        Assert.True(betterWon > bouts / 2, $"the better fighter won only {betterWon} of {bouts}");
        // Between equals the draw decides it, and both come out a winner sometimes.
        var winners = new HashSet<string>(StringComparer.Ordinal);
        for (ulong seed = 1; seed <= 40; seed++)
            winners.Add(SparRules.Fight(Ground, Fighter("npc.a", 5), Fighter("npc.b", 5), 100, new CombatConstants(), Rolls(seed)).WinnerKey);
        Assert.Equal(new[] { "npc.a", "npc.b" }, winners.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        // Six levels apart is not a bout at all: the same draw has no say.
        for (ulong seed = 1; seed <= 40; seed++)
            Assert.Equal("npc.veteran", SparRules.Fight(Ground, Fighter("npc.veteran", 8), Fighter("npc.recruit", 2), 100, new CombatConstants(), Rolls(seed)).WinnerKey);
    }

    [Fact]
    public void TheSameBoutRollsTheSameAgain_SoASaveReproducesIt()
    {
        var once = SparRules.Fight(Ground, Fighter("npc.a", 5), Fighter("npc.b", 4), tick: 100, new CombatConstants(), Rolls(11));
        var again = SparRules.Fight(Ground, Fighter("npc.a", 5), Fighter("npc.b", 4), tick: 100, new CombatConstants(), Rolls(11));
        Assert.Equal(once.WinnerKey, again.WinnerKey);
        Assert.Equal(once.Rounds, again.Rounds);
        Assert.Equal(once.FirstHealth, again.FirstHealth);
        Assert.Equal(once.SecondHealth, again.SecondHealth);
    }

    [Fact]
    public void ACloseBoutLeavesMoreRegard_AndBothLeaveWithSome()
    {
        var first = Fighter("npc.a", 5);
        var second = Fighter("npc.b", 5);
        var bout = SparRules.Fight(Ground, first, second, 100, new CombatConstants(), Rolls(3));

        // The winner earns more respect than the one they beat, and a bout leaves something between them either way.
        Assert.True(SparRules.RegardGain(bout, bout.WinnerKey) > SparRules.RegardGain(bout, bout.LoserKey));
        Assert.True(SparRules.RegardGain(bout, bout.LoserKey) > 0);
        // A bout decided on the last exchange is worth more than the same bout won clearly.
        var drawnOut = bout with { FirstHealth = bout.First.Defense.MaxHealth, SecondHealth = bout.Second.Defense.MaxHealth - 1 };
        Assert.True(SparRules.RegardGain(drawnOut, drawnOut.WinnerKey) > SparRules.RegardGain(bout with { FirstHealth = 30, SecondHealth = 100 }, bout.WinnerKey));
    }

    [Fact]
    public void AGroundDrillsOnItsSchedule_AndNowhereElse()
    {
        Assert.True(Ground.Live(day: 2, hour: 17));
        Assert.True(Ground.Live(day: 4, hour: 18));
        Assert.False(Ground.Live(day: 2, hour: 16));   // before the drill
        Assert.False(Ground.Live(day: 2, hour: 19));   // after it
        Assert.False(Ground.Live(day: 3, hour: 17));   // not its day
    }
}
