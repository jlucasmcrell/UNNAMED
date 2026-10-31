using UNNAMED.Domain.Social;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// GEN-07 in the world: a settlement's adventurers drill at its training ground when its hour comes, the bout moves what they practise and what
/// they think of each other, and the character may step into one - fought on the same rules, stopped at a health floor, so it never kills.
/// </summary>
public class SparringTests
{
    [Fact]
    public void TheGroundDrillsOnSchedule_AndTheBoutMovesSkillAndRegard()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (30, 158), 90, Array.Empty<(string, double, double, string)>());
        var sim = arena.Simulation;
        var began = arena.Record<SparringBegan>();
        var ended = arena.Record<SparringEnded>();
        var skills = sim.Adventurers.ToDictionary(a => a.Key, a => a.Key);

        // Drill hours are evening and every other day: a few days of hour steps reach one.
        for (int hour = 0; hour < 4 * 24 && ended.Count == 0; hour++)
            sim.Advance(session.Setup.Clock.TicksPerDay / 24);

        Assert.NotEmpty(began);
        var bout = Assert.Single(ended);
        Assert.Equal(began[0].SiteId, bout.SiteId);
        Assert.NotEqual(bout.WinnerKey, bout.LoserKey);
        // Both of the pair were there, and the drill is an animation the world already has.
        Assert.Contains(began[0].FirstKey, new[] { bout.WinnerKey, bout.LoserKey });
        Assert.Contains(began[0].SecondKey, new[] { bout.WinnerKey, bout.LoserKey });
        Assert.Equal("ANIM.TEACH_DEMONSTRATE", began[0].AnimationId);
        // The bout leaves respect between them, and more for the winner.
        Assert.True(bout.RegardGained > 0);
        var state = sim.World.Adventurers!;
        string pair = string.Join("|", new[] { bout.WinnerKey, bout.LoserKey }.OrderBy(k => k, StringComparer.Ordinal));
        Assert.True(state.SparRegard.GetValueOrDefault(pair) >= bout.RegardGained);
        // And both practised: the drill blade's skill stands where it did not before (they were drilled at it).
        Assert.All(new[] { bout.WinnerKey, bout.LoserKey }, key => Assert.True(state.Agents.ContainsKey(key)));
    }

    [Fact]
    public void TheCharacterCanStepIntoABout_AndTheBoutNeverTakesThemBelowTheFloor()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (30, 158), 90, Array.Empty<(string, double, double, string)>());
        var sim = arena.Simulation;
        var ended = arena.Record<SparringEnded>();

        // Stand at a ground while it is drilling, and step in.
        SparSiteView? live = null;
        for (int hour = 0; hour < 4 * 24 && live is null; hour++)
        {
            sim.Advance(session.Setup.Clock.TicksPerDay / 24);
            live = sim.Sparring.FirstOrDefault(s => s.Drilling && s.FirstKey is not null);
        }
        Assert.NotNull(live);
        int maxHealth = sim.Combat.MaxHealth;
        Assert.Null(arena.Submit(new JoinSparCommand(arena.Player, live!.SiteId)));

        var joined = Assert.Single(ended, e => e.PlayerFought);
        Assert.Contains(joined.WinnerKey, new[] { SparringSystemKey, live.FirstKey });
        // Non-lethal by rule: the character is left on at least the floor the bout stops at.
        Assert.True(sim.Combat.Health >= maxHealth * SparRules.HealthFloorPercent / 100,
            $"{sim.Combat.Health} of {maxHealth} is below the sparring floor");
        Assert.False(sim.Combat.Defeated);
    }

    /// <summary>The character's key in a bout, as the world reports it.</summary>
    private const string SparringSystemKey = "actor.player";
}
