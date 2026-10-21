using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Spec 25-27 through the world (STEALTH_DETECTION_AND_THREAT.md §12-§14, §20; owner findings 26 and 27): a dagger's blow on a body that has not
/// noticed the character, the takedown as an armour-and-vital check, a coated blade, the control effects, and the stealth skill's quieter step.
/// </summary>
public class StealthTests
{
    private const string Dagger = "item.weapon.iron_dagger";
    private const string Sword = "item.weapon.rusted_sword";
    private const string Flask = "item.consumable.poison_flask";
    private const string Armour = "creature.construct.animated_armour";

    private static Arena Place(GameSession session, (double X, double Z) player, int facingDeg, params (string DefId, double X, double Z, string Role)[] creatures) =>
        Arena.OpenCreatures(session, session.Setup, player, facingDeg, creatures);

    /// <summary>The character with a weapon in hand, optionally a flask, and optionally a stealth level of its own.</summary>
    private static Arena Armed(GameSession session, (double X, double Z) player, int facingDeg, string weapon, string? spare = null,
        int stealthLevel = 0, Func<PlayerRecord, PlayerRecord>? change = null, params (string DefId, double X, double Z, string Role)[] creatures) =>
        Arena.OpenCreatures(session, session.Setup, player, facingDeg, creatures, r =>
        {
            var inventory = r.Inventory.ToList();
            foreach (var defId in new[] { weapon, spare })
                if (defId is not null && inventory.All(e => e.DefId != defId))
                    inventory.Add(Arena.Stack(defId, 5));
            var item = inventory.First(e => e.DefId == weapon).ItemId;
            var progression = stealthLevel == 0
                ? r.Progression
                : r.Progression with { Skills = r.Progression.Skills.SetItem("skill.stealth", new SkillState(stealthLevel, 0)) };
            var record = new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, progression, r.FacingMdeg, r.Discoveries,
                new[] { KeyValuePair.Create(EquipSlot.MainHand, item) }, r.Currency, r.Effects);
            return change?.Invoke(record) ?? record;
        });

    private static CreatureView Only(Arena arena) => arena.Simulation.Creatures.Single();

    /// <summary>Where a creature placed alone at a spot faces when the world starts (the placement roll is keyed, so it faces the same way again).</summary>
    private static double FacingOf(GameSession session, (double X, double Z) at, string defId = Arena.Wolf, string role = "pack_hunter") =>
        Place(session, (at.X, at.Z - 60), 0, (defId, at.X, at.Z, role)).Simulation.Creatures.Single().Body.FacingMdeg / 1000.0 * Math.PI / 180;

    private static (double X, double Z) Along((double X, double Z) from, double facing, double metres) =>
        (from.X + Math.Sin(facing) * metres, from.Z + Math.Cos(facing) * metres);

    private static int FacingDeg(double facing) => (int)Math.Round(facing * 180 / Math.PI);

    /// <summary>
    /// Does a sprint at 12 m behind the creature reach its ears at this stealth level? The footfall is not written down anywhere a test can read
    /// (the noise stream is consumed by perception and cleared), so the question is asked of the creature instead: it heard nothing, or it did.
    /// </summary>
    private static bool SprintIsHeardAtTwelveMetres(GameSession session, (double X, double Z) wolf, int stealthLevel)
    {
        double facing = FacingOf(session, wolf);
        var arena = Armed(session, Along(wolf, facing, -12), FacingDeg(facing), Sword, stealthLevel: stealthLevel,
            creatures: (Arena.Wolf, wolf.X, wolf.Z, "pack_hunter"));
        var noticed = arena.Record<CreatureNoticed>();
        arena.Tick(2);
        var body = arena.Simulation.Player.Body;
        double dx = (wolf.X + arena.Origin.X) * 1000 - body.XMm, dz = (wolf.Z + arena.Origin.Z) * 1000 - body.ZMm;
        Assert.Null(arena.Submit(new MoveCommand(arena.Player, Harness.Toward(dx, dz, Gait.Sprint))));
        arena.Tick(12);
        Assert.Null(arena.Submit(new MoveCommand(arena.Player, MoveIntent.Idle(arena.Simulation.Player.Body.FacingMdeg))));
        return Only(arena).Awareness > 0 || Only(arena).Mind != CreatureMind.Unaware || noticed.Count > 0;
    }

    // ── the skill is quieter, never invisible ───────────────────────────────

    [Fact]
    public void TheStealthSkill_QuietsTheStep_ButNeverSilencesIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var wolf = (120.0, 80.0);

        // A sprint carries 16 m, so at 12 m a creature that has not seen the character hears it (STEALTH §7).
        Assert.True(SprintIsHeardAtTwelveMetres(session, wolf, stealthLevel: 0), "an untrained sprint must be heard at 12 m");

        // At rank 3 the same sprint is 16 m x 0.55 = 8.8 m: quieter, and still a sound it could hear from closer (§20: no hidden flag).
        Assert.False(SprintIsHeardAtTwelveMetres(session, wolf, stealthLevel: 8), "a rank-3 sprint must not carry 12 m");
    }

    // ── a dagger, and the takedown it makes possible ────────────────────────

    [Fact]
    public void ATakedown_FromBehindAnUnawareEvenWolf_ExecutesIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var wolf = (120.0, 80.0);
        double facing = FacingOf(session, wolf);
        var arena = Armed(session, Along(wolf, facing, -1.2), FacingDeg(facing), Dagger, creatures: (Arena.Wolf, wolf.Item1, wolf.Item2, "pack_hunter"));
        arena.Tick(2);
        Assert.Equal(CreatureMind.Unaware, Only(arena).Mind);
        int before = Only(arena).Health;

        Assert.Null(arena.Submit(new TakedownCommand(arena.Player)));
        arena.Tick(2);
        var after = Only(arena);
        Assert.False(after.Alive);
        Assert.Equal(0,after.Health);
    }

    [Fact]
    public void ATakedown_NeedsADagger()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var wolf = (120.0, 80.0);
        double facing = FacingOf(session, wolf);
        var arena = Armed(session, Along(wolf, facing, -1.2), FacingDeg(facing), Sword, creatures: (Arena.Wolf, wolf.Item1, wolf.Item2, "pack_hunter"));
        arena.Tick(2);
        Assert.Equal("a takedown needs a dagger", arena.Submit(new TakedownCommand(arena.Player)));
        Assert.True(Only(arena).Alive);
    }

    [Fact]
    public void ATakedown_IsRefusedOnABodyThatHasNoticedYou()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var wolf = (120.0, 80.0);
        double facing = FacingOf(session, wolf);
        // In front of it, close: it sees the character and its awareness climbs past suspicion.
        var arena = Armed(session, Along(wolf, facing, 4.0), FacingDeg(facing + Math.PI), Dagger,
            creatures: (Arena.Wolf, wolf.Item1, wolf.Item2, "pack_hunter"));
        arena.Tick(30);
        Assert.True(Only(arena).Awareness >= session.Setup.Combat.Awareness.Suspicious, "the wolf must be looking at the character");
        Assert.Equal("it has noticed you", arena.Submit(new TakedownCommand(arena.Player)));
    }

    [Fact]
    public void AppliedSleep_AllowsATakedownAttempt_EvenAfterTheTargetNoticedYou()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var wolf = (120.0, 80.0);
        double facing = FacingOf(session, wolf);
        var arena = Armed(session, Along(wolf, facing, 1.4), FacingDeg(facing + Math.PI), Dagger,
            creatures: (Arena.Wolf, wolf.Item1, wolf.Item2, "pack_hunter"));
        arena.Tick(30);
        Assert.True(Only(arena).Awareness >= session.Setup.Combat.Awareness.Suspicious);
        Assert.Null(arena.Simulation.Submit(new ApplyEffect(Only(arena).Id, "effect.sleep")));
        arena.Tick(1);
        Assert.True(Only(arena).Asleep);
        Assert.Null(arena.Submit(new TakedownCommand(arena.Player)));
        // Frontal position still follows the ordinary vital check rather than the rear execution rule.
    }

    [Fact]
    public void AnArmouredBody_LivesThroughATakedown_AndTurnsOnTheKnife()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var armour = (120.0, 80.0);
        double facing = FacingOf(session, armour, Armour, "sentinel");
        var arena = Armed(session, Along(armour, facing, -1.2), FacingDeg(facing), Dagger, creatures: (Armour, armour.Item1, armour.Item2, "sentinel"));
        arena.Tick(2);
        Assert.Equal(CreatureMind.Unaware, Only(arena).Mind);

        Assert.Null(arena.Submit(new TakedownCommand(arena.Player)));
        arena.Tick(2);
        var after = Only(arena);
        Assert.True(after.Alive, "armour is the resistance §14 asks for, not a stealth_immune flag: it survives the knife");
        Assert.Equal(CreatureMind.Engaged, after.Mind);
    }

    // ── poison as a coating ────────────────────────────────────────────────

    [Fact]
    public void AACoatedBlade_PutsItsVenomIntoWhatItWounds()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var wolf = (120.0, 80.0);
        double facing = FacingOf(session, wolf);
        // A flask in the pack, the blade in hand, the wolf in front and inside reach.
        var arena = Armed(session, Along(wolf, facing, 1.4), FacingDeg(facing + Math.PI), Dagger, spare: Flask,
            creatures: (Arena.Wolf, wolf.Item1, wolf.Item2, "pack_hunter"));
        arena.Tick(2);
        var flask = arena.Simulation.Player.Inventory.First(e => e.DefId == Flask);
        Assert.Null(arena.Submit(new UseItemCommand(arena.Player, flask.ItemId)));
        Assert.Contains(arena.Simulation.Combat.Effects, e => e.EffectId == "effect.coating_venom");

        var poisoned = arena.Record<EffectApplied>();
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        arena.Tick(30);
        Assert.Contains(poisoned, e => e.EffectId == "effect.venom");
        Assert.True(Only(arena).Poisoned);
    }

    // ── control effects ────────────────────────────────────────────────────

    [Theory]
    [InlineData("snarebind_resin", "root")]
    [InlineData("sleep_coating", "sleep")]
    [InlineData("paralyse_coating", "paralyse")]
    public void NewCoatings_ReplaceVenom_ConsumeOne_AndDeliverOnAWound(string item, string effect)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var wolf = (120.0, 80.0);
        double facing = FacingOf(session, wolf);
        var arena = Armed(session, Along(wolf, facing, 1.4), FacingDeg(facing + Math.PI), Dagger,
            spare: "item.consumable." + item, creatures: (Arena.Wolf, wolf.Item1, wolf.Item2, "pack_hunter"));
        arena.Tick(2);
        Assert.Null(arena.Simulation.Submit(new ApplyEffect(arena.Player, "effect.coating_venom")));
        var flask = arena.Simulation.Player.Inventory.Single(e => e.DefId == "item.consumable." + item);
        var applied = arena.Record<EffectApplied>();
        Assert.Null(arena.Submit(new UseItemCommand(arena.Player, flask.ItemId)));
        Assert.Equal(flask.Count - 1, arena.Simulation.Player.Inventory.Single(e => e.ItemId == flask.ItemId).Count);
        Assert.DoesNotContain(arena.Simulation.Combat.Effects, e => e.EffectId == "effect.coating_venom");
        Assert.Contains(arena.Simulation.Combat.Effects, e => e.EffectId == "effect.coating_" + effect);
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        arena.Tick(30);
        Assert.Contains(applied, e => e.Target == Only(arena).Id && e.EffectId == "effect." + effect);
        Assert.DoesNotContain(applied, e => e.EffectId == "effect.venom");
        Assert.Equal(effect == "root", Only(arena).Rooted);
        Assert.Equal(effect == "paralyse", Only(arena).Paralysed);
        Assert.Equal(effect == "sleep", Only(arena).Asleep);
    }

    [Fact]
    public void ASleepingBody_TakesNoTurn_AndAWoundWakesIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var wolf = (120.0, 80.0);
        double facing = FacingOf(session, wolf);
        var arena = Armed(session, Along(wolf, facing, -1.2), FacingDeg(facing), Dagger, creatures: (Arena.Wolf, wolf.Item1, wolf.Item2, "pack_hunter"));
        arena.Tick(2);
        var id = Only(arena).Id;
        Assert.Null(arena.Simulation.Submit(new ApplyEffect(id, "effect.sleep")));
        arena.Tick(2);
        var slept = Only(arena).Body;
        arena.Tick(40);
        Assert.Equal((slept.XMm, slept.ZMm), (Only(arena).Body.XMm, Only(arena).Body.ZMm));

        // A wound breaks it (effect.sleep's own rule): the body does not stay down through being stabbed.
        var expired = arena.Record<EffectExpired>();
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        arena.Tick(30);
        Assert.Contains(expired, e => e.EffectId == "effect.sleep");
    }

    [Fact]
    public void ARootedBody_KeepsItsFeet()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var wolf = (120.0, 80.0);
        double facing = FacingOf(session, wolf);
        var arena = Armed(session, Along(wolf, facing, 3.0), FacingDeg(facing + Math.PI), Dagger,
            creatures: (Arena.Wolf, wolf.Item1, wolf.Item2, "pack_hunter"));
        arena.Tick(40);
        Assert.NotEqual(CreatureMind.Unaware, Only(arena).Mind);   // it is coming
        var id = Only(arena).Id;
        Assert.Null(arena.Simulation.Submit(new ApplyEffect(id, "effect.root")));
        arena.Tick(2);
        var rooted = Only(arena).Body;
        arena.Tick(30);
        Assert.Equal((rooted.XMm, rooted.ZMm), (Only(arena).Body.XMm, Only(arena).Body.ZMm));
    }

    [Fact]
    public void Root_CancelsACharge_AndHoldsEveryTickUntilExpiry()
    {
        using var profile=new TempProfile();var session=Harness.Boot(profile);
        var boar=(120.0,80.0);const string id="creature.beast.bristleback_boar";
        double facing=FacingOf(session,boar,id,"sentinel");
        var arena=Armed(session,Along(boar,facing,5),FacingDeg(facing+Math.PI),Dagger,
            creatures:(id,boar.Item1,boar.Item2,"sentinel"));
        for(int i=0;i<100&&Only(arena).Phase!=CombatPhase.Windup;i++)arena.Tick(1);
        Assert.Equal(CombatPhase.Windup,Only(arena).Phase);
        var before=Only(arena).Body;
        var applied=arena.Record<EffectApplied>();
        Assert.Null(arena.Simulation.Submit(new ApplyEffect(Only(arena).Id,"effect.root")));
        arena.Tick(1);
        Assert.True(Only(arena).Rooted);
        long expiry=applied.Single(e=>e.EffectId=="effect.root").ExpiresTick;
        while(arena.Simulation.WorldTick<expiry-1)
        {
            arena.Tick(1);var held=Only(arena);
            Assert.Equal((before.XMm,before.ZMm),(held.Body.XMm,held.Body.ZMm));
        }
        arena.Tick(2);Assert.False(Only(arena).Rooted);
    }
}
