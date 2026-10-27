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
        Assert.Equal(7, routines.Length);
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
                string activity = r.ActivityAt(hour, session.Setup.NpcLife.Rules);
                var anchor = r.AnchorFor(activity, hour);
                Assert.Equal((anchor.XMm, anchor.ZMm), (body.XMm, body.ZMm));
                Assert.Equal(session.Setup.Layout.Space.HeightAtMm(body.XMm, body.ZMm), body.YMm);
                if (hour == 23)
                    Assert.Equal(18_200, body.YMm);
            }
        }
        var barrier = session.Setup.Layout.Barriers.Single(b => b.Key == "barrier.foldscar_fold");
        Assert.Null(sim.Submit(new SetWorldFlag(Simulation.CellOf(session.Setup.Layout.SpaceId, barrier), barrier.FlagId, 1)));
        sim.Advance(session.Setup.Clock.TicksPerDay / 24);
        var freed = sim.Npcs.Single(n => n.Id == tavar).Body;
        Assert.Equal(18_200, freed.YMm);
        Assert.InRange(freed.ZMm, 714_000, 722_000);
    }

    [Fact]
    public void TheSameMovementUsedInPlay_ClimbsAndDescendsTheStair()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var space = session.Setup.Layout.Space;
        var body = new Body(453_000, space.HeightAtMm(453_000, 726_000), 726_000, 180_000);
        for (int i = 0; i < 120; i++)
            body = Kinematics.Step(body, new MoveIntent(0, -MoveIntent.FullDeflection, Gait.Walk, 180_000), session.Setup.Movement, space, [], 50);
        Assert.InRange(body.ZMm, 717_300, 718_100);
        Assert.Equal(18_200, body.YMm);
        for (int i = 0; i < 120; i++)
            body = Kinematics.Step(body, new MoveIntent(0, MoveIntent.FullDeflection, Gait.Walk, 0), session.Setup.Movement, space, [], 50);
        Assert.True(body.ZMm > 725_000);
        Assert.True(body.YMm < 16_000);
    }
}
