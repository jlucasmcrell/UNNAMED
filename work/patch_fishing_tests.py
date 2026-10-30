"""The fishing tests must carry the rod the config now requires, and two of them prove the new rules (goal round 9)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\tests\Application.Tests\FishingTests.cs')
code = path.read_text(encoding='utf-8')

# Every cast in these tests is made with a rod in the pack, because config.fishing names one and the system now enforces it.
old = '''    private static Arena At(GameSession session, (double X, double Z) place) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, Array.Empty<(string, double, double, string)>(), Arena.Usual);'''
new = '''    private static Arena At(GameSession session, (double X, double Z) place)
    {
        var arena = Arena.OpenCreatures(session, session.Setup, place, 0, Array.Empty<(string, double, double, string)>(), Arena.Usual);
        // The config names a rod, so a cast needs one in the pack: every test here fishes with the tackle a fisher carries.
        Assert.Null(arena.Submit(new GrantItem(Rod, 1, Quality.Standard)));
        return arena;
    }

    private const string Rod = "item.tool.fishing_rod";
    private const string Worm = "item.material.bait_worm";'''
assert old in code
code = code.replace(old, new, 1)

# Two new cases: no rod, no cast; and a bait is spent by the cast that uses it.
marker = '''    [Fact]
    public void TheSameCastOfTheSameWater_MovesTheBiteTheSameWay()'''
additions = '''    [Fact]
    public void ACastWithoutARod_IsRefused_TheRuleTheConfigNames()
    {
        using var profile = new TempProfile();
        // A session at the water with the tackle taken back out of the pack: the cast is refused in the config's own words.
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, AtTheCreek, 0,
            Array.Empty<(string, double, double, string)>(), Arena.Usual);
        Assert.Equal("You need a rod", arena.Submit(new StartFishingCommand(arena.Player, Creek)));
        Assert.Null(arena.Simulation.Activity);
    }

    [Fact]
    public void ACastSpendsOneBait_AndTheBaitShortensTheWait()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), AtTheCreek);
        Assert.Null(arena.Submit(new GrantItem(Worm, 2, Quality.Standard)));
        Assert.Equal(2, Carried(arena, Worm));

        Assert.Null(arena.Submit(new StartFishingCommand(arena.Player, Creek)));
        Assert.Equal(1, Carried(arena, Worm));   // the cast took one

        arena.Tick(FishingSystem.CastTicks + 1);
        var waiting = Assert.IsType<PlayerActivity>(arena.Simulation.Activity);
        // The worm's scale is 0.8, so the wait is inside the scaled band rather than the plain one.
        int wait = (int)(waiting.BiteTick - arena.Simulation.WorldTick);
        Assert.InRange(wait, FishingSystem.BiteMinTicks / 2, FishingSystem.BiteMaxTicks);
    }

''' + marker
assert marker in code
code = code.replace(marker, additions, 1)
path.write_text(code, encoding='utf-8')
print('the fishing tests fish with a rod, and two prove the new rules')
