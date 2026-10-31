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

    // Region metres, the same space the props are authored in, not the hollow-relative offsets the combat arenas take.
    private static Arena Open(GameSession session, (double X, double Z) at, int facingDeg) =>
        Arena.OpenCreatures(session, session.Setup, at, facingDeg, Nobody, origin: (0, 0));

    /// <summary>
    /// Press into a structure for a while and report the highest the feet stood above the terrain, which is what a climb does and a wall does
    /// not. The peak, not the end: a run that goes on presses over the top and down the far side.
    /// </summary>
    private static (double X, double Z, long Rose) Press(GameSession session, (double X, double Z) from, int facingDeg, int ticks)
    {
        var arena = Open(session, from, facingDeg);
        var terrain = arena.Simulation.Setup.Layout.Space.Terrain;
        int dz = facingDeg == 0 ? 1000 : -1000;
        (double X, double Z, long Rose) peak = (from.X, from.Z, 0);
        for (int tick = 0; tick < ticks; tick++)
        {
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, new MoveIntent(0, dz, Gait.Run, facingDeg * 1000)));
            arena.Tick();
            var body = arena.Simulation.Player.Body;
            long above = body.YMm - terrain.HeightAtMm(body.XMm, body.ZMm);
            if (above > peak.Rose)
                peak = (body.XMm / 1000.0, body.ZMm / 1000.0, above);
        }
        return peak;
    }

    /// <summary>The fallen timber lies 0.8 m high across x 563-571, z 747-747.8. A run into it from the south mounts it.</summary>
    [Fact]
    public void ABodyPressingIntoTheFallenTimber_MountsIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (x, z, rose) = Press(session, (567.0, 745.0), facingDeg: 0, ticks: 120);
        _out.WriteLine($"peaked at region ({x:0.00}, {z:0.00}), {rose} mm above the ground");
        Assert.True(rose > 400, $"a 0.8 m timber inside the mantle reach was not climbed: the body rose {rose} mm at ({x:0.00}, {z:0.00})");
    }

    /// <summary>The cart wreck is 1.4 m across x 555.5-558.5, z 761.2-763.2 - above the 1.2 m vault window, inside the 3 m mantle reach - so it is climbed, not vaulted.</summary>
    [Fact]
    public void ABodyPressingIntoTheCartWreck_ClimbsIt()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (x, z, rose) = Press(session, (557.0, 759.0), facingDeg: 0, ticks: 160);
        _out.WriteLine($"peaked at region ({x:0.00}, {z:0.00}), {rose} mm above the ground");
        Assert.True(rose > 600, $"a 1.4 m cart inside the mantle reach was not climbed: the body rose {rose} mm at ({x:0.00}, {z:0.00})");
    }
}
