using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// M6: the companion over the game's own content (SYSTEMS.md S-25; PROTOTYPE.md C16 and §6.2's companion-state row; content bible §17)
/// - Tavar recruited through his conversation; follow, wait and follow again; the lodge walked through without a snag that lasts;
/// catching up when left far behind; fighting what hunts the character, and being fought; downed, helped up, and falling back to
/// the Ashen Waystone; and his state through a save, field by field.
/// </summary>
public class CompanionTests
{
    private const string Tavar = "npc.ashen_hollow.tavar_orr";
    private const string Bow = "item.weapon.hunting_bow";
    private const string Arrows = "item.ammo.arrow_rough";

    /// <summary>An open stretch east of the smithy, clear of everything.</summary>
    private static readonly (double X, double Z) Open = (70, 152);

    private static CompanionRecord Joined(double x, double z, CompanionOrder order = CompanionOrder.Follow, int health = 100) =>
        new(Tavar, order, CompanionCondition.Up, (long)(x * 1000), (long)(z * 1000), 90_000, health);

    private static Arena With(GameSession session, (double X, double Z) player, CompanionRecord tavar,
        IEnumerable<(string, double, double, string)>? creatures = null, Func<PlayerRecord, PlayerRecord>? change = null) =>
        Arena.OpenCreatures(session, session.Setup, player, 90, creatures ?? Array.Empty<(string, double, double, string)>(),
            r => (change?.Invoke(r) ?? r).WithCompanions(new[] { tavar }));

    private static CompanionView Him(Arena arena) => arena.Simulation.Companions.Single();

    private static double Apart(Arena arena)
    {
        var him = Him(arena).Body;
        var me = arena.Simulation.Player.Body;
        return Math.Sqrt(Math.Pow(him.XMm - me.XMm, 2) + Math.Pow(him.ZMm - me.ZMm, 2));
    }

    private static void Walk(Arena arena, params (double X, double Z)[] route)
    {
        foreach (var (x, z) in route)
            Assert.True(arena.WalkTo(x, z), $"never reached ({x}, {z})");
    }

    private static string? Order(Arena arena, CompanionOrder order) => arena.Submit(new OrderCompanionCommand(arena.Player, Tavar, order));

