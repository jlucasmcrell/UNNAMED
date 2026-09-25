using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Progression;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// M3e: three formulas of three domains, cast for Focus and Strain over the game's own content - never mana
/// (MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md; PROGRESSION.md §7; the content bible's §13).
/// </summary>
public class MagicTests
{
    private const string Bolt = "spell.force.impulse_bolt";
    private const string Ward = "spell.warding.brace_ward";
    private const string Thread = "spell.vital.mending_thread";
    private const string Primer = "item.tome.resonance_primer";
    private static readonly (double X, double Z) Ground = (120, 60);

    private static PlayerRecord Knowing(PlayerRecord r, params string[] formulas) =>
        r.WithProgression(r.Progression with
        {
            Known = r.Progression.Known.SetItems(formulas.Select(f => KeyValuePair.Create(f, new KnownTechnique(LearningSource.Book, Primer, 0)))),
        });

    private static PlayerRecord Skilled(PlayerRecord r, string skill, int level) =>
        r.WithProgression(r.Progression with { Skills = r.Progression.Skills.SetItem(skill, new SkillState(level, 0)) });

    private static PlayerRecord WithPools(PlayerRecord r, int? health = null, int? focus = null, int strain = 0) =>
        r.WithProgression(r.Progression with { Pools = r.Progression.Pools with { Health = health, Focus = focus, Strain = strain } });

    /// <summary>A caster who knows the three formulas, skilled enough in each domain that nothing fizzles - unless a change says otherwise.</summary>
    private static Arena Caster(GameSession session, Func<PlayerRecord, PlayerRecord>? change = null, ulong seed = 42,
        params (string DefId, double X, double Z, string Role)[] creatures) =>
        Arena.OpenCreatures(session, session.Setup, Ground, 0, creatures, r =>
        {
            var caster = Knowing(r, Bolt, Ward, Thread);
            foreach (string skill in new[] { "skill.force", "skill.warding", "skill.vital" })
                caster = Skilled(caster, skill, 5);
            return change?.Invoke(caster) ?? caster;
        }, seed);

    private static int Ticks(Arena arena, string formula)
    {
        var f = arena.Simulation.Setup.Magic.Formulas[formula];
        return f.CastTicks + 1 + f.RecoveryTicks + 1;
    }

    /// <summary>Cast and wait out the working: its tell, the release, the recovery.</summary>
    private static void Cast(Arena arena, string formula)
    {
        Assert.Null(arena.Submit(new CastCommand(arena.Player, formula)));
        arena.Tick(Ticks(arena, formula));
    }

    /// <summary>Swing the sword at what stands in front, and wait the swing out: the wolf is in the fight now.</summary>
    private static void Provoke(Arena arena)
    {
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        arena.Tick(arena.Simulation.Combat.Weapon.TotalTicks + 1);
    }

    // ── knowing ─────────────────────────────────────────────────────────────

