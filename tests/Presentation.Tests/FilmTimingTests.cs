using UNNAMED.Presentation.Capture;

namespace UNNAMED.Presentation.Tests;

public class FilmTimingTests
{
    [Fact]
    public void SixtyRenderSamplesAdvanceEvenOnFramesWithNoSimulationTick()
    {
        int fps = FilmTiming.RenderFps(["--path", "game", "--fixed-fps", "60"]);
        int frames = FilmTiming.Frames(1, fps);
        Assert.Equal(60, frames);
        var samples = Enumerable.Range(0, frames).Select(i => FilmTiming.Progress(i, frames)).ToArray();
        Assert.Equal(0, samples[0]); Assert.Equal(1, samples[^1]);
        Assert.Equal(60, samples.Distinct().Count());
        for (int i = 1; i < frames; i++)
            Assert.InRange(samples[i] - samples[i - 1], 1f / 59 - 1e-7f, 1f / 59 + 1e-7f);
        Assert.Equal(20, Enumerable.Range(0, frames).Select(i => i * 20 / fps).Distinct().Count());
        Assert.Equal(360, FilmTiming.Frames(6, fps));
        Assert.Equal(1, FilmTiming.Frames(.01, fps));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-60")]
    [InlineData("invalid")]
    public void AnUnpinnedOrInvalidClockCannotSilentlyProduceATwentyHertzFilm(string fps)
    {
        Assert.Throws<ArgumentException>(() => FilmTiming.RenderFps(["--fixed-fps", fps]));
        Assert.Throws<ArgumentException>(() => FilmTiming.RenderFps([]));
    }
}
