using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// A stretch of the hollow with the game's own content and rules, the player placed where a test wants them and wolves
/// placed exactly (spawn radius 0), each in a role, instead of the region's spawners - or the region's own spawners when
/// a test keeps them. Stepped directly, one tick per call.
/// </summary>
internal sealed class Arena
{
    public const string Wolf = "creature.beast.wolf_grey";
    public const string Bite = "ability.creature.wolf_bite";

    private Arena(Simulation simulation, EventBus bus)
    {
        Simulation = simulation;
        Bus = bus;
    }

    public Simulation Simulation { get; }
    public EventBus Bus { get; }
    public EntityId Player => Simulation.PlayerId;

    public static Arena Open(GameSession session, (double X, double Z) player, int facingDeg, IEnumerable<(double X, double Z)> wolves,
        Func<PlayerRecord, PlayerRecord>? change = null, ulong seed = 42, long startTick = 0, string role = "pack_hunter", bool keepSpawns = false) =>
        OpenWith(session, session.Setup, player, facingDeg, wolves, change, seed, startTick, role, keepSpawns);

    /// <summary>The same, under changed rules (a different creature table, say).</summary>
    public static Arena OpenWith(GameSession session, SimulationSetup rules, (double X, double Z) player, int facingDeg,
        IEnumerable<(double X, double Z)> wolves, Func<PlayerRecord, PlayerRecord>? change = null, ulong seed = 42, long startTick = 0,
        string role = "pack_hunter", bool keepSpawns = false) =>
        OpenPlaced(session, rules, player, facingDeg, wolves.Select(w => (w.X, w.Z, role)), change, seed, startTick, keepSpawns);

    /// <summary>Wolves placed exactly, each in its own role.</summary>
    public static Arena OpenPlaced(GameSession session, SimulationSetup rules, (double X, double Z) player, int facingDeg,
        IEnumerable<(double X, double Z, string Role)> wolves, Func<PlayerRecord, PlayerRecord>? change = null, ulong seed = 42, long startTick = 0,
        bool keepSpawns = false) =>
        OpenCreatures(session, rules, player, facingDeg, wolves.Select(w => (Wolf, w.X, w.Z, w.Role)), change, seed, startTick, keepSpawns);

    /// <summary>
    /// Any creatures placed exactly, each in its own role, each its own spawner (<c>spawn.test.creature_N</c>) - in a world of the
    /// session's generator, or of another one (a baseline from before a change).
    /// </summary>
    public static Arena OpenCreatures(GameSession session, SimulationSetup rules, (double X, double Z) player, int facingDeg,
        IEnumerable<(string DefId, double X, double Z, string Role)> creatures, Func<PlayerRecord, PlayerRecord>? change = null, ulong seed = 42,
        long startTick = 0, bool keepSpawns = false, long respawnTicks = 0, ICellBaselineGenerator? generator = null)
    {
        var spawns = creatures.Select((w, i) => new SpawnSite($"spawn.test.{(w.DefId == Wolf ? "wolf" : "creature")}_{i}", (long)(w.X * 1000),
            (long)(w.Z * 1000), 0, ImmutableArray.Create(new SpawnMember(w.DefId, w.Role))) { RespawnTicks = respawnTicks }).ToImmutableArray();
        var setup = keepSpawns ? rules : rules with { Combat = rules.Combat with { Spawns = spawns } };
        var fresh = Simulation.NewCharacter(setup, EntityId.NewId(EntityKind.Character), "Wanderer", 7);
        long x = (long)(player.X * 1000), z = (long)(player.Z * 1000);
        var record = new PlayerRecord(fresh.Id, fresh.Name, x, setup.Layout.Space.Terrain.HeightAtMm(x, z), z, fresh.AppearanceSeed, fresh.Inventory,
            fresh.Progression, facingDeg * 1000 % 360_000, equipment: fresh.Equipment);
        record = change?.Invoke(record) ?? record;
        var bus = new EventBus();
        return new Arena(Simulation.Start(setup, record, new WorldDelta(generator ?? session.Generator, seed, new Registry()), startTick, bus), bus);
    }

