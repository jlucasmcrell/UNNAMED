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

    /// <summary>
    /// The rim boulder is 2.57 m tall and sits at (10, 84) with a 2.2 m radius: inside the 3 m mantle reach, so a body pressing into it from
    /// the south must end up standing on its top - feet well above the ground - not stopped against its face.
    /// </summary>
    [Fact]
    public void ABodyPressingIntoTheRimBoulder_ClimbsItsTop()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (x, z, above) = Press(session, (10.0, 81.0), facingDeg: 0, ticks: 90);
        _out.WriteLine($"ended at ({x:0.00}, {z:0.00}), feet {above} mm above the ground");
        Assert.True(above > 500, $"a 2.57 m boulder inside the mantle reach was not climbed: rose {above} mm at ({x:0.00}, {z:0.00})");
    }

    /// <summary>
    /// The smithy fence is 1.2 m: a run into it must mount it (a vault), not stop at its face.
    /// </summary>
    [Fact]
    public void ABodyPressingIntoTheSmithyFence_MountsIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (x, z, above) = Press(session, (47.5, 149.5), facingDeg: 0, ticks: 90);
        _out.WriteLine($"ended at ({x:0.00}, {z:0.00}), feet {above} mm above the ground");
        Assert.True(above > 300, $"a 1.2 m fence inside the vault window was not mounted: rose {above} mm at ({x:0.00}, {z:0.00})");
    }

    /// <summary>
    /// The den gorge wall is 7.35 m, far beyond the reach: the body must be stopped by it, which is what keeps the reach meaningful.
    /// </summary>
    [Fact]
    public void ABodyPressingIntoAFaceBeyondTheReach_IsStopped()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (x, z, above) = Press(session, (100.0, 176.0), facingDeg: 0, ticks: 90);
        _out.WriteLine($"ended at ({x:0.00}, {z:0.00}), feet {above} mm above the ground");
        Assert.True(above < 800, $"a 7.35 m face was climbed, but it is beyond the mantle reach: the body rose {above} mm");
    }
}