    [Fact]
    public void ThePrimer_TeachesItsThreeFormulas_WithoutASingleCast()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, Ground, 0, Array.Empty<(string, double, double, string)>(),
            r => new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Append(Arena.Stack(Primer, 1)), r.Progression,
                r.FacingMdeg, r.Discoveries, r.Equipment, r.Currency, r.Effects));
        var learned = arena.Record<TechniqueLearned>();
        var cast = arena.Record<CastStarted>();

        // Unread, nothing can be cast: a formula is known only through a learning event (PROGRESSION.md §4.4).
        Assert.Equal($"{Bolt} is not known", arena.Submit(new CastCommand(arena.Player, Bolt)));
        var primer = arena.Simulation.Player.Inventory.Single(e => e.DefId == Primer);
        Assert.Null(arena.Submit(new UseItemCommand(arena.Player, primer.ItemId)));

        Assert.Equal(new[] { Bolt, Ward, Thread }, learned.Select(l => l.DefinitionId));
        Assert.All(learned, l => Assert.Equal("book", l.Source));
        Assert.All(new[] { Bolt, Ward, Thread }, f => Assert.Equal(Primer, arena.Simulation.Player.Progression.Known[f].SourceRef));
        Assert.DoesNotContain(arena.Simulation.Player.Inventory, e => e.DefId == Primer);   // read once
        Assert.Empty(cast);
        Assert.Empty(arena.Simulation.Player.Progression.Skills);                         // study teaches formulas, never skill
    }

    [Fact]
    public void AWorking_NeedsItsFocus_AndAFreeBody()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var tired = Caster(session, r => WithPools(r, focus: 9));
        Assert.Equal("not enough Focus", tired.Submit(new CastCommand(tired.Player, Bolt)));    // 10 Focus
        Assert.Equal("not enough Focus", tired.Submit(new CastCommand(tired.Player, Ward)));    // 12 Focus
        Assert.Equal("spell.force.nothing is not a formula this build knows", tired.Submit(new CastCommand(tired.Player, "spell.force.nothing")));

        var busy = Caster(session);
        Assert.Null(busy.Submit(new CastCommand(busy.Player, Thread)));
        busy.Tick(3);
        Assert.Equal("already casting (windup)", busy.Submit(new CastCommand(busy.Player, Ward)));
        Assert.Equal("already casting (windup)", busy.Submit(new AttackCommand(busy.Player)));
    }

    // ── the three formulas ──────────────────────────────────────────────────

    [Fact]
    public void TheImpulseBolt_StrikesAtRange_AndOnlyAWoundTeachesForce()
    {
        using var profile = new TempProfile();
        var arena = Caster(Harness.Boot(profile), creatures: (Arena.Wolf, 120, 75, "sentinel"));
        var hits = arena.Record<HitResolved>();
        var missed = arena.Record<AttackMissed>();
        var practised = arena.Record<SkillPracticed>();

        Cast(arena, Bolt);   // 15 m along the facing
        var hit = Assert.Single(hits, h => h.Attacker == arena.Player);
        Assert.Equal((Bolt, arena.Creature().Id), (hit.Source, hit.Target));
        Assert.True(hit.Damage > 0);
        var force = Assert.Single(practised, p => p.SkillId == "skill.force");
        Assert.True(force.Xp > 0);

        // A working thrown at nothing teaches nothing.
        arena.TurnTo(180);
        arena.Tick();
        Cast(arena, Bolt);
        Assert.Contains(missed, m => m.Attacker == arena.Player && m.Source == Bolt);
        Assert.Single(practised, p => p.SkillId == "skill.force");
    }

    [Fact]
    public void Resonance_NotMight_GivesTheBoltItsForce()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        int Damage(CharacterAttribute? raised)
        {
            var arena = Caster(session, r => raised is not { } attribute ? r : r.WithProgression(r.Progression with
            {
                Level = 6,
                Allocation = r.Progression.Allocation.SetItem(attribute, 5),
            }), creatures: (Arena.Wolf, 120, 75, "sentinel"));
            var hits = arena.Record<HitResolved>();
            Cast(arena, Bolt);
            return hits.Single(h => h.Attacker == arena.Player).Damage;
        }

        int plain = Damage(null);
        Assert.Equal(plain, Damage(CharacterAttribute.Might));   // the same roll, and Might adds nothing to a working
        Assert.True(Damage(CharacterAttribute.Will) > plain);   // Will raises Resonance, and Resonance the working
    }

    [Fact]
    public void BraceWard_HardensTheBody_ForTenSeconds()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        (int Damage, Arena Arena) Bites(bool braced)
        {
            var arena = Caster(session, creatures: (Arena.Wolf, 120, 62, "sentinel"));
            var hits = arena.Record<HitResolved>();
            if (braced)
                Cast(arena, Ward);
            else
                arena.Tick(Ticks(arena, Ward));
            Provoke(arena);
            arena.Tick(120);
            return (hits.Where(h => h.Target == arena.Player).Take(4).Sum(h => h.Damage), arena);
        }

        int bare = Bites(braced: false).Damage;
        var (warded, arena) = Bites(braced: true);
        Assert.True(warded < bare, $"four bites did {warded} braced against {bare} bare");

        var expired = arena.Record<EffectExpired>();
        arena.Tick(200);
        Assert.Contains(expired, e => e.Target == arena.Player && e.EffectId == "effect.braced");
    }

    [Fact]
    public void MendingThread_Mends_AndStopsTheBleed()
    {
        using var profile = new TempProfile();
        var arena = Caster(Harness.Boot(profile), r => WithPools(r, health: 60).WithEffects(new[] { new ActiveEffect("effect.bleeding", 2, 5_000, 20) }));
        var changed = arena.Record<HealthChanged>();
        Cast(arena, Thread);

        Assert.DoesNotContain(arena.Simulation.Combat.Effects, e => e.EffectId == "effect.bleeding");
        Assert.Contains(arena.Simulation.Combat.Effects, e => e.EffectId == "effect.mending");
        int bledBefore = changed.Count(c => c.Source == "effect.bleeding");
        arena.Tick(140);
        Assert.Equal(bledBefore, changed.Count(c => c.Source == "effect.bleeding"));   // the bleed stopped at the release
        Assert.Equal(18, changed.Where(c => c.Source == "effect.mending").Sum(c => c.Delta));
    }

    // ── Strain, Focus, and the risk past tolerance ───────────────────────────

    [Fact]
    public void Strain_BuildsWithEachWorking_AndEbbsAtRest_WhileFocusReturns()
    {
        using var profile = new TempProfile();
        var arena = Caster(Harness.Boot(profile));
        var completed = arena.Record<CastCompleted>();
        for (int i = 0; i < 3; i++)
            Cast(arena, Ward);
        int strain = completed.Sum(c => c.Strain);
        Assert.Equal((9, strain), (completed[0].Strain, arena.Simulation.Combat.Strain));   // 10 at complexity 3, 6% less at skill 5
        Assert.Equal(80 - 3 * 12, arena.Simulation.Combat.Focus);

        arena.Tick(20);   // too soon after the last working: nothing returns yet
        Assert.Equal((strain, 44), (arena.Simulation.Combat.Strain, arena.Simulation.Combat.Focus));
        arena.Tick(60);
        Assert.InRange(arena.Simulation.Combat.Strain, 1, strain - 1);
        Assert.InRange(arena.Simulation.Combat.Focus, 45, 79);
        arena.Tick(400);
        Assert.Equal((0, 80), (arena.Simulation.Combat.Strain, arena.Simulation.Combat.Focus));
    }

    [Fact]
    public void PastTolerance_AWorkingCostsHealth_NotPermission()
    {
        using var profile = new TempProfile();
        var arena = Caster(Harness.Boot(profile), r => WithPools(r, strain: 38));   // tolerance 40 at Will 10, Endurance 10
        var backlash = arena.Record<StrainBacklash>();
        var applied = arena.Record<EffectApplied>();
        int health = arena.Simulation.Combat.Health;
        Assert.True(arena.Simulation.Combat.Strained);

        Cast(arena, Ward);   // 9 more Strain: 7 past the limit, 2 health a point

        Assert.Equal(14, Assert.Single(backlash).Damage);
        Assert.Equal((40, health - 14), (arena.Simulation.Combat.Strain, arena.Simulation.Combat.Health));
        Assert.Contains(applied, a => a.EffectId == "effect.braced");   // the working still took hold
    }

    [Fact]
    public void AboveOnesSkill_AWorkingSometimesFizzles_AndAtItsComplexityNever()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        (int Fizzled, int Released) Casts(int skill)
        {
            var arena = Caster(session, r => Skilled(r, "skill.vital", skill));
            var fizzled = arena.Record<CastFizzled>();
            var completed = arena.Record<CastCompleted>();
            for (int i = 0; i < 40; i++)
            {
                Cast(arena, Thread);
                arena.Tick(120);   // let Focus and Strain come back
            }
            return (fizzled.Count, fizzled.Count + completed.Count);
        }

        var novice = Casts(0);
        Assert.Equal(40, novice.Released);
        Assert.InRange(novice.Fizzled, 1, 20);   // 15% at complexity 5 over skill 0
        Assert.Equal((0, 40), Casts(5));
    }

    // ── concentration ───────────────────────────────────────────────────────

    [Fact]
    public void AWoundInTheTell_BreaksTheWorking_AndADodgeAbandonsIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Caster(session, creatures: (Arena.Wolf, 120, 62, "sentinel"));
        var broken = arena.Record<CastInterrupted>();
        var applied = arena.Record<EffectApplied>();
        var bites = arena.Record<AttackStarted>();
        Provoke(arena);
        for (int i = 0; i < 200 && !bites.Any(b => b.Attacker == arena.Creature().Id); i++)
            arena.Tick();
        // The wolf has begun its bite: a one-second mending cannot beat it.
        Assert.Null(arena.Submit(new CastCommand(arena.Player, Thread)));
        arena.Tick(Ticks(arena, Thread));

        var interrupted = Assert.Single(broken);
        Assert.Equal((Thread, "wounded"), (interrupted.FormulaId, interrupted.Reason));
        Assert.DoesNotContain(applied, a => a.EffectId == "effect.mending");
        Assert.Equal((80 - 14, 0), (arena.Simulation.Combat.Focus, arena.Simulation.Combat.Strain));   // the Focus is gone; no Strain was taken

        var dodger = Caster(session);
        var abandoned = dodger.Record<CastInterrupted>();
        Assert.Null(dodger.Submit(new CastCommand(dodger.Player, Thread)));
        dodger.Tick(2);
        Assert.Null(dodger.Submit(new DodgeCommand(dodger.Player, 1000, 0)));
        Assert.Equal((Thread, "dodged"), (Assert.Single(abandoned).FormulaId, abandoned[0].Reason));
    }

    // ── what teaches what (the ratified two-mechanic split) ─────────────────

    [Fact]
    public void FiveHundredTrivialWorkings_TeachNoDomainSkill_AndNoFormula()
    {
        using var profile = new TempProfile();
        var arena = Arena.OpenCreatures(Harness.Boot(profile), Harness.Boot(profile).Setup, Ground, 0, Array.Empty<(string, double, double, string)>(),
            r => Knowing(r, Thread));
        var practised = arena.Record<SkillPracticed>();
        var learned = arena.Record<TechniqueLearned>();
        var released = 0;
        void Count(object _) => released++;
        arena.Bus.Subscribe<CastCompleted>(Count);
        arena.Bus.Subscribe<CastFizzled>(Count);

        // Mending an unhurt body, out of any fight, again and again: whenever Focus and Strain allow.
        var thread = arena.Simulation.Setup.Magic.Formulas[Thread];
        for (int ticks = 0; released < 500 && ticks < 400_000; ticks++)
        {
            var combat = arena.Simulation.Combat;
            if (combat.Phase == CombatPhase.Idle && combat.Focus >= thread.FocusCost && combat.Strain + 10 <= combat.StrainTolerance)
                Assert.Null(arena.Submit(new CastCommand(arena.Player, Thread)));
            arena.Tick();
        }

        Assert.Equal(500, released);
        Assert.Empty(practised);
        Assert.Empty(learned);
        Assert.Equal(0, ProgressionEngine.SkillLevel(arena.Simulation.Player.Progression, "skill.vital"));
        Assert.Equal(new[] { Thread }, arena.Simulation.Player.Progression.Known.Keys.Where(k => k.StartsWith("spell.", StringComparison.Ordinal)));
        Assert.DoesNotContain(Thread, arena.Simulation.Player.Progression.NoveltyFirsts);
    }

    /// <summary>
    /// The Phase-1 technical audit, L-10: a swing at the air marked the character as in a fight, so a swing before each working made it a
    /// challenged one - practice farmed with no enemy anywhere. A swing is not a fight.
    /// </summary>
    [Fact]
    public void ASwingAtTheAir_BeforeEachWorking_TeachesNothing()
    {
        using var profile = new TempProfile();
        var arena = Arena.OpenCreatures(Harness.Boot(profile), Harness.Boot(profile).Setup, Ground, 0, Array.Empty<(string, double, double, string)>(),
            r => Knowing(r, Thread));
        var practised = arena.Record<SkillPracticed>();
        var released = 0;
        void Count(object _) => released++;
        arena.Bus.Subscribe<CastCompleted>(Count);
        arena.Bus.Subscribe<CastFizzled>(Count);

        var thread = arena.Simulation.Setup.Magic.Formulas[Thread];
        bool swung = false;
        for (int ticks = 0; released < 100 && ticks < 200_000; ticks++)
        {
            var combat = arena.Simulation.Combat;
            if (combat.Phase == CombatPhase.Idle && !swung)
                swung = arena.Submit(new AttackCommand(arena.Player)) is null;
            else if (combat.Phase == CombatPhase.Idle && combat.Focus >= thread.FocusCost && combat.Strain + 10 <= combat.StrainTolerance)
                swung = arena.Submit(new CastCommand(arena.Player, Thread)) is not null;
            arena.Tick();
        }

        Assert.Equal(100, released);
        Assert.Empty(practised);
    }

    [Fact]
    public void AWardCastInTheFight_TeachesWarding_AndTheSameCastInPeaceDoesNot()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var fighting = Caster(session, creatures: (Arena.Wolf, 120, 62, "sentinel"));
        var practised = fighting.Record<SkillPracticed>();
        Provoke(fighting);
        Cast(fighting, Ward);
        Assert.Contains(practised, p => p.SkillId == "skill.warding" && p.Xp > 0);

        var peaceful = Caster(session);
        var none = peaceful.Record<SkillPracticed>();
        Cast(peaceful, Ward);
        Assert.Empty(none);
    }

    // ── saved ───────────────────────────────────────────────────────────────

    [Fact]
    public void ASaveAfterWorking_KeepsFocusStrainAndTheWard()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Caster(session);
        Cast(arena, Ward);
        Cast(arena, Bolt);
        arena.Tick(10);

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("warded"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        var loaded = store.Load(SaveSlots.Manual("warded"), new LoadContext(session.Generator, session.Content, new Registry()));
        var again = Simulation.Start(arena.Simulation.Setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick, new EventBus());

        Assert.Equal(arena.Simulation.StateDigest(), again.StateDigest());
        Assert.Equal((arena.Simulation.Combat.Focus, arena.Simulation.Combat.Strain), (again.Combat.Focus, again.Combat.Strain));
        Assert.Equal(arena.Simulation.Combat.Effects.ToList(), again.Combat.Effects.ToList());
        Assert.Contains(again.Combat.Effects, e => e.EffectId == "effect.braced");
        Assert.Equal(new[] { Bolt, Thread, Ward }, again.Player.Progression.Known.Keys.Where(k => k.StartsWith("spell.", StringComparison.Ordinal)));
    }
}