    /// <summary>A loaded save, stepped the same way.</summary>
    public static Arena Resume(SimulationSetup setup, LoadResult loaded)
    {
        var bus = new EventBus();
        return new Arena(Simulation.Start(setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick, bus), bus);
    }

    public List<T> Record<T>()
    {
        var seen = new List<T>();
        Bus.Subscribe<T>(seen.Add);
        return seen;
    }

    public string? Submit(GameCommand command)
    {
        string? reason = null;
        void Rejected(CommandRejected r) => reason = r.Reason;
        Bus.Subscribe<CommandRejected>(Rejected);
        Simulation.Enqueue(command);
        Simulation.DrainCommands();
        Bus.Unsubscribe<CommandRejected>(Rejected);
        return reason;
    }

    public void Tick(int ticks = 1)
    {
        for (int i = 0; i < ticks; i++)
        {
            Simulation.DrainCommands();
            Simulation.Step();
        }
    }

    public CreatureView Creature(int index = 0) => Simulation.Creatures.OrderBy(c => c.Key, StringComparer.Ordinal).ElementAt(index);

    /// <summary>Stand still, facing a creature.</summary>
    public void Face(CreatureView creature)
    {
        var body = Simulation.Player.Body;
        Simulation.Enqueue(new MoveCommand(Player, MoveIntent.Idle(CombatRules.FacingTowards(body.XMm, body.ZMm, creature.Body.XMm, creature.Body.ZMm))));
    }

    public void TurnTo(int facingDeg) => Simulation.Enqueue(new MoveCommand(Player, MoveIntent.Idle(facingDeg * 1000 % 360_000)));

    /// <summary>Run to a point in metres the way the keys would, and stop there; false when it is not reached in time.</summary>
    public bool WalkTo(double x, double z, int maxTicks = 3000)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            var body = Simulation.Player.Body;
            double dx = x * 1000 - body.XMm, dz = z * 1000 - body.ZMm;
            if (Math.Sqrt(dx * dx + dz * dz) <= 300)
            {
                Simulation.Enqueue(new MoveCommand(Player, MoveIntent.Idle(body.FacingMdeg)));
                Tick();
                return true;
            }
            Simulation.Enqueue(new MoveCommand(Player, Harness.Toward(dx, dz)));
            Tick();
        }
        return false;
    }

    /// <summary>Fight one creature the simplest competent way: face it, close to reach, swing or shoot whenever free.</summary>
    public int Fight(CreatureView target, int maxTicks, bool stayPut = false)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            var creature = Simulation.Creatures.Single(c => c.Key == target.Key);
            if (!creature.Alive)
                return i;
            var body = Simulation.Player.Body;
            var combat = Simulation.Combat;
            double dx = creature.Body.XMm - body.XMm, dz = creature.Body.ZMm - body.ZMm;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            int facing = CombatRules.FacingTowards(body.XMm, body.ZMm, creature.Body.XMm, creature.Body.ZMm);
            bool inReach = combat.Weapon.Ranged || distance <= combat.Weapon.ReachMm + 450 - 150;
            Simulation.Enqueue(new MoveCommand(Player, inReach || stayPut
                ? MoveIntent.Idle(facing)
                : new MoveIntent((int)Math.Round(dx / distance * 1000), (int)Math.Round(dz / distance * 1000), Gait.Run, facing)));
            if (inReach && combat.Phase == CombatPhase.Idle)
                Simulation.Enqueue(new AttackCommand(Player));
            Tick();
        }
        return maxTicks;
    }

    public static InventoryEntry Stack(string defId, int count) => new(EntityId.NewId(EntityKind.Item), defId, count);
}

