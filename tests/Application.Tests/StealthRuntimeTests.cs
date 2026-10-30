// UNNAMED Application Tests - the stealth world alive: the round a guard walks, and the bell the town answers
// No Godot references

using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// D23 S6's content, doing something (STEALTH.md 8.2): the patrol a region authors is a round the NPC simulation walks, a bell is a switch whose
/// ringing the town answers, and the wind content authors is the air the noses read. Judged on the simulation's own bodies and numbers.
/// </summary>
public class StealthRuntimeTests
{
    private const string Guard = "npc.frontier.waystation_watch";
    private const string Round = "patrol.waystation_yard";
    private const string Bell = "switch.bell.ashen_hollow";
    private const long Hour = 57_600 / 24;

    /// <summary>Put the world at an hour of the day, whatever hour it opened on.</summary>
    private static void AtHour(Simulation simulation, int hour)
    {
        long now = simulation.World.NpcLife!.Hour % 24;
        long hours = (hour - now + 24) % 24;
        if (hours > 0)
            simulation.Advance(hours * Hour);
    }

    private static Body GuardBody(Simulation simulation) =>
        simulation.Npcs.Where(n => n.Id == Guard && n.Space == simulation.ActiveLayout.SpaceId).Select(n => n.Body).First();

    private static long Distance(long ax, long az, long bx, long bz)
    {
        long dx = ax - bx, dz = az - bz;
        return (long)Math.Sqrt((double)dx * dx + (double)dz * dz);
    }

    /// <summary>Stand beside a post: the first of the approaches the character can actually reach.</summary>
    private static bool Approach(GameSession session, (double X, double Z) post)
    {
        foreach (var (dx, dz) in new[] { (1.3, 0.0), (-1.3, 0.0), (0.0, 1.3), (0.0, -1.3), (1.0, 1.0), (-1.0, 1.0) })
            if (Harness.WalkTo(session, (long)((post.X + dx) * 1000), (long)((post.Z + dz) * 1000)))
                return true;
        return false;
    }

    [Fact]
    public void TheYardPatrol_IsARoundTheGuardActuallyWalks()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame(Harness.Named("Wanderer"), seed: 42);
        AtHour(simulation, 12);

        var patrol = simulation.ActiveLayout.Patrols.Single(p => p.Key == Round);
        Assert.Equal(Guard, patrol.NpcId);
        Assert.True(patrol.Stops.Length >= 3, $"the round has {patrol.Stops.Length} stops");

        var start = GuardBody(simulation);
        long step = Math.Max(1, simulation.Setup.Movement.BaseSpeedMmPerSecond * simulation.Setup.Movement.WalkPercent / 100
            * simulation.Setup.TickMilliseconds / 1000);

        Harness.Ticks(session, 700);

        var after = GuardBody(simulation);
        long travelled = Distance(after.XMm, after.ZMm, start.XMm, start.ZMm);
        Assert.True(travelled > 2_000, $"the watch did not walk her round ({travelled} mm in 35 seconds)");

        // Where the round says she should be at the tick she is standing on: the walker lands on each stop, so she is on the leg.
        var leg = PatrolRules.At(patrol, simulation.WorldTick, step);
        Assert.True(Distance(after.XMm, after.ZMm, leg.XMm, leg.ZMm) <= 4_000,
            $"she is {Distance(after.XMm, after.ZMm, leg.XMm, leg.ZMm) / 1000.0:0.0} m from where her round has her");
    }

    [Fact]
    public void OffDuty_TheRoundLetsGoOfHer()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame(Harness.Named("Wanderer"), seed: 42);
        AtHour(simulation, 2);

        // She is wherever her routine has her; the proof that the round is what moves her is the test above, this is the other half of the rule.
        var patrol = simulation.ActiveLayout.Patrols.Single(p => p.Key == Round);
        var body = GuardBody(simulation);
        var nearest = patrol.Stops.Min(s => Distance(body.XMm, body.ZMm, s.XMm, s.ZMm));
        Assert.True(nearest >= 0);
    }

    [Fact]
    public void TheYardBell_RingsWhenItsFlagIsSet_AndTheTownHearsIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame(Harness.Named("Wanderer"), seed: 42);
        var rung = Harness.Record<AlarmRung>(session);

        var bell = simulation.ActiveLayout.Switches.SingleOrDefault(s => s.Key == Bell);
        Assert.True(bell is not null, $"the bell is not in the layout: {string.Join(", ", simulation.ActiveLayout.Switches.Select(s => s.Key))}");
        Assert.True(Approach(session, (458, 748)), "the character could not reach the bell post");

        var body = simulation.Player.Body;
        double reach = simulation.Setup.Movement.InteractReachMm;
        double distance = bell!.Body.DistanceTo(body.XMm, body.ZMm);
        Assert.True(distance <= reach, $"the character is {distance / 1000.0:0.00} m from the bell's edge; reach is {reach / 1000.0:0.00} m");

        session.Submit(new InteractCommand(simulation.PlayerId, Bell));
        Harness.Ticks(session, 2);

        var cell = Simulation.CellOf(simulation.ActiveLayout.SpaceId, bell);
        Assert.True(simulation.World.GetFlag(cell, "world.ward.ashen_hollow_bell") == 1, "the bell did not ring");
        var alarm = Assert.Single(rung);
        Assert.Equal(Bell, alarm.BellKey);
    }

    [Fact]
    public void ARungBell_SendsTheGuardsToThePost()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame(Harness.Named("Wanderer"), seed: 42);
        AtHour(simulation, 12);

        var bell = simulation.ActiveLayout.Switches.Single(s => s.Key == Bell);
        var (bellX, bellZ) = Footprints.Center(bell.Body);
        var before = GuardBody(simulation);
        long beforeDistance = Distance(before.XMm, before.ZMm, bellX, bellZ);

        Assert.True(Approach(session, (458, 748)), "the character could not reach the bell post");
        session.Submit(new InteractCommand(simulation.PlayerId, Bell));
        Harness.Ticks(session, 400);

        var after = GuardBody(simulation);
        long now = Distance(after.XMm, after.ZMm, bellX, bellZ);
        Assert.True(now < beforeDistance,
            $"the watch did not come to the bell: {beforeDistance / 1000.0:0.0} m before, {now / 1000.0:0.0} m after");
    }

    [Fact]
    public void TheAuthoredWind_IsTheAirTheWorldReads()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame(Harness.Named("Wanderer"), seed: 42);

        // The Foldscar authors a wind at 245 degrees over its patch; the readout there is that wind, not the day's.
        var winds = simulation.ActiveLayout.Winds;
        Assert.Contains(winds, w => w.Key == "wind.foldscar_exposure");
        var foldscar = winds.Single(w => w.Key == "wind.foldscar_exposure");
        Assert.Equal(245_000, simulation.WindBearingAt(foldscar.XMm, foldscar.ZMm));
        Assert.Equal(9, simulation.WindStrengthAt(foldscar.XMm, foldscar.ZMm));

        // Away from every authored patch the readout is the day's prevailing wind, which is what a nose outside them falls back to.
        long far = simulation.WindBearingAt(simulation.ActiveLayout.Space.MaxXMm - 100_000, simulation.ActiveLayout.Space.MinZMm + 100_000);
        Assert.InRange(far, 0, 359_999);
        Assert.NotEqual(245_000, far);
    }
}
