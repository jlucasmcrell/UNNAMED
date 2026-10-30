"""Submit the cast so its refusal is recorded, then keep the beat's own bookkeeping (goal round 14)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.ActionShots.cs')
code = path.read_text(encoding='utf-8')
old = '''                Press("fish");
                _step = 1;
                _stepFrame = _frame;
                _stepNote = $"cast (N) at tick {tick}";'''
new = '''                if (_session.Submit(new StartFishingCommand(_session.Player, simulation.WaterKey!)) is { } refused)
                {
                    Fail($"the cast was refused: {refused}");
                    return;
                }
                _step = 1;
                _stepFrame = _frame;
                _stepNote = $"cast (N) at tick {tick}";'''
assert old in code
path.write_text(code.replace(old, new, 1), encoding='utf-8')
print('the beat submits the cast and reports its refusal')
