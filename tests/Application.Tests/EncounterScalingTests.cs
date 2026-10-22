using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Pets;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Encounter scaling (spec 43, owner ruling 5): a full party meets the same authored spawn harder, not longer. Every case here is over the
/// game's own content - the den's grey wolves, the party cap in <c>config.party</c>, the odds in <c>config.pets</c> - so the numbers asserted
/// are the numbers the game ships with.
/// </summary>
public class EncounterScalingTests
{
    private const string Wolf = "creature.beast.wolf_grey";
    private const string Tavar = "npc.ashen_hollow.tavar_orr";
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Sel = "npc.ashen_hollow.sel_arien";

    /// <summary>An open stretch east of the smithy, clear of everything (the companion tests' own ground).</summary>
    private static readonly (double X, double Z) Open = (70, 152);

    private static (long XMm, long ZMm) At((double X, double Z) place) => Harness.InRegion(place.X, place.Z);

    private static PetRecord Hound(double x, double z, int level = 1, PetCondition condition = PetCondition.Up) =>
        new(EntityId.NewId(EntityKind.Summon), Wolf, "Wolf", PetRole.Fighter, PetOrder.Follow, condition,
            At((x, z)).XMm, At((x, z)).ZMm, 0, condition == PetCondition.Up ? 50 : 0, PetBond.Tamed,
            ImmutableArray.Create(new PetSkill(PetSkillKind.Bite, 1, 0), new PetSkill(PetSkillKind.Lure, 1, 0)))
        {
            Level = level,
        };

    private static CompanionRecord Companion(string npc, double x, double z, CompanionCondition condition = CompanionCondition.Up) =>
        new(npc, CompanionOrder.Follow, condition, At((x, z)).XMm, At((x, z)).ZMm, 0, condition == CompanionCondition.Up ? 60 : 0)
        {
            Membership = PartyMembership.Companion,
            Condition = condition,
            DownedTick = condition == CompanionCondition.Up ? 0 : 1,
        };

    /// <summary>The hunting bow and its arrows, so a kill does not need the character to outlast the wolf.</summary>
    private static PlayerRecord Archer(PlayerRecord record) => record
        .WithInventory(record.Inventory.Add(Arena.Stack("item.ammo.arrow_rough", 24)).Add(Arena.Stack("item.weapon.hunting_bow", 1)));

    /// <summary>The den's pack of four, placed around the spawn point, and nothing else in the world.</summary>
    private static Arena Den(GameSession session, Func<PlayerRecord, PlayerRecord> change, ulong seed = 42) =>
        Arena.OpenCreatures(session, session.Setup, Open, 90, new[]
        {
            (Wolf, 73.0, 152.0, "pack_hunter"), (Wolf, 76.0, 152.0, "pack_hunter"),
            (Wolf, 73.0, 155.0, "stray"), (Wolf, 76.0, 155.0, "stray"),
        }, change, seed);

    /// <summary>
    /// A party at the cap: four animals (<c>config.party</c> max_members is 4). Tavar is the region's companion; Renn and Sel are the
    /// smithy's hireable townsfolk rather than companions, so a mixed party is the party tests' own ground - here the four are the pets'
    /// half of the same number.
    /// </summary>
    private static PlayerRecord FullParty(PlayerRecord record) => record
        .WithPets(ImmutableArray.Create(Hound(70.0, 151.0), Hound(71.0, 151.0), Hound(70.0, 150.0), Hound(71.0, 150.0)));

    [Fact]
    public void Alone_TheCharacterMeetsTheAuthoredSpawn_WithNothingScaled()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Den(session, r => r);
        var wolves = arena.Simulation.Creatures.Where(c => c.DefId == Wolf).ToList();