    [Fact]
    public void Tavar_JoinsWhenAsked_AndFollows()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // Freed already: the fold is not what this test is about.
        var rules = session.Setup with { Layout = session.Setup.Layout with { Barriers = ImmutableArray<BarrierSite>.Empty } };
        var arena = Arena.OpenCreatures(session, rules, (146.2, 42.8), 240, Array.Empty<(string, double, double, string)>());
        var recruited = arena.Record<CompanionRecruited>();

        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Tavar)));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "found")));
        Assert.Contains("join", arena.Simulation.Conversation!.Replies.Select(r => r.Id));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "join")));
        Assert.Equal("joined", arena.Simulation.Conversation!.NodeId);
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "ok")));

        Assert.Equal(new CompanionRecruited(Tavar, 0), Assert.Single(recruited));
        var him = Him(arena);
        Assert.Equal((CompanionOrder.Follow, CompanionCondition.Up, 100, 100, "healthy"), (him.Order, him.Condition, him.Health, him.MaxHealth, him.Standing));
        // Asked again, he is already with the character: he offers to wait, not to join.
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Tavar)));
        Assert.Equal(new[] { "stones", "wait", "done" }, arena.Simulation.Conversation!.Replies.Select(r => r.Id));
        Assert.Null(arena.Submit(new LeaveCommand(arena.Player)));

        // He keeps close as the character walks off across the Foldscar.
        Walk(arena, (140, 55), (130, 70));
        arena.Tick(40);
        Assert.InRange(Apart(arena), 0, 4_000);
    }

    /// <summary>C16's order sequence - follow, wait, follow - with the character ~18 m off while he waits (PROTOTYPE.md §7.4 step 5).</summary>
    [Fact]
    public void FollowWaitFollow_HeKeepsHisPlaceWhileWaiting_AndComesWhenCalled()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = With(session, Open, Joined(68, 152));
        var ordered = arena.Record<CompanionOrdered>();
        var caughtUp = arena.Record<CompanionCaughtUp>();

        Assert.Null(Order(arena, CompanionOrder.Wait));
        var waitingAt = Him(arena).Body;
        Walk(arena, (88, 152));
        arena.Tick(20);
        Assert.Equal((waitingAt.XMm, waitingAt.ZMm), (Him(arena).Body.XMm, Him(arena).Body.ZMm));
        Assert.Equal("waiting", Him(arena).Doing);
        Assert.InRange(Apart(arena), 17_000, 21_000);

        Assert.Null(Order(arena, CompanionOrder.Follow));
        int ticks = 0;
        while (Apart(arena) > 3_000 && ticks++ < 300)
            arena.Tick();
        Assert.True(ticks < 300, "he never came");
        Assert.Equal(new[] { CompanionOrder.Wait, CompanionOrder.Follow }, ordered.Select(o => o.Order));
        Assert.Empty(caughtUp);   // 18 m in the open is walked, not skipped
        Assert.Null(Order(arena, CompanionOrder.Follow));   // already following: nothing to do
        Assert.Equal(2, ordered.Count);
    }

    /// <summary>
    /// C16: into Renn's lodge through its narrow door, out again and round the building, with him following - and no snag that holds him
    /// for more than 15 seconds (PROTOTYPE.md: "Kesh requires > 15 s to recover from any single geometry snag" falsifies it).
    /// </summary>
    [Fact]
    public void ThroughTheLodgeAndRoundIt_NoSnagHoldsHimFifteenSeconds()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = With(session, (44, 138), Joined(42, 139));
        var caughtUp = arena.Record<CompanionCaughtUp>();
        long maxHeld = 0, held = 0;
        Body last = Him(arena).Body;
        void Watch()
        {
            var now = Him(arena).Body;
            held = (now.XMm, now.ZMm) == (last.XMm, last.ZMm) && Apart(arena) > 3_500 ? held + 1 : 0;
            maxHeld = Math.Max(maxHeld, held);
            last = now;
        }

        foreach (var (x, z) in new[] { (44.0, 138.0), (54.5, 134.0), (53.0, 128.0) })
        {
            while (!ArriveOnce(arena, x, z))
                Watch();
        }
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, "door.longhouse")));
        foreach (var (x, z) in new[] { (50.5, 128.0), (44.0, 128.6), (38.5, 130.5), (44.0, 128.6), (50.5, 128.0), (53.5, 128.0), (54.5, 134.0),
                     (55.0, 121.0), (35.0, 121.0), (34.0, 134.0), (44.0, 138.0) })
        {
            int budget = 0;
            while (!ArriveOnce(arena, x, z))
            {
                Watch();
                Assert.True(budget++ < 2_000, $"never reached ({x}, {z})");
            }
        }
        for (int i = 0; i < 100; i++)
        {
            arena.Tick();
            Watch();
        }

        Assert.True(maxHeld < 15 * 20, $"he was held {maxHeld} ticks");
        Assert.Empty(caughtUp);   // the trail took him through the door and round the walls: he never had to be put down
        Assert.InRange(Apart(arena), 0, 4_000);
    }

    /// <summary>One tick of walking towards a point; true once there.</summary>
    private static bool ArriveOnce(Arena arena, double x, double z)
    {
        var body = arena.Simulation.Player.Body;
        double dx = x * 1000 - body.XMm, dz = z * 1000 - body.ZMm;
        if (Math.Sqrt(dx * dx + dz * dz) <= 300)
        {
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, MoveIntent.Idle(body.FacingMdeg)));
            arena.Tick();
            return true;
        }
        arena.Simulation.Enqueue(new MoveCommand(arena.Player, Harness.Toward(dx, dz)));
        arena.Tick();
        return false;
    }

    [Fact]
    public void LeftFarBehind_HeCatchesUp_ToASpotNearTheCharacter()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = With(session, Open, Joined(68, 152));
        var caughtUp = arena.Record<CompanionCaughtUp>();

        Assert.Null(Order(arena, CompanionOrder.Wait));
        Walk(arena, (90, 152), (110, 150));
        Assert.True(Apart(arena) > session.Setup.Social.Companions!.CatchUpBeyondMm);
        Assert.Null(Order(arena, CompanionOrder.Follow));
        arena.Tick();

        var jump = Assert.Single(caughtUp);
        Assert.Equal("distance", jump.Reason);
        Assert.InRange(Apart(arena), 2_000, 3_000);
        Assert.True(Kinematics.IsClear(Him(arena).Body.XMm, Him(arena).Body.ZMm, session.Setup.Movement.BodyRadiusMm, session.Setup.Layout.Space,
            new List<Blocker>()), "he was put down inside something");
    }

    /// <summary>
    /// He fights what hunts the character: a wolf set on the character is his to fight, the wolf turns on him because he is nearer, and
    /// a kill that is his earns the character nothing (M6's default).
    /// </summary>
    [Fact]
    public void HeFightsWhatHuntsTheCharacter_AndItFightsHim()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = With(session, Open, Joined(72, 153), new[] { (Arena.Wolf, 76.0, 152.0, "pack_hunter") },
            r => r.WithInventory(r.Inventory.Add(Arena.Stack(Bow, 1)).Add(Arena.Stack(Arrows, 10))));
        var tavarId = Him(arena).InstanceId;
        var hits = arena.Record<HitResolved>();
        var killed = arena.Record<CreatureKilled>();

        // An arrow sets the wolf on the character.
        var bow = arena.Simulation.Player.Inventory.Single(e => e.DefId == Bow).ItemId;
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, bow)));
        arena.Face(arena.Creature());
        arena.Tick();
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        for (int i = 0; i < 1_200 && killed.Count == 0; i++)
            arena.Tick();

        Assert.Contains(hits, h => h.Attacker == tavarId && h.Damage > 0);   // his spear
        Assert.Contains(hits, h => h.Target == tavarId);                   // and the wolf's teeth on him
        var kill = Assert.Single(killed);
        Assert.Equal(tavarId, kill.Killer);
        Assert.Equal(0, arena.Simulation.Player.Progression.LifetimeXp.GetValueOrDefault(XpSource.Combat));   // his kill, not the character's
    }

    /// <summary>
    /// The Phase-1 technical audit, L-23: a creature's swing is aimed afresh each tick, so a husk whose swing downed Tavar went on to land
    /// the same swing on the character behind him. One swing lands on one body.
    /// </summary>
    [Fact]
    public void OneSwing_LandsOnOneBody_ThoughItsFoeFallsMidSwing()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = With(session, (70.5, 149.4), Joined(70, 151.1, health: 3), new[] { ("creature.undead.bone_walker_husk", 70.0, 152.0, "pack_hunter") },
            r => r.WithInventory(r.Inventory.Add(Arena.Stack(Bow, 1)).Add(Arena.Stack(Arrows, 10))));
        var started = arena.Record<AttackStarted>();
        var hits = arena.Record<HitResolved>();
        var downed = arena.Record<CompanionDowned>();
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, arena.Simulation.Player.Inventory.Single(e => e.DefId == Bow).ItemId)));
        arena.Face(arena.Creature());
        arena.Tick();
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        for (int i = 0; i < 600 && downed.Count == 0; i++)
            arena.Tick();
        arena.Tick(40);

        Assert.True(downed.Count > 0, string.Join("; ", hits.Select(h => $"{h.Tick} {h.AttackerDefId}->{h.Target} {h.Source} {h.Damage} hp{h.HealthAfter}")) + " / started " + string.Join(", ", started.Select(s => $"{s.Tick} {s.Source}")) + $" / tavar {Him(arena).Health} {Him(arena).Doing} at {Him(arena).Body.XMm},{Him(arena).Body.ZMm}; husk {arena.Creature().Health} {arena.Creature().Mind} at {arena.Creature().Body.XMm},{arena.Creature().Body.ZMm}");
        var husk = arena.Creature().Id;
        var swings = started.Where(s => s.Attacker == husk).ToList();
        Assert.NotEmpty(swings);
        foreach (var swing in swings)
            Assert.True(hits.Count(h => h.Attacker == husk && h.Tick > swing.Tick && h.Tick <= swing.Tick + swing.WindupTicks + swing.ActiveTicks) <= 1,
                $"the swing begun at tick {swing.Tick} landed more than once");
    }

    /// <summary>The Phase-1 technical audit, L-24: a charging boar ran through Tavar standing in its line. People are solid to a charge.</summary>
    [Fact]
    public void AChargingBoar_DoesNotRunThroughTavar()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // South of the Foldscar's heart, on the basin's level floor: the character, Tavar a step in front, and the boar further south.
        var arena = With(session, (153, 45.5), Joined(153, 44.7, CompanionOrder.Wait), new[] { ("creature.beast.bristleback_boar", 153.0, 35.7, "sentinel") },
            r => r.WithInventory(r.Inventory.Add(Arena.Stack(Bow, 1)).Add(Arena.Stack(Arrows, 10))));
        var charged = arena.Record<AttackStarted>();
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, arena.Simulation.Player.Inventory.Single(e => e.DefId == Bow).ItemId)));
        arena.Face(arena.Creature());
        arena.Tick();
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        double nearest = double.MaxValue;
        for (int i = 0; i < 400; i++)
        {
            arena.Tick();
            var boar = arena.Creature().Body;
            var him = Him(arena).Body;
            nearest = Math.Min(nearest, Math.Sqrt(Math.Pow(boar.XMm - him.XMm, 2) + Math.Pow(boar.ZMm - him.ZMm, 2)));
        }

        Assert.Contains(charged, a => a.Source == "ability.creature.boar_charge");
        Assert.True(nearest >= 900, $"the boar came within {nearest / 1000:0.00} m of Tavar");
    }

    [Fact]
    public void Downed_HeCannotTalk_ButCanBeHelpedUp()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // A wolf at his shoulder and the character just behind him: he is the nearer, so the wolf's bite is his.
        var arena = With(session, (70, 148.2), Joined(70, 150, health: 3), new[] { (Arena.Wolf, 70.0, 151.3, "pack_hunter") },
            r => r.WithInventory(r.Inventory.Add(Arena.Stack(Bow, 1)).Add(Arena.Stack(Arrows, 10))));
        var downed = arena.Record<CompanionDowned>();
        var revived = arena.Record<CompanionRevived>();

        var bow = arena.Simulation.Player.Inventory.Single(e => e.DefId == Bow).ItemId;
        Assert.Null(arena.Submit(new EquipCommand(arena.Player, bow)));
        arena.TurnTo(0);
        arena.Tick();
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        for (int i = 0; i < 400 && downed.Count == 0; i++)
            arena.Tick();

        Assert.Equal(Arena.Wolf, Assert.Single(downed).ByDefId);
        Assert.Equal(("downed", "downed", 0), (Him(arena).Doing, Him(arena).Standing, Him(arena).Health));
        Assert.True(arena.Simulation.Npcs.Single(n => n.Id == Tavar).Downed);
        Assert.Equal("Tavar Orr is down", arena.Submit(new TalkCommand(arena.Player, Tavar)));
        Assert.Equal("Tavar Orr is down", Order(arena, CompanionOrder.Wait));

        Assert.Null(arena.Submit(new ReviveCommand(arena.Player, Tavar)));
        Assert.Equal(40, Assert.Single(revived).Health);   // config.companion: revive_percent 40
        Assert.Equal((CompanionCondition.Up, 40), (Him(arena).Condition, Him(arena).Health));
        Assert.Equal("Tavar Orr is not down", arena.Submit(new ReviveCommand(arena.Player, Tavar)));
    }

    [Fact]
    public void DownedTooLong_HeFalls_AndIsBackAtTheWaystone_Waiting()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var down = new CompanionRecord(Tavar, CompanionOrder.Follow, CompanionCondition.Downed, 70_000, 150_000, 0, 0) { DownedTick = 0 };
        var arena = Arena.OpenCreatures(session, session.Setup, Open, 90, Array.Empty<(string, double, double, string)>(), r => r.WithCompanions(new[] { down }));
        var fell = arena.Record<CompanionFell>();
        int window = session.Setup.Social.Companions!.ReviveWindowTicks;

        Assert.Equal(window, Him(arena).FallsAtTick);
        arena.Tick(window - 1);
        Assert.Empty(fell);
        arena.Tick(2);

        var fall = Assert.Single(fell);
        var spawn = session.Setup.Layout.Spawn;
        Assert.InRange(Math.Sqrt(Math.Pow(fall.At.XMm - spawn.XMm, 2) + Math.Pow(fall.At.ZMm - spawn.ZMm, 2)), 1_000, 2_500);
        var him = Him(arena);
        Assert.Equal((CompanionCondition.Up, CompanionOrder.Wait, 100, "waiting"), (him.Condition, him.Order, him.Health, him.Doing));
    }

    /// <summary>PROTOTYPE.md §7.4: "Kesh exists, is at the same position, in wait" - and everything else about him, field by field.</summary>
    [Fact]
    public void HisState_RoundTripsThroughASave_FieldByField_AndGoesOnTheSame()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = With(session, Open, Joined(68, 152, health: 71));
        Walk(arena, (80, 152), (84, 150));   // a trail behind the character
        Assert.Null(Order(arena, CompanionOrder.Wait));
        Walk(arena, (100, 150));             // ~18 m off
        arena.Tick(3);

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("tavar"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        var loaded = Arena.Resume(arena.Simulation.Setup, store.Load(SaveSlots.Manual("tavar"), new LoadContext(session.Generator, session.Content, new Registry())));

        var before = Assert.Single(arena.Simulation.CaptureRecord().Companions);
        var after = Assert.Single(loaded.Simulation.CaptureRecord().Companions);
        Assert.Equal((before.NpcId, before.Order, before.Condition, before.XMm, before.ZMm, before.FacingMdeg, before.Health),
            (after.NpcId, after.Order, after.Condition, after.XMm, after.ZMm, after.FacingMdeg, after.Health));
        Assert.Equal((before.DownedTick, before.StuckTicks, before.LastCombatTick), (after.DownedTick, after.StuckTicks, after.LastCombatTick));
        Assert.Equal(before.Trail, after.Trail);
        Assert.Equal(before.Route, after.Route);
        Assert.Equal(CompanionOrder.Wait, after.Order);
        Assert.InRange(Apart(loaded), 15_000, 21_000);
        Assert.Equal(arena.Simulation.StateDigest(), loaded.Simulation.StateDigest());

        // And the loaded world goes on as the saved one does: called, he comes the same way in both.
        foreach (var world in new[] { arena, loaded })
        {
            Assert.Null(Order(world, CompanionOrder.Follow));
            world.Tick(200);
        }
        Assert.Equal(arena.Simulation.StateDigest(), loaded.Simulation.StateDigest());
        Assert.InRange(Apart(loaded), 0, 4_000);
    }

    [Fact]
    public void OrdersAndAHand_AreRefused_ForSomeoneWhoIsNotWithTheCharacter()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, Open, 90, Array.Empty<(string, double, double, string)>());

        Assert.Equal("Tavar Orr is not with you", Order(arena, CompanionOrder.Wait));
        Assert.Equal("Tavar Orr is not with you", arena.Submit(new ReviveCommand(arena.Player, Tavar)));
        Assert.Empty(arena.Simulation.Companions);
        Assert.Empty(arena.Simulation.CaptureRecord().Companions);
    }
}
