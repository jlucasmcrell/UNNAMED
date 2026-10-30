"""The tracking beat walks to the nearest animal instead of standing on a spawn centre (goal: tracking footage).

The re-shoot failed honestly and informatively: ten seconds of reads at the Charwood spawn returned "0 went 0 mdeg, 0 old" with no marks at all.
A read reaches six metres and sign lies where a creature has walked, so the beat now finds the nearest creature in the simulation and walks
toward it, reading as it goes - which is what a tracker does.
"""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.ActionShots.cs')
code = path.read_text(encoding='utf-8')

old = '''                // Walk in short stretches and read as we go: sign lies where the animals went, and a read reaches six metres.
                if ((_frame - _stepFrame) % 40 < 20)
                    Press("move_forward");'''
new = '''                // Walk toward the nearest animal and read as we go: sign lies where a creature has walked, and a read reaches six metres.
                var me = simulation.Player.Body;
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
assert old in code
path.write_text(code.replace(old, new, 1), encoding='utf-8')
print('the beat walks to the nearest animal')
