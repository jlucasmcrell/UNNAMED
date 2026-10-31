using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// S1's dousable lights (STEALTH.md section 2.2): a lamp the character puts out stops lighting the lane, and the band the sight check reads falls with
/// it - the same number the HUD shows, so the gem cannot say one thing while a watch sees another.
/// </summary>
public class StealthDouseTests
{
    /// <summary>The yard lamp authored in the starting frontier, which is where the route's lane is.</summary>
    private const string Lamp = "light.waystation_yard";

    /// <summary>Standing under the lamp is bright; putting it out leaves the lane dark; lighting it again brings it back.</summary>
    [Fact]
    public void PuttingTheLampOut_DarkensTheLane_AndLightingItAgainBringsItBack()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The lamp is at (447, 730) in the starting frontier's own metres.
        var arena = Arena.OpenCreatures(session, session.Setup, (447, 731), 0, Array.Empty<(string, double, double, string)>(),
            null, startTick: 40_000, origin: (0, 0));
        arena.Tick(2);
        int lit = arena.Simulation.PlayerLitPermille;
        Assert.True(lit > 500, $"standing at the yard lamp has to be bright (it was {lit} permille)");

        Assert.Null(arena.Submit(new DouseLightCommand(arena.Player, Lamp, Lit: false)));
        arena.Tick(2);
        int dark = arena.Simulation.PlayerLitPermille;
        Assert.True(dark < lit / 2, $"putting it out has to darken the lane ({lit} to {dark} permille)");
        Assert.True(arena.Simulation.IsLightDoused(Lamp));

        Assert.Null(arena.Submit(new DouseLightCommand(arena.Player, Lamp, Lit: true)));
        arena.Tick(2);
        Assert.Equal(lit, arena.Simulation.PlayerLitPermille);
        Assert.False(arena.Simulation.IsLightDoused(Lamp));
    }

    /// <summary>A lamp across the region cannot be put out from here: a hand reaches an arm and a step, not a field.</summary>
    [Fact]
    public void ALampOutOfReach_IsRefused()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (600, 760), 0, Array.Empty<(string, double, double, string)>(),
            null, startTick: 40_000, origin: (0, 0));
        arena.Tick(2);
        Assert.Equal("too far to reach it", arena.Submit(new DouseLightCommand(arena.Player, Lamp, Lit: false)));
    }
}