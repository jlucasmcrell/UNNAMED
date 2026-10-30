using UNNAMED.Domain;
using UNNAMED.Domain.Creatures;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// S6's ghost ledger (STEALTH.md section 9): a clean run is the one where no creature ever found the character, and the count is what a ghost or
/// non-lethal bonus will be paid against.
/// </summary>
public class StealthGhostTests
{
    private const string Wolf = "creature.beast.wolf_grey";

    /// <summary>A minute of crouched walking with nothing near is a clean run: the ledger stays at zero.</summary>
    [Fact]
    public void ARunNobodyNotices_IsAGhost()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (600, 760), 0, Array.Empty<(string, double, double, string)>(),
            null, startTick: 12_000, origin: (0, 0));
        arena.Tick(200);
        Assert.Equal(0, arena.Simulation.StealthDetections());
    }

    /// <summary>A wolf that sees the character ends the clean run, and the ledger says so once.</summary>
    [Fact]
    public void ACreatureThatFindsTheCharacter_EndsTheCleanRun()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (470, 745), 0, new[] { (Wolf, 470.0, 745.0 + 6, "pack_hunter") },
            null, startTick: 12_000, origin: (0, 0));
        arena.Tick(200);
        int seen = 0;
        for (int i = 0; i < 200 && seen == 0; i++)
        {
            arena.Tick(1);
            seen = arena.Simulation.StealthDetections();
        }
        Assert.Equal(1, seen);
        Assert.Equal(CreatureMind.Engaged, arena.Creature().Mind);
    }
}