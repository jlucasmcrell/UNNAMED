// UNNAMED Application - D23 S6: a space crossed unseen is a ghost crossing, and it pays (STEALTH.md 9.2-9.3)

using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;
using Xunit;

namespace UNNAMED.Application.Tests;

/// <summary>
/// S6 of D23. Canon's Ghost tag is system-detected from signed facts, not a hidden score: nobody grew suspicious on sight, nobody was killed. The
/// crossing pays on the way OUT of a space, so the exterior's Ghosted deed lands when the character steps through the threshold into the Barrowworks -
/// which is what this checks, on a world with nothing hostile in it so the crossing is genuinely clean.
/// </summary>
public class StealthGhostTests
{
    private const string Threshold = "portal.barrowworks.threshold";


    private static GameSession Boot(TempProfile profile) =>
        GameSession.Boot(new GameOptions(Path.Combine(Harness.RepoRoot(), "content"), profile.Root)
        {
            RegionId = "region.starting_frontier",
            Barrowworks = true,
        });

    [Fact]
    public void TheExteriorCrossedWithoutAKill_RecordsAGhostDeed_WhenTheCharacterLeavesIt()
    {
        using var profile = new TempProfile();
        var session = Boot(profile);
        var (ox, oz) = Harness.HollowOf(session.Setup);
        // Nothing hostile and no ambient spawns: canon's Ghost tag needs nothing to have grown suspicious, and the crossing is what is under test.
        var arena = Arena.OpenWith(session, session.Setup, (440 - ox, 621 - oz), 0, [], keepSpawns: false);
        var simulation = arena.Simulation;
        string exterior = simulation.CaptureRecord().SpaceId;
        string label = exterior.Length == 0 ? "space.exterior" : exterior;

        // Two ticks first: the tally remembers which space it is counting from.
        arena.Tick(2);
        Assert.DoesNotContain(simulation.CaptureRecord().Deeds, d => d.Kind == DeedKind.Ghosted);

        Assert.Null(arena.Submit(new EnterSpaceCommand(simulation.PlayerId, Threshold)));
        arena.Tick(2);

        var record = simulation.CaptureRecord();
        Assert.NotEqual(exterior, record.SpaceId);
        Assert.Contains(record.Deeds, d => d.Kind == DeedKind.Ghosted && d.SubjectId == label);
        // Nothing in an empty world ever engaged, so the same crossing is canonical UNSEEN as well as GHOST.
        Assert.Contains(record.Deeds, d => d.Kind == DeedKind.Unseen && d.SubjectId == label);
    }

    [Fact]
    public void TheGhostDeeds_AreTalliedWithStableKeys_SoAQuestCanWaitForOne()
    {
        // The keys are saved with the player: renaming one silently orphans every save that counted it.
        Assert.Equal("ghosted", DeedKeys.Key(DeedKind.Ghosted));
        Assert.Equal("spared", DeedKeys.Key(DeedKind.Spared));
        Assert.Equal("unseen", DeedKeys.Key(DeedKind.Unseen));
        Assert.Contains(DeedKind.Ghosted, DeedKeys.Tallied);
        Assert.Contains(DeedKind.Spared, DeedKeys.Tallied);
        Assert.Contains(DeedKind.Unseen, DeedKeys.Tallied);
    }
}
