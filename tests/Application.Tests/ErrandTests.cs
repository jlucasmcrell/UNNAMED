using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The errand (M7 design §3.12, §4.15; E9): Kera Voss asked, in person, to work at the character's anvil bench walks there - opening
/// doors, never closing one - lands exactly on its work anchor facing the work, stands working, and walks home when let go or when the
/// bench comes down, landing exactly on her place; every refusal in its order; talk refused while she walks, read from the saved phase.
/// </summary>
public class ErrandTests
{
    private const string Kera = "npc.ashen_hollow.kera_voss", Renn = "npc.ashen_hollow.renn_vale", Tavar = "npc.ashen_hollow.tavar_orr";
    private const string Pad = "piece.pad.timber", Bench = "piece.station.anvil", ShedDoor = "door.forge_shed";

    /// <summary>The workshop bench's work anchor (R07), and Kera's place.</summary>
    private static readonly (long X, long Z, int Facing) Anchor = (100_750, 103_500, 270_000), Home = (61_600, 139_600, 300_000);

    /// <summary>A pad and the workshop's bench on it (R07's pose), placed from (102, 102): open ground all round, no walls.</summary>
    private static (Arena Arena, EntityId Bench) OpenBench(GameSession session, SimulationSetup? rules = null, (double X, double Z)? from = null, long coin = 0)
    {
        rules ??= session.Setup;
        var arena = Arena.OpenCreatures(session, rules, from ?? (102.0, 102.0), 0, Array.Empty<(string, double, double, string)>(), r =>
            new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(new[] { 20, 20, 5 }.Select(n => Arena.Stack("item.material.timber", n))),
                r.Progression, r.FacingMdeg, equipment: r.Equipment, currency: coin));
        Assert.Null(BuildingTests.Place(arena, Pad, 100_500, 103_500, 0));
        Assert.Null(BuildingTests.Place(arena, Bench, 100_500, 103_500, 3));
        return (arena, arena.Simulation.Pieces.Single(p => p.DefId == Bench).Id);
    }

    /// <summary>
    /// From the crossing to Kera's side in the forge shed, opening its door, as R17 and R18 walk it - to (60.4, 140.0), not R18's (60.0, 140.2),
    /// because this walk stops within 300 mm, not 50, and talk reach is 1.95 m.
    /// </summary>
    internal static void ToKera(Arena arena, params (double X, double Z)[] first)
    {
        foreach (var (x, z) in first.Concat(new[] { (90.0, 112.0), (80.0, 118.0), (62.0, 130.0), (51.8, 136.0), (51.8, 142.0) }))
            Assert.True(arena.WalkTo(x, z), $"the walk to the shed stopped at {arena.Simulation.Player.Body}");
        arena.TurnTo(90);
        arena.Tick();
        if (!arena.Simulation.Doors.Single(d => d.Site.Key == ShedDoor).Open)
            Assert.Null(arena.Submit(new InteractCommand(arena.Player, ShedDoor)));
        Assert.True(arena.WalkTo(54.5, 142.0) && arena.WalkTo(60.4, 140.0), $"the walk into the shed stopped at {arena.Simulation.Player.Body}");
        arena.TurnTo(110);
        arena.Tick();
    }

    /// <summary>Out of the shed and away to the south-east, clear of Kera's way to the crossing and of her landing.</summary>
    internal static void OutOfHerWay(Arena arena)
    {
        foreach (var (x, z) in new[] { (54.5, 142.0), (51.8, 142.0), (51.8, 136.0), (62.0, 130.0), (80.0, 118.0), (104.0, 97.0) })
            Assert.True(arena.WalkTo(x, z), $"the walk away stopped at {arena.Simulation.Player.Body}");
    }

    internal static string? Assign(Arena arena, string npc, EntityId piece) => arena.Submit(new AssignWorkerCommand(arena.Player, npc, piece));

    internal static string? Release(Arena arena, string npc) => arena.Submit(new ReleaseWorkerCommand(arena.Player, npc));

    private static NpcView Npc(Arena arena, string id) => arena.Simulation.Npcs.Single(n => n.Id == id);

    private static (long X, long Z, int Facing) Pose(Body body) => (body.XMm, body.ZMm, body.FacingMdeg);

    /// <summary>Ticks until an event arrives (at most <paramref name="cap"/>), checking every tick that Kera never moves more than 81 mm.</summary>
    private static void Until<T>(Arena arena, List<T> seen, int cap)
    {
        var last = Npc(arena, Kera).Body;
        for (int i = 0; i < cap && seen.Count == 0; i++)
        {
            arena.Tick();
            var now = Npc(arena, Kera).Body;
            long dx = now.XMm - last.XMm, dz = now.ZMm - last.ZMm;
            Assert.True(dx * dx + dz * dz <= 81 * 81, $"Kera moved {Math.Sqrt(dx * dx + dz * dz):0} mm in one tick at {arena.Simulation.WorldTick}");
            last = now;
        }
        Assert.NotEmpty(seen);
    }

    [Fact]
    public void Kera_WalksToTheBench_ArrivesExactly_AndHomeAgain()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, bench) = OpenBench(session);
        ToKera(arena);
        var assigned = arena.Record<WorkerAssigned>();
        var arrived = arena.Record<NpcArrivedAtWork>();
        var released = arena.Record<WorkerReleased>();
        var home = arena.Record<NpcReturnedHome>();

        Assert.Null(Assign(arena, Kera, bench));
        Assert.Equal((Kera, bench, Anchor.X, Anchor.Z, Anchor.Facing), (assigned[0].NpcId, assigned[0].PieceId, assigned[0].AnchorXMm, assigned[0].AnchorZMm,
            assigned[0].FacingMdeg));
        var errand = arena.Simulation.World.NpcErrand(Kera)!;
        Assert.Equal((NpcErrandPhase.ToWork, bench, arena.Player), (errand.Phase, errand.PieceId, errand.WorkOwner));
        Assert.Equal(new WorkAssignmentView(Kera, bench, Anchor.X, Anchor.Z, Anchor.Facing, NpcErrandPhase.ToWork), Assert.Single(arena.Simulation.WorkAssignments));
        Assert.Equal(Kera, arena.Simulation.Pieces.Single(p => p.Id == bench).WorkerNpcId);

        OutOfHerWay(arena);
        Until(arena, arrived, 3_000);
        Assert.Equal(Anchor, Pose(Npc(arena, Kera).Body));
        Assert.Equal(bench, arrived[0].PieceId);
        Assert.Equal(NpcErrandPhase.AtWork, arena.Simulation.World.NpcErrand(Kera)!.Phase);
        Assert.Equal(NavRouteStatus.None, arena.Simulation.World.NpcErrand(Kera)!.Route.Status);
        arena.Tick(40);
        Assert.Equal(Anchor, Pose(Npc(arena, Kera).Body));   // standing at work: still, facing the work

        Assert.True(arena.WalkTo(101.8, 102.2));
        Assert.Null(Release(arena, Kera));
        Assert.Equal((Kera, bench, "released"), (released[0].NpcId, released[0].PieceId, released[0].Reason));
        Assert.Equal(new WorkAssignmentView(Kera, null, Home.X, Home.Z, Home.Facing, NpcErrandPhase.ToHome), Assert.Single(arena.Simulation.WorkAssignments));
        Assert.Null(arena.Simulation.Pieces.Single(p => p.Id == bench).WorkerNpcId);
        Until(arena, home, 3_000);
        Assert.Equal(Home, Pose(Npc(arena, Kera).Body));
        Assert.Null(arena.Simulation.World.NpcErrand(Kera));
        Assert.Empty(arena.Simulation.WorkAssignments);
        Assert.Equal(0, session.SubscriberFailures);
    }

    [Fact]
    public void AnErrand_OpensDoors_AndNeverClosesThem()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, bench) = OpenBench(session);
        ToKera(arena);
        var toggled = arena.Record<DoorToggled>();
        var arrived = arena.Record<NpcArrivedAtWork>();
        Assert.Null(Assign(arena, Kera, bench));
        // Out of the shed, and the door shut behind the character (R19): Kera has to open it.
        Assert.True(arena.WalkTo(54.5, 142.0) && arena.WalkTo(51.8, 142.0));
        arena.TurnTo(90);
        arena.Tick();
        Assert.Null(arena.Submit(new InteractCommand(arena.Player, ShedDoor)));
        Assert.False(arena.Simulation.Doors.Single(d => d.Site.Key == ShedDoor).Open);
        foreach (var (x, z) in new[] { (51.8, 136.0), (62.0, 130.0), (80.0, 118.0), (104.0, 97.0) })
            Assert.True(arena.WalkTo(x, z));
        Until(arena, arrived, 3_000);

        var kera = Npc(arena, Kera).InstanceId;
        Assert.Equal(new[] { (ShedDoor, false, arena.Player), (ShedDoor, true, kera) }, toggled.Select(t => (t.DoorKey, t.Open, t.Actor)));
        Assert.True(arena.Simulation.Doors.Single(d => d.Site.Key == ShedDoor).Open);   // left open: NPCs never close a door
    }

    /// <summary>Assign's refusals in their order (M7 design §4.15), each where the ones before it pass; a foreign bench is `ForeignPieces_…`'s.</summary>
    [Fact]
    public void Assign_RefusesInOrder()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, bench) = OpenBench(session);
        var pad = arena.Simulation.Pieces.Single(p => p.DefId == Pad).Id;

        Assert.StartsWith("unknown actor", arena.Submit(new AssignWorkerCommand(EntityId.NewId(EntityKind.Character), Kera, bench)));
        Assert.Equal("there is no one called npc.ashen_hollow.nobody here", Assign(arena, "npc.ashen_hollow.nobody", bench));
        Assert.Equal("Kera Voss is out of reach", Assign(arena, Kera, bench));
        ToKera(arena);
        Assert.Equal("there is no such station", Assign(arena, Kera, pad));
        Assert.Equal("there is no such station", Assign(arena, Kera, EntityId.NewId(EntityKind.Piece)));
        Assert.Null(Assign(arena, Kera, bench));
        Assert.Equal("someone already works there", Assign(arena, Kera, bench));

        // A second bench: she already works at the first.
        OutOfHerWay(arena);
        Assert.Null(BuildingTests.Place(arena, Pad, 103_500, 100_500, 0));
        Assert.Null(BuildingTests.Place(arena, Bench, 103_500, 100_500, 0));
        var second = arena.Simulation.Pieces.Single(p => p.DefId == Bench && p.Id != bench).Id;
        var arrived = arena.Record<NpcArrivedAtWork>();
        Until(arena, arrived, 3_000);
        Assert.True(arena.WalkTo(101.8, 102.2));
        Assert.Equal("Kera Voss already works for you", Assign(arena, Kera, second));

        // A companion travels with the character: refused before reach or station are asked.
        var profileOfTavar = session.Setup.Social.Npcs[Tavar].Companion!;
        var withTavar = Arena.OpenCreatures(session, session.Setup, (102.0, 102.0), 0, Array.Empty<(string, double, double, string)>(), r => r.WithCompanions(new[]
        {
            new CompanionRecord(Tavar, CompanionOrder.Wait, CompanionCondition.Up, 100_500, 108_500, 180_000, profileOfTavar.MaxHealth),
        }));
        Assert.Equal("Tavar Orr travels with you", Assign(withTavar, Tavar, bench));

        // A station of a kind she does not work: with her works_at edited to the forge alone.
        var npcs = session.Setup.Social.Npcs;
        var forgeOnly = session.Setup with
        {
            Social = session.Setup.Social with { Npcs = npcs.SetItem(Kera, npcs[Kera] with { WorksAt = ImmutableArray.Create("forge") }) },
        };
        var (smith, anvil) = OpenBench(session, forgeOnly);
        ToKera(smith);
        Assert.Equal("Kera Voss does not work an anvil", Assign(smith, Kera, anvil));

        // A bench she cannot get to: fenced round by standing barriers, which a person who opens doors still cannot pass. Every gate is
        // passable to the placement check, so the bench is placed; the authoritative planner reads the barriers' flags.
        var layout = session.Setup.Layout;
        BarrierSite Fence(string key, long x0, long z0, long x1, long z1) =>
            new($"barrier.test_{key}", $"world.test_fence_{key}", new BoxBlocker($"barrier.test_{key}", x0, z0, x1, z1, 2_000), "a fence");
        var fenced = session.Setup with
        {
            Layout = layout with
            {
                Barriers = layout.Barriers.AddRange(new[]
                {
                    Fence("w", 97_000, 100_000, 97_400, 107_000), Fence("e", 103_600, 100_000, 104_000, 107_000),
                    Fence("s", 97_000, 100_000, 104_000, 100_400), Fence("n", 97_000, 106_600, 104_000, 107_000),
                }),
            },
        };
        var (outside, walled) = OpenBench(session, fenced, from: (102.0, 99.0));
        ToKera(outside, (106.0, 99.0), (106.0, 110.0));
        Assert.Equal("Kera Voss cannot get there", Assign(outside, Kera, walled));
    }

    /// <summary>Release's refusals in their order: the actor; working for them; in reach.</summary>
    [Fact]
    public void Release_RefusesInOrder()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, bench) = OpenBench(session);
        Assert.StartsWith("unknown actor", arena.Submit(new ReleaseWorkerCommand(EntityId.NewId(EntityKind.Character), Kera)));
        Assert.Equal("Kera Voss does not work for you", Release(arena, Kera));
        Assert.Equal("Renn Vale does not work for you", Release(arena, Renn));
        ToKera(arena);
        Assert.Null(Assign(arena, Kera, bench));
        OutOfHerWay(arena);
        Assert.Equal("Kera Voss is out of reach", Release(arena, Kera));
        var arrived = arena.Record<NpcArrivedAtWork>();
        Until(arena, arrived, 3_000);
        Assert.True(arena.WalkTo(101.8, 102.2));
        Assert.Null(Release(arena, Kera));
        Assert.Equal("Kera Voss does not work for you", Release(arena, Kera));   // walking home, she works for no one
    }

    [Fact]
    public void TakingDownTheBench_SendsTheWorkerHome()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        foreach (bool destroy in new[] { false, true })
        {
            var (arena, bench) = OpenBench(session);
            ToKera(arena);
            Assert.Null(Assign(arena, Kera, bench));
            OutOfHerWay(arena);
            var arrived = arena.Record<NpcArrivedAtWork>();
            Until(arena, arrived, 3_000);
            var released = arena.Record<WorkerReleased>();
            var home = arena.Record<NpcReturnedHome>();
            Assert.True(arena.WalkTo(100.8, 102.5));
            if (destroy)
            {
                // Face the bench, north-west of here, within a sword's reach of it and clear of Kera at the anchor, and strike it until it goes.
                arena.TurnTo(CombatRulesFacing(arena, 99_800, 103_500) / 1000);
                arena.Tick();
                // Thirty blows of 10 on its 300; the run here left the character short of breath, so each waits for the stamina it needs.
                for (int i = 0; i < 3_000 && arena.Simulation.Pieces.Any(p => p.Id == bench); i++)
                {
                    if (arena.Simulation.Combat.Phase == CombatPhase.Idle)
                    {
                        string? refused = arena.Submit(new AttackCommand(arena.Player));
                        Assert.True(refused is null or "too tired to attack", refused);
                    }
                    arena.Tick();
                }
            }
            else
            {
                Assert.Null(arena.Submit(new DismantlePieceCommand(arena.Player, bench)));
            }
            Assert.DoesNotContain(arena.Simulation.Pieces, p => p.Id == bench);
            Assert.Equal((Kera, bench, destroy ? "destroyed" : "dismantled"), (released[0].NpcId, released[0].PieceId, released[0].Reason));
            Until(arena, home, 3_000);
            Assert.Equal(Home, Pose(Npc(arena, Kera).Body));
        }
    }

    private static int CombatRulesFacing(Arena arena, long x, long z)
    {
        var body = arena.Simulation.Player.Body;
        return Domain.Combat.CombatRules.FacingTowards(body.XMm, body.ZMm, x, z);
    }

    /// <summary>At work she talks as ever, but only the mover turns her: she keeps facing the work (L5).</summary>
    [Fact]
    public void AnErrandNpc_KeepsHerWorkFacing_WhileTalking()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, bench) = OpenBench(session);
        ToKera(arena);
        Assert.Null(Assign(arena, Kera, bench));
        OutOfHerWay(arena);
        Until(arena, arena.Record<NpcArrivedAtWork>(), 3_000);
        Assert.True(arena.WalkTo(101.8, 102.2));
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Kera)));
        Assert.NotNull(arena.Simulation.Conversation);
        arena.Tick(40);
        Assert.Equal(Anchor, Pose(Npc(arena, Kera).Body));
    }

    /// <summary>G21: while she walks, talk is refused - read from the saved phase, the same after a load; at work, or home, she talks.</summary>
    [Fact]
    public void ATalkWithAWalkingErrandNpc_IsRefused_FromItsSavedPhase()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, bench) = OpenBench(session);
        ToKera(arena);
        Assert.Null(Assign(arena, Kera, bench));
        Assert.Equal("Kera Voss is walking to work", arena.Submit(new TalkCommand(arena.Player, Kera)));
        arena.Tick(10);
        var loaded = SavedAndLoaded(profile, session, arena, "walking");
        Assert.Equal("Kera Voss is walking to work", loaded.Submit(new TalkCommand(loaded.Player, Kera)));

        OutOfHerWay(arena);
        Until(arena, arena.Record<NpcArrivedAtWork>(), 3_000);
        Assert.True(arena.WalkTo(101.8, 102.2));
        Assert.Null(Release(arena, Kera));
        Assert.Equal("Kera Voss is walking home", arena.Submit(new TalkCommand(arena.Player, Kera)));
    }

    internal static Arena SavedAndLoaded(TempProfile profile, GameSession session, Arena arena, string slot)
    {
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual(slot), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        var loaded = store.Load(SaveSlots.Manual(slot), new LoadContext(session.Generator, session.Content, new Registry()));
        Assert.True(loaded.IsComplete, string.Join("; ", loaded.RejectedRecords.Select(r => $"{r.Key}: {r.Reason}")));
        return Arena.Resume(arena.Simulation.Setup, loaded);
    }

    /// <summary>G18: saved while she walks to work, the loaded world walks her on exactly as the unsaved one does, to the same arrival.</summary>
    [Fact]
    public void AnErrand_SavedMidWalk_ContinuesLikeTheUnsavedWorld()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, bench) = OpenBench(session);
        ToKera(arena);
        Assert.Null(Assign(arena, Kera, bench));
        OutOfHerWay(arena);
        var errand = arena.Simulation.World.NpcErrand(Kera)!;
        Assert.Equal((NpcErrandPhase.ToWork, NavRouteStatus.Active), (errand.Phase, errand.Route.Status));
        SideBySide(profile, session, arena, "mid_walk", 1_500);
    }

    /// <summary>G21: at work and in conversation when saved; a load starts outside any conversation, and the errand goes on the same.</summary>
    [Fact]
    public void ATalkWithAWorkingNpc_SavedMidConversation_ContinuesEqual()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, bench) = OpenBench(session);
        ToKera(arena);
        Assert.Null(Assign(arena, Kera, bench));
        OutOfHerWay(arena);
        Until(arena, arena.Record<NpcArrivedAtWork>(), 3_000);
        Assert.True(arena.WalkTo(101.8, 102.2));
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Kera)));
        var loaded = SideBySide(profile, session, arena, "talking", 200);
        Assert.NotNull(arena.Simulation.Conversation);
        Assert.Null(loaded.Simulation.Conversation);
    }

    /// <summary>
    /// G22: walking home far from the character, in a cell 140.7 m away - tier A in the world that went on (it kept A inside the 10 m
    /// hysteresis) and B in the loaded one (a load settles from scratch). The errand has no tier gate, so both walk her home the same.
    /// </summary>
    [Fact]
    public void AnErrandFarFromThePlayer_SavedMidWalk_ContinuesEqual()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, bench) = OpenBench(session);
        ToKera(arena);
        Assert.Null(Assign(arena, Kera, bench));
        OutOfHerWay(arena);
        Until(arena, arena.Record<NpcArrivedAtWork>(), 3_000);
        Assert.True(arena.WalkTo(101.8, 102.2));
        Assert.Null(Release(arena, Kera));
        foreach (var (x, z) in new[] { (140.0, 60.0), (199.3, 0.7) })
            Assert.True(arena.WalkTo(x, z), $"the walk to the far corner stopped at {arena.Simulation.Player.Body}");
        Assert.Equal(NpcErrandPhase.ToHome, arena.Simulation.World.NpcErrand(Kera)!.Phase);

        var loaded = SavedAndLoaded(profile, session, arena, "far_check");
        const string HerCell = "r_0_0:c_00_01";
        Assert.Equal((SimulationTier.A, SimulationTier.B), (arena.Simulation.CellTiers[HerCell], loaded.Simulation.CellTiers[HerCell]));
        SideBySide(profile, session, arena, "far", 1_200);
        Assert.Null(arena.Simulation.World.NpcErrand(Kera));   // home in both, on the same tick
    }

    /// <summary>
    /// Saved now and loaded again; both worlds go on a tick at a time, their digests compared every tick, until she arrives in both on the
    /// same tick (or the ticks run out), and every field compared at the end.
    /// </summary>
    private static Arena SideBySide(TempProfile profile, GameSession session, Arena arena, string slot, int ticks)
    {
        var loaded = SavedAndLoaded(profile, session, arena, slot);
        Assert.Equal(arena.Simulation.StateDigest(), loaded.Simulation.StateDigest());
        Assert.Equal(arena.Simulation.Navigation.Movers.ToList(), loaded.Simulation.Navigation.Movers.ToList());
        var (arrived, arrivedLoaded) = (arena.Record<NpcArrivedAtWork>(), loaded.Record<NpcArrivedAtWork>());
        for (int i = 0; i < ticks; i++)
        {
            arena.Tick();
            loaded.Tick();
            Assert.Equal(arena.Simulation.StateDigest(), loaded.Simulation.StateDigest());
        }
        Assert.Equal(arrived.Select(a => a.Tick), arrivedLoaded.Select(a => a.Tick));
        var differences = StateDump.Compare(StateDump.Render(arena.Simulation), StateDump.Render(loaded.Simulation), out int leaves);
        Assert.True(differences.Count == 0, $"{differences.Count} of {leaves} fields differ: {string.Join("; ", differences.Take(6))}");
        return loaded;
    }

    /// <summary>At work she trades as ever: reach is measured to her body at the bench; her wares stay at her place, gated as at home.</summary>
    [Fact]
    public void KeraAtWork_TradesAtTheBench_AndHerWaresStayHome()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (arena, bench) = OpenBench(session, coin: 200);
        ToKera(arena);
        var atHome = arena.Simulation.Wares(Kera)!;
        Assert.DoesNotContain(atHome.Wares, w => w.ItemId == "item.material.iron_ingot");   // withheld until the waystation accepts you
        Assert.Null(Assign(arena, Kera, bench));
        OutOfHerWay(arena);
        Until(arena, arena.Record<NpcArrivedAtWork>(), 3_000);
        Assert.True(arena.WalkTo(101.8, 102.2));

        var atWork = arena.Simulation.Wares(Kera)!;
        Assert.Equal(atHome.Wares.ToList(), atWork.Wares.ToList());
        var arrows = atWork.Wares.First(w => w.ItemId == "item.ammo.arrow_rough");
        Assert.Null(arena.Submit(new BuyCommand(arena.Player, Kera, arrows.Ref, 1)));
        Assert.Contains(arena.Simulation.Player.Inventory, e => e.DefId == "item.ammo.arrow_rough");
    }
}
