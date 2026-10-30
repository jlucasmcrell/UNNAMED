"""Cast the way the key does, and report the refusal's own words when it is refused (goal round 14).

The press alone tells the report nothing about why: the key routes to StartFishingCommand and its reason is dropped. Submitting the same
command from the beat keeps the beat honest and puts the simulation's own sentence in the report.
"""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.ActionShots.cs')
code = path.read_text(encoding='utf-8')

old = '''                Press("fish");
                _step = 1;
                _stepFrame = _frame;
                _stepNote = $"cast at tick {tick}";'''
new = '''                if (_session.Submit(new StartFishingCommand(_session.Player, simulation.WaterKey!)) is { } refused)
                {
                    Fail($"the cast was refused: {refused}");
                    return;
                }
                _step = 1;
                _stepFrame = _frame;
                _stepNote = $"cast at tick {tick}";'''
assert old in code
path.write_text(code.replace(old, new, 1), encoding='utf-8')
print('the beat submits the cast and reports its refusal')
