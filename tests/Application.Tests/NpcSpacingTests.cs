using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Bodies do not stand in each other (owner finding 15): three adventurers leaving by the same gate stood in the same spot, and the frame
/// showed one figure where three were. The party leaves in file now, and nothing else in the day puts two bodies in one place either.
/// </summary>
public class NpcSpacingTests
{
    private const long Hour = 2_400;
    private const long Minute = 40;
    private const long NearestMm = 400;

    [Fact]
    public void TheParty_LeavesTheGateInFile_AStrideApart()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AdventurerWorldTests.AtTheInn(session);
        var simulation = arena.Simulation;
        // The second day, 07:50: five minutes before the departure hour, the three of them are on the way out.
        simulation.Advance(58_400);
        var leaving = simulation.Npcs.Where(n => n.Role == "adventurer" && n.Id.Contains("ashen_hollow")).ToList();
        Assert.Equal(3, leaving.Count);
        Assert.All(leaving, b => Assert.True(Far(b, leaving)));
        // In file: about 0.8 m between one and the next, not scattered about.
        var ordered = leaving.OrderBy(b => b.Body.XMm).ThenBy(b => b.Body.ZMm).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            long step = Tow(ordered[i].Body, ordered[i - 1].Body);
            Assert.InRange(step, 700, 900);
        }
    }

    [Fact]
    public void NoTwoBodies_StandInTheSamePlace_ThroughTheDay()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AdventurerWorldTests.AtTheInn(session);
        var simulation = arena.Simulation;
        // A whole day of the second day, a minute at a time: every place the schedule puts people, walked and not walked.
        simulation.Advance(39_600);
        for (int minute = 0; minute < 24 * 60; minute++)
        {
            var bodies = simulation.Npcs.Where(n => n.Role != "player").ToList();
            for (int i = 0; i < bodies.Count; i++)
                for (int j = i + 1; j < bodies.Count; j++)
                    Assert.True(Tow(bodies[i].Body, bodies[j].Body) >= NearestMm,
                        $"{bodies[i].Id} and {bodies[j].Id} stand {Tow(bodies[i].Body, bodies[j].Body) / 1000.0:0.00} m apart at " +
                        $"{session.Setup.Clock.MinuteOfDay(simulation.WorldTick) / 60:00}:{session.Setup.Clock.MinuteOfDay(simulation.WorldTick) % 60:00}");
            simulation.Advance(Minute);
        }
    }

    private static bool Far(NpcView body, List<NpcView> others) =>
        others.Where(o => o.Id != body.Id).All(o => Tow(o.Body, body.Body) >= NearestMm);

    private static long Tow(Body a, Body b)
    {
        long dx = a.XMm - b.XMm, dz = a.ZMm - b.ZMm;
        return (long)Math.Sqrt(dx * dx + dz * dz);
    }
}
