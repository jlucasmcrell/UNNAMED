using UNNAMED.Domain;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// S1's prone/crawl (STEALTH.md section 2.6): the stance existed with its own eye height, sight gain and noise radius, and nothing could adopt it. Now the
/// crouch command can lay the character down, and a crawl is the quietest and slowest way a body moves.
/// </summary>
public class StealthProneTests
{
    private static (long Noise, Stance Stance) At(Stance stance)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (600, 760), 0, Array.Empty<(string, double, double, string)>(),
            null, startTick: 12_000, origin: (0, 0));
        arena.Tick(5);
        arena.Submit(new CrouchCommand(arena.Player, Crouched: stance != Stance.Standing, Prone: stance == Stance.Prone));
        arena.Tick(2);
        arena.Submit(new MoveCommand(arena.Player, new MoveIntent(1_000, 0, Gait.Walk, 0)));
        arena.Tick(3);
        return (arena.Simulation.PlayerNoiseMm, arena.Simulation.Posture.Stance);
    }

    /// <summary>A crawl is quieter than a crouch-walk, and it is the stance the game says it is.</summary>
    [Fact]
    public void ACrawl_IsQuieterThanACrouchWalk()
    {
        var (crouchNoise, crouchStance) = At(Stance.Crouched);
        var (proneNoise, proneStance) = At(Stance.Prone);
        Assert.Equal(Stance.Crouched, crouchStance);
        Assert.Equal(Stance.Prone, proneStance);
        Assert.True(crouchNoise > 0, "a crouch-walk still makes a sound");
        Assert.True(proneNoise < crouchNoise, $"a crawl is quieter than a crouch-walk ({crouchNoise} mm to {proneNoise} mm)");
    }
}