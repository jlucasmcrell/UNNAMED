using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Progression;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// CMB-24 in the running world: the slice's seven techniques as a fight uses them - a blow caught on the flat, a guard beaten away, a point
/// kept live, a charge stopped on a planted point, a point into a gap, a strike nobody hears, and a shot whose aim holds. Parry, Beat the
/// Guard, Giving Ground, Set Against the Charge, Gap Thrust, Quiet Blade, Steady Draw (CAP05 §4-5, §8; SKILLS_AND_TECHNIQUES §3.1).
/// </summary>
public class TechniqueCombatTests
{
    private const string Parry = "ability.blade.parry";
    private const string BeatTheGuard = "ability.blade.beat_the_guard";
    private const string GivingGround = "ability.polearm.giving_ground";
    private const string SetAgainstTheCharge = "ability.polearm.set_against_the_charge";
    private const string GapThrust = "ability.dagger.gap_thrust";
    private const string QuietBlade = "ability.dagger.quiet_blade";
    private const string SteadyDraw = "ability.bow.steady_draw";
    private const string Raider = "creature.humanoid.back_ledger_raider";
    private const string Boar = "creature.beast.bristleback_boar";
    private const string Warden = "creature.construct.gate_warden";

    private static readonly (double X, double Z) Open = (120, 60);

    /// <summary>The character as the test wants them: what they know, and the blade skill they know it at.</summary>
    private static PlayerRecord Knowing(PlayerRecord r, string skill, int band, params string[] known) =>
        r.WithProgression(r.Progression with
        {
            Skills = r.Progression.Skills.SetItem(skill, new SkillState(band, 0)),
            Known = known.Aggregate(r.Progression.Known, (k, id) => k.SetItem(id, new KnownTechnique(LearningSource.Teacher, "npc.ashen_hollow.kera_voss", 0))),
        });

    /// <summary>Carrying a weapon and an arrow by the handful, the way a shopper would (the pattern TeachingTests and CombatTests use).</summary>
    private static PlayerRecord Carrying(PlayerRecord r, params InventoryEntry[] items) =>
        r.WithInventory(r.Inventory.Concat(items));

