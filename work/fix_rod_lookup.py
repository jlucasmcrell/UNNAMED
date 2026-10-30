"""Carrying a rod is about the character's inventory, not the pack view's placed stacks (goal round 13).

The capture's report was decisive: 'bank ok, rod carried, baits [worm], activity none' and then 'the cast was refused'. The beat reads
`Player.Inventory`, but the rod gate read the pack view (`_inventory.Pack()`), whose containers hold *placed* stacks - and a fresh
character's starting items are not placed yet. The gate and the bait therefore looked at the wrong list, and every fresh character was
refused a cast while clearly carrying a rod.
"""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\World\Runtime\Fishing.cs')
code = path.read_text(encoding='utf-8')

old_carries = '''    /// <summary>True when the pack holds <paramref name="definitionId"/> in any stack, placed or not.</summary>
    private bool Carries(string definitionId) =>
        _pack().Containers.Any(c => c.Stacks.Any(s => s.DefId == definitionId))
        || _pack().Unplaced.Any(s => s.DefId == definitionId);'''
new_carries = '''    /// <summary>True when the character carries <paramref name="definitionId"/> at all, however the pack has it arranged.</summary>
    private bool Carries(string definitionId) =>
        State.Player.Inventory.Any(entry => entry.DefId == definitionId && entry.Count > 0);'''
assert old_carries in code
code = code.replace(old_carries, new_carries, 1)

old_bait = '''        foreach (var container in State.Pack.Containers)
            foreach (var stack in container.Stacks)
                if (Rules.Baits.TryGetValue(stack.DefId, out var bait))'''
new_bait = '''        foreach (var entry in State.Player.Inventory)
            if (entry.Count > 0 && Rules.Baits.TryGetValue(entry.DefId, out var bait))'''
assert old_bait in code
code = code.replace(old_bait, new_bait, 1)

old_take = '''                    _context.Dispatch(new ExchangeItems(ImmutableArray.Create(new StackTake(stack.ItemId, 1)), null, 0, Quality.Standard));'''
new_take = '''                    _context.Dispatch(new ExchangeItems(ImmutableArray.Create(new StackTake(entry.ItemId, 1)), null, 0, Quality.Standard));'''
assert old_take in code
code = code.replace(old_take, new_take, 1)
path.write_text(code, encoding='utf-8')
print('the rod gate and the bait read the inventory')
