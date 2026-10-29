// UNNAMED Application - D23 S6: a space crossed unseen is a ghost crossing, and it pays (STEALTH.md 9.2-9.3)

using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;
using Xunit;

namespace UNNAMED.Application.Tests;

/// <summary>
/// S6 of D23. Canon's Ghost tag is system-detected from signed facts, not a hidden score: nobody grew suspicious on sight, nobody was killed. This
/// checks the vocabulary a reward reads - the stable keys - and not yet the crossing itself: the award is written
/// (<c>Creatures.TrackGhostCrossing</c>) but the arena route that would prove it end to end is still owed, and a test that passes without proving it
/// would be worse than this one. Tracked in CODEX_PROGRESS as the open item of S6.
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
