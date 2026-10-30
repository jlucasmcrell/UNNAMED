"""The fishing beat gives itself the tackle, and says why a cast was refused (goal round 10).

The capture ran twice under a real lease and both runs failed honestly: first 'not at a fishable bank' (no stand point), now 'the cast was
refused' - and the refusal's own words were not in the report, which is why it took a second run to learn nothing. The beat now grants the
rod and a bait the config names, and reports the refusal text.
"""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.ActionShots.cs')
code = path.read_text(encoding='utf-8')

old = '''                if (!simulation.CanFish)
                {
                    Fail($"not at a fishable bank: water '{simulation.WaterKey ?? "none"}'");
                    return;
                }
                Press("fish");'''
new = '''                if (!simulation.CanFish)
                {
                    Fail($"not at a fishable bank: water '{simulation.WaterKey ?? "none"}'");
                    return;
                }
                // The pack has to carry what the config names: a rod to cast and a bait to shorten the wait this beat waits on.
                if (simulation.Setup.Fishing.RodItemId is { } rod && simulation.Player.Inventory.All(e => e.DefId != rod))
                {
                    _session.Submit(new GrantItem(rod, 1, Domain.Items.Quality.Standard));
                    _moments.Add($"- granted {rod}");
                }
                foreach (string bait in simulation.Setup.Fishing.Baits.Keys)
                    if (simulation.Player.Inventory.All(e => e.DefId != bait))
                    {
                        _session.Submit(new GrantItem(bait, 3, Domain.Items.Quality.Standard));
                        _moments.Add($"- granted 3 x {bait}");
                    }
                if (simulation.Submit(new StartFishingCommand(simulation.Player, simulation.WaterKey!)) is { } refused)
                {
                    Fail($"the cast was refused: {refused}");
                    return;
                }
                _step = 1;
                _stepFrame = _frame;
                _stepNote = $"cast at tick {tick}";
                return;
                Press("fish");'''
assert old in code
path.write_text(code.replace(old, new, 1), encoding='utf-8')
print('the beat grants its tackle and reports the refusal')
