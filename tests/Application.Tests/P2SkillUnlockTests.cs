// UNNAMED Application tests - Playtest 2's first-hour skill unlocks, learned the way a player learns them.

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Six techniques an ordinary player reaches in the first hour, two for each discipline the hollow trains. Every one is a lesson a real teacher
/// already offers (an NPC's <c>teaches</c> block) - no new system, no cap change - and the test takes each in a fresh session: the first rung of a
/// discipline at skill 0, the second once the skill reaches the familiar band, which is what the level asks of the player.
/// </summary>
public sealed class P2SkillUnlockTests
{
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string Durn = "npc.cairnwold.durn_skell";

    private static readonly (double X, double Z) AtKera = (60.3, 140.3);
    private static readonly (double X, double Z) AtDurn = (980.7, 674.4);

    /// <summary>A fresh character, with only the one skill the lesson needs raised to its band.</summary>
    private static PlayerRecord With(PlayerRecord r, string? skill, int level) =>
        new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory, r.Progression, r.FacingMdeg, r.Discoveries,
                r.Equipment, r.Currency + 100, r.Effects, r.Relationships, r.Conversations)  // a character who has trained to a band has picked up coin
            .WithProgression(skill is null
                ? r.Progression
                : r.Progression with { Skills = r.Progression.Skills.SetItem(skill, new SkillState(level, 0)) });

    private static Arena At(GameSession session, (double X, double Z) at, string? skill, int level) =>
        Arena.OpenCreatures(session, session.Setup, (Harness.Hollow.X + at.X, Harness.Hollow.Z + at.Z), 0,
            Array.Empty<(string, double, double, string)>(), r => With(r, skill, level), origin: Harness.Region);

    /// <summary>A lesson is something a teacher gives in conversation: stand with them, open the talk, then ask.</summary>
    private static string? Learn(Arena arena, string npc, string ability)
    {
        var who = arena.Simulation.Npcs.Single(n => n.Id == npc);
        Assert.Null(arena.Simulation.Submit(new Relocate(who.Body)));
        arena.Tick();
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, npc)));
        arena.Tick();
        return arena.Submit(new LearnFromTeacherCommand(arena.Player, npc, ability));
    }

    private static bool Known(Arena arena, string ability) =>
        arena.Simulation.Player.Progression.Known.ContainsKey(ability);

    [Theory]
    [InlineData(Kera, "ability.blade.guarded_cut", null, 0)]
    [InlineData(Kera, "ability.bow.steady_aim", null, 0)]
    [InlineData(Durn, "ability.polearm.set_against_the_charge", null, 0)]
    public void TheFirstRungOfEachDiscipline_IsLearnedInAFreshSession(string npc, string ability, string? skill, int level)
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), npc == Kera ? AtKera : AtDurn, skill, level);

        Assert.False(Known(arena, ability));
        string? refused = Learn(arena, npc, ability);
        Assert.True(refused is null, $"{ability} refused: {refused}");
        Assert.True(Known(arena, ability), $"{ability} was not learned");
    }

    [Theory]
    [InlineData(Kera, "ability.blade.hamstring", "skill.one_hand_blade")]
    [InlineData(Kera, "ability.bow.parting_shot", "skill.bow")]
    [InlineData(Durn, "ability.polearm.hooked_pull", "skill.polearm")]
    public void TheSecondRung_AsksForTheProficientBand(string npc, string ability, string skill)
    {
        using var profile = new TempProfile();
        var at = npc == Kera ? AtKera : AtDurn;

        // a character who has never practised the discipline is refused, in the rules' own words
        var raw = At(Harness.Boot(profile), at, null, 0);
        string? refusal = Learn(raw, npc, ability);
        Assert.NotNull(refusal);
        Assert.False(Known(raw, ability));

        // and one at the proficient band takes it
        using var experienced = new TempProfile();
        var arena = At(Harness.Boot(experienced), at, skill, 30);
        string? learned = Learn(arena, npc, ability);
        Assert.True(learned is null, $"{ability} refused at the proficient band: {learned}");
        Assert.True(Known(arena, ability), $"{ability} was not learned at the proficient band");
    }
}
