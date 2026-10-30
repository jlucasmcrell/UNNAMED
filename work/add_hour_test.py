"""Prove the third rule too: the catch comes from the table the hour and the water's kind schedule (goal round 10)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\tests\Application.Tests\FishingTests.cs')
code = path.read_text(encoding='utf-8')

marker = '''    [Fact]
    public void AReelInTheBiteWindow_TakesTheWatersCatch_AndTrainsFishing()'''
addition = '''    [Fact]
    public void ACatch_ComesFromTheTableTheHourSchedules()
    {
        using var profile = new TempProfile();
        var arena = At(Harness.Boot(profile), AtTheCreek);
        var waiting = Waiting(arena, Creek);

        // The hour the cast began in names the table; the creek is scheduled differently by night than by day.
        var rules = arena.Simulation.Setup.Fishing;
        Assert.Equal("item.tool.fishing_rod", rules.RodItemId);
        var clock = arena.Simulation.Setup.Clock;
        double hour = clock.Hour(waiting.StartedTick) % clock.HoursPerDay;
        string? scheduled = rules.TableFor("creek", hour);
        Assert.NotNull(scheduled);
        Assert.True(arena.Simulation.Setup.Items.LootTables.ContainsKey(scheduled!));

        // Whatever the hour gives, a catch is an item that table holds.
        var caught = arena.Record<FishCaught>();
        arena.Tick((int)(waiting.BiteTick - arena.Simulation.WorldTick));
        Assert.Null(arena.Submit(new ReelInCommand(arena.Player)));
        var take = Assert.Single(caught);
        var entries = arena.Simulation.Setup.Items.LootTables[scheduled!];
        Assert.Contains(entries.Entries, entry => entry.ItemId == take.ItemId || entries.Entries.Any(e => e.ItemId == take.ItemId));
    }

''' + marker
assert marker in code
code = code.replace(marker, addition, 1)
path.write_text(code, encoding='utf-8')
print('the hour-scheduled table is asserted')
