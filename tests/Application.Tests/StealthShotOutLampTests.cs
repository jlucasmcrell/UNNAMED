using UNNAMED.Domain;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// S1's "shot out" (STEALTH.md section 2.2): an arrow reaches a lamp a hand cannot, so the same command that douses a nearby lantern carries the shot's
/// own reach - and the doused set is the one the sight check and the HUD read, so the lane goes dark either way.
/// </summary>
public class StealthShotOutLampTests
{
    private const string Lamp = "light.waystation_yard";

    /// <summary>A shot puts a lamp out from across the yard; the same command by hand is refused from there.</summary>
    [Fact]
    public void AShotReachesALamp_AHandDoesNot()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // Across the yard from the lamp, so the distance is real and the hand cannot get there.
        var arena = Arena.OpenCreatures(session, session.Setup, (600, 760), 0, Array.Empty<(string, double, double, string)>(),
            null, startTick: 40_000, origin: (0, 0));
        arena.Tick(2);

        Assert.Equal("too far to reach it", arena.Submit(new DouseLightCommand(arena.Player, Lamp, Lit: false)));
        Assert.False(arena.Simulation.IsLightDoused(Lamp));

        Assert.Null(arena.Submit(new DouseLightCommand(arena.Player, Lamp, Lit: false, ByShot: true)));
        arena.Tick(2);
        Assert.True(arena.Simulation.IsLightDoused(Lamp), "an arrow through the lantern puts it out from across the yard");
    }
}