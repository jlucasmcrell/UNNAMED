"""A held forward key must reach the stick, or a beat that walks stands still (tracking footage).

The action capture passed Vector3.Zero to SteerWorld after reading the keys, so every movement press the beat made was thrown away - the tracking
run reported walking to a doe 26 m off and never left 612,718. The drive is now the camera's own forward while move_forward is held, which is the
look the beat sets earlier in the same frame.
"""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.ActionShots.cs')
code = path.read_text(encoding='utf-8')
old = '            _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera, faceCamera: true);'
new = ('            // The beat presses the game\'s own movement keys, so a held forward key has to reach the stick or a beat that walks would\n'
       '            // stand still: the drive is the camera\'s own forward, which is the look this frame already set.\n'
       '            var drive = Input.IsActionPressed("move_forward")\n'
       '                ? new Vector3(-Mathf.Sin(_camera.Yaw), 0f, -Mathf.Cos(_camera.Yaw))\n'
       '                : Vector3.Zero;\n'
       '            _controller.SteerWorld(drive, Gait.Run, _camera, faceCamera: true);')
assert old in code
path.write_text(code.replace(old, new, 1), encoding='utf-8')
print('the beat can walk')
