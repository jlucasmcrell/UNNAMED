using System.Collections.Immutable;
using System.Diagnostics;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit.Abstractions;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// M3d: creatures perceive, act by role, die into lootable corpses, return by their spawner's timer, and are saved - over
/// the game's own content (STEALTH_DETECTION_AND_THREAT.md; the content bible's five archetypes).
/// </summary>
public class CreatureTests
{
    private const string Hound = "creature.beast.ash_ember_hound";
    private const string Husk = "creature.undead.bone_walker_husk";
    private const string Armour = "creature.construct.animated_armour";
    private const string Boar = "creature.beast.bristleback_boar";
    private const string Spider = "creature.beast.cave_hunting_spider";

    private readonly ITestOutputHelper _output;

    public CreatureTests(ITestOutputHelper output) => _output = output;

    // ── helpers ─────────────────────────────────────────────────────────────

    private static Arena Place(GameSession session, (double X, double Z) player, int facingDeg, params (string DefId, double X, double Z, string Role)[] creatures) =>
        Arena.OpenCreatures(session, session.Setup, player, facingDeg, creatures);

    private static Arena Armed(GameSession session, (double X, double Z) player, int facingDeg, string weapon, params (string DefId, double X, double Z, string Role)[] creatures) =>
        Arena.OpenCreatures(session, session.Setup, player, facingDeg, creatures, r =>
        {
            var inventory = r.Inventory.Append(Arena.Stack("item.ammo.arrow_rough", 20)).ToList();
            var item = inventory.Single(e => e.DefId == weapon).ItemId;
            return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg, r.Discoveries,
                new[] { KeyValuePair.Create(EquipSlot.MainHand, item) }, r.Currency, r.Effects);
        });

    /// <summary>Where a creature placed alone at a spot faces when the world starts (placement is keyed, so it faces the same way again).</summary>
    private static double FacingOf(GameSession session, string defId, (double X, double Z) at, string role) =>
        Place(session, (at.X, at.Z - 60), 0, (defId, at.X, at.Z, role)).Simulation.Creatures.Single().Body.FacingMdeg / 1000.0 * Math.PI / 180;

    private static (double X, double Z) Along((double X, double Z) from, double facing, double metres) =>
        (from.X + Math.Sin(facing) * metres, from.Z + Math.Cos(facing) * metres);

    private static void Walk(Arena arena, (double X, double Z) to, Gait gait, int maxTicks = 2_000)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            var body = arena.Simulation.Player.Body;
            double dx = to.X * 1000 - body.XMm, dz = to.Z * 1000 - body.ZMm;
            if (Math.Sqrt(dx * dx + dz * dz) <= 300)
                break;
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, Harness.Toward(dx, dz, gait)));
            arena.Tick();
        }
        arena.Simulation.Enqueue(new MoveCommand(arena.Player, MoveIntent.Idle(arena.Simulation.Player.Body.FacingMdeg)));
        arena.Tick();
    }

    private static CreatureView Only(Arena arena) => arena.Simulation.Creatures.Single();

    private static CreatureView Of(Arena arena, string defId) => arena.Simulation.Creatures.Single(c => c.DefId == defId);

    private static double Distance(Body a, Body b) => Math.Sqrt(Math.Pow(a.XMm - b.XMm, 2) + Math.Pow(a.ZMm - b.ZMm, 2)) / 1000;

    private static void Shoot(Arena arena, (double X, double Z) at)
    {
        var body = arena.Simulation.Player.Body;
        arena.Simulation.Enqueue(new MoveCommand(arena.Player, MoveIntent.Idle(CombatRules.FacingTowards(body.XMm, body.ZMm, (long)(at.X * 1000), (long)(at.Z * 1000)))));
        arena.Tick();
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        arena.Tick(arena.Simulation.Combat.Weapon.TotalTicks + 1);
    }

    // ── perception ──────────────────────────────────────────────────────────

    [Fact]
    public void AWalkBehindAWolf_GoesUnheard_ButASprintIsHeard()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var wolf = (120.0, 80.0);
        double facing = FacingOf(session, Arena.Wolf, wolf, "pack_hunter");
        var start = Along(wolf, facing, -12);
        var close = Along(wolf, facing, -5);

        // Behind it, out of its field of view, a walk is quieter than 5 m carries (STEALTH §2, §7).
        var walker = Place(session, start, 0, (Arena.Wolf, wolf.Item1, wolf.Item2, "pack_hunter"));
        Walk(walker, close, Gait.Walk);
        walker.Tick(20);
        Assert.Equal((CreatureMind.Unaware, 0), (Only(walker).Mind, Only(walker).Awareness));

        // The same approach at a sprint carries 16 m: it hears, and turns to look.
        var sprinter = Place(session, start, 0, (Arena.Wolf, wolf.Item1, wolf.Item2, "pack_hunter"));
        var noticed = sprinter.Record<CreatureNoticed>();
        Walk(sprinter, close, Gait.Sprint);
        Assert.NotEmpty(noticed);
        Assert.NotEqual(CreatureMind.Unaware, Only(sprinter).Mind);
    }

    [Fact]
    public void AWallHidesYou_EvenFaceToFace()
    {
        using var profile = new TempProfile();
        // A wolf shut in the longhouse, the player outside its north wall, still and silent.
        var arena = Place(Harness.Boot(profile), (46, 52), 180, (Arena.Wolf, 46, 44, "pack_hunter"));
        arena.Tick(100);
        Assert.Equal((CreatureMind.Unaware, 0), (Only(arena).Mind, Only(arena).Awareness));
    }

    [Fact]
    public void AGuardiansHowl_BringsItsPack_ButNotOtherKinds()
    {
        using var profile = new TempProfile();
        var arena = Armed(Harness.Boot(profile), (120, 55), 0, "item.weapon.hunting_bow",
            (Arena.Wolf, 120, 70, "den_guardian"), (Arena.Wolf, 120, 95, "pack_hunter"), (Boar, 94, 74, "territorial"));
        var called = arena.Record<CreatureCalled>();

        Shoot(arena, (120, 70));   // wound the guardian: it has found its target, and howls
        arena.Tick(60);

        var guardian = arena.Simulation.Creatures.Single(c => c.Key.EndsWith("_0#0", StringComparison.Ordinal));
        Assert.Equal(guardian.Id, Assert.Single(called).Creature);
        // The hunter 25 m off heard its own kind and came; the boar in earshot knows nothing of it (no shared awareness).
        Assert.NotEqual(CreatureMind.Unaware, arena.Simulation.Creatures.Single(c => c.Key.EndsWith("_1#0", StringComparison.Ordinal)).Mind);
        Assert.Equal(CreatureMind.Unaware, Of(arena, Boar).Mind);
    }

    [Fact]
    public void ASleepingWolf_WakesToASprint_ButNotToAWalk()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var walker = Place(session, (120, 60), 0, (Arena.Wolf, 120, 75, "sleeper"));
        Walk(walker, (120, 71.5), Gait.Walk);   // 3.5 m from it: a walk carries 3 m
        walker.Tick(20);
        Assert.True(Only(walker).Asleep);

        var sprinter = Place(session, (120, 60), 0, (Arena.Wolf, 120, 75, "sleeper"));
        Walk(sprinter, (120, 66), Gait.Sprint);   // 9 m from it: a sprint carries 16 m, and asleep it hears 12
        Assert.False(Only(sprinter).Asleep);
    }

    [Fact]
    public void LostBehindADoor_ItSearches_ThenGivesUpAndGoesHome()
    {
        using var profile = new TempProfile();
        var arena = Armed(Harness.Boot(profile), (55.5, 44), 270, "item.weapon.hunting_bow", (Arena.Wolf, 64, 44, "pack_hunter"));
        var noticed = arena.Record<CreatureNoticed>();
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, "door.longhouse")));
        arena.Tick();
        Walk(arena, (52.6, 44), Gait.Walk);

        Shoot(arena, (64, 44));   // through the open door: now it knows where the shot came from
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, "door.longhouse")));   // and the door shuts
        arena.Tick(700);

        var minds = noticed.Select(n => n.Mind).ToList();
        int searched = minds.IndexOf(CreatureMind.Searching);
        Assert.True(searched >= 0 && minds.Skip(searched).Contains(CreatureMind.Returning), string.Join(" > ", minds));
        var wolf = Only(arena);
        Assert.Equal(CreatureMind.Unaware, wolf.Mind);
        Assert.InRange(Math.Sqrt(Math.Pow(wolf.Body.XMm - 64_000, 2) + Math.Pow(wolf.Body.ZMm - 44_000, 2)), 0, 800);
    }

    // ── roles ───────────────────────────────────────────────────────────────

    [Fact]
    public void AStrayKeepsItsDistance_Unhurt_AndRunsWhenBadlyHurt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var stray = (120.0, 72.0);
        double facing = FacingOf(session, Arena.Wolf, stray, "stray");
        var arena = Place(session, Along(stray, facing, 10), 0, (Arena.Wolf, stray.Item1, stray.Item2, "stray"));
        var started = arena.Record<AttackStarted>();
        var noticed = arena.Record<CreatureNoticed>();
        arena.Tick(20);   // it sees the player 10 m in front of it
        Assert.Equal(CreatureMind.Engaged, Only(arena).Mind);

        // The player walks in; it backs off rather than closing to bite.
        double nearest = double.MaxValue;
        for (int i = 0; i < 100; i++)
        {
            var wolf = Only(arena).Body;
            var body = arena.Simulation.Player.Body;
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, Harness.Toward(wolf.XMm - body.XMm, wolf.ZMm - body.ZMm, Gait.Walk)));
            arena.Tick();
            nearest = Math.Min(nearest, Distance(Only(arena).Body, arena.Simulation.Player.Body));
        }
        Assert.DoesNotContain(started, s => s.Attacker == Only(arena).Id);
        Assert.True(nearest > 3.5, $"it let the player within {nearest:0.0} m");

        // Hurt, it fights - and below 30% health it runs (STEALTH §16).
        arena.Fight(Only(arena), 1_200);
        Assert.Contains(noticed, n => n.Mind == CreatureMind.Fleeing);
    }

    [Fact]
    public void ARoamerWalksItsSpawnersRoute()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The game's own spawners; the player shut in the outpost, out of everyone's way.
        var arena = Arena.Open(session, (60, 50), 0, Array.Empty<(double, double)>(), keepSpawns: true);
        var route = session.Setup.Combat.Spawns.Single(s => s.Key == "spawn.hollow.east_pack").Route;
        var nearest = route.Select(_ => double.MaxValue).ToArray();
        for (int i = 0; i < 1_800; i++)
        {
            arena.Tick();
            foreach (var roamer in arena.Simulation.Creatures.Where(c => c.Key.StartsWith("spawn.hollow.east_pack#", StringComparison.Ordinal)))
            {
                for (int p = 0; p < route.Length; p++)
                    nearest[p] = Math.Min(nearest[p], Math.Sqrt(Math.Pow(roamer.Body.XMm - route[p].XMm, 2) + Math.Pow(roamer.Body.ZMm - route[p].ZMm, 2)));
            }
        }
        Assert.All(nearest, d => Assert.InRange(d, 0, 1_000));
    }

    // ── the five archetypes ─────────────────────────────────────────────────

    [Fact]
    public void TheHound_OutrunsASprint()
    {
        using var profile = new TempProfile();
        var arena = Armed(Harness.Boot(profile), (120, 72), 0, "item.weapon.hunting_bow", (Hound, 120, 80, "hunter"));
        var hits = arena.Record<HitResolved>();
        Shoot(arena, (Only(arena).Body.XMm / 1000.0, Only(arena).Body.ZMm / 1000.0));
        // Run for it: south at a sprint.
        long fastest = 0;
        var last = Only(arena).Body;
        for (int i = 0; i < 400 && !hits.Any(h => h.Target == arena.Player); i++)
        {
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, new MoveIntent(0, -1000, Gait.Sprint, 180_000)));
            arena.Tick();
            var now = Only(arena);
            if (now.Phase == CombatPhase.Idle)
                fastest = Math.Max(fastest, (long)(Distance(last, now.Body) * 1000));
            last = now.Body;
        }
        Assert.InRange(fastest, 280, 320);   // 6 m/s against a 5.12 m/s sprint
        Assert.Contains(hits, h => h.Attacker == Only(arena).Id && h.Target == arena.Player);
    }

    [Fact]
    public void TheHoundsEmberLunge_GoesThroughARaisedGuard()
    {
        using var profile = new TempProfile();
        var arena = Place(Harness.Boot(profile), (120, 60), 0, (Hound, 120, 66, "hunter"));
        var hits = arena.Record<HitResolved>();
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));   // a swing at the air: it hears, turns, and comes
        arena.Tick(20);
        for (int i = 0; i < 200 && !hits.Any(h => h.Target == arena.Player); i++)
        {
            arena.Face(Only(arena));
            if (!arena.Simulation.Combat.Blocking && arena.Simulation.Combat.Phase == CombatPhase.Idle)
                arena.Submit(new BlockCommand(arena.Player, true));
            arena.Tick();
        }
        var bite = hits.First(h => h.Target == arena.Player);
        Assert.Equal(Hound, bite.AttackerDefId);
        Assert.False(bite.Blocked);   // fire: the guard takes only physical blows
        Assert.True(bite.Damage > 0);
    }

    [Fact]
    public void TheHusk_OutreachesTheSword_AndArrowsBarelyScratchIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Place(session, (120, 60), 0, (Husk, 120, 64, "roamer"));
        var hits = arena.Record<HitResolved>();
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));   // a swing at the air: it hears, and comes
        double reachedFrom = 0;
        for (int i = 0; i < 400 && reachedFrom == 0; i++)
        {
            arena.Face(Only(arena));
            arena.Tick();
            if (hits.FirstOrDefault(h => h.Target == arena.Player) is not null)
                reachedFrom = Distance(Only(arena).Body, arena.Simulation.Player.Body);
        }
        // Centre to centre: the rusted sword reaches 1.8 m + the husk's 0.35 m body; the husk's cleave lands from further.
        Assert.InRange(reachedFrom, 2.2, 2.8);

        var archer = Armed(session, (120, 60), 0, "item.weapon.hunting_bow", (Husk, 120, 78, "roamer"));
        var arrows = archer.Record<HitResolved>();
        for (int shot = 0; shot < 4; shot++)
            Shoot(archer, (Only(archer).Body.XMm / 1000.0, Only(archer).Body.ZMm / 1000.0));
        var landed = arrows.Where(h => h.Attacker == archer.Player && h.Damage > 0).ToList();
        Assert.NotEmpty(landed);
        Assert.True(landed.Average(h => h.Damage) <= 5, $"arrows averaged {landed.Average(h => h.Damage):0.0} on bone");
    }

    [Fact]
    public void TheArmoursOpenBack_IsWhereABlowFromBehindLands()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var armour = (120.0, 63.0);
        double facing = FacingOf(session, Armour, armour, "sentinel");
        var behind = Along(armour, facing, -1.8);
        var arena = Place(session, behind, (int)Math.Round(facing * 180 / Math.PI + 360) % 360, (Armour, armour.Item1, armour.Item2, "sentinel"));
        var hits = arena.Record<HitResolved>();

        arena.Fight(Only(arena), 600, stayPut: true);
        var blows = hits.Where(h => h.Attacker == arena.Player && h.Damage > 0).ToList();
        // It turns at 90 degrees a second: the first blow lands behind it, on the helm's open back.
        Assert.Equal(BodyRegion.Head, blows[0].Region);
        Assert.True(blows[0].Damage >= 10, $"the first blow from behind did {blows[0].Damage}");
        // Once it faces the player, plate takes most of what lands on its body.
        var plated = blows.Skip(3).Where(h => h.Region != BodyRegion.Head).ToList();
        Assert.NotEmpty(plated);
        Assert.True(plated.Average(h => h.Damage) <= 5, $"blows on plate averaged {plated.Average(h => h.Damage):0.0}");
    }

    [Fact]
    public void ADodgedCharge_RunsTheBoarIntoTheRock_AndStunsIt()
    {
        using var profile = new TempProfile();
        // South of a boulder, the boar further south on the same line. It holds its ground here (a sentinel, for the test).
        var arena = Armed(Harness.Boot(profile), (33, 117.8), 180, "item.weapon.hunting_bow", (Boar, 33, 108, "sentinel"));
        var stunned = arena.Record<CreatureStunned>();
        var hits = arena.Record<HitResolved>();
        Shoot(arena, (33, 108));

        bool dodged = false;
        for (int i = 0; i < 200 && stunned.Count == 0; i++)
        {
            if (!dodged && Only(arena).Phase == CombatPhase.Active && arena.Simulation.Combat.Phase == CombatPhase.Idle)
            {
                // The run has begun and its line is set: step aside.
                Assert.Null(arena.Submit(new DodgeCommand(arena.Player, 1000, 0)));
                dodged = true;
            }
            arena.Tick();
        }
        Assert.True(dodged);
        Assert.Single(stunned);
        Assert.DoesNotContain(hits, h => h.Target == arena.Player);
        arena.Tick();
        Assert.Equal(CombatPhase.Staggered, Only(arena).Phase);
    }

    [Fact]
    public void AnUndodgedCharge_KnocksThePlayerDown()
    {
        using var profile = new TempProfile();
        var arena = Armed(Harness.Boot(profile), (33, 117.8), 180, "item.weapon.hunting_bow", (Boar, 33, 108, "sentinel"));
        var hits = arena.Record<HitResolved>();
        Shoot(arena, (33, 108));
        for (int i = 0; i < 200 && !hits.Any(h => h.Target == arena.Player); i++)
            arena.Tick();
        var charge = hits.First(h => h.Target == arena.Player);
        Assert.Equal("ability.creature.boar_charge", charge.Source);
        Assert.True(charge.Staggered);
        Assert.InRange(charge.Damage, 12, 16);
    }

    [Fact]
    public void TheSpider_LetsAWalkerPass_TakesARunner_AndKeepsToItsLair()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var walker = Place(session, (158, 40), 0, (Spider, 150, 60, "ambusher"));
        Walk(walker, (158, 80), Gait.Walk);   // 8 m past it: out of its 6 m sight, and a walk carries 3 m
        Assert.Equal(CreatureMind.Unaware, Only(walker).Mind);

        var runner = Place(session, (158, 40), 0, (Spider, 150, 60, "ambusher"));
        var noticed = runner.Record<CreatureNoticed>();
        Walk(runner, (158, 62), Gait.Run);   // a run carries 8 m: it feels it and goes for it
        Assert.Contains(noticed, n => n.Mind == CreatureMind.Engaged);
        double furthest = 0;
        for (int i = 0; i < 400; i++)
        {
            runner.Simulation.Enqueue(new MoveCommand(runner.Player, new MoveIntent(0, 1000, Gait.Sprint, 0)));
            runner.Tick();
            var spider = Only(runner).Body;
            furthest = Math.Max(furthest, Math.Sqrt(Math.Pow(spider.XMm - 150_000, 2) + Math.Pow(spider.ZMm - 60_000, 2)) / 1000);
        }
        Assert.InRange(furthest, 0, 15);   // it never leaves its lair's 14 m
    }

    [Fact]
    public void BloodlessBodies_DoNotBleed()
    {
        using var profile = new TempProfile();
        var combat = Harness.Boot(profile).Setup.Combat;
        var bleeding = combat.Effects["effect.bleeding"];
        Assert.True(bleeding.ImmuneTags.Overlaps(combat.Creatures[Husk].Tags));
        Assert.True(bleeding.ImmuneTags.Overlaps(combat.Creatures[Armour].Tags));
        Assert.False(bleeding.ImmuneTags.Overlaps(combat.Creatures[Arena.Wolf].Tags));
    }

    // ── corpses, respawn, persistence ───────────────────────────────────────

    [Fact]
    public void ACorpseHoldsItsLoot_AndSearchedEmpty_IsGone()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // Find a world whose wolf drops something (its table can roll nothing).
        for (ulong seed = 1; seed < 20; seed++)
        {
            var arena = Arena.OpenCreatures(session, session.Setup, (120, 60), 0, new[] { (Arena.Wolf, 120.0, 62.0, "pack_hunter") }, seed: seed);
            var gone = arena.Record<CorpseGone>();
            arena.Fight(Only(arena), 600);
            var wolf = Only(arena);
            Assert.Equal(CreatureCondition.Corpse, wolf.Condition);
            var corpse = arena.Simulation.Containers.Single(c => c.Site.Key == wolf.CorpseKey);
            if (corpse.Items.IsEmpty)
                continue;

            Walk(arena, (wolf.Body.XMm / 1000.0, wolf.Body.ZMm / 1000.0 - 1), Gait.Walk);
            foreach (var item in corpse.Items)
                Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, item.Ref, ItemPlace.In(wolf.CorpseKey), ItemPlace.Carried, item.Count)));
            Assert.Equal(CreatureCondition.Gone, Only(arena).Condition);
            Assert.Equal(wolf.CorpseKey, Assert.Single(gone).CorpseKey);
            Assert.DoesNotContain(arena.Simulation.Containers, c => c.Site.Key == wolf.CorpseKey);
            Assert.Null(arena.Simulation.World.Container(wolf.CorpseKey));
            Assert.All(corpse.Items, item => Assert.Contains(arena.Simulation.Player.Inventory, e => e.DefId == item.DefId));
            return;
        }
        Assert.Fail("no world in 19 dropped anything");
    }

    [Fact]
    public void ACreaturesDeathWoundsAndCorpse_SurviveSaveAndLoad()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (120, 60), 0,
            new[] { (Arena.Wolf, 120.0, 62.0, "pack_hunter"), (Arena.Wolf, 124.0, 60.0, "pack_hunter") });
        arena.Fight(arena.Creature(0), 800);
        var dead = arena.Creature(0);
        Assert.Equal(CreatureCondition.Corpse, dead.Condition);
        // Leave the other wounded and hunting, then save mid-hunt.
        arena.Fight(arena.Creature(1), 40);
        arena.Tick(10);

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("hunt"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        var loaded = store.Load(SaveSlots.Manual("hunt"), new LoadContext(session.Generator, session.Content, new Registry()));
        var again = Simulation.Start(arena.Simulation.Setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick, new EventBus());

        Assert.Equal(arena.Simulation.StateDigest(), again.StateDigest());
        Assert.Equal(arena.Simulation.Creatures.Select(c => (c.Key, c.Condition, c.Health, c.Body, c.Mind, c.Generation)),
            again.Creatures.Select(c => (c.Key, c.Condition, c.Health, c.Body, c.Mind, c.Generation)));
        Assert.Equal(arena.Simulation.Containers.Single(c => c.Site.Key == dead.CorpseKey).Items.Select(i => (i.DefId, i.Count)),
            again.Containers.Single(c => c.Site.Key == dead.CorpseKey).Items.Select(i => (i.DefId, i.Count)));
    }

    [Fact]
    public void ASpawnerBringsItsCreatureBack_AfterItsWindow_EvenAcrossALoad()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (120, 60), 0, new[] { (Arena.Wolf, 120.0, 62.0, "pack_hunter") }, respawnTicks: 1_200);
        var killed = arena.Record<CreatureKilled>();
        arena.Fight(Only(arena), 800);
        long died = Assert.Single(killed).Tick;
        var first = Only(arena);
        Walk(arena, (120, 110), Gait.Run);   // off beyond its leash: it will not return under the player's nose
        arena.Tick((int)(died + 600 - arena.Simulation.WorldTick));
        Assert.Equal(died + 1_200, arena.Simulation.World.Creature(first.Key)!.RespawnTick);

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("wait"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        var loaded = store.Load(SaveSlots.Manual("wait"), new LoadContext(session.Generator, session.Content, new Registry()));
        var bus = new EventBus();
        var back = new List<CreatureRespawned>();
        bus.Subscribe<CreatureRespawned>(back.Add);
        var again = Simulation.Start(arena.Simulation.Setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick, bus);
        for (int i = 0; i < 700; i++)
        {
            again.DrainCommands();
            again.Step();
        }
        var respawned = Assert.Single(back);
        Assert.Equal((died + 1_200, 1), (respawned.Tick, respawned.Generation));
        var wolf = again.Creatures.Single();
        Assert.Equal((CreatureCondition.Alive, 50), (wolf.Condition, wolf.Health));
        Assert.NotEqual(first.Id, wolf.Id);   // each generation its own identity
    }

    [Fact]
    public void ASaturatedSpawnCluster_TakesTwiceAsLongToRefill()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // AG-3 with a threshold of two kills, so the second kill at one spawner saturates it.
        var progression = session.Setup.Progression with { Guards = session.Setup.Progression.Guards with { ClusterThreshold = 2 } };
        var site = new SpawnSite("spawn.test.pair", 120_000, 63_000, 1_500,
            ImmutableArray.Create(new SpawnMember(Arena.Wolf, "pack_hunter"), new SpawnMember(Arena.Wolf, "pack_hunter"))) { RespawnTicks = 1_000 };
        var rules = session.Setup with { Progression = progression };
        var setup = rules with { Combat = rules.Combat with { Spawns = ImmutableArray.Create(site) } };
        var arena = Arena.OpenCreatures(session, setup, (120, 60), 0, Array.Empty<(string, double, double, string)>(), keepSpawns: true);
        var saturated = arena.Record<SpawnerSaturated>();
        var killed = arena.Record<CreatureKilled>();
        arena.Fight(arena.Creature(0), 800);
        arena.Fight(arena.Creature(1), 800);

        Assert.Equal(2, killed.Count);
        Assert.Equal("spawn.test.pair", Assert.Single(saturated).SpawnKey);
        Assert.Equal(killed[0].Tick + 1_000, arena.Simulation.World.Creature(arena.Creature(0).Key)!.RespawnTick);
        Assert.Equal(killed[1].Tick + 2_000, arena.Simulation.World.Creature(arena.Creature(1).Key)!.RespawnTick);
    }

    // ── cost ────────────────────────────────────────────────────────────────

    [Fact]
    public void SixtyCreatures_TickWithinTheBudget()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        string[] roles = { "pack_hunter", "roamer", "hunter", "sentinel", "stray", "territorial" };
        string[] kinds = { Arena.Wolf, Hound, Husk, Armour, Boar, Spider };
        var crowd = Enumerable.Range(0, 60).Select(i => (kinds[i % kinds.Length], 90.0 + i % 10 * 8, 80.0 + i / 10 * 8, roles[i % roles.Length])).ToArray();
        var arena = Arena.OpenCreatures(session, session.Setup, (126, 100), 0, crowd);
        arena.Tick(20);
        var clock = Stopwatch.StartNew();
        const int ticks = 400;
        arena.Tick(ticks);
        double ms = clock.Elapsed.TotalMilliseconds / ticks;
        _output.WriteLine($"60 creatures: {ms:0.000} ms a tick");
        // The M3 budget leaves the simulation a few milliseconds of a 16.7 ms frame; creature AI must not eat it.
        Assert.True(ms < 4, $"{ms:0.00} ms a tick for 60 creatures");
    }
}
