"""Grant items through the simulation, which is where GrantItem is a command the tests submit (goal round 9)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\tests\Application.Tests\FishingTests.cs')
code = path.read_text(encoding='utf-8')
code = code.replace('Assert.Null(arena.Submit(new GrantItem(Rod, 1, Quality.Standard)));',
                    'Assert.Null(arena.Simulation.Submit(new GrantItem(Rod, 1, Quality.Standard)));')
code = code.replace('Assert.Null(arena.Submit(new GrantItem(Worm, 2, Quality.Standard)));',
                    'Assert.Null(arena.Simulation.Submit(new GrantItem(Worm, 2, Quality.Standard)));')
path.write_text(code, encoding='utf-8')
print('grants go through the simulation')
