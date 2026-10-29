using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// BRIEF D6: the climb trigger, checked headlessly on the hollow's own geometry before anything is filmed. A body that walks into a wall it
/// cannot pass must start climbing it when the top is inside its mantle reach - the den gorge wall (den_rock_west, 7.35 m) is a face it
/// cannot reach, while the rim boulder (rock_rim_01, 2.57 m) and the smithy fence (1.2 m) are both inside the 3 m reach. The owner saw the
/// character stuck against a wall; this is the test that has to pass before a capture is taken again.
/// </summary>
public class TraversalClimbTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out;

    public TraversalClimbTests(Xunit.Abstractions.ITestOutputHelper output) => _out = output;

    private static readonly (string, double, double, string)[] Nobody = Array.Empty<(string, double, double, string)>();

    private static Arena Open(GameSession session, (double X, double Z) at, int facingDeg) =>
        Arena.OpenCreatures(session, session.Setup, at, facingDeg, Nobody);

    /// <summary>Press into a structure for a while and report how far the body rose, which is what a climb does and a wall does not.</summary>
    private static (double X, double Z, long Rose) Press(GameSession session, (double X, double Z) from, int facingDeg, int ticks)
    {
        var arena = Open(session, from, facingDeg);
        int dz = facingDeg == 0 ? 1000 : -1000;
        long start = arena.Simulation.Player.Body.YMm;
        (double X, double Z, long Rose) last = (from.X, from.Z, 0);
        for (int tick = 0; tick < ticks; tick++)
        {
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, new MoveIntent(0, dz, Gait.Run, facingDeg * 1000)));
            arena.Tick();
            var body = arena.Simulation.Player.Body;
            last = (body.XMm / 1000.0, body.ZMm / 1000.0, body.YMm - start);
        }
        return last;
    }

    /// <summary>The fallen timber lies 0.8 m high across x 163-171, z 147-147.8. A run into it from the south mounts it.</summary>
    [Fact]
    public void ABodyPressingIntoTheFallenTimber_MountsIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (x, z, rose) = Press(session, (567.0, 769.0), facingDeg: 0, ticks: 120);
        _out.WriteLine($"ended at region ({x:0.00}, {z:0.00}), rose {rose} mm");
        Assert.True(rose > 400, $"a 0.8 m timber inside the mantle reach was not climbed: the body rose {rose} mm at ({x:0.00}, {z:0.00})");
    }

    /// <summary>The cart wreck is 1.4 m - above the 1.2 m vault window, inside the 3 m mantle reach - so it is climbed, not vaulted.</summary>
    [Fact]
    public void ABodyPressingIntoTheCartWreck_ClimbsIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (x, z, rose) = Press(session, (557.0, 784.0), facingDeg: 0, ticks: 160);
        _out.WriteLine($"ended at region ({x:0.00}, {z:0.00}), rose {rose} mm");
        Assert.True(rose > 600, $"a 1.4 m cart inside the mantle reach was not climbed: the body rose {rose} mm at ({x:0.00}, {z:0.00})");
    }
}
