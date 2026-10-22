using System.Collections.Immutable;
using UNNAMED.Domain.Pets;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// Spec 43's pet rules, read without a world: the odds of taming a wild animal, the party cap the animals share with companions, a pet's
/// own skills levelling with use, the lure, the wound that sends one home instead of killing it, and the encounter scaling a party earns
/// (the owner's rulings of 2026-10-21, 1 to 5).
/// </summary>
public class PetsTests
{
    private static readonly PetRules Rules = new(
        new PetTamingRules(15, 4, 20, 15, -15, 90, 50, new Dictionary<string, int> { ["wary"] = 0, ["bold"] = -10, ["skittish"] = 15 }
            .ToImmutableSortedDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)),
        new PetSkillRules(ImmutableArray.Create(3, 8, 15)),
        new PetLureRules(12_000, 18_000, 6, 20, 1_800),
        new PetRetireRules(1, 2, 0, 0),
        new EncounterRules(15, 165, 0.035));

    [Fact]
    public void Taming_CountsTheApproachTheFoodAndTheHandling()
    {
        // The same animal, reached for with a bare hand and after a small fortune of meat, unseen or seen.
        int bare = PetTaming.Chance(Rules.Tame, 0, 0, 2, 2, 50, 50, unnoticed: false, "wary");
        int quiet = PetTaming.Chance(Rules.Tame, 3, 25, 2, 2, 50, 50, unnoticed: true, "wary");
        Assert.Equal(15, bare);
        Assert.Equal(15 + 12 + 25 + 20, quiet);
        // Wounded animals are easier; a skittish one is not; one past the character's level never is.
        Assert.Equal(quiet + 15, PetTaming.Chance(Rules.Tame, 3, 25, 2, 2, 10, 50, unnoticed: true, "wary"));
        Assert.Equal(quiet - 15, PetTaming.Chance(Rules.Tame, 3, 25, 3, 2, 50, 50, unnoticed: true, "wary"));
        Assert.True(PetTaming.Chance(Rules.Tame, 0, 0, 2, 2, 50, 50, unnoticed: false, "bold")
                    < PetTaming.Chance(Rules.Tame, 0, 0, 2, 2, 50, 50, unnoticed: false, "skittish"));
        // Never certain, and never nothing: a wild thing is a wild thing.
        Assert.Equal(90, PetTaming.Chance(Rules.Tame, 60, 60, 1, 9, 1, 50, unnoticed: true, "skittish"));
        Assert.Equal(1, PetTaming.Chance(Rules.Tame, 0, 0, 9, 2, 50, 50, unnoticed: false, "wary"));
    }

    [Fact]
    public void ThePartyCap_IsOneNumberForPetsAndCompanions()
    {
        // Owner ruling 2: any mix, up to config.party's max_members.
        Assert.True(PetParty.Room(companions: 2, pets: 1, max: 4));
        Assert.False(PetParty.Room(companions: 2, pets: 2, max: 4));
        Assert.Null(PetParty.Refusal(0, 1, 4));
        Assert.Equal("your party is full (4 with you)", PetParty.Refusal(3, 1, 4));
    }

    [Fact]
    public void APetsOwnSkill_LevelsWithUse_AndStopsAtFour()
    {
        var skill = new PetSkill(PetSkillKind.Bite, 1, 0);
        var first = PetSkills.Practice(skill, Rules.Skills);
        Assert.False(first.Levelled);
        Assert.Equal((1, 1), (first.Skill.Level, first.Skill.Uses));
        // The third use is the second level, the eighth the third, the fifteenth the fourth.
        var third = PetSkills.Practice(skill with { Uses = 2 }, Rules.Skills);
        Assert.True(third.Levelled);
        Assert.Equal(2, third.Skill.Level);
        Assert.Equal(3, PetSkills.Practice(skill with { Uses = 7 }, Rules.Skills).Skill.Level);
        Assert.Equal(4, PetSkills.Practice(skill with { Uses = 14 }, Rules.Skills).Skill.Level);
        // Past the last threshold nothing is learnt, and the record stops growing.
        var full = new PetSkill(PetSkillKind.Bite, 4, 15);
        Assert.Equal((4, 15), (PetSkills.Practice(full, Rules.Skills).Skill.Level, PetSkills.Practice(full, Rules.Skills).Skill.Uses));
        // And the level is worth something: a tenth more bite per level past the first.
        Assert.Equal(1.3, PetSkills.DamageMultiplier(full), 3);
        Assert.Equal(1.0, PetSkills.DamageMultiplier(null));
    }

    [Fact]
    public void TheLure_CanFail_AndNeverCertainlyWorks()
    {
        var lure = new PetSkill(PetSkillKind.Lure, 1, 0);
        Assert.Equal(100 - 12, PetLure.Chance(Rules.Lure, lure, creatureLevel: 2));
        Assert.Equal(Rules.Lure.MinPercent, PetLure.Chance(Rules.Lure, lure, creatureLevel: 20));
        Assert.True(PetLure.Chance(Rules.Lure, new PetSkill(PetSkillKind.Lure, 4, 15), creatureLevel: 2)
                    > PetLure.Chance(Rules.Lure, lure, creatureLevel: 2));
        // Closer than the rules' travel is led the whole way; a stronger enemy is led less far.
        Assert.True(PetLure.Travel(Rules.Lure, 1) > PetLure.Travel(Rules.Lure, 4));
    }

    [Fact]
    public void ABadlyWoundedPet_GoesHomeAndMends_RatherThanDying()
    {
        // Owner ruling 1: at no health it is home for content's days and then collected again.
        int days = PetWounds.Days(Rules.Retire, _ => 0.0);
        Assert.Equal(1, days);
        Assert.Equal(2, PetWounds.Days(Rules.Retire, _ => 0.99));
        long ticksPerDay = 72_000;
        long until = PetWounds.RecoversAt(1_000, days, ticksPerDay);
        Assert.False(PetWounds.Home(1_000 + ticksPerDay - 1, until));
        Assert.True(PetWounds.Home(until, until));
    }

    [Fact]
    public void AWaryAnimal_DoesNotTakeAHardOrder_AndAnEarnedBondDoes()
    {
        // Owner ruling 1: a tamed animal starts wary and obeys less. The bond it builds is what buys obedience.
        Assert.Equal(25, PetBond.Obey(PetBond.Tamed, PetOrder.Pull));
        Assert.Equal(40, PetBond.Obey(PetBond.Tamed, PetOrder.WaitHere));
        Assert.Equal(100, PetBond.Obey(PetBond.Sure, PetOrder.Pull));
        Assert.True(PetBond.Obey(40, PetOrder.AttackTarget) > PetBond.Obey(PetBond.Tamed, PetOrder.AttackTarget));
    }

    [Fact]
    public void Encounters_ScaleWithTheParty_AndNotWithTheCharacter()
    {
        // Owner ruling 5: the solo character meets the fight exactly as authored.
        Assert.Equal(EncounterScale.Authored, Encounters.Of(Encounters.Points(0, 0), Rules.Encounter));
        // One pet is one point; a full party of four pets (levels 2 to 4) is six and a quarter.
        var one = Encounters.Of(Encounters.Points(1, 1), Rules.Encounter);
        var full = Encounters.Of(Encounters.Points(4, 9), Rules.Encounter);
        // One pet at level 2 is one and a quarter points, so a little over content's 15 percent a point.
        Assert.Equal(119, one.Percent);
        Assert.Equal(0, one.Levels);
        Assert.Equal(165, full.Percent);
        Assert.Equal(2, full.Levels);
        Assert.True(full.HealthFactor > one.HealthFactor);
        // And it stops at content's cap: a party of ten does not become an unkillable wall.
        Assert.Equal(165, Encounters.Of(Encounters.Points(10, 40), Rules.Encounter).Percent);
    }

    [Fact]
    public void TheRules_AreRefusedWhenTheyCannotMeanAnything()
    {
        Assert.Null(Rules.Problem());
        Assert.NotNull((Rules with { Tame = Rules.Tame with { ChannelTicks = 0 } }).Problem());
        Assert.NotNull((Rules with { Lure = Rules.Lure with { MinPercent = 0 } }).Problem());
        Assert.NotNull((Rules with { Retire = new PetRetireRules(3, 2, 0, 0) }).Problem());
        Assert.NotNull((Rules with { Encounter = new EncounterRules(-1, 165, 0.035) }).Problem());
    }

    [Fact]
    public void ASpecies_EatsOnlyWhatItsContentNames()
    {
        var species = new PetSpecies("creature.beast.wolf_grey", "Wolf", PetRole.Fighter,
            ImmutableArray.Create(PetSkillKind.Bite, PetSkillKind.Lure),
            new Dictionary<string, int> { ["item.material.raw_meat"] = 15 }.ToImmutableSortedDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            "wary", null, 0, "location.outpost");
        Assert.True(species.Eats("item.material.raw_meat"));
        Assert.False(species.Eats("item.material.bog_cotton"));
        Assert.Equal(15, species.Quality("item.material.raw_meat"));
        Assert.Equal(0, species.Quality("item.consumable.roast_meat"));
        Assert.False(species.Young);
        Assert.True((species with { GrowsInto = "creature.beast.wolf_grey", GrowDays = 2 }).Young);
    }
}
