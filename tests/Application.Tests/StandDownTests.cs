// UNNAMED Application tests - standing a duty construct down rather than breaking it, end to end (CMB-43)
using UNNAMED.Domain.Combat;
using UNNAMED.World.Runtime;
using UNNAMED.World;

namespace UNNAMED.Application.Tests;

/// <summary>
/// CMB-43 at the world's own level: a guardian in front of the player, answered correctly, stands down and stops being a fight; answered
/// wrongly, it refuses in its own voice and stays one. Both outcomes are legal, and the world keeps the difference.
/// </summary>
public class StandDownTests
{
    private const string Warden = "creature.construct.gate_warden";

    private static readonly (double X, double Z) Yard = (48, 126.2);

    [Fact]
    public void AGuardianAnsweredWithItsOwnWord_StandsDown_AndTheWorldKeepsTheAnswer()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, Yard, 0,
            new[] { (Warden, 52.0, 126.2, "guard") });

        var stood = arena.Record<ConstructStoodDown>();
        var warden = arena.Simulation.Creatures.Single(c => c.DefId == Warden);

        Assert.Null(arena.Submit(new StandDownCommand(arena.Player, warden.Id, StandDownRoute.Word, "the gate is kept")));

        var answered = Assert.Single(stood);
        Assert.Equal(warden.Key, answered.Key);
        Assert.Equal(StandDownRoute.Word, answered.Route);
        Assert.Equal(CreatureCondition.StoodDown, arena.Simulation.Creatures.Single(c => c.Id == warden.Id).Condition);
    }

    [Fact]
    public void AGuardianAnsweredWithTheWrongWord_RefusesInItsOwnVoice_AndStaysAFight()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, Yard, 0,
            new[] { (Warden, 52.0, 126.2, "guard") });

        var refused = arena.Record<StandDownRefused>();
        var stood = arena.Record<ConstructStoodDown>();
        var warden = arena.Simulation.Creatures.Single(c => c.DefId == Warden);

        Assert.Equal("'No.' The ward takes its old stance like a door closing.",
            arena.Submit(new StandDownCommand(arena.Player, warden.Id, StandDownRoute.Word, "let me past")));

        Assert.Single(refused);
        Assert.Empty(stood);
        Assert.Equal(CreatureCondition.Alive, arena.Simulation.Creatures.Single(c => c.Id == warden.Id).Condition);
    }

    /// <summary>A construct content gives no terms is kill-only: the command has nothing to answer.</summary>
    [Fact]
    public void AConstructWithNoTerms_CannotBeReasonedWith()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, Yard, 0,
            new[] { ("creature.construct.animated_armour", 52.0, 126.2, "guard") });

        var armour = arena.Simulation.Creatures.Single(c => c.DefId == "creature.construct.animated_armour");
        Assert.Contains("cannot be reasoned with",
            arena.Submit(new StandDownCommand(arena.Player, armour.Id, StandDownRoute.Word, "the gate is kept")));
        Assert.Equal(CreatureCondition.Alive, arena.Simulation.Creatures.Single(c => c.Id == armour.Id).Condition);
    }
}