    /// <summary>Tick until the creature has begun a blow, or the budget runs out. True when it has.</summary>
    private static bool WaitForTell(Arena arena, List<AttackStarted> tells, int maxTicks)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            arena.Tick();
            if (tells.Any(t => t.Attacker != arena.Player))
                return true;
        }
        return false;
    }

    [Fact]
    public void AParry_CatchesTheBlowOnTheFlat_TakesItOutOfTheFight_AndOpensTheAttacker()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A parry window of 0.4 s is a player's timing, not a test's: the opening is widened here so the catch is the thing under test,
        // and the shipped window is asserted below and in the content suite.
        var shipped = session.Setup.Techniques[Parry];
        Assert.Equal(8, shipped.ParryTicks);
        var rules = session.Setup with
        {
            Techniques = session.Setup.Techniques.SetItem(Parry, shipped with { ParryTicks = 40 }),
        };
        var arena = Arena.OpenCreatures(session, rules, Open, 0, new[] { (Arena.Wolf, Open.X, Open.Z + 2.5, "pack_hunter") },
            r => Knowing(r, "skill.one_hand_blade", 20, Parry));
        var tells = arena.Record<AttackStarted>();
        var parried = arena.Record<Parried>();
        var pressed = arena.Record<CreaturePressed>();
        var hits = arena.Record<HitResolved>();
        var wolf = arena.Creature();
        arena.Face(wolf);   // the blow has to come at the front, where a parry can meet it
        int health = arena.Simulation.Combat.Health;

        // The parry is a reaction: a swing of the character's own is what brings the wolf on, and the blow is met inside its windup.
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        arena.Tick(30);
        Assert.True(WaitForTell(arena, tells, 300), "the wolf came on after the character's swing");
        arena.Face(wolf);
        Assert.Null(arena.Submit(new TechniqueCommand(arena.Player, Parry)));
        Assert.True(arena.Simulation.Combat.ParryTicksLeft > 0, "the parry window is open");
        // The caught blow is the one that costs the character nothing: whatever came before it is the wolf's own business.
        long parriedOn = -1;
        for (int i = 0; i < 40 && parried.Count == 0; i++)
        {
            int before = arena.Simulation.Combat.Health;
            arena.Tick();
            if (parried.Count > 0)
            {
                parriedOn = before;
                Assert.Equal(before, arena.Simulation.Combat.Health);
            }
        }
        Assert.True(parriedOn > 0, "the blow was caught");

        var caught = Assert.Single(parried);
        Assert.Equal(wolf.Id, caught.Attacker);
        Assert.Equal(Parry, caught.TechniqueId);
        Assert.Contains(pressed, p => p.Effect == "parried" && p.Creature == wolf.Id);
        // The caught blow resolved as nothing at all: no hit was written for it.
        Assert.DoesNotContain(hits, h => h.Target == arena.Player && h.Tick == caught.Tick);
    }

    [Fact]
    public void BeatTheGuard_TakesAHumanlikesGuardAway_ThatAPlainBlowCannot()
    {
        static (int Blocked, bool GuardGone) Fight(bool beat)
        {
            using var profile = new TempProfile();
            var session = Harness.Boot(profile);
            var arena = Arena.OpenCreatures(session, session.Setup, Open, 0, new[] { (Raider, Open.X, Open.Z - 1.6, "pack_hunter") },
                r => Knowing(r, "skill.one_hand_blade", 20, BeatTheGuard));
            var pressed = arena.Record<CreaturePressed>();
            var hits = arena.Record<HitResolved>();
            var raider = arena.Creature();
            arena.Face(raider);
            arena.Fight(raider, 60);   // blows land, and the human-like body turns them on its guard
            if (beat)
            {
                Assert.Null(arena.Submit(new TechniqueCommand(arena.Player, BeatTheGuard)));
                arena.Tick(40);
                arena.Fight(raider, 40);
            }
            return (hits.Count(h => h.Target == raider.Id && h.Blocked), pressed.Any(p => p.Creature == raider.Id && p.Effect == "guard beaten"));
        }

        var plain = Fight(beat: false);
        var beat = Fight(beat: true);
        Assert.True(plain.Blocked > 0, "a human-like body turns a plain blow on its guard");
        Assert.False(plain.GuardGone);
        Assert.True(beat.GuardGone, "the guard beat takes the guard away");
    }

    [Fact]
    public void GivingGround_KeepsTheGuardFree_WhileThePointIsLive()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, Open, 0, new[] { (Arena.Wolf, Open.X, Open.Z + 2.5, "pack_hunter") },
            r => Knowing(Carrying(r, Arena.Stack("item.weapon.march_spear", 1)), "skill.polearm", 20, GivingGround));
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, arena.Simulation.Player.Inventory.Single(e => e.DefId == "item.weapon.march_spear").ItemId)));
        var wolf = arena.Creature();
        arena.Face(wolf);

        int StaminaNow() => arena.Simulation.CaptureRecord().Progression.Pools.Stamina ?? 0;
        arena.Tick(30);
        int before = StaminaNow();
        var used = arena.Record<TechniqueUsed>();
        Assert.Null(arena.Submit(new TechniqueCommand(arena.Player, GivingGround)));
        Assert.Equal(GivingGround, Assert.Single(used).TechniqueId);
        arena.Tick(40);
        // Six stamina for the technique, and none for the guard the live point held: the wolf's blows cost the character nothing to turn.
        Assert.True(StaminaNow() > before - 20, $"the live point's guard is free: {before} -> {StaminaNow()}");
    }

    [Fact]
    public void SetAgainstTheCharge_StopsAChargingBoarOnThePoint_AndIsNotKnockedDown()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, Open, 0, new[] { (Boar, Open.X, Open.Z + 9.0, "pack_hunter") },
            r => Knowing(Carrying(r, Arena.Stack("item.weapon.march_spear", 1)), "skill.polearm", 35, SetAgainstTheCharge));
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, arena.Simulation.Player.Inventory.Single(e => e.DefId == "item.weapon.march_spear").ItemId)));
        var stopped = arena.Record<ChargeStopped>();
        var pressed = arena.Record<CreaturePressed>();
        var boar = arena.Creature();
        arena.Face(boar);
        arena.Tick(20);   // it notices the character and closes

        Assert.Null(arena.Submit(new TechniqueCommand(arena.Player, SetAgainstTheCharge)));
        for (int i = 0; i < 200 && stopped.Count == 0; i++)
            arena.Tick();

        var stop = Assert.Single(stopped);
        Assert.Equal(boar.Id, stop.Charger);
        Assert.Equal(SetAgainstTheCharge, stop.TechniqueId);
        Assert.True(stop.Damage > 0, "the charger's own mass drives it onto the point");
        Assert.Contains(pressed, p => p.Creature == boar.Id && p.Effect == "stopped on the point");
    }

    [Fact]
    public void GapThrust_ReadsTheGapThroughTheReader_ThatCMB04WillOwn()
    {
        // The seam: a gap thrust asks the armour-gap reader where it can go in, and an ordinary blow never asks. What the reader answers is
        // CMB-04's business; that it is asked, with the gap the technique names, is this row's.
        List<ArmourGap> Asked(string? technique)
        {
            using var profile = new TempProfile();
            var session = Harness.Boot(profile);
            var reader = new CountingGaps();
            var rules = session.Setup with { ArmourGaps = reader };
            var stack = Arena.Stack("item.weapon.iron_dagger", 1);
            var arena = Arena.OpenCreatures(session, rules, Open, 0, new[] { (Warden, Open.X, Open.Z - 1.6, "pack_hunter") },
                r => Knowing(Carrying(r, stack), "skill.one_hand_blade", 20, GapThrust));
            Assert.Null(arena.Submit(new EquipCommand(arena.Player, arena.Simulation.Player.Inventory.Single(e => e.DefId == "item.weapon.iron_dagger").ItemId)));
            arena.Face(arena.Creature());
            if (technique is null)
                Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
            else
                Assert.Null(arena.Submit(new TechniqueCommand(arena.Player, technique)));
            arena.Tick(80);
            return reader.GapsAsked;
        }

        Assert.Equal(new[] { ArmourGap.Throat }, Asked(GapThrust));
        Assert.Empty(Asked(null));
    }

    /// <summary>The reader a world is given in this test: it answers every gap, and remembers what it was asked.</summary>
    private sealed class CountingGaps : IArmourGapReader
    {
        public List<ArmourGap> GapsAsked { get; } = new();

        public int OpenPermille(EntityId target, ArmourGap gap)
        {
            GapsAsked.Add(gap);
            return 600;
        }
    }

    [Fact]
    public void QuietBlade_IsNotHeard_WhereAPlainSwingIs()
    {
        int AwarenessAfter(string? technique)
        {
            using var profile = new TempProfile();
            var session = Harness.Boot(profile);
            var stack = Arena.Stack("item.weapon.iron_dagger", 1);
            var arena = Arena.OpenCreatures(session, session.Setup, Open, 0, new[] { (Arena.Wolf, Open.X + 6.0, Open.Z, "resting") },
                r => Knowing(Carrying(r, stack), "skill.one_hand_blade", 35, QuietBlade));
            Assert.Null(arena.Submit(new EquipCommand(arena.Player, arena.Simulation.Player.Inventory.Single(e => e.DefId == "item.weapon.iron_dagger").ItemId)));
            var wolf = arena.Creature();
            arena.TurnTo(0);   // face away from it, so only the sound can tell it anything
            if (technique is null)
                Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
            else
                Assert.Null(arena.Submit(new TechniqueCommand(arena.Player, technique)));
            arena.Tick(30);
            return arena.Simulation.Creatures.Single(c => c.Key == wolf.Key).Awareness;
        }

        Assert.Equal(0, AwarenessAfter(QuietBlade));
        Assert.True(AwarenessAfter(null) > 0, "an ordinary swing is heard");
    }

    [Fact]
    public void SteadyDraw_LoosesAHeldShot_WithoutLosingItsAim()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var bow = Arena.Stack("item.weapon.hunting_bow", 1);
        var arena = Arena.OpenCreatures(session, session.Setup, Open, 0, new[] { (Arena.Wolf, Open.X, Open.Z + 8.0, "pack_hunter") },
            r => Knowing(Carrying(r, bow, Arena.Stack("item.ammo.arrow_rough", 5)), "skill.bow", 20, SteadyDraw));
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, arena.Simulation.Player.Inventory.Single(e => e.DefId == "item.weapon.hunting_bow").ItemId)));
        var wolf = arena.Creature();
        arena.Face(wolf);
        var used = arena.Record<TechniqueUsed>();
        var loosed = arena.Record<ShotLoosed>();
        var steps = arena.Simulation.Setup.Techniques[SteadyDraw];

        Assert.Null(arena.Submit(new TechniqueCommand(arena.Player, SteadyDraw)));
        arena.Tick(60);

        Assert.Equal(SteadyDraw, Assert.Single(used).TechniqueId);
        Assert.Equal(SteadyDraw, Assert.Single(loosed).Source);
        Assert.True(steps.SteadyCritPercent > 0, "the held shot is truer than a hope");
    }

    [Fact]
    public void TheSevenBuildAsTechniquesTheHandCanUse_OnTheirOwnFamilies()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var techniques = session.Setup.Techniques;
        Assert.Equal(TechniqueKind.Parry, techniques[Parry].Kind);
        Assert.Equal(TechniqueKind.GuardBeat, techniques[BeatTheGuard].Kind);
        Assert.Equal(TechniqueKind.GivingGround, techniques[GivingGround].Kind);
        Assert.Equal(TechniqueKind.SetAgainstCharge, techniques[SetAgainstTheCharge].Kind);
        Assert.Equal(TechniqueKind.GapThrust, techniques[GapThrust].Kind);
        Assert.Equal(TechniqueKind.QuietBlade, techniques[QuietBlade].Kind);
        Assert.Equal(TechniqueKind.SteadyDraw, techniques[SteadyDraw].Kind);
        Assert.Equal(("skill.one_hand_blade", "skill.polearm", "skill.one_hand_blade", "skill.bow"),
            (techniques[Parry].SkillId, techniques[GivingGround].SkillId, techniques[GapThrust].SkillId, techniques[SteadyDraw].SkillId));
        // The dagger's techniques are made with the dagger: the family trains them, the weapon is the art.
        Assert.Equal(new[] { "item.weapon.iron_dagger", "item.weapon.haldas_knife" }, techniques[GapThrust].Weapons);
        Assert.True(techniques[GapThrust].MadeWith("item.weapon.iron_dagger", "skill.one_hand_blade"));
        Assert.False(techniques[GapThrust].MadeWith("item.weapon.rusted_sword", "skill.one_hand_blade"));
    }
}
