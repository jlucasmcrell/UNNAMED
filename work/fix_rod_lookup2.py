"""Point the rod gate and the bait at the character's inventory (goal round 13)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\World\Runtime\Fishing.cs')
code = path.read_text(encoding='utf-8')

code = code.replace('''    private bool Carries(string definitionId) =>
        _pack().Containers.Any(c => c.Stacks.Any(s => s.DefId == definitionId))
        || _pack().Unplaced.Any(s => s.DefId == definitionId);''',
'''    private bool Carries(string definitionId) =>
        State.Player.Inventory.Any(entry => entry.DefId == definitionId && entry.Count > 0);''', 1)

code = code.replace('''        foreach (var container in _pack().Containers)
            foreach (var stack in container.Stacks)
                if (Rules.Baits.TryGetValue(stack.DefId, out var bait))''',
'''        foreach (var entry in State.Player.Inventory)
            if (entry.Count > 0 && Rules.Baits.TryGetValue(entry.DefId, out var bait))''', 1)

code = code.replace('new StackTake(stack.ItemId, 1)', 'new StackTake(entry.ItemId, 1)', 1)
path.write_text(code, encoding='utf-8')
print('gate and bait read the inventory')
