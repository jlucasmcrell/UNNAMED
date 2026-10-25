using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// The opening keeps to its score on a slow machine (coordinator 2026-10-25): its time is the wall clock's, so at 15 frames a second or 30
/// each line, pulse and the hit comes on the first frame at or past its moment, once, and the opening ends at 67 s.
/// </summary>
public class OpeningClockTests
{
    private static readonly double[] LineCues = { 2.2, 12.0, 23.4, 32.6, 42.6, 51.4 };

    /// <summary>Runs the opening at a steady frame rate from a start of <paramref name="start"/> s; each cue and the second it fired.</summary>
    private static List<(OpeningCue Cue, int Index, double At)> Run(double fps, double start = 1000)
    {
        var clock = new OpeningClock();
        clock.Start(start);
        var fired = new List<(OpeningCue, int, double)>();
        for (int frame = 1; frame < 10_000; frame++)
        {
            foreach (var (cue, index) in clock.Advance(start + frame / fps))
                fired.Add((cue, index, clock.Seconds));
            if (fired.Any(f => f.Item1 == OpeningCue.End))
                break;
        }
        return fired;
    }

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    public void AnyFrameRate_MeetsEveryCue_AndEndsAt67Seconds(double fps)
    {
        var fired = Run(fps);
        double frame = 1 / fps;

        var lines = fired.Where(f => f.Cue == OpeningCue.Line).ToList();
        Assert.Equal(Enumerable.Range(0, LineCues.Length), lines.Select(l => l.Index));
        for (int i = 0; i < LineCues.Length; i++)
        {
            Assert.Equal(LineCues[i], OpeningWords.Lines[i].From);
            Assert.InRange(lines[i].At, LineCues[i], LineCues[i] + frame + 1e-9);
        }
        var pulses = fired.Where(f => f.Cue == OpeningCue.Pulse).ToList();
        Assert.Equal(OpeningWords.PulseAt.Length, pulses.Count);
        for (int i = 0; i < pulses.Count; i++)
            Assert.InRange(pulses[i].At, OpeningWords.PulseAt[i], OpeningWords.PulseAt[i] + frame + 1e-9);
        Assert.InRange(fired.Single(f => f.Cue == OpeningCue.Hit).At, 62, 62 + frame + 1e-9);
        Assert.InRange(fired.Single(f => f.Cue == OpeningCue.End).At, 67, 67 + frame + 1e-9);
    }

    [Fact]
    public void AStalledFrame_FiresWhatItSkipped_OnceAndInOrder()
    {
        var clock = new OpeningClock();
        clock.Start(0);
        Assert.Empty(clock.Advance(1));
        // A long stall over two lines' starts: both come on the frame after it, in order, and never again.
        var reached = clock.Advance(13.5);
        Assert.Equal(new[] { (OpeningCue.Line, 0), (OpeningCue.Line, 1) }, reached.ToArray());
        Assert.Empty(clock.Advance(13.6));
        Assert.Equal(13.6, clock.Seconds, 9);
    }
}
