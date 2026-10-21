using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Spec 15's night (owner finding 15): the inn is where they sleep, 22:00 to 06:00, and the common room is not where a crowd of hireable
/// bodies stands at midnight - the complaint that started all of this. Their beds are the inn's own places, so this is measured against the
/// content, not against a number.
/// </summary>
public class InnNightTests
{
    private const long Hour = 2_400;

    [Fact]
    public void AtElevenAtNight_EveryAdventurerInTownIsAtABedSide_AndTheRoomIsEmpty()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AdventurerWorldTests.AtTheInn(session);
        var simulation = arena.Simulation;
        var inn = session.Setup.Adventurers!.Settlements["ashen_hollow"].Inn!;
        // The world opens at 07:30; the twenty-third hour is 37,200 ticks in.
        simulation.Advance(37_200);
        Assert.Equal(23, session.Setup.Clock.MinuteOfDay(simulation.WorldTick) / 60);

        var bodies = simulation.Npcs.Where(n => n.Role == "adventurer" && n.Id.Contains("ashen_hollow")).ToList();
        Assert.NotEmpty(bodies);
        foreach (var body in bodies)
        {
            long toBed = inn.Beds.Min(b => Distance(body.Body.XMm, body.Body.ZMm, b.XMm, b.ZMm));
            Assert.True(toBed < 1_000, $"{body.Id} stands {toBed / 1000.0:0.0} m from the nearest bed at 23:00");
            // ...and not in the common room: the board, the hearth and the stools are across the room from the beds.
            foreach (var place in inn.Seats.Append(inn.Board).Append(inn.Hearth))
                Assert.True(Distance(body.Body.XMm, body.Body.ZMm, place.XMm, place.ZMm) > 1_500,
                    $"{body.Id} is in the common room at 23:00");
        }
    }

    [Fact]
    public void AtTheDepartureHour_TheyStandAtTheGate_AndThenThereIsNoBodyAtAll()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = AdventurerWorldTests.AtTheInn(session);
        var simulation = arena.Simulation;
        var inn = session.Setup.Adventurers!.Settlements["ashen_hollow"].Inn!;
        // The first day is the world's own half day (it opens at 07:30 and plans nothing before its first midnight), so the departure hour is
        // the second day's: tick 58,400 is 07:50, five minutes before the gate walk ends and the day's work begins.
        simulation.Advance(58_400);
        Assert.Equal(7, session.Setup.Clock.MinuteOfDay(simulation.WorldTick) / 60);
        var atTheHour = simulation.Npcs.Where(n => n.Role == "adventurer" && n.Id.Contains("ashen_hollow")).ToList();
        Assert.NotEmpty(atTheHour);
        // The departure hour is spent at the gate - the walk out is what the character sees of them going.
        Assert.All(atTheHour, b => Assert.True(
            Distance(b.Body.XMm, b.Body.ZMm, inn.Gate.XMm, inn.Gate.ZMm) < 2_000,
            $"{b.Id} is at {b.Body.XMm},{b.Body.ZMm} at 07:50; the gate is {inn.Gate.XMm},{inn.Gate.ZMm}; " +
            $"plan day {simulation.World.Adventurers!.Agents[b.Id].Plan.Day} depart {simulation.World.Adventurers!.Agents[b.Id].Plan.DepartTick} " +
            $"tick {simulation.WorldTick}"));
        // The departure hour over, they are out: no body at all.
        for (int tick = 0; tick < 3_000 && atTheHour.Any(b => simulation.Npcs.Any(n => n.Id == b.Id)); tick++)
            simulation.Advance(1);
        var gone = simulation.Adventurers.Where(a => !a.InTown && a.Party is null).Select(a => a.Key).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(gone);
        Assert.DoesNotContain(simulation.Npcs, n => n.Role == "adventurer" && gone.Contains(n.Id));
    }

    private static long Distance(long ax, long az, long bx, long bz)
    {
        long dx = ax - bx, dz = az - bz;
        return (long)Math.Sqrt(dx * dx + dz * dz);
    }
}
