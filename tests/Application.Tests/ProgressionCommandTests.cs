// UNNAMED Application tests - the two progression decisions a player pays for (queue item 7).

using UNNAMED.Domain;
using UNNAMED.Domain.Progression;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Designating a discipline and respecing an attribute build, through the commands a player submits: what they cost, what they change, what they
/// refuse, and that both survive a save.
/// </summary>
public sealed class ProgressionCommandTests
{
    private const string Blade = "skill.one_hand_blade";
    private const string Bow = "skill.bow";

    private static Arena Open(GameSession session, long coin, int unspent, params (CharacterAttribute Attribute, int Points)[] spent)
    {
        var allocation = spent.Aggregate(
            System.Collections.Immutable.ImmutableSortedDictionary<CharacterAttribute, int>.Empty,
            (map, pair) => map.SetItem(pair.Attribute, pair.Points));
        PlayerRecord Change(PlayerRecord record) =>
            new(record.Id, record.Name, record.XMm, record.YMm, record.ZMm, record.AppearanceSeed, record.Inventory,
                    record.Progression with { UnspentAttributePoints = unspent, Allocation = allocation },
                    record.FacingMdeg, record.Discoveries, record.Equipment, coin, record.Effects, record.Relationships,
                    record.Conversations);

        return Arena.OpenCreatures(session, session.Setup, (Harness.Hollow.X + 60.3, Harness.Hollow.Z + 140.3), 0,
            Array.Empty<(string, double, double, string)>(), Change, origin: Harness.Region);
    }

    [Fact]
    public void DesignatingADiscipline_CostsCoin_AndIsKept()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Open(session, coin: 1_500, unspent: 0);

        Assert.Null(arena.Submit(new DesignateSkillCommand(arena.Player, Blade)));
        Assert.Equal(Blade, arena.Simulation.Player.Progression.DesignatedSkill);
        Assert.Equal(1_000, arena.Simulation.Player.Currency);        // the content's designation cost

        // switching costs again, and the same discipline twice is refused before the coin moves
        Assert.Null(arena.Submit(new DesignateSkillCommand(arena.Player, Bow)));
        Assert.Equal(Bow, arena.Simulation.Player.Progression.DesignatedSkill);
        Assert.Equal(500, arena.Simulation.Player.Currency);

        // and back again, at the same price, leaving the purse empty
        Assert.Null(arena.Submit(new DesignateSkillCommand(arena.Player, Blade)));
        Assert.Equal(Blade, arena.Simulation.Player.Progression.DesignatedSkill);
        Assert.Equal(0, arena.Simulation.Player.Currency);

        Assert.NotNull(arena.Submit(new DesignateSkillCommand(arena.Player, "not.a.skill")));
    }

    [Fact]
    public void ARespec_GivesThePointsBack_AndChargesForTheLevel()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Open(session, coin: 1_000, unspent: 1, (CharacterAttribute.Might, 3), (CharacterAttribute.Endurance, 2));

        int level = arena.Simulation.Player.Progression.Level;
        int cost = session.Setup.Progression.RespecCost(level);
        Assert.Equal(100, cost);                                       // a new character is level 1 and pays the base
        Assert.Null(arena.Submit(new RespecAttributesCommand(arena.Player)));
        Assert.Empty(arena.Simulation.Player.Progression.Allocation);
        Assert.Equal(6, arena.Simulation.Player.Progression.UnspentAttributePoints);   // 1 unspent + 5 spent
        Assert.Equal(1_000 - cost, arena.Simulation.Player.Currency);
    }

    [Fact]
    public void ARespecWithNothingSpent_IsRefused_AndCostsNothing()
    {
        using var profile = new TempProfile();
        var arena = Open(Harness.Boot(profile), coin: 500, unspent: 0);
        Assert.Equal("no attribute points are spent", arena.Submit(new RespecAttributesCommand(arena.Player)));
        Assert.Equal(500, arena.Simulation.Player.Currency);
    }

    [Fact]
    public void ADesignationAndARespec_SurviveASaveAndLoad()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Open(session, coin: 2_000, unspent: 2, (CharacterAttribute.Might, 4));

        Assert.Null(arena.Submit(new DesignateSkillCommand(arena.Player, Blade)));
        Assert.Null(arena.Submit(new RespecAttributesCommand(arena.Player)));

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("progress"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(),
            session.Content, arena.Simulation.WorldTick, 0));
        var loaded = Arena.Resume(arena.Simulation.Setup, store.Load(SaveSlots.Manual("progress"),
            new LoadContext(session.Generator, session.Content, new Registry())));

        var progression = loaded.Simulation.Player.Progression;
        Assert.Equal(Blade, progression.DesignatedSkill);
        Assert.Empty(progression.Allocation);
        Assert.Equal(6, progression.UnspentAttributePoints);
    }
}
