using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Pets;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Spec 43 over the game's own content, with the owner's rulings of 2026-10-21: a grey wolf taken out of the wild with meat and a calm
/// approach, and refused above the character's own level; a pup taken as the animal that is already the character's; the tactical orders,
/// four of them naming a place on the ground; a pet that fights beside the character and is struck for it; at no health, home badly wounded
/// and collected again rather than dead; the party cap it shares with the companions; and an encounter that scales to the party it meets.
/// </summary>
public class PetTests
{
    private const string Wolf = "creature.beast.wolf_grey";
    private const string Pup = "creature.beast.hound_pup";
    private const string Meat = "item.material.raw_meat";
    private const string Kera = "npc.ashen_hollow.kera_voss";

    /// <summary>An open stretch east of the smithy, clear of everything (the companion tests' own ground).</summary>
    private static readonly (double X, double Z) Open = (70, 152);

    private static (long XMm, long ZMm) At((double X, double Z) place) => Harness.InRegion(place.X, place.Z);

    private static PetRecord Hound(double x, double z, PetOrder order = PetOrder.Follow, int health = 50, int bond = 20,
        string creature = Wolf, ImmutableArray<PetSkill>? skills = null) =>
        new(EntityId.NewId(EntityKind.Summon), creature, "Wolf", PetRole.Fighter, order, PetCondition.Up,
            At((x, z)).XMm, At((x, z)).ZMm, 0, health, bond,
            skills ?? ImmutableArray.Create(new PetSkill(PetSkillKind.Bite, 1, 0), new PetSkill(PetSkillKind.Lure, 1, 0)));

    private static Arena ArenaWith(GameSession session, Func<PlayerRecord, PlayerRecord> change, bool keepSpawns = false,
        IEnumerable<(string DefId, double X, double Z, string Role)>? creatures = null, ulong seed = 42) =>
        Arena.OpenCreatures(session, session.Setup, Open, 90, creatures ?? Array.Empty<(string, double, double, string)>(), change, seed,
            keepSpawns: keepSpawns);

    /// <summary>A character who can reach for a wolf at all: level 2, and carrying meat.</summary>
    private static PlayerRecord Able(PlayerRecord record) =>
        record.WithProgression(record.Progression with { Level = 2 }).WithInventory(record.Inventory.Add(Arena.Stack(Meat, 4)));

    [Fact]
    public void AWolfTakenWithMeat_IsTheCharacters_AndItsSpawnSlotStandsEmpty()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A wolf at its sleep in the role that lies up, three metres off: an animal nobody has to fight to reach, and one the character
        // must still not walk up on - a sleeper woken by a footfall is a sleeper that sees you.
        var tamed = new List<PetTamed>();
        Arena? arena = null;
        // The roll is content's odds against the world's own channel, so which seeds it falls on is fixed: a handler with the right meat
        // takes the animal on one of the first few.
        foreach (ulong seed in Enumerable.Range(1, 8).Select(i => (ulong)i))
        {
            arena = ArenaWith(session, Able, creatures: new[] { (Wolf, 73.0, 152.0, "sleeper") }, seed: seed);
            tamed = arena.Record<PetTamed>();
            var attempted = arena.Record<TameAttempted>();
            var wolf = arena.Creature();
            Assert.Null(arena.Submit(new TameCommand(arena.Player, wolf.Id, Meat)));
            Assert.Single(attempted);
            Assert.InRange(attempted[0].ChancePercent, 1, 90);
            arena.Tick((int)(arena.Simulation.PetRules.Tame.ChannelTicks + 4));
            if (tamed.Count > 0)
                break;
        }