/// <summary>M3c: the combat core over the game's own content (PROTOTYPE.md §6.2's combat, death and consumption rows).</summary>
public class CombatTests
{
    private static readonly (double X, double Z) Open = (120, 60);

    private static PlayerRecord WithHealth(PlayerRecord record, int health) =>
        record.WithProgression(record.Progression with { Pools = record.Progression.Pools with { Health = health } });

    private static PlayerRecord Carrying(PlayerRecord record, params InventoryEntry[] extra) =>
        new(record.Id, record.Name, record.XMm, record.YMm, record.ZMm, record.AppearanceSeed, record.Inventory.Concat(extra), record.Progression,
            record.FacingMdeg, record.Discoveries, record.Equipment, record.Currency, record.Effects);

    private static PlayerRecord Wielding(PlayerRecord record, string weapon)
    {
        // The bow is found in the world, not carried from the start (M6): a character without one is handed one.
        var inventory = record.Inventory.Any(e => e.DefId == weapon) ? record.Inventory : record.Inventory.Add(Arena.Stack(weapon, 1));
        var item = inventory.Single(e => e.DefId == weapon).ItemId;
        return new PlayerRecord(record.Id, record.Name, record.XMm, record.YMm, record.ZMm, record.AppearanceSeed, inventory, record.Progression,
            record.FacingMdeg, record.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, item) }, record.Currency, record.Effects);
    }

    [Fact]
    public void TheWolf_IsInTheWorld_AtItsAuthoredLevel_AndTheStraysStandInTheValley()
    {
        using var profile = new TempProfile();
        var simulation = Harness.Boot(profile).NewGame("Tester", seed: 42);
        var strays = simulation.Creatures.Where(c => c.Key.StartsWith("spawn.hollow.valley_strays#", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, strays.Count);
        Assert.All(strays, s =>
        {
            Assert.Equal(Arena.Wolf, s.DefId);
            Assert.Equal((50, 50, true, false, "stray"), (s.Health, s.MaxHealth, s.Alive, s.Hostile, s.RoleId));
            Assert.InRange(Math.Sqrt(Math.Pow(s.Body.XMm - 118_000, 2) + Math.Pow(s.Body.ZMm - 128_000, 2)), 0, 6_000);
        });
        Assert.Equal(50, simulation.Setup.Combat.Creatures[Arena.Wolf].MaxHealth);
    }

    [Fact]
    public void TheSword_KillsAWolf_AndOnlyEffectiveBlowsTrainTheBlade()
    {
        using var profile = new TempProfile();
        var arena = Arena.Open(Harness.Boot(profile), Open, 0, new[] { (Open.X, Open.Z + 2.0) });
        var hits = arena.Record<HitResolved>();
        var practice = arena.Record<SkillPracticed>();
        var killed = arena.Record<CreatureKilled>();
        var xp = arena.Record<ExperienceGained>();

        int ticks = arena.Fight(arena.Creature(), 400);

        var kill = Assert.Single(killed);
        Assert.Equal(Arena.Wolf, kill.DefId);
        Assert.InRange(ticks, 40, 200);
        var effective = hits.Where(h => h.Attacker == arena.Player && h.Damage > 0).ToList();
        Assert.Equal(effective.Count, practice.Count);   // one practice per wounding blow, none for anything else
        Assert.All(practice, p => Assert.Equal("skill.one_hand_blade", p.SkillId));
        Assert.True(ProgressionEngine.SkillLevel(arena.Simulation.Player.Progression, "skill.one_hand_blade") >= 1);
        Assert.Contains(xp, x => x.Source == XpSource.Combat && x.Awarded > 0);
        Assert.False(arena.Creature().Alive);
    }

    [Fact]
    public void ASwingAtNothing_Misses_AndTeachesNothing()
    {
        using var profile = new TempProfile();
        var arena = Arena.Open(Harness.Boot(profile), Open, 0, Array.Empty<(double, double)>());
        var missed = arena.Record<AttackMissed>();
        var practice = arena.Record<SkillPracticed>();
        var started = arena.Record<AttackStarted>();

        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        Assert.Equal("already attacking (windup)", arena.Submit(new AttackCommand(arena.Player)));
        arena.Tick(20);

        var swing = Assert.Single(started);
        Assert.Equal((6, 3, 5), (swing.WindupTicks, swing.ActiveTicks, swing.RecoveryTicks));   // 0.7 s: 40% windup, 20% active
        Assert.Single(missed);
        Assert.Empty(practice);
        Assert.Equal(CombatPhase.Idle, arena.Simulation.Combat.Phase);
    }

    [Fact]
    public void TheBow_SpendsAnArrowAtEachRelease_AndWithNoneLeft_CannotShoot()
    {
        using var profile = new TempProfile();
        var arena = Arena.Open(Harness.Boot(profile), Open, 0, new[] { (Open.X, Open.Z + 12.0) },
            r => Wielding(Carrying(r, Arena.Stack("item.ammo.arrow_rough", 3)), "item.weapon.hunting_bow"));
        var spent = arena.Record<ItemConsumed>();
        var hits = arena.Record<HitResolved>();
        var practice = arena.Record<SkillPracticed>();

        for (int shot = 0; shot < 3; shot++)
        {
            arena.Face(arena.Creature());
            Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
            arena.Tick(arena.Simulation.Combat.Weapon.TotalTicks + 1);
        }

        Assert.Equal(3, spent.Count);
        Assert.All(spent, s => Assert.Equal(("item.ammo.arrow_rough", 1), (s.DefId, s.Count)));
        Assert.DoesNotContain(arena.Simulation.Player.Inventory, e => e.DefId == "item.ammo.arrow_rough");
        Assert.Contains(hits, h => h.Attacker == arena.Player && h.Source == "item.weapon.hunting_bow" && h.Damage > 0);
        Assert.Empty(practice);   // Phase 1 trains no bow skill
        Assert.Equal("no item.ammo.arrow_rough to shoot", arena.Submit(new AttackCommand(arena.Player)));
    }

    [Fact]
    public void AWall_StopsTheArrow_ThoughTheArrowIsSpent()
    {
        using var profile = new TempProfile();
        // Inside the longhouse, facing its north wall; the wolf stands outside it.
        var arena = Arena.Open(Harness.Boot(profile), (44, 128), 0, new[] { (44.0, 134.5) },
            r => Wielding(Carrying(r, Arena.Stack("item.ammo.arrow_rough", 1)), "item.weapon.hunting_bow"));
        var missed = arena.Record<AttackMissed>();
        var spent = arena.Record<ItemConsumed>();

        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        arena.Tick(30);

        Assert.Single(missed);
        Assert.Single(spent);
        Assert.Equal(50, arena.Creature().Health);
        Assert.False(arena.Creature().Hostile);   // it felt nothing, so it knows nothing
    }

    [Fact]
    public void TheGuard_TakesAFrontalBite_ForStamina_ButNotOneFromBehind()
    {
        using var profile = new TempProfile();
        var arena = Arena.Open(Harness.Boot(profile), Open, 0, new[] { (Open.X, Open.Z + 1.9) });
        var hits = arena.Record<HitResolved>();

        // Wound it so it turns on the player, then raise the guard toward it.
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        arena.Tick(15);
        Assert.True(arena.Creature().Hostile);
        arena.Face(arena.Creature());
        Assert.Null(arena.Submit(new BlockCommand(arena.Player, true)));
        for (int i = 0; i < 80 && !hits.Any(h => h.Target == arena.Player); i++)
            arena.Tick();
        var guarded = hits.Single(h => h.Target == arena.Player);
        Assert.True(guarded.Blocked);
        Assert.False(guarded.Staggered);

        // Turn the guard away: the next bite comes from outside its arc.
        arena.TurnTo(180);
        int before = hits.Count(h => h.Target == arena.Player);
        for (int i = 0; i < 80 && hits.Count(h => h.Target == arena.Player) == before; i++)
            arena.Tick();
        Assert.False(hits.Last(h => h.Target == arena.Player).Blocked);
    }

    [Fact]
    public void ADodgeInsideTheWindup_AvoidsTheBite()
    {
        using var profile = new TempProfile();
        var arena = Arena.Open(Harness.Boot(profile), Open, 0, new[] { (Open.X, Open.Z + 1.9) });
        var started = arena.Record<AttackStarted>();
        var hits = arena.Record<HitResolved>();

        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        for (int i = 0; i < 80 && !started.Any(s => s.Attacker != arena.Player); i++)
            arena.Tick();
        var bite = started.Single(s => s.Attacker != arena.Player);
        Assert.Equal(Arena.Bite, bite.Source);
        int health = arena.Simulation.Combat.Health;
        // Straight back, out of the lunge, inside its 0.45 s tell.
        Assert.Null(arena.Submit(new DodgeCommand(arena.Player, 0, -1000)));
        arena.Tick(bite.WindupTicks + bite.ActiveTicks);

        Assert.DoesNotContain(hits, h => h.Target == arena.Player && h.Damage > 0);
        Assert.Equal(health, arena.Simulation.Combat.Health);
    }

    [Fact]
    public void Dying_OwesXpDebt_Once_RespawnsAtTheOutpost_Weakened_AndNamesItsKiller()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.Open(session, Open, 0, new[] { (Open.X, Open.Z + 1.9) }, r => WithHealth(r, 3));
        var died = arena.Record<PlayerDied>();
        var respawned = arena.Record<PlayerRespawned>();
        var before = arena.Simulation.Player.Progression;

        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        for (int i = 0; i < 200 && died.Count == 0; i++)
            arena.Tick();

        var death = Assert.Single(died);
        Assert.Equal((Arena.Wolf, Arena.Bite), (death.KillerDefId, death.Cause));
        Assert.NotEmpty(death.Recap);
        var rules = session.Setup.Progression;
        long owed = (long)Math.Round(rules.Curve.ToReach(2) * rules.Guards.DebtFraction, MidpointRounding.AwayFromZero);
        Assert.Equal(owed, death.DebtAdded);
        var after = arena.Simulation.Player.Progression;
        Assert.Equal((before.Level, before.LevelProgressXp, owed), (after.Level, after.LevelProgressXp, after.XpDebt));   // debt, not lost XP
        Assert.Equal(PoolState.Full, after.Pools);
        var spawn = session.Setup.Layout.Spawn;
        Assert.Equal((spawn.XMm, spawn.ZMm), (Assert.Single(respawned).Body.XMm, arena.Simulation.Player.Body.ZMm));
        var weakened = Assert.Single(arena.Simulation.Combat.Effects);
        Assert.Equal(("effect.weakened", death.Tick + 1_200), (weakened.EffectId, weakened.ExpiresTick));
        arena.Tick(1);
        Assert.False(arena.Creature().Hostile);

        // The penalty applies exactly once (PROTOTYPE.md C17).
        arena.Tick(400);
        Assert.Single(died);
        Assert.Equal(owed, arena.Simulation.Player.Progression.XpDebt);
    }

    [Fact]
    public void Weakness_TakesAQuarterOffEveryBlow()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        int Blow(IEnumerable<ActiveEffect> effects)
        {
            var arena = Arena.Open(session, Open, 0, new[] { (Open.X, Open.Z + 1.9) }, r => r.WithEffects(effects));
            var hits = arena.Record<HitResolved>();
            Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
            arena.Tick(12);
            return hits.Single(h => h.Attacker == arena.Player).Damage;
        }

        int strong = Blow(Array.Empty<ActiveEffect>());
        int weak = Blow(new[] { new ActiveEffect("effect.weakened", 1, 1_200, 1) });
        Assert.True(weak < strong, $"weakened {weak} vs {strong}");
    }

    [Fact]
    public void Effects_ResumeAfterASaveAndLoad_OnTheSameTicks()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var bleeding = new[] { new ActiveEffect("effect.bleeding", 2, 120, 20) };
        // The game's own spawners on both sides of the save: the loaded session places the same wolves, doing the same things.
        var continuous = Arena.Open(session, (60, 50), 0, Array.Empty<(double, double)>(), r => r.WithEffects(bleeding), keepSpawns: true);
        continuous.Tick(50);

        new SaveStore(profile.Root).Save(SaveSlots.Manual("bleeding"), SaveDocuments.Capture(continuous.Simulation.World,
            continuous.Simulation.CaptureRecord(), session.Content, continuous.Simulation.WorldTick, 0));
        var loaded = session.Load(SaveSlots.Manual("bleeding"));
        Assert.Equal(bleeding[0] with { NextTickAt = 60 }, Assert.Single(loaded.Player.Effects));

        continuous.Tick(100);
        for (int i = 0; i < 100; i++)
        {
            session.Simulation!.DrainCommands();
            session.Simulation.Step();
        }
        Assert.Equal(continuous.Simulation.WorldTick, session.Simulation!.WorldTick);
        Assert.Equal(continuous.Simulation.StateDigest(), session.Simulation.StateDigest());
        Assert.Empty(session.Simulation.Combat.Effects);
        Assert.Equal(session.Simulation.Combat.MaxHealth - 12, session.Simulation.Combat.Health);   // 6 ticks x 2 stacks
    }

    [Fact]
    public void TheSalve_Mends18Over6Seconds_AndIsUsedUp()
    {
        using var profile = new TempProfile();
        var arena = Arena.Open(Harness.Boot(profile), Open, 0, Array.Empty<(double, double)>(),
            r => WithHealth(r, 40));   // the starting kit's salve (content bible §14)
        var healed = arena.Record<HealthChanged>();
        var salve = arena.Simulation.Player.Inventory.Single(e => e.DefId == "item.consumable.salve_minor");

        Assert.Null(arena.Submit(new UseItemCommand(arena.Player, salve.ItemId)));
        arena.Tick(140);

        Assert.Equal(18, healed.Where(h => h.Source == "effect.mending").Sum(h => h.Delta));
        Assert.Equal(6, healed.Count(h => h.Source == "effect.mending"));
        Assert.DoesNotContain(arena.Simulation.Player.Inventory, e => e.DefId == "item.consumable.salve_minor");
        var flask = arena.Simulation.Player.Inventory.Single(e => e.DefId == "item.tool.water_flask").ItemId;
        Assert.Equal("item.tool.water_flask has no use", arena.Submit(new UseItemCommand(arena.Player, flask)));
    }

    [Fact]
    public void Sprinting_SpendsStamina_AndAnEmptyPoolOnlyRuns()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.Open(session, Open, 90, Array.Empty<(double, double)>());
        arena.Simulation.Enqueue(new MoveCommand(arena.Player, new MoveIntent(1000, 0, Gait.Sprint, 90_000)));
        long x0 = arena.Simulation.Player.Body.XMm;
        arena.Tick(40);
        var combat = arena.Simulation.Combat;
        Assert.Equal(combat.MaxStamina - 20, combat.Stamina);   // 10 a second for 2 s
        Assert.InRange(arena.Simulation.Player.Body.XMm - x0, 40 * 256 - 40, 40 * 256 + 40);   // 5.12 m/s

        var spent = Arena.Open(session, Open, 90, Array.Empty<(double, double)>(),
            r => r.WithProgression(r.Progression with { Pools = r.Progression.Pools with { Stamina = 0 } }));
        spent.Simulation.Enqueue(new MoveCommand(spent.Player, new MoveIntent(1000, 0, Gait.Sprint, 90_000)));
        long x1 = spent.Simulation.Player.Body.XMm;
        spent.Tick(10);
        Assert.InRange(spent.Simulation.Player.Body.XMm - x1, 10 * 160 - 20, 10 * 160 + 20);   // 3.2 m/s: a run
        Assert.Equal("too tired to dodge", spent.Submit(new DodgeCommand(spent.Player, 1000, 0)));
    }

    [Fact]
    public void Stagger_NeverLocks_ABody()
    {
        using var profile = new TempProfile();
        // Two points of Might: every torso blow of the rusted sword clears the wolf's stagger threshold.
        var arena = Arena.Open(Harness.Boot(profile), Open, 0, new[] { (Open.X, Open.Z + 1.9) },
            r => r.WithProgression(r.Progression with
            {
                Allocation = ImmutableSortedDictionary.CreateRange(new[] { KeyValuePair.Create(CharacterAttribute.Might, 2) }),
            }));
        var hits = arena.Record<HitResolved>();

        arena.Fight(arena.Creature(), 400, stayPut: true);

        var staggers = hits.Where(h => h.Attacker == arena.Player && h.Staggered).Select(h => h.Tick).ToList();
        Assert.NotEmpty(staggers);
        Assert.All(staggers.Zip(staggers.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 40, $"staggered at {pair.First} and {pair.Second}"));
    }

    [Fact]
    public void AFightReplayedFromItsCommandLog_EndsIdentical()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var live = Arena.Open(session, Open, 0, new[] { (Open.X, Open.Z + 2.0) });
        var start = live.Simulation.CaptureRecord();
        var killed = live.Record<CreatureKilled>();
        live.Fight(live.Creature(), 400);
        live.Tick(40);
        Assert.Single(killed);

        // The replay starts from the same record and world seed, and applies each command at its logged tick.
        var replay = Arena.Open(session, Open, 0, new[] { (Open.X, Open.Z + 2.0) }, _ => start);
        var replayKilled = replay.Record<CreatureKilled>();
        var log = live.Simulation.CommandLog.GroupBy(c => c.Tick).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Sequence).ToList());
        while (replay.Simulation.WorldTick < live.Simulation.WorldTick)
        {
            foreach (var command in log.GetValueOrDefault(replay.Simulation.WorldTick) ?? new List<LoggedCommand>())
                replay.Simulation.Enqueue(command.Command);
            replay.Tick();
        }

        Assert.Equal(killed.Single().Tick, Assert.Single(replayKilled).Tick);
        Assert.Equal(live.Simulation.StateDigest(), replay.Simulation.StateDigest());
    }

    [Fact]
    public void AWolfWoundedFromBeyondItsLeash_GivesUp_AndGoesHome()
    {
        using var profile = new TempProfile();
        var arena = Arena.Open(Harness.Boot(profile), (120, 50), 0, new[] { (120.0, 89.5) },
            r => Wielding(Carrying(r, Arena.Stack("item.ammo.arrow_rough", 1)), "item.weapon.hunting_bow"));
        var hits = arena.Record<HitResolved>();

        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        arena.Tick(20);
        Assert.Contains(hits, h => h.Attacker == arena.Player && h.Damage > 0);
        Assert.True(arena.Creature().Hostile);

        // Step back past 40 m from its home: it stops, and walks back.
        arena.Simulation.Enqueue(new MoveCommand(arena.Player, new MoveIntent(0, -1000, Gait.Run, 0)));
        arena.Tick(20);
        arena.Simulation.Enqueue(new MoveCommand(arena.Player, MoveIntent.Idle(0)));
        arena.Tick(200);
        var wolf = arena.Creature();
        Assert.False(wolf.Hostile);
        Assert.InRange(Math.Abs(wolf.Body.ZMm - 89_500), 0, 1_000);
        Assert.True(wolf.Health < wolf.MaxHealth);   // giving up does not heal it
    }
}
