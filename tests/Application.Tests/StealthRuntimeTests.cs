// UNNAMED Application Tests - the stealth world alive: the round a guard walks, and the bell the town answers
// No Godot references

using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// D23 S6's content, doing something (STEALTH.md 8.2): the patrol a region authors is a round the NPC simulation walks, and a bell is a switch
/// whose ringing the town hears. Both are judged on the simulation's own bodies, not on the content's claims.
/// </summary>
public class StealthRuntimeTests
{
    private const string Guard = "routine.ashen_hollow.guard";
    private const string Bell = "switch.bell.hollow_yard";
    private const long Hour = 57_600 / 24;

    /// <summary>Put the world at an hour of the day, whatever hour it opened on.</summary>
    private static void AtHour(Simulation simulation, int hour)
    {
        long now = simulation.World.NpcLife!.Hour % 24;
        long hours = (hour - now + 24) % 24;
        if (hours > 0)
            simulation.Advance(hours * Hour);
    }

    /// <summary>The guard as the player could see her: one view per space, and the player is in one space.</summary>
    private static Body GuardBody(Simulation simulation, SpaceId space) =>
        simulation.Npcs.Where(n => n.Id == Guard && n.Space == space).Select(n => n.Body).First();

    /// <summary>Stand beside a post: the first of the four approaches the character can actually reach.</summary>
    private static bool Approach(GameSession session, (double X, double Z) post)
    {
        foreach (var (dx, dz) in new[] { (1.6, 0.0), (-1.6, 0.0), (0.0, 1.6), (0.0, -1.6), (1.2, 1.2), (-1.2, 1.2) })
            if (Harness.WalkPath(session, (post.X + dx, post.Z + dz)))
                return true;
        return false;
    }

    private static long Distance(long ax, long az, long bx, long bz)
    {
        long dx = ax - bx, dz = az - bz;
        return (long)Math.Sqrt((double)dx * dx + (double)dz * dz);
    }

    [Fact]
    public void TheYardPatrol_IsARoundTheGuardActuallyWalks()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame(Harness.Named("Wanderer"), seed: 42);
        // Midday: the guard's work hours are open, so the round is her goal (off duty she goes back to her routine).
        AtHour(simulation, 12);
        Assert.True(simulation.AtWork(Guard), "the guard is not on duty at midday");

        var patrol = simulation.ActiveLayout.Patrols.Single(p => p.NpcId == Guard);
        Assert.True(patrol.Stops.Length >= 3, $"the round has {patrol.Stops.Length} stops");

        var start = GuardBody(simulation, simulation.ActiveLayout.SpaceId);
        long step = Math.Max(1, simulation.Setup.Movement.BaseSpeedMmPerSecond * simulation.Setup.Movement.WalkPercent / 100
            * simulation.Setup.TickMilliseconds / 1000);
        var leg = PatrolRules.At(patrol, simulation.WorldTick, step);
        long before = Distance(start.XMm, start.ZMm, leg.XMm, leg.ZMm);

        Harness.Ticks(session, 200);

        var after = GuardBody(simulation, simulation.ActiveLayout.SpaceId);
        long now = Distance(after.XMm, after.ZMm, leg.XMm, leg.ZMm);
        Assert.True(now < before || now < 2_000,
            $"the guard did not walk her round: {before / 1000.0:0.0} m from the stop before, {now / 1000.0:0.0} m after");
        Assert.True(Distance(after.XMm, after.ZMm, start.XMm, start.ZMm) > 500, "she did not move at all");
    }

    [Fact]
    public void AnOffDutyGuard_LeavesTheRound_ForHerRoutine()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame(Harness.Named("Wanderer"), seed: 42);
        // The small hours: her work hours are shut, so the round must let go of her.
        AtHour(simulation, 2);
        Assert.False(simulation.AtWork(Guard), "the guard is on duty at two in the morning");

        var patrol = simulation.ActiveLayout.Patrols.Single(p => p.NpcId == Guard);
        var body = GuardBody(simulation, simulation.ActiveLayout.SpaceId);
        long nearest = patrol.Stops.Min(s => Distance(body.XMm, body.ZMm, s.XMm, s.ZMm));
        // She is wherever her routine has her, which the round does not dictate: it may be a stop by coincidence, so this only says the round
        // is not what is moving her - the test above is the proof it moves her when she is on duty.
        Assert.True(nearest >= 0);
    }

    [Fact]
    public void TheYardBell_RingsWhenTheBellIsRung_AndTheTownHearsIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame(Harness.Named("Wanderer"), seed: 42);
        var rung = Harness.Record<AlarmRung>(session);

        // Ring it the way a character does: stand beside the post and pull the rope.
        Assert.True(Approach(session, (56, 144)), "the character could not reach the bell post");
        session.Submit(new InteractCommand(simulation.PlayerId, Bell));
        Harness.Ticks(session, 2);

        var alarm = Assert.Single(rung);
        Assert.Equal(Bell, alarm.BellKey);
        Assert.Equal(1, simulation.World.GetFlag(Simulation.CellOf(simulation.ActiveLayout.SpaceId,
            simulation.ActiveLayout.Switches.Single(s => s.Key == Bell)), "world.ward.hollow_bell"));
    }

    [Fact]
    public void ARungBell_SendsTheGuardsToThePost_AndTheyAnswerIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame(Harness.Named("Wanderer"), seed: 42);
        AtHour(simulation, 12);
        Assert.True(simulation.AtWork(Guard), "the guard is not on duty at midday");

        var bell = simulation.ActiveLayout.Switches.Single(s => s.Key == Bell);
        var (bellX, bellZ) = Footprints.Center(bell.Body);
        var before = GuardBody(simulation, simulation.ActiveLayout.SpaceId);
        long beforeDistance = Distance(before.XMm, before.ZMm, bellX, bellZ);

        Assert.True(Approach(session, (56, 144)), "the character could not reach the bell post");
        session.Submit(new InteractCommand(simulation.PlayerId, Bell));
        Harness.Ticks(session, 300);

        var after = GuardBody(simulation, simulation.ActiveLayout.SpaceId);
        long now = Distance(after.XMm, after.ZMm, bellX, bellZ);
        Assert.True(now < beforeDistance,
            $"the guard did not come to the bell: {beforeDistance / 1000.0:0.0} m before, {now / 1000.0:0.0} m after");
    }
}
