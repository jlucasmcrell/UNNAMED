"""Read the character's inventory through State, and drop the pack view the gate no longer needs (goal round 13)."""
from pathlib import Path

root = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')
fish = root / 'src' / 'World' / 'Runtime' / 'Fishing.cs'
code = fish.read_text(encoding='utf-8')
code = code.replace('State.Player.Inventory', 'State.Inventory')
code = code.replace('''    private readonly Func<PackView> _pack;

''', '')
code = code.replace(',Func<PackView> pack)', ')')
code = code.replace('''        _pack=pack;
''', '')
fish.write_text(code, encoding='utf-8')

sim = root / 'src' / 'World' / 'Runtime' / 'Simulation.cs'
code = sim.read_text(encoding='utf-8')
code = code.replace('_fishing = new FishingSystem(_context,activity,player.Id,_inventory.Pack);',
                    '_fishing = new FishingSystem(_context,activity,player.Id);')
sim.write_text(code, encoding='utf-8')
print('the rod gate reads State.Inventory; the pack view is gone')
