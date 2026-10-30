"""The fish key only casts when the camera's focus is water, which a fixed capture camera never is (goal round 14).

Main.Input's Fish() submits the cast only when `_controller.FocusOn(_camera)` resolves to water. The beat's camera is fixed at yaw 90 and
pitch -0.28, so the key was a no-op and the beat reported 'the cast was refused' with a bank, a rod and bait all in order. The beat now
submits the same command the key submits, and if the activity still does not start it says what the simulation shows.
"""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.ActionShots.cs')
code = path.read_text(encoding='utf-8')

old = '''                Press("fish");
                _step = 1;
                _stepFrame = _frame;
                _stepNote = $"cast (N) at tick {tick}";
                return;'''
new = '''                // The key casts only when the camera's focus is water; a capture camera is fixed, so the beat submits what the key submits.
                _session.Submit(new StartFishingCommand(simulation.PlayerId, simulation.WaterKey!));
                _step = 1;
                _stepFrame = _frame;
                _stepNote = $"cast (N) at tick {tick}";
                return;'''
assert old in code
code = code.replace(old, new, 1)

old_fail = '''            if (_step == 1)
            {
                if (activity is { Kind: ActivityKind.FishCast } && _frame - _stepFrame == 14)'''
new_fail = '''            if (_step == 1)
            {
                if (activity is null && _frame - _stepFrame > 20)
                {
                    Fail($"the cast did not start: water '{simulation.WaterKey ?? "none"}', can fish {simulation.CanFish}");
                    return;
                }
                if (activity is { Kind: ActivityKind.FishCast } && _frame - _stepFrame == 14)'''
assert old_fail in code
code = code.replace(old_fail, new_fail, 1)
path.write_text(code, encoding='utf-8')
print('the beat casts what the key casts, and says when it does not start')
