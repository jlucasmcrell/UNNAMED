using UNNAMED.Domain.Knowledge;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Tests;

public class OpeningFilmWordsTests
{
    [Fact]
    public void ApprovedEightRecordings_HaveNonoverlappingWindows_AndReachTheTitle()
    {
        Assert.Equal(8,OpeningFilmWords.Lines.Length);
        double previous=0;
        for(int i=0;i<OpeningFilmWords.Lines.Length;i++)
        {
            var line=OpeningFilmWords.Lines[i];
            Assert.Equal($"opening2.{i+1:00}",line.Voice);
            Assert.Null(LoreRules.Leak(line.Words));
            Assert.True(line.From>=previous&&line.Until>line.From);
            Assert.True(line.Until<=OpeningFilmWords.End);
            previous=line.Until;
        }
        Assert.InRange(OpeningFilmWords.Hit,OpeningFilmWords.Lines[^1].From,OpeningFilmWords.Lines[^1].Until);
        Assert.InRange(OpeningFilmWords.End,40,50);
    }
}
