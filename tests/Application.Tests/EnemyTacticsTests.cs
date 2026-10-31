using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Pets;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Smarter enemies (spec 43, owner ruling 5; the note of 2026-10-22): a predator that reads a fight finishes the weakest of the party rather
/// than the nearest, goes past an animal to the handler who set it on, and does not fall for a lure it has already followed - while an
/// ordinary role keeps fighting the animal that bit it. Every case is over the game's own content, so the roles asserted are
/// <c>config.creature_behaviour</c>'s own: <c>pack_hunter</c> reads a fight, <c>den_guardian</c> does not.
/// </summary>
public class EnemyTacticsTests
{
    private const string Wolf = "creature.beast.wolf_grey";

    /// <summary>An open stretch east of the smithy, clear of everything (the companion and pet tests' own ground).</summary>
    private static readonly (double X, double Z) Open = (70, 152);

    private static (long XMm, long ZMm) At(double x, double z) => Harness.InRegion(x, z);

    /// <summary>An animal on its feet, standing where it is put: a wait order keeps the geometry of a test still.</summary>
    private static PetRecord Hound(double x, double z, int health) =>
        new(EntityId.NewId(EntityKind.Summon), Wolf, "Wolf", PetRole.Fighter, PetOrder.WaitHere, PetCondition.Up,
            At(x, z).XMm, At(x, z).ZMm, 0, health, PetBond.Sure,
            ImmutableArray.Create(new PetSkill(PetSkillKind.Bite, 1, 0), new PetSkill(PetSkillKind.Lure, 1, 0)))
        {
            WaitXMm = At(x, z).XMm,
            WaitZMm = At(x, z).ZMm,
            Level = 1,
        };

    private static double To((long XMm, long ZMm) place, Body body) =>
        Math.Sqrt(Math.Pow(place.XMm - body.XMm, 2) + Math.Pow(place.ZMm - body.ZMm, 2));

    /// <summary>
    /// A wolf placed, and the fight begun the way a player begins one: the character swings at it. A wolf that hears the blade and then sees
    /// the character is engaged; that is the state every question here is asked in.
    /// </summary>
    private static Arena Roused(GameSession session, (string DefId, double X, double Z, string Role) wolf,
        Func<PlayerRecord, PlayerRecord> change, SimulationSetup? rules = null)
    {
        var arena = Arena.OpenCreatures(session, rules ?? session.Setup, Open, 0, new[] { wolf }, change);
        arena.Tick(10);
        arena.Face(arena.Creature());
        arena.Submit(new AttackCommand(arena.Player));
        for (int i = 0; i < 300 && !arena.Creature().Hostile; i++)
            arena.Tick();
        Assert.True(arena.Creature().Hostile, "the wolf never engaged the party");
        return arena;
    }

    [Fact]
    public void APackHunter_FinishesTheHurtAnimal_AndWalksPastTheWholeOne()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A whole animal two metres behind the wolf and a hurt one six metres past that. Nearest is the whole one; a role that reads a fight
        // goes by it for the hurt one (spec 43 ruling 5: "focus the weakest" - a party that lets one animal get low loses that animal).
        var arena = Roused(session, (Wolf, 74.0, 152.0, "pack_hunter"),
            r => r.WithPets(ImmutableArray.Create(Hound(76.0, 152.0, 50), Hound(80.0, 152.0, 20))));
        var wolf = arena.Creature();
        // The role the game ships has to read a fight at all: a content edit that dropped the flag would make the walk below meaningless.
        Assert.True(session.Setup.Combat.Roles["pack_hunter"].FocusWeakest, "pack_hunter does not focus the weakest in this content");
        Assert.Equal(2, arena.Simulation.Pets.Length);
        double started = To(At(80.0, 152), wolf.Body);

        // It has to pass the whole animal, which stands at 76: a wolf that ends east of it was not stopped by the nearest body on the way.
        for (int i = 0; i < 240 && wolf.Body.XMm < At(76.0, 152).XMm; i++)
        {
            arena.Tick();
            wolf = arena.Creature();
        }

