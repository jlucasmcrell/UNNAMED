using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.World.Tests;

/// <summary>
/// The errand in the running world on the game's own content (M7 design §3.12, §4.15; E9), from worlds crafted as a save would leave them:
/// an unreachable home holds and retries, never teleports, and is walked to once the way opens (N-A5); an NPC is never both a companion and
/// on an errand - a recruit is refused, and a loaded errand of a companion is dropped and reported (G23).
/// </summary>
public class ErrandWorldTests
{
    private const string Tavar = "npc.ashen_hollow.tavar_orr";
    private static readonly Lazy<SimulationSetup> Hollow = new(TestWorlds.HollowSetup);

    /// <summary>Records every event published, and never throws.</summary>
    private sealed class RecordingBus : IEventBus
    {
        public List<object> Published { get; } = new();

        public void Subscribe<T>(Action<T> handler)
        {
        }

        public void Unsubscribe<T>(Action<T> handler)
        {
        }

        public void Publish<T>(T @event) => Published.Add(@event!);
    }

    private static string HostOf(long xMm, long zMm) => CellKey.OfWorld(xMm / 1000.0, zMm / 1000.0).ToString();

    private static Body Body(Simulation simulation, string npcId) => simulation.Npcs.Single(n => n.Id == npcId).Body;

    /// <summary>Tavar walking home to his place, the Foldscar's centre, from 5 m west of it: a crafted save's errand.</summary>
    private static NpcErrandRecord WalkingHome() =>
        new(Tavar, HostOf(145_000, 42_000), NpcErrandPhase.ToHome, null, null, 140_000, 42_000, 90_000);

    // N-A5
    [Fact]
    public void AnUnreachableGoal_LeavesTheNpcWaiting()
    {
        var bus = new RecordingBus();
        var simulation = TestWorlds.HollowSimulation(Hollow.Value, bus, craft: (world, _) => world.SetNpcErrand(WalkingHome()));
        Assert.Equal((140_000L, 42_000L, 90_000), (Body(simulation, Tavar).XMm, Body(simulation, Tavar).ZMm, Body(simulation, Tavar).FacingMdeg));

        // The fold stands: his home cannot be reached. He stays exactly where he is, retrying every 40 ticks, and shows blocked from 200.
        for (int tick = 1; tick <= 400; tick++)
        {
            simulation.Step();
            var body = Body(simulation, Tavar);
            Assert.Equal((140_000L, 42_000L), (body.XMm, body.ZMm));
            Assert.Equal(tick >= 200, simulation.Navigation.Movers.Single(m => m.NpcId == Tavar).Blocked);
        }
        var planned = bus.Published.OfType<RoutePlanned>().Where(p => p.MoverKey == Tavar).ToList();
        Assert.Equal(Enumerable.Range(0, 10).Select(i => 1 + 40L * i), planned.Select(p => p.Tick));
        Assert.All(planned, p => Assert.Equal("goal_blocked", p.Outcome));
        Assert.Equal(NavRouteStatus.Unreachable, simulation.World.NpcErrand(Tavar)!.Route.Status);

        // The Foldscar steadied, the fold lifts: the next retry finds the way, and he walks home, landing exactly.
        var fold = Hollow.Value.Layout.Barriers.Single(b => b.Key == "barrier.foldscar_fold");
        simulation.World.SetFlag(Simulation.CellOf(fold), fold.FlagId, 1);
        for (int i = 0; i < 2_000 && !bus.Published.OfType<NpcReturnedHome>().Any(); i++)
            simulation.Step();
        Assert.Equal("found", bus.Published.OfType<RoutePlanned>().Where(p => p.MoverKey == Tavar).Last().Outcome);
        var home = Assert.Single(bus.Published.OfType<NpcReturnedHome>());
        Assert.Equal(Tavar, home.NpcId);
        var at = Body(simulation, Tavar);
        Assert.Equal((145_000L, 42_000L, 53_000), (at.XMm, at.ZMm, at.FacingMdeg));
        Assert.Null(simulation.World.NpcErrand(Tavar));
    }

