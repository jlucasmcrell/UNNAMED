"""Walk to the quarry with the camera the beat owns, which the steer call downstream already reads (tracking footage)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.ActionShots.cs')
code = path.read_text(encoding='utf-8')
old = '''                var me = simulation.Player.Body;
                var quarry = simulation.Creatures
                    .Where(c => c.Condition != CreatureCondition.Defeated)
                    .OrderBy(c => Math.Pow(c.Body.XMm - me.XMm, 2) + Math.Pow(c.Body.ZMm - me.ZMm, 2))
                    .FirstOrDefault();
                if (quarry is not null)
                {
                    double dx = quarry.Body.XMm - me.XMm, dz = quarry.Body.ZMm - me.ZMm;
                    // The view direction is (-sin yaw, -cos yaw), so the yaw that faces the animal is atan2(-dx, -dz).
                    _yaw = (float)Math.Atan2(-dx, -dz);
                    _pitch = -0.30f;
                    _moments.Add($"- tick {simulation.WorldTick}: walking to {quarry.DefId} {Math.Sqrt(dx * dx + dz * dz) / 1000.0:0.0} m off");
                }
                Press("move_forward");'''
new = '''                var me = simulation.Player.Body;
                var quarry = simulation.Creatures
                    .OrderBy(c => Math.Pow(c.Body.XMm - me.XMm, 2) + Math.Pow(c.Body.ZMm - me.ZMm, 2))
                    .FirstOrDefault();
                if (quarry is not null)
                {
                    double dx = quarry.Body.XMm - me.XMm, dz = quarry.Body.ZMm - me.ZMm;
                    // The view direction is (-sin yaw, -cos yaw), so the yaw that faces the animal is atan2(-dx, -dz). The camera set
                    // here is the one the steer call downstream reads, so the walk goes where the beat looks.
                    _camera.Yaw = (float)Math.Atan2(-dx, -dz);
                    _camera.Pitch = -0.30f;
                    if ((_frame - _stepFrame) % 40 == 0)
                        _moments.Add($"- tick {simulation.WorldTick}: walking to {quarry.DefId} {Math.Sqrt(dx * dx + dz * dz) / 1000.0:0.0} m off");
                }
                Press("move_forward");'''
assert old in code
path.write_text(code.replace(old, new, 1), encoding='utf-8')
print('the beat walks to the quarry')
