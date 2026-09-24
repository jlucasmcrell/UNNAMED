using UNNAMED.Domain.Progression;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The character sheet's one action (the owner's M6 playtest; PROTOTYPE.md C11): a point from a level-up spent on an attribute a derived
/// value reads, changing that value - and refused with no point to spend, or on an attribute nothing reads yet.
/// </summary>
public class CharacterSheetTests
{
    [Fact]
    public void ALevelUpsPoint_SpentOnEndurance_RaisesHealthAndStamina_AndThenThereIsNoneLeft()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (70, 152), 0, Array.Empty<(string, double, double, string)>(),
            r => r.WithProgression(r.Progression with { Level = 2, UnspentAttributePoints = 1 }));
        var spent = arena.Record<AttributeSpent>();
        var before = arena.Simulation.Player.Stats;

        Assert.Null(arena.Submit(new SpendAttributeCommand(arena.Player, CharacterAttribute.Endurance)));

        var after = arena.Simulation.Player.Stats;
        var rules = session.Setup.Progression.Derived;
        Assert.Equal(before.HealthMax + rules.HealthMax.PerPoint[CharacterAttribute.Endurance], after.HealthMax);
        Assert.Equal(before.StaminaMax + rules.StaminaMax.PerPoint[CharacterAttribute.Endurance], after.StaminaMax);
        Assert.Equal(new AttributeSpent(CharacterAttribute.Endurance, session.Setup.Progression.AttributeBase + 1, 0, spent.Single().Tick), spent.Single());
        Assert.Equal("no attribute points to spend", arena.Submit(new SpendAttributeCommand(arena.Player, CharacterAttribute.Might)));
    }

    [Fact]
    public void APoint_OnAnAttributeNothingReadsYet_IsRefused_AndKept()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (70, 152), 0, Array.Empty<(string, double, double, string)>(),
            r => r.WithProgression(r.Progression with { Level = 2, UnspentAttributePoints = 1 }));

        Assert.Equal(new[] { CharacterAttribute.Might, CharacterAttribute.Endurance, CharacterAttribute.Will }.Order(), session.Setup.Progression.Derived.Live);
        Assert.Contains("not in play", arena.Submit(new SpendAttributeCommand(arena.Player, CharacterAttribute.Agility)));
        Assert.Equal(1, arena.Simulation.Player.Progression.UnspentAttributePoints);
    }
}
