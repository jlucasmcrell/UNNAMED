using UNNAMED.Domain.Knowledge;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Tests;

/// <summary>The opening's lines (owner 2026-10-24): world words only, one at a time, all said before the name comes in, each with its own voice slot.</summary>
public class OpeningWordsTests
{
    [Fact]
    public void EveryLine_PassesTheWorldTextLint()
    {
        foreach (var line in OpeningWords.Lines)
            Assert.Null(LoreRules.Leak(line.Words));
    }

    [Fact]
    public void TheLines_ComeOneAtATime_BeforeTheHit()
    {
        for (int i = 0; i < OpeningWords.Lines.Length; i++)
        {
            var line = OpeningWords.Lines[i];
            Assert.True(line.From < line.Until);
            Assert.True(line.Until <= OpeningWords.Hit);
            if (i > 0)
                Assert.True(OpeningWords.Lines[i - 1].Until <= line.From);
        }
        Assert.True(OpeningWords.Hit < OpeningWords.End);
    }

    [Fact]
    public void EachLine_HasItsOwnVoiceSlot() =>
        Assert.Equal(OpeningWords.Lines.Length, OpeningWords.Lines.Select(l => l.Voice).Distinct().Count());
}
