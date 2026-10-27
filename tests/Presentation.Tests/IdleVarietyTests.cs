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

    [Fact]
    public void DistinctMocapReplacesTheLegacyAlias_AndActivitiesDoNotRepeat()
    {
        var idles = new IdleVariety("renn");
        string? previous = null;
        var seen = new HashSet<string>();
        for (int i = 0; i < 40; i++)
        {
            string clip = idles.Step(30, "merchant", _ => true, _ => 2);
            Assert.NotEqual("idle_look", clip);
            Assert.NotEqual(previous, clip);
            seen.Add(clip);
            previous = clip;
            Assert.Equal("idle", idles.Step(3, "merchant", _ => true, _ => 2));
        }
        Assert.Equal(3, seen.Count);
    }
}