        Assert.True(wolf.Body.XMm > At(76.0, 152).XMm, $"the wolf stopped on the whole animal at {wolf.Body.XMm / 1000.0:0.0} m");
        Assert.True(To(At(80.0, 152), wolf.Body) < started - 2_500,
            $"the wolf closed {started - To(At(80.0, 152), wolf.Body):0} m on the hurt animal: it is going for the nearest instead");
    }

    [Fact]
    public void AnAnimalThatSavagesAPackHunter_BringsItOnItsHandler_AndAPlainRoleFightsTheAnimal()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The animal bites the wolf, and what the wolf does about it is its role's: a pack hunter goes for the handler behind the animal, a
        // guardian that does not read the fight turns on the animal itself (spec 43 ruling 5, "attacking handlers").
        double hunter = DistanceToHandler(session, "pack_hunter");
        double guardian = DistanceToHandler(session, "den_guardian");
        Assert.True(hunter < guardian - 2_000,
            $"the pack hunter ended {hunter / 1000:0.0} m from the character and the guardian {guardian / 1000:0.0} m: no handler hunting");
        Assert.True(hunter < 4_000, $"the pack hunter stayed {hunter / 1000:0.0} m off the handler it went after");
    }

    /// <summary>Provoke a wolf with an animal's bite, let it answer, and measure what it did: how far it ended from the character.</summary>
    private static double DistanceToHandler(GameSession session, string role)
    {
        // On the bank, not in the creek at (76, 152): water masks half of a swing's ten metres (STEALTH §3.4) and the wolf would not hear it.
        var arena = Roused(session, (Wolf, 76.0, 150.0, role),
            r => r.WithPets(ImmutableArray.Create(Hound(77.4, 150.0, 50))));
        var pet = Assert.Single(arena.Simulation.Pets);
        var started = arena.Record<AttackStarted>();
        // The animal goes for it: a bite is what makes the wolf turn on the party's animal in the first place.
        arena.Submit(new OrderPetCommand(arena.Player, pet.Id, PetOrder.Pull));
        arena.Tick(60);
        Assert.Contains(started, a => a.Attacker == pet.Id);
        // Now what does it go for: the animal at its heels, or the character six metres back?
        arena.Tick(120);
        return To(At(70.0, 152), arena.Creature().Body);
    }

    [Fact]
    public void ALureThatTook_IsNotTakenAgain_ByTheSameWolf()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The drawn window is content's own ninety seconds; over a test it is shortened, so the wolf is back in the fight while the fight still
        // has a party standing in it.
        var pets = session.Setup.Pets!;
        var quick = session.Setup with { Pets = pets with { Rules = pets.Rules with { Lure = pets.Rules.Lure with { ImmunityTicks = 60 } } } };
        var arena = Roused(session, (Wolf, 74.0, 152.0, "pack_hunter"),
            r => r.WithPets(ImmutableArray.Create(Hound(71.0, 152.0, 50))), quick);
        var lured = arena.Record<PetLured>();
        var refused = arena.Record<LureRefused>();
        var pet = Assert.Single(arena.Simulation.Pets);
        var wolf = arena.Creature();

        // The mark is laid where the animal is, and the lure is asked for until the roll takes: a lure is content's odds, not a certainty.
        var (tx, tz) = (wolf.Body.XMm, wolf.Body.ZMm);
        for (int attempt = 0; attempt < 8 && lured.Count == 0; attempt++)
        {
            arena.Submit(new OrderPetCommand(arena.Player, pet.Id, PetOrder.LureToHere, tx, tz));
            arena.Tick(80);
        }
        Assert.Single(lured);
        Assert.Equal(wolf.Id, lured[0].Creature);

        // It was led off - and it has seen the trick. Once the drawn wolf has found the party again, the next lure laid for it is refused and
        // takes nothing.
        for (int i = 0; i < 600 && !arena.Creature().Hostile; i++)
        {
            if (i % 100 == 0)
            {
                arena.Face(arena.Creature());
                arena.Submit(new AttackCommand(arena.Player));
            }
            arena.Tick();
        }
        Assert.True(arena.Creature().Hostile, "the drawn wolf never found the party again");
        var back = arena.Creature();
        for (int attempt = 0; attempt < 4; attempt++)
        {
            arena.Submit(new OrderPetCommand(arena.Player, pet.Id, PetOrder.LureToHere, back.Body.XMm, back.Body.ZMm));
            arena.Tick(80);
        }
        Assert.NotEmpty(refused);
        Assert.Single(lured);
    }
}