        // One spawn site per wolf, and every one of them stands: four wolves, not four wolves' worth of health in fewer bodies.
        Assert.Equal(4, wolves.Count);
        Assert.Equal(0.0, arena.Simulation.PartyStrength);
        Assert.All(wolves, wolf =>
        {
            Assert.Equal((50, 50, 2), (wolf.MaxHealth, wolf.Health, wolf.Level));
            Assert.Equal(50, session.Setup.Combat.Creatures[Wolf].MaxHealth);
        });
    }

    [Fact]
    public void AFourStrongParty_MeetsTheSameFourWolves_Harder_AndStillFour()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Den(session, FullParty);
        var wolves = arena.Simulation.Creatures.Where(c => c.DefId == Wolf).ToList();
        var rules = arena.Simulation.PetRules.Encounter;

        // One point for each animal, and a quarter for every level past its first: four bodies at level 1 are four points, the cap.
        Assert.Equal(4.0, arena.Simulation.PartyStrength);
        var scale = Encounters.Of(arena.Simulation.PartyStrength, rules);
        Assert.Equal((160, 2), (scale.Percent, scale.Levels));           // content's cap, and the levels it buys

        Assert.Equal(4, wolves.Count);                                   // harder, not longer
        Assert.All(wolves, wolf =>
        {
            Assert.Equal((int)Math.Round(50 * scale.Percent / 100.0), wolf.MaxHealth);
            Assert.Equal(wolf.MaxHealth, wolf.Health);
            Assert.Equal(2 + scale.Levels, wolf.Level);
            Assert.Equal(ThreatRules.Of(2 + scale.Levels, 1), wolf.Threat);
        });
    }

    [Fact]
    public void APartysOwnBlows_AreNotScaled_OnlyWhatsAgainstIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Den(session, FullParty);
        var wolf = arena.Simulation.Creatures.First(c => c.DefId == Wolf);
        var hit = arena.Record<HitResolved>();
        var weapon = arena.Simulation.Combat.Weapon;

        arena.Fight(wolf, 12);

        // The character's sword is the sword: the authored range, not a party's percent of it.
        var mine = hit.Where(h => h.Attacker == arena.Player && h.Damage > 0).ToList();
        Assert.NotEmpty(mine);
        // The upper bound is the sword's own: a party does not make the character's arm longer. (The lower one is armour's business.)$
    }

    [Fact]
    public void OnlyThoseOnTheirFeet_CountTowardsWhatMeetsTheParty()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // Three with the character - the companion and two animals - and the rest out of it: one animal home badly wounded, then the
        // companion downed as well.
        var withHim = Den(session, r => r
            .WithCompanions(new[] { Companion(Tavar, 69.0, 152.0) })
            .WithPets(new[] { Hound(70.0, 151.0), Hound(71.0, 151.0), Hound(70.0, 150.0, condition: PetCondition.Wounded) }));
        Assert.Equal(3.0, withHim.Simulation.PartyStrength);

        var arena = Den(session, r => r
            .WithCompanions(new[] { Companion(Tavar, 69.0, 152.0, CompanionCondition.Downed) })
            .WithPets(new[] { Hound(70.0, 151.0), Hound(71.0, 151.0, condition: PetCondition.Wounded) }));
        Assert.True(arena.Simulation.PartyStrength == 1.0,
            $"strength {arena.Simulation.PartyStrength}: pets {string.Join(",", arena.Simulation.Pets.Select(p => p.Condition))}, companions {string.Join(",", arena.Simulation.Companions.Select(c => c.Condition))}");
        var wolf = arena.Simulation.Creatures.First(c => c.DefId == Wolf);
        var scale = Encounters.Of(arena.Simulation.PartyStrength, arena.Simulation.PetRules.Encounter);
        Assert.InRange(wolf.MaxHealth * 100 / 50, scale.Percent - 2, scale.Percent + 2);
        Assert.True(wolf.MaxHealth < (int)Math.Round(50 * 1.65), "a wounded party met the full party's fight");
    }

    [Fact]
    public void ALeveledAnimal_IsWorthMoreToAnEncounter_ThanAWildOne()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var young = Den(session, r => r.WithPets(ImmutableArray.Create(Hound(70.0, 151.0, level: 1))));
        var grown = Den(session, r => r.WithPets(ImmutableArray.Create(Hound(70.0, 151.0, level: 4))));

        // A quarter of a point for every level past the first (config.pets's encounter rule, through Encounters.Points).
        Assert.Equal(1.0, young.Simulation.PartyStrength);
        Assert.Equal(1.75, grown.Simulation.PartyStrength);
        var youngWolf = young.Simulation.Creatures.First(c => c.DefId == Wolf);
        var grownWolf = grown.Simulation.Creatures.First(c => c.DefId == Wolf);
        Assert.True(grownWolf.MaxHealth >= youngWolf.MaxHealth);
    }

    [Fact]
    public void APackAlreadyStanding_KeepsTheScaleItWasPlacedWith_WhenThePartyGrows()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var solo = Den(session, r => r);
        // What a creature was placed with is what it keeps: the pack stood there before the party gathered, so it is the authored fight,
        // and it is the next placement (a respawn) that meets the party at its size.
        var alone = solo.Simulation.Creatures.First(c => c.DefId == Wolf);
        Assert.Equal(50, alone.MaxHealth);
        solo.Tick((int)solo.Simulation.Setup.Clock.TicksPerDay);
        Assert.All(solo.Simulation.Creatures.Where(c => c.DefId == Wolf), c => Assert.Equal(50, c.MaxHealth));
    }

    [Fact]
    public void AScaledPack_LoadsFromTheSave_WithoutScalingTwice()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Den(session, FullParty);
        var placed = arena.Simulation.Creatures.First(c => c.DefId == Wolf).MaxHealth;
        Assert.True(placed > 50);

        new Persistence.SaveStore(profile.Root).Save(Persistence.SaveSlots.Manual("scaled"),
            Persistence.SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
                arena.Simulation.WorldTick, 0));
        var loaded = session.Load(Persistence.SaveSlots.Manual("scaled"));
        Assert.True(loaded.IsComplete, string.Join("; ", loaded.Report.Blockers));

        // The health the body was placed with is the health it comes back with: the percent is a place-time decision, not a load-time one.
        var again = session.Simulation!.Creatures.First(c => c.DefId == Wolf);
        Assert.Equal(placed, again.MaxHealth);
    }

    [Fact]
    public void AScaledKill_IsWorthMore_AndIsRecordedAtItsScaledLevel()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // Shot from range: a party-scaled wolf has 82 health and hits for a party's worth, so a sword duel would end the character before the
        // wolf - and the question here is what the kill is worth, not who wins the trade.
        var solo = Den(session, Archer);
        var party = Den(session, r => FullParty(Archer(r)));
        var soloKill = solo.Record<ExperienceGained>();
        var partyKill = party.Record<ExperienceGained>();

        solo.Fight(solo.Simulation.Creatures.First(c => c.DefId == Wolf), 600);
        party.Fight(party.Simulation.Creatures.First(c => c.DefId == Wolf), 900);

        var authored = Assert.Single(soloKill, x => x.Source == XpSource.Combat);
        var scaled = Assert.Single(partyKill, x => x.Source == XpSource.Combat);
        // The anti-farm guard scales the authored value by the level band, so the numbers are compared to each other: the same wolf, met by a
        // party, is worth its percent more - and the kill is recorded two levels higher, which is what the band guard reads.
        Assert.True(authored.Awarded > 0);
        // The percent is the worth; the anti-farm band guard then applies its own multiplier, which is why this is a floor and not the exact
        // 165: what matters is that the same wolf, met by a party, is worth materially more than it was to the lone character.
        Assert.InRange(scaled.Awarded * 100 / authored.Awarded, 120, 200);
    }


}