        Assert.Single(tamed);
        var pet = Assert.Single(arena!.Simulation.Pets);
        Assert.Equal((Wolf, PetCondition.Up, PetOrder.Follow, 50, PetBond.Tamed), (pet.CreatureId, pet.Condition, pet.Order, pet.Health, pet.Bond));
        Assert.Equal("following", pet.Doing);
        // It was a wild animal: its slot in the pack is empty, and it does not stand there again.
        Assert.DoesNotContain(arena.Simulation.Creatures, c => c.DefId == Wolf && c.Alive);
    }

    [Fact]
    public void AnAnimalOneLevelAbove_IsStillReachedFor_AndOneThatSeesYou_BreaksTheOffer()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A hunting wolf at the character's own level band is level 2; the character here is level 1, so it is one level above and the odds
        // already say what that costs (config.pets tame.over_level) rather than the offer being refused: the hollow's own wolf is tameable
        // from the first evening. Two levels above is a fight, not a taming.
        var arena = ArenaWith(session, r => r.WithInventory(r.Inventory.Add(Arena.Stack(Meat, 4))),
            creatures: new[] { (Wolf, 73.0, 152.0, "sleeper") });
        var wolf = arena.Creature();
        Assert.Null(arena.Submit(new TameCommand(arena.Player, wolf.Id, Meat)));
        Assert.NotNull(arena.Simulation.TameOf(wolf.Id));

        // The den's leader is not food's work: the alpha is level 3 and carries no pet block, so it is refused for what it is - content says
        // which animals an offer reaches at all, and the level wall past one above the character is read in the odds test at the domain edge.
        var far = ArenaWith(session, r => r.WithInventory(r.Inventory.Add(Arena.Stack(Meat, 4))),
            creatures: new[] { ("creature.beast.wolf_grey_alpha", 76.0, 152.0, "sleeper") });
        Assert.Contains("not an animal you can tame", far.Submit(new TameCommand(far.Player, far.Creature().Id, Meat)));

        // At level 2 and near it, a roused animal is not offered anything: the approach is the whole of the taming.
        var roused = ArenaWith(session, Able, creatures: new[] { (Wolf, 72.0, 152.0, "pack_hunter") });
        roused.Tick(400);
        Assert.True(roused.WalkTo(71.0, 152.0));
        var hunting = roused.Simulation.Creatures.First();
        string? refused = roused.Submit(new TameCommand(roused.Player, hunting.Id, Meat));
        Assert.NotNull(refused);
        Assert.Contains("seen you", refused);
    }

    [Fact]
    public void ThePup_IsTakenFromThePack_AsTheAnimalItAlreadyIs()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = ArenaWith(session, r => r.WithInventory(r.Inventory.Add(Arena.Stack("item.misc.hound_pup", 1))));
        var adopted = arena.Record<PetAdopted>();
        var pup = arena.Simulation.Player.Inventory.Single(e => e.DefId == "item.misc.hound_pup");

        string? refused = arena.Submit(new AdoptPetCommand(arena.Player, pup.ItemId));
        Assert.Null(refused);
        Assert.Single(adopted);
        var pet = Assert.Single(arena.Simulation.Pets);
        Assert.Equal((Pup, "Pup", PetCondition.Up, 24, 24, 1), (pet.CreatureId, pet.Name, pet.Condition, pet.Health, pet.MaxHealth, pet.Level));
        // The lead is not carried any more, and the bond a pup is raised with is a different animal from one taken off the hill.
        Assert.DoesNotContain(arena.Simulation.Player.Inventory, e => e.DefId == "item.misc.hound_pup");
        Assert.Equal(PetBond.Tamed, pet.Bond);
    }

    [Fact]
    public void ThePup_IsBoughtOffKerasShelf_AndTakenIn()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // Spec 43's buy route, over the game's own prices: her shelf, her hours and her anchor. The purse is the FIXTURE - a character with coin
        // in hand, which is what a player who has sold anything at all has - because the pup's price is content's (60) and a capture that has to
        // show the purchase is given a purse rather than the item being made cheap.
        var arena = Arena.OpenCreatures(session, session.Setup, (61.6, 139.6), 0, Array.Empty<(string, double, double, string)>(),
            r => r.WithCurrency(200));
        Assert.True(arena.WalkTo(61.6, 139.6), "the character could not walk to the stall");
        Assert.Null(arena.Simulation.TradeRefusal(Kera));

        var pup = Assert.Single(arena.Simulation.Wares(Kera)!.Wares.Where(w => w.ItemId == "item.misc.hound_pup"));
        Assert.Equal(60, pup.Price);
        Assert.Null(arena.Submit(new BuyCommand(arena.Player, Kera, pup.Ref, 1)));
        Assert.Equal(140, arena.Simulation.Player.Currency);

        // And it is the character's to take in.
        var lead = Assert.Single(arena.Simulation.Player.Inventory.Where(e => e.DefId == "item.misc.hound_pup"));
        Assert.Null(arena.Submit(new AdoptPetCommand(arena.Player, lead.ItemId)));
        var pet = Assert.Single(arena.Simulation.Pets);
        Assert.Equal((Pup, PetCondition.Up), (pet.CreatureId, pet.Condition));
    }

    [Fact]
    public void WhatTheWainGave_IsWhatTheStallBuys()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The other half of the buy route, priced by her own shelf: of the overturned wain's load, the hides are what she pays for; the timber is
        // worth nothing to her at the forty percent a merchant pays, and the water flask is a tool she does not buy at all.
        var arena = Arena.OpenCreatures(session, session.Setup, (61.6, 139.6), 0, Array.Empty<(string, double, double, string)>(),
            r => r.WithInventory(r.Inventory.Add(Arena.Stack("item.material.timber", 6)).Add(Arena.Stack("item.material.small_hide", 2))));
        Assert.True(arena.WalkTo(61.6, 139.6), "the character could not walk to the stall");

        var refusals = new List<string>();
        foreach (var entry in arena.Simulation.Player.Inventory
                     .Where(e => e.DefId is "item.material.timber" or "item.material.small_hide").ToArray())
            if (arena.Submit(new SellCommand(arena.Player, Kera, entry.ItemId, entry.Count)) is { } why)
                refusals.Add($"{entry.DefId}: {why}");
        var refused = Assert.Single(refusals);
        Assert.Contains("timber", refused);
        Assert.Contains("nothing for that", refused);
        Assert.Equal(4, arena.Simulation.Player.Currency);
    }

    [Fact]
    public void ThePartyCap_IsSharedWithTheCompanions()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // Four animals is the whole party (config.party's max_members): the fifth is refused in the party's own words.
        var four = Enumerable.Range(0, 4).Select(i => Hound(70.0 + i, 152.0)).ToImmutableArray();
        var arena = ArenaWith(session, r => r.WithPets(four).WithInventory(r.Inventory.Add(Arena.Stack("item.misc.hound_pup", 1))));
        Assert.Equal(4, arena.Simulation.Pets.Length);
        var pup = arena.Simulation.Player.Inventory.Single(e => e.DefId == "item.misc.hound_pup");
        Assert.Equal("your party is full (4 with you)", arena.Submit(new AdoptPetCommand(arena.Player, pup.ItemId)));
        // And a tameable animal would be refused the same way, before any roll is made.
        Assert.Empty(arena.Record<TameAttempted>());
    }

    [Fact]
    public void APetWaitsWhereItIsPut_AndComesBackWhenCalled()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = ArenaWith(session, r => r.WithPets(ImmutableArray.Create(Hound(70.0, 152.0) with { Bond = PetBond.Sure })));
        var pet = arena.Simulation.Pets.Single();

        // "Wait here", at a point ten metres off: it walks there and holds it while the character walks away.
        var (waitX, waitZ) = At((72.0, 160.0));
        Assert.Null(arena.Submit(new OrderPetCommand(arena.Player, pet.Id, PetOrder.WaitHere, waitX, waitZ)));
        arena.WalkTo(60.0, 140.0);
        arena.Tick(30);
        var waiting = arena.Simulation.Pets.Single();
        Assert.Equal(PetOrder.WaitHere, waiting.Order);
        Assert.True(Distance(waiting.Body, new Body(waitX, waiting.Body.YMm, waitZ, 0)) < 2_000, "it did not walk to the place it was sent");
        arena.Tick(200);
        Assert.True(Distance(arena.Simulation.Pets.Single().Body, new Body(waitX, 0, waitZ, 0)) < 2_500, "a waiting pet wandered off its post");

        // Called back: it closes on the character again.
        Assert.Null(arena.Submit(new OrderPetCommand(arena.Player, pet.Id, PetOrder.Follow)));
        arena.Tick(400);
        var back = arena.Simulation.Pets.Single();
        Assert.Equal(PetOrder.Follow, back.Order);
        Assert.True(Distance(back.Body, arena.Simulation.Player.Body) < 6_000, "it did not come back to the character");
    }

    [Fact]
    public void FeedingAnAnimalAtTheCharactersSide_CostsTheFood_AndBuildsTheBond()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var pet = Hound(70.0, 152.0);
        var arena = ArenaWith(session, r => r
            .WithPets(ImmutableArray.Create(pet))
            .WithInventory(r.Inventory.Add(Arena.Stack(Meat, 2)).Add(Arena.Stack("item.material.iron_ore", 1))));
        var fed = arena.Record<PetFed>();
        var meat = arena.Simulation.Player.Inventory.First(e => e.DefId == Meat);

        Assert.Null(arena.Submit(new FeedPetCommand(arena.Player, pet.Id, meat.ItemId)));
        var once = arena.Simulation.Pets.Single();
        Assert.Equal(PetBond.Tamed + 3, once.Bond);
        // The food is eaten, not put back: it came out of the pack (the bug Codex found - the bond rose and the meat stayed).
        Assert.Equal(1, arena.Simulation.Player.Inventory.First(e => e.DefId == Meat).Count);
        Assert.Single(fed);

        // What it will not eat is refused, and the pack is untouched for it.
        var ore = arena.Simulation.Player.Inventory.First(e => e.DefId == "item.material.iron_ore");
        Assert.Contains("will not eat", arena.Submit(new FeedPetCommand(arena.Player, pet.Id, ore.ItemId)));
        Assert.Equal(1, arena.Simulation.Player.Inventory.First(e => e.DefId == "item.material.iron_ore").Count);

        // Out of reach is out of reach: it eats from the hand, so a pet left on guard is called back first.
        var far = Hound(700.0, 1_000.0);
        var distant = ArenaWith(session, r => r
            .WithPets(ImmutableArray.Create(far))
            .WithInventory(r.Inventory.Add(Arena.Stack(Meat, 1))));
        var carried = distant.Simulation.Player.Inventory.First(e => e.DefId == Meat);
        Assert.Contains("too far", distant.Submit(new FeedPetCommand(distant.Player, far.Id, carried.ItemId)));
        Assert.Equal(1, distant.Simulation.Player.Inventory.First(e => e.DefId == Meat).Count);
    }

    [Fact]
    public void ACreatureTheHoundHasBitten_FightsTheHound_NotTheCharacter()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The wolf is turned by the character's own blow, and the hound at its shoulder bites it. From then on the wolf's quarrel is with the
        // hound: it lands its blows on the animal, not on the character standing beside it (spec 43 ruling 5 - enemies learn what hurts them).
        var arena = ArenaWith(session, r => r.WithPets(ImmutableArray.Create(Hound(71.0, 152.0, bond: PetBond.Sure))),
            creatures: new[] { (Wolf, 72.0, 152.0, "pack_hunter") });
        var bite = arena.Record<PetSkillUsed>();
        var onPet = arena.Record<PetStruck>();
        var onPlayer = arena.Record<HitResolved>();

        arena.Fight(arena.Creature(), 30);
        int beforeBite = onPlayer.Count(h => h.Target == arena.Player);
        arena.Tick(240);

        Assert.NotEmpty(bite);
        Assert.NotEmpty(onPet);
        Assert.True(arena.Simulation.Creatures.First().OnAnAnimal, "the wolf is not on the hound");
        // The wolf's own hands are on the hound: nothing it threw in the window after the first bite landed on the character.
        int afterBite = onPlayer.Count(h => h.Target == arena.Player);
        // It splits its attention rather than forgetting the character: the animal is a foe in the fight, not a shield. What the grudge buys is
        // that the wolf's blows are on the hound at all - before it, the animal was struck only by the pet system's own retaliation.
        Assert.True(afterBite > beforeBite, "the wolf forgot the character entirely");    }

    [Fact]
    public void APetThatFights_IsStruckBack_AndAtNoHealthGoesHomeRatherThanDying()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A wolf engaged with the character (struck once, which is what turns it), and a pet following into the fight with one blow left in
        // it: the pet bites what is fighting the party, and what it bit bites back.
        var arena = ArenaWith(session, r => r.WithPets(ImmutableArray.Create(
                Hound(71.0, 152.0, PetOrder.Follow, health: 3, bond: PetBond.Sure))),
            creatures: new[] { (Wolf, 72.0, 152.0, "pack_hunter") });
        var wounded = arena.Record<PetWoundedHome>();
        var bit = arena.Record<PetSkillUsed>();
        var struck = arena.Record<PetStruck>();
        arena.Fight(arena.Creature(), 40);
        arena.Tick(300);

        Assert.True(wounded.Count == 1, $"wounded={wounded.Count} bites={bit.Count} struck={struck.Count} pet={arena.Simulation.Pets.Single().Health}");
        var home = arena.Simulation.Pets.Single();
        Assert.Equal(PetCondition.Wounded, home.Condition);
        Assert.Equal(0, home.Health);
        Assert.Equal("badly wounded, home", home.Standing);
        Assert.True(home.HomeUntilTick > arena.Simulation.WorldTick);
        // Not dead: the record is still the character's, and the days at home bring it back to its feet.
        arena.Simulation.Advance((home.HomeUntilTick ?? 0) - arena.Simulation.WorldTick + 4);
        var mended = arena.Simulation.Pets.Single();
        Assert.Equal((PetCondition.Up, 50, "following"), (mended.Condition, mended.Health, mended.Doing));
    }

    [Fact]
    public void Encounters_AreScaledToTheParty_AHoundPupIsWeakerAndALevelTwoWolfIsStronger()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The region's own pack, faced by the character alone and then with four animals at its side.
        var solo = ArenaWith(session, r => r, keepSpawns: true);
        var party = ArenaWith(session, r => r.WithPets(Enumerable.Range(0, 4)
            .Select(i => Hound(70.0 + i, 152.0)).ToImmutableArray()), keepSpawns: true);
        var alone = solo.Simulation.Creatures.First(c => c.Alive);
        var with = party.Simulation.Creatures.First(c => c.Alive && c.Key == alone.Key);
        Assert.Equal(0.0, solo.Simulation.PartyStrength);
        Assert.True(party.Simulation.PartyStrength >= 4.0);
        Assert.True(with.MaxHealth > alone.MaxHealth, $"a full party met {with.MaxHealth} where the solo character met {alone.MaxHealth}");
        Assert.True(with.Level > alone.Level, "the party's encounter is not the harder one");
        // The scale is content's own, and it is what the body was placed with: the party's percent of the authored fight.
        var expected = Encounters.Of(party.Simulation.PartyStrength, party.Simulation.PetRules.Encounter).Percent;
        Assert.InRange(with.MaxHealth * 100 / alone.MaxHealth, expected - 2, expected + 2);
        Assert.Equal(100, alone.MaxHealth * 100 / alone.MaxHealth);
    }

    [Fact]
    public void ASaveKeepsThePet_FieldForField()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var skills = ImmutableArray.Create(new PetSkill(PetSkillKind.Bite, 2, 3), new PetSkill(PetSkillKind.Lure, 1, 1));
        var pet = Hound(70.0, 152.0, PetOrder.WaitHere, health: 37, bond: 44, skills: skills) with
        {
            TamedFromKey = "spawn.test.wolf_0#0",
            WaitXMm = At((72.0, 160.0)).XMm,
            WaitZMm = At((72.0, 160.0)).ZMm,
            Level = 2,
            LastFedTick = 3_400,
        };
        var arena = ArenaWith(session, r => r.WithPets(ImmutableArray.Create(pet)));
        new Persistence.SaveStore(profile.Root).Save(Persistence.SaveSlots.Manual("pets"),
            Persistence.SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
                arena.Simulation.WorldTick, 0));
        var loaded = session.Load(Persistence.SaveSlots.Manual("pets"));
        Assert.True(loaded.IsComplete, string.Join("; ", loaded.Report.Blockers.Concat(loaded.RejectedRecords.Select(r => r.Reason))));

        var kept = Assert.Single(loaded.Player.Pets);
        Assert.Equal(pet.Id, kept.Id);
        Assert.Equal((pet.CreatureId, pet.Name, pet.Order, pet.Health, pet.Bond, pet.Level), (kept.CreatureId, kept.Name, kept.Order, kept.Health, kept.Bond, kept.Level));
        Assert.Equal(pet.Skills.Select(s => s.ToString()), kept.Skills.Select(s => s.ToString()));
        Assert.Equal((pet.WaitXMm, pet.WaitZMm, pet.TamedFromKey, pet.LastFedTick), (kept.WaitXMm, kept.WaitZMm, kept.TamedFromKey, kept.LastFedTick));
        // And the world it loads into has the animal where the save left it.
        var back = Assert.Single(session.Simulation!.Pets);
        Assert.Equal((PetCondition.Up, PetOrder.WaitHere, 37, 44), (back.Condition, back.Order, back.Health, back.Bond));
    }

    private static double Distance(Body a, Body b) => Math.Sqrt(Math.Pow(a.XMm - b.XMm, 2) + Math.Pow(a.ZMm - b.ZMm, 2));
}
