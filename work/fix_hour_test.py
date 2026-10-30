"""The table holds its entries in Weighted/Independent/Guaranteed, not one Entries list (goal round 10)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\tests\Application.Tests\FishingTests.cs')
code = path.read_text(encoding='utf-8')
old = '''        var entries = arena.Simulation.Setup.Items.LootTables[scheduled!];
        Assert.Contains(entries.Entries, entry => entry.ItemId == take.ItemId || entries.Entries.Any(e => e.ItemId == take.ItemId));'''
new = '''        var table = arena.Simulation.Setup.Items.LootTables[scheduled!];
        var offered = table.Weighted.Concat(table.Independent).Concat(table.Guaranteed).Select(entry => entry.ItemId);
        Assert.Contains(take.ItemId, offered);'''
assert old in code
path.write_text(code.replace(old, new, 1), encoding='utf-8')
print('the table check reads its own entry lists')
