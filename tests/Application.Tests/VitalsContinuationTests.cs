using UNNAMED.Domain;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The character's pools go on across a save as they would have without it (schema 16; the owner's ruling on the M7 E8.5 STOP): the
/// regeneration delays after a blow, an exertion and a working, and the part-points each pool has accrued, are saved, so a loaded world
/// regenerates, tires and recovers on the ticks the unsaved one does. Found by the Crossing Workshop's step 10, where a save taken in a
/// fight with a wolf regenerated health 8 s early after the load (80 against 85 after 600 ticks).
/// </summary>
public class VitalsContinuationTests
{
    private static readonly (double X, double Z) Open = (120, 60);

    private static PlayerRecord Pools(PlayerRecord r, int? health = null, int? stamina = null, int? focus = null, int strain = 0) =>
        r.WithProgression(r.Progression with { Pools = r.Progression.Pools with { Health = health, Stamina = stamina, Focus = focus, Strain = strain } });

    /// <summary>The world saved now and loaded again, stepped the same way.</summary>
    private static Arena SavedAndLoaded(TempProfile profile, GameSession session, Arena arena, string slot)
    {
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual(slot), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        return Arena.Resume(arena.Simulation.Setup, store.Load(SaveSlots.Manual(slot), new LoadContext(session.Generator, session.Content, new Registry())));
    }

    private static (long Tick, int Health, int Stamina, int Focus, int Strain) Vitals(Arena arena)
    {
        var combat = arena.Simulation.Combat;
        return (arena.Simulation.WorldTick, combat.Health, combat.Stamina, combat.Focus, combat.Strain);
    }

    /// <summary>
    /// Both worlds on for <paramref name="ticks"/> ticks, the same commands each tick, and their pools compared every tick; at the end,
    /// every field of the two worlds. Returns the running world's pools, tick by tick.
    /// </summary>
    private static List<(long Tick, int Health, int Stamina, int Focus, int Strain)> GoOnTogether(Arena running, Arena loaded, int ticks,
        Func<Arena, GameCommand?>? each = null)
    {
        Assert.Equal(running.Simulation.CaptureRecord().Vitals, loaded.Simulation.CaptureRecord().Vitals);
        Assert.Equal(Vitals(running), Vitals(loaded));
        var seen = new List<(long Tick, int Health, int Stamina, int Focus, int Strain)>();
        for (int i = 0; i < ticks; i++)
        {
            foreach (var arena in new[] { running, loaded })
            {
                if (each?.Invoke(arena) is { } command)
                    arena.Simulation.Enqueue(command);
                arena.Tick();
            }
            Assert.Equal(Vitals(running), Vitals(loaded));
            seen.Add(Vitals(running));
        }
        var differences = StateDump.Compare(StateDump.Render(running.Simulation), StateDump.Render(loaded.Simulation), out int leaves);
        Assert.True(differences.Count == 0, $"{differences.Count} of {leaves} fields differ: {string.Join("; ", differences.Take(6))}");
        Assert.Equal(running.Simulation.StateDigest(), loaded.Simulation.StateDigest());
        return seen;
    }

    /// <summary>
    /// Health returns only 8 s after the last blow (<c>out_of_combat_s</c>), taken or dealt - a bleed's among them. A save 20 ticks after
    /// the killing blow on a wolf is inside that pause, and keeps what is left of it: the loaded world regenerates on the very ticks the
    /// unsaved one does, the first of them only once the pause is over.
    /// </summary>
    [Fact]
    public void ASaveInTheRegenerationDelay_KeepsWhatIsLeftOfIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.Open(session, Open, 0, new[] { (Open.X, Open.Z + 2.5) }, r => Pools(r, health: 60));
        var wolf = arena.Creature();
        Assert.True(arena.Fight(wolf, 1_200) < 1_200, "the wolf still stands");
        arena.Tick(20);
        Assert.True(arena.Simulation.Combat.Health < arena.Simulation.Combat.MaxHealth);
        long pause = arena.Simulation.Setup.Combat.Constants.OutOfCombatTicks;
        long saved = arena.Simulation.WorldTick;
        Assert.InRange(saved - arena.Simulation.CaptureRecord().Vitals.LastCombatTick, 1, pause - 1);   // inside the pause

