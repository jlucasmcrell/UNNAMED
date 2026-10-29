using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Tests;

public class CharacterPerformanceTests
{
    [Fact]
    public void DialogueAndSchedulesChoosePerformanceWithoutInventingWork()
    {
        Assert.Equal("warm", DialoguePerformance.Mood("Welcome, traveller."));
        Assert.Equal("sad", DialoguePerformance.Mood("We lost him."));
        Assert.Equal("curious", DialoguePerformance.Mood("Where are you going?"));
        Assert.Equal("neutral", DialoguePerformance.Mood(null));
        Assert.Equal("stern", DialoguePerformance.Mood("Welcome!", "stern"));
        Assert.Equal("work_smith", CharacterActivity.Clip("work", "smith"));
        Assert.Null(CharacterActivity.Clip("idle", "smith"));
        Assert.Null(CharacterActivity.Clip("work", "unknown"));
        Assert.Null(CharacterActivity.Clip("sleep", "smith"));
    }
}
