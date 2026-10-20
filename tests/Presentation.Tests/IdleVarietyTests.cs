using UNNAMED.Presentation.Art;
namespace UNNAMED.Presentation.Tests;
public class IdleVarietyTests
{
    [Fact]
    public void PeopleHaveDifferentPhases_AndActivitiesFinish()
    {
        var a = new IdleVariety("renn"); var b = new IdleVariety("sel");
        Assert.NotEqual(a.StartPhase, b.StartPhase);
        bool Has(string clip) => clip == "idle_look";
        var clip = a.Step(30, "merchant", Has, _ => 2);
        Assert.Equal("idle_look", clip);
        Assert.Equal("idle", a.Step(3, "merchant", Has, _ => 2));
        a.Interrupt();
        Assert.Equal("idle", a.Step(.1, "merchant", Has, _ => 2));
    }
    [Fact]
    public void MissingClipsKeepTheNativeIdle() => Assert.Equal("idle", new IdleVariety("a").Step(30, "guard", _ => false, _ => 1));
}
