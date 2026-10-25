using UNNAMED.Domain.Guidance;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// 1I's guidance budget and stuck help (SYS-166): hints are spaced and capped so a run of moments cannot become a lecture, and a
/// player who has stopped making progress is offered one hint - never over words, never while down, twice a session at most.
/// </summary>
public class HintPacingTests
{
    // ---- the budget -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheFirstHint_ShowsWheneverItComes()
    {
        Assert.True(HintPacing.Allowed(0, Array.Empty<double>()));
        Assert.True(HintPacing.Allowed(9_999, new[] { 1.0 }));
    }

    [Fact]
    public void TwoHints_AreNeverCloserThanTheMinimumGap()
    {
        var shown = new List<double> { 100 };
        Assert.False(HintPacing.Allowed(100 + HintPacing.MinGapSeconds - 0.1, shown));
        Assert.True(HintPacing.Allowed(100 + HintPacing.MinGapSeconds, shown));
    }

    [Fact]
    public void TheStretchBudget_StopsTheFourthHint_AndReopensAsTheOldestLeaves()
    {
        var shown = new List<double> { 0, 20, 40 };
        Assert.False(HintPacing.Allowed(60, shown));          // three inside the stretch already
        Assert.False(HintPacing.Allowed(HintPacing.WindowSeconds - 1, shown));
        Assert.True(HintPacing.Allowed(HintPacing.WindowSeconds, shown));   // the first has left the window
        // One hint, then a long quiet: the budget is not a lifetime cap.
        Assert.True(HintPacing.Allowed(10_000, new[] { 1.0 }));
    }

    // ---- stuck help -----------------------------------------------------------------------------------------------------------

    private static StuckFrame Frame(double now, double lastProgress, bool prompt = false, bool busy = false) => new(now, lastProgress, prompt, busy);

    [Fact]
    public void APlayerWhoHasNotMoved_IsOfferedHelpAfterTheIdleTime()
    {
        var offered = new List<double>();
        Assert.False(StuckHelp.Offer(Frame(10, 0), offered));
        Assert.False(StuckHelp.Offer(Frame(StuckHelp.IdleSeconds - 1, 0), offered));
        Assert.True(StuckHelp.Offer(Frame(StuckHelp.IdleSeconds, 0), offered));
    }

    [Fact]
    public void APromptLeftStanding_CountsAsStuckSooner()
    {
        var offered = new List<double>();
        Assert.False(StuckHelp.Offer(Frame(StuckHelp.PromptIdleSeconds - 1, 0, prompt: true), offered));
        Assert.True(StuckHelp.Offer(Frame(StuckHelp.PromptIdleSeconds, 0, prompt: true), offered));
        // The same pause with no prompt is not stuck yet: walking about is not being stuck.
        Assert.False(StuckHelp.Offer(Frame(StuckHelp.PromptIdleSeconds, 0), Array.Empty<double>()));
    }

    [Theory]
    [InlineData(true, false)]    // words on screen
    [InlineData(false, true)]    // downed
    public void NoHelpIsOfferedOverWordsOrWhileDown(bool busy, bool other)
    {
        Assert.False(StuckHelp.Offer(Frame(10_000, 0, busy: busy || other), Array.Empty<double>()));
    }

    [Fact]
    public void TheOffersAreRare_TwiceASession_AndMinutesApart()
    {
        var offered = new List<double> { 100 };
        Assert.False(StuckHelp.Offer(Frame(100 + StuckHelp.GapSeconds - 1, 0), offered));
        Assert.True(StuckHelp.Offer(Frame(100 + StuckHelp.GapSeconds, 0), offered));
        offered.Add(100 + StuckHelp.GapSeconds);
        Assert.False(StuckHelp.Offer(Frame(100 + 10 * StuckHelp.GapSeconds, 0), offered));
        Assert.Equal(2, StuckHelp.MaxPerSession);
    }

    [Fact]
    public void TheStuckHintComesFirst_ThenWhateverUnseenHintRemains()
    {
        HintDefinition[] hints =
        {
            new("hint.moving", "movement", HintTriggers.NewGame, "{key:move_forward} moves.", 0),
            new("hint.stuck", "help", HintTriggers.Stuck, "Stuck? {key:help} lists every key.", 0),
            new("hint.pack", "inventory", HintTriggers.PackFull, "The pack is full.", 0),
        };
        var seen = new HashSet<string>(StringComparer.Ordinal) { "hint.moving" };
        var candidates = StuckHints.Candidates(hints, seen);
        Assert.Equal(new[] { "hint.stuck", "hint.pack" }, candidates.Select(h => h.Id));
        // Every hint seen: nothing to offer.
        Assert.Empty(StuckHints.Candidates(hints, hints.Select(h => h.Id).ToHashSet(StringComparer.Ordinal)));
    }
}