    // G23
    [Fact]
    public void AnErrandOfACompanion_IsDroppedOnLoad_AndReported()
    {
        var bus = new RecordingBus();
        var profile = Hollow.Value.Social.Npcs[Tavar].Companion!;
        var simulation = TestWorlds.HollowSimulation(Hollow.Value, bus,
            change: r => r.WithCompanions(new[] { new CompanionRecord(Tavar, CompanionOrder.Wait, CompanionCondition.Up, 100_500, 108_500, 180_000, profile.MaxHealth) }),
            craft: (world, _) => world.SetNpcErrand(WalkingHome()));

        Assert.Null(simulation.World.NpcErrand(Tavar));
        Assert.Equal(new StructureConflict($"npc errand {Tavar}", "they travel with you: the errand is dropped"), Assert.Single(simulation.StructureAudit));
        Assert.Equal(new[] { Tavar }, simulation.Navigation.Movers.Select(m => m.NpcId));   // one mover: the companion
        Assert.Equal((100_500L, 108_500L), (Body(simulation, Tavar).XMm, Body(simulation, Tavar).ZMm));
        Assert.Empty(bus.Published);   // a load publishes nothing (G27)
    }

    // G23
    [Fact]
    public void ARecruitOfAnNpcWithAnErrand_IsRefused()
    {
        // Tavar at work at the character's bench in open ground north of the waystation (a crafted save), the fold steadied so he speaks,
        // and the character 1.45 m south of him: asked to walk back with the character, he does not join - the errand stands.
        const long x = 126_000, z = 60_000;
        var anchor = (X: x, Z: z - 250, Facing: 0);
        var bus = new RecordingBus();
        var terrain = Hollow.Value.Layout.Space.Terrain;
        var simulation = TestWorlds.HollowSimulation(Hollow.Value, bus,
            change: r => new PlayerRecord(r.Id, r.Name, x, terrain.HeightAtMm(x, anchor.Z - 1_450), anchor.Z - 1_450, r.AppearanceSeed, r.Inventory, r.Progression, 0,
                equipment: r.Equipment),
            craft: (world, player) =>
            {
                var bench = EntityId.Derived(EntityKind.Piece, 1, "unnamed.piece/v1", player.Value);
                world.PlacePiece(new PieceRecord(bench, "piece.station.anvil", HostOf(x, z), x, z, 0, player, 300), 1);
                world.SetNpcErrand(new NpcErrandRecord(Tavar, HostOf(145_000, 42_000), NpcErrandPhase.AtWork, bench, player, anchor.X, anchor.Z, anchor.Facing));
                var fold = Hollow.Value.Layout.Barriers.Single(b => b.Key == "barrier.foldscar_fold");
                world.SetFlag(Simulation.CellOf(fold), fold.FlagId, 1);
            });
        Assert.Equal(NpcErrandPhase.AtWork, simulation.World.NpcErrand(Tavar)!.Phase);

        string? Submit(GameCommand command)
        {
            int rejected = bus.Published.OfType<CommandRejected>().Count();
            simulation.Enqueue(command);
            simulation.DrainCommands();
            return bus.Published.OfType<CommandRejected>().Skip(rejected).FirstOrDefault()?.Reason;
        }
        Assert.Null(Submit(new TalkCommand(simulation.PlayerId, Tavar)));
        Assert.Null(Submit(new ChooseCommand(simulation.PlayerId, "found")));
        Assert.Null(Submit(new ChooseCommand(simulation.PlayerId, "join")));

        Assert.Empty(simulation.Companions);
        Assert.DoesNotContain(bus.Published, e => e is CompanionRecruited);
        Assert.Equal(NpcErrandPhase.AtWork, simulation.World.NpcErrand(Tavar)!.Phase);
        simulation.Step();
        Assert.Equal((anchor.X, anchor.Z, anchor.Facing), (Body(simulation, Tavar).XMm, Body(simulation, Tavar).ZMm, Body(simulation, Tavar).FacingMdeg));
    }
}
