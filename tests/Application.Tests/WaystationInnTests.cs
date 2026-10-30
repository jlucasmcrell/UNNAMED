using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

public class WaystationInnTests
{
    [Fact]
    public void ResidentsWorkDineAndSleepUpstairs_AndTavarWaitsForHisRelease()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AdventurerWorldTests.AtTheInn(session);
        var sim = arena.Simulation;
        var routines = session.Setup.NpcLife.Routines.Values.Where(r => r.Settlement == "ashen_hollow").ToArray();
        // The townsfolk who keep the hollow (7), and D23 S6's three watches: the waystation, Cairnwold and the road each sleep
        // at home in ashen_hollow's houses, so their routines are part of the inn's own rule about beds and stools.
        Assert.Equal(10, routines.Length);
        // A visitor must not claim a stool reached by a resident's supper anchor.
        var inn = session.Setup.Adventurers!.Settlements["ashen_hollow"].Inn!;
        Assert.Equal(180_000, Assert.Single(inn.Beds).FacingMdeg);
        Assert.All(routines, r => Assert.Equal(180_000, r.Anchors["home"].FacingMdeg));
        var guestSeats = inn.Seats;
        foreach (var routine in routines)
        foreach (var guest in guestSeats)
        {
            var supper = routine.Anchors["supper"];
            long dx = supper.XMm - guest.XMm, dz = supper.ZMm - guest.ZMm;
            Assert.True(dx * dx + dz * dz > 850L * 850, $"{routine.Key} shares a supper chair with a guest");
        }
        const string tavar = "npc.ashen_hollow.tavar_orr";
        var held = sim.Npcs.Single(n => n.Id == tavar).Body;
        void At(int hour)
        {
            long target = session.Setup.Clock.TicksPerDay + hour * session.Setup.Clock.TicksPerDay / 24 - session.Setup.Clock.StartTicks;
            sim.Advance(target - sim.WorldTick);
        }
        foreach (int hour in new[] { 8, 13, 21, 23 })
        {
            At(hour);
            foreach (var r in routines)
            {
                var body = sim.Npcs.Single(n => n.Id == r.Key).Body;
                if (r.Key == tavar)
                {
                    Assert.Equal((held.XMm, held.ZMm), (body.XMm, body.ZMm));
                    continue;
                }
                // A watch with a round is not standing on her work anchor in work hours: the round is her goal then (D23 S6), and the
                // inn's rule about beds and stools is about everyone the rounds leave at home.
                if (session.Setup.Layout.Patrols.Any(p => p.NpcId == r.Key))
                    continue;
                string activity = r.ActivityAt(hour, session.Setup.NpcLife.Rules);
                var anchor = r.AnchorFor(activity, hour);
                Assert.Equal((anchor.XMm, anchor.ZMm), (body.XMm, body.ZMm));
                Assert.Equal(session.Setup.Layout.Space.HeightAtMm(body.XMm, body.ZMm), body.YMm);
                if (hour == 23)
                    Assert.Equal(10_000, body.YMm);
            }
        }
        var barrier = session.Setup.Layout.Barriers.Single(b => b.Key == "barrier.foldscar_fold");
        Assert.Null(sim.Submit(new SetWorldFlag(Simulation.CellOf(session.Setup.Layout.SpaceId, barrier), barrier.FlagId, 1)));
        sim.Advance(session.Setup.Clock.TicksPerDay / 24);
        var freed = sim.Npcs.Single(n => n.Id == tavar).Body;
        Assert.Equal(10_000, freed.YMm);
        Assert.InRange(freed.ZMm, 714_000, 722_000);
    }

    [Fact]
    public void TheSameMovementUsedInPlay_ClimbsAndDescendsTheStair()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var space = session.Setup.Layout.Space;
        var body = new Body(453_000, space.HeightAtMm(453_000, 727_000), 727_000, 180_000);
        Assert.Equal(space.Terrain.HeightAtMm(body.XMm, body.ZMm), body.YMm);
        for (int i = 0; i < 160 && body.ZMm > 718_000; i++)
        {
            long before = body.YMm;
            body = Kinematics.Step(body, new MoveIntent(0, -MoveIntent.FullDeflection, Gait.Walk, 180_000), session.Setup.Movement, space, [], 50);
            Assert.InRange(body.YMm - before, -100, 100); // no jump onto an incorrectly raised stair foot
        }
        Assert.True(body.ZMm <= 718_000);
        Assert.Equal(10_000, body.YMm);
        for (int i = 0; i < 32; i++)
            body = Kinematics.Step(body, new MoveIntent(-MoveIntent.FullDeflection, 0, Gait.Walk, 270_000), session.Setup.Movement, space, [], 50);
        Assert.True(body.XMm < 451_000); // through the real upper doorway into the corridor
        Assert.Equal(10_000, body.YMm);
        for (int i = 0; i < 32; i++)
            body = Kinematics.Step(body, new MoveIntent(MoveIntent.FullDeflection, 0, Gait.Walk, 90_000), session.Setup.Movement, space, [], 50);
        for (int i = 0; i < 120; i++)
            body = Kinematics.Step(body, new MoveIntent(0, MoveIntent.FullDeflection, Gait.Walk, 0), session.Setup.Movement, space, [], 50);
        Assert.True(body.ZMm > 726_500);
        Assert.Equal(space.Terrain.HeightAtMm(body.XMm, body.ZMm), body.YMm);
    }
}
