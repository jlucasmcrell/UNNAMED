using System.Collections.Immutable;
using UNNAMED.Domain.Combat;

namespace UNNAMED.Application.Tests;

/// <summary>
/// CMB-39 multi-part bosses and CMB-41 arena hazards, as rules rather than as frames.
/// <para>
/// A part is structure riding on one controller, so a blow spends the part whose region it lands on while the creature keeps its
/// one health pool; a hazard is a function of the tick, so a damage zone opens and closes on its period and a rising floor lifts
/// on schedule and stops at its cap. Both are deterministic: same tick, same answer, every run.
/// </para>
/// </summary>
public class ArenaPartAndHazardTests
{
    private static AttackProfile Blow(string source) =>
        new(source, 10, 14, "physical_slash", 2_000, 20, 6, 30, 4);

    /// <summary>A boss of three parts: a pipe on the torso, a face on the head, a foot on the limbs.</summary>
    private static CreatureDefinition Pipe() =>
        new("creature.boss.test.pipe", "construct", 5, 400, ImmutableSortedDictionary<BodyRegion, int>.Empty,
            ImmutableSortedDictionary<string, double>.Empty, Blow("ability.test.sweep"), 1_000, 1_600, 200)
        {
            Parts = ImmutableArray.Create(
                new CreaturePart("long pipe", BodyRegion.Torso, 120, "effect.stagger"),
                new CreaturePart("gauge face", BodyRegion.Head, 60),
                new CreaturePart("footing", BodyRegion.Limbs, 90)),
        };

    [Fact]
    public void EveryPartAnswersForOneRegion_AndTheCreatureKeepsOnePool()
    {
        var boss = Pipe();
        Assert.Equal(3, boss.Parts.Length);
        Assert.Equal(3, boss.Parts.Select(part => part.Region).Distinct().Count());
        // the parts hold structure, not health: the creature still has the one pool its definition authored
        Assert.Equal(400, boss.MaxHealth);
        Assert.All(boss.Parts, part => Assert.True(part.Structure > 0));
    }

    [Fact]
    public void APartBreaksWhenItsStructureIsSpent_AndSaysWhatItDoes()
    {
        var pipe = new CreaturePart("long pipe", BodyRegion.Torso, 120, "effect.stagger");
        Assert.False(pipe.Broken(120));
        Assert.False(pipe.Broken(1));
        Assert.True(pipe.Broken(0));
        Assert.Equal("effect.stagger", pipe.BrokenEffect);
    }

    [Fact]
    public void ADamageZoneOpensOnItsPeriod_AndNoZoneIsAlwaysClosed()
    {
        var vent = new ArenaHazard("hazard.test.vent", "damage_zone", 0, 0, 6_000, 4, 180, 60, 0, 0, 0);
        Assert.True(vent.ActiveAt(0));
        Assert.True(vent.ActiveAt(59));
        Assert.False(vent.ActiveAt(60));
        Assert.False(vent.ActiveAt(179));
        Assert.True(vent.ActiveAt(180));

        var always = new ArenaHazard("hazard.test.always", "damage_zone", 0, 0, 6_000, 3, 0, 0, 0, 0, 0);
        Assert.True(always.ActiveAt(0));
        Assert.True(always.ActiveAt(10_000));
    }

    [Fact]
    public void AZoneOnlyHoldsWhatStandsInItsFootprint()
    {
        var vent = new ArenaHazard("hazard.test.vent", "damage_zone", 512_000, 878_000, 6_000, 4, 180, 60, 0, 0, 0);
        Assert.True(vent.Holds(512_000, 878_000));
        Assert.True(vent.Holds(517_000, 878_000));
        Assert.False(vent.Holds(519_000, 878_000));
        Assert.False(vent.Holds(512_000, 885_000));
    }

    [Fact]
    public void ARisingFloorLiftsOnSchedule_StopsAtItsCap_AndHasAnEscapeDeadline()
    {
        // the Undermeasure's water: 2 mm a tick to a 1800 mm cap
        var water = new ArenaHazard("hazard.test.water", "rising_floor", 0, 0, 24_000, 0, 0, 0, 2, 1_800, 0);
        Assert.True(water.IsRising);
        Assert.Equal(0, water.LevelAt(0));
        Assert.Equal(200, water.LevelAt(100));
        Assert.Equal(1_800, water.LevelAt(900));
        Assert.Equal(1_800, water.LevelAt(100_000));
        // wading depth 1200 mm is where leaving stops being optional
        Assert.Equal(600, water.ReachesAt(1_200));
        Assert.Equal(0, new ArenaHazard("hazard.test.still", "damage_zone", 0, 0, 1_000, 1, 1, 1, 0, 0, 0).LevelAt(50));
    }

    [Fact]
    public void AHazardWithNoPeriodNeverRises()
    {
        var zone = new ArenaHazard("hazard.test.zone", "damage_zone", 0, 0, 1_000, 1, 60, 30, 0, 0, 0);
        Assert.False(zone.IsRising);
        Assert.Equal(0, zone.LevelAt(100));
        Assert.Equal(long.MaxValue, zone.ReachesAt(1_200));
    }
}