        var loaded = SavedAndLoaded(profile, session, arena, "delay");
        var seen = GoOnTogether(arena, loaded, 800);
        int first = Enumerable.Range(1, seen.Count - 1).FirstOrDefault(i => seen[i].Health > seen[i - 1].Health);
        Assert.True(first > 0, "nothing regenerated in 800 ticks");
        long lastBlow = arena.Simulation.CaptureRecord().Vitals.LastCombatTick;
        Assert.True(seen[first].Tick >= lastBlow + pause, $"health returned at tick {seen[first].Tick}, before the pause after tick {lastBlow} was over");
    }

    /// <summary>
    /// Stamina returns only after a pause from the last exertion, a point at a time from thousandths accrued each tick; a sprint drains
    /// it the same way. Saves taken just after swings (in the pause), in the middle of a return (part of a point accrued) and in the
    /// middle of a sprint (part of a point drained) all go on as the unsaved world does.
    /// </summary>
    [Fact]
    public void Stamina_AfterExertion_ReturnsOnTheSameTicks_AcrossASave()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.Open(session, Open, 0, Array.Empty<(double, double)>());
        int swingTicks = arena.Simulation.Combat.Weapon.TotalTicks + 1;
        for (int i = 0; i < 3; i++)
        {
            Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
            arena.Tick(swingTicks);
        }
        Assert.True(arena.Simulation.Combat.Stamina < arena.Simulation.Combat.MaxStamina);
        var inThePause = SavedAndLoaded(profile, session, arena, "pause");
        GoOnTogether(arena, inThePause, 7);

        // Seven ticks on, the return has begun; a save now carries its part-point.
        var midReturn = SavedAndLoaded(profile, session, arena, "return");
        GoOnTogether(arena, midReturn, 200);

        // A sprint north, saved on its seventh tick, and sprinted on in both.
        GameCommand Sprint(Arena a) => new MoveCommand(a.Player, new MoveIntent(0, MoveIntent.FullDeflection, Gait.Sprint, 0));
        for (int i = 0; i < 7; i++)
        {
            arena.Simulation.Enqueue(Sprint(arena));
            arena.Tick();
        }
        var midSprint = SavedAndLoaded(profile, session, arena, "sprint");
        GoOnTogether(arena, midSprint, 60, Sprint);
    }

    /// <summary>
    /// Focus returns, and Strain ebbs, only after a pause from the last working. A save taken just after a working keeps both pauses;
    /// both pools then move on the same ticks as in the unsaved world.
    /// </summary>
    [Fact]
    public void FocusAndStrain_AfterAWorking_MoveOnTheSameTicks_AcrossASave()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const string Ward = "spell.warding.brace_ward";
        var arena = Arena.Open(session, Open, 0, Array.Empty<(double, double)>(), r => r.WithProgression(r.Progression with
        {
            Known = r.Progression.Known.SetItem(Ward, new KnownTechnique(LearningSource.Book, "item.tome.resonance_primer", 0)),
            Skills = r.Progression.Skills.SetItem("skill.warding", new SkillState(5, 0)),
        }));
        var formula = arena.Simulation.Setup.Magic.Formulas[Ward];
        Assert.Null(arena.Submit(new CastCommand(arena.Player, Ward)));
        arena.Tick(formula.CastTicks + 1 + formula.RecoveryTicks + 1);
        var after = arena.Simulation.Combat;
        Assert.True(after.Focus < after.MaxFocus && after.Strain > 0, $"the working left Focus {after.Focus} and Strain {after.Strain}");

        var loaded = SavedAndLoaded(profile, session, arena, "working");
        GoOnTogether(arena, loaded, 600);
        Assert.True(arena.Simulation.Combat.Focus > after.Focus && arena.Simulation.Combat.Strain < after.Strain, "nothing returned in 600 ticks");
    }
}
