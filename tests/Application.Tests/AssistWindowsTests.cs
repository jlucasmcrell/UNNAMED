// UNNAMED Application tests - GEN-10 in the fight: generous timing widens the player's own parry and dodge, and nothing else.

using UNNAMED.Domain;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Combat;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The assist reaches the fight through <see cref="SimulationSetup.TimingWindows"/>, read where a window opens. The parry's window is the ticks a
/// used parry waits for a blow; the dodge's is the ticks it cannot be struck through. Under generous timing both are longer - and the creature's
/// own action is judged by the fight's constants, not the player's, so no enemy dodge grows.
/// </summary>
public class AssistWindowsTests
{
    private const string Parry = "ability.blade.parry";


    private static readonly (double X, double Z) Open = (120, 60);

    /// <summary>The character as the test wants them: the blade parried at a band that can hold it.</summary>
    private static PlayerRecord Knowing(PlayerRecord r) =>
        r.WithProgression(r.Progression with
        {
            Skills = r.Progression.Skills.SetItem("skill.one_hand_blade", new SkillState(20, 0)),
            Known = r.Progression.Known.SetItem(Parry, new KnownTechnique(LearningSource.Teacher, "npc.ashen_hollow.kera_voss", 0)),
        });

    private static Arena At(GameSession session, TimingWindows windows) =>
        Arena.OpenCreatures(session, session.Setup with { TimingWindows = () => windows }, Open, 0,
            new[] { (Arena.Wolf, Open.X, Open.Z + 2.5, "pack_hunter") }, Knowing);

    [Theory]
    [InlineData(TimingWindows.Normal)]
    [InlineData(TimingWindows.Generous)]
    public void AUsedParryWaitsForItsWindow_WidenedWhenThePlayerAsksForGenerousTiming(TimingWindows windows)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, windows);
        int shipped = session.Setup.Techniques[Parry].ParryTicks;

        Assert.Null(arena.Submit(new TechniqueCommand(arena.Player, Parry)));
        Assert.Equal(AssistRules.WindowTicks(shipped, windows), arena.Simulation.Combat.ParryTicksLeft);
    }

    [Fact]
    public void GenerousTiming_HoldsTheDodgeOpenLonger_ThanTheTunedWindow()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        int dodgeTicks = session.Setup.Combat.Constants.DodgeTicks;

        var tuned = At(session, TimingWindows.Normal);
        Assert.Null(tuned.Submit(new DodgeCommand(tuned.Player, 1_000, 0)));
        var generous = At(session, TimingWindows.Generous);
        Assert.Null(generous.Submit(new DodgeCommand(generous.Player, 1_000, 0)));

        // The tuned dodge is spent after its own ticks; the generous one is still a dodge at that moment, and ends on its own wider tick.
        tuned.Tick(dodgeTicks + 1);
        generous.Tick(dodgeTicks + 1);
        Assert.NotEqual(CombatPhase.Dodge, tuned.Simulation.Combat.Phase);
        Assert.Equal(CombatPhase.Dodge, generous.Simulation.Combat.Phase);

        int generousTicks = AssistRules.WindowTicks(dodgeTicks, TimingWindows.Generous);
        Assert.True(generousTicks > dodgeTicks);
        generous.Tick(generousTicks - dodgeTicks);
        Assert.NotEqual(CombatPhase.Dodge, generous.Simulation.Combat.Phase);
    }

    [Fact]
    public void TheAssistIsThePlayers_SoACreaturesOwnDodgeIsUntouched()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The rule is the domain's own: only the player's path is judged by the widened constants, and the constants a creature reads are the
        // tuned ones. Two setups therefore differ in exactly one field, and the tuned one is the object the fight was built with.
        var constants = session.Setup.Combat.Constants;
        var widened = AssistRules.Windowed(constants, TimingWindows.Generous);
        Assert.Equal(AssistRules.WindowTicks(constants.DodgeTicks, TimingWindows.Generous), widened.DodgeTicks);
        Assert.Equal(constants.DodgeTicks, constants.DodgeTicks);
    }
}
