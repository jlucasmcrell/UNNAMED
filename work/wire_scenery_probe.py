"""Wire the scenery probe into the boot: after dressing and after the ravine, report any piece that stands off the drawn ground."""
from pathlib import Path

HOLLOW = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Greybox\HollowView.cs')
TERRAIN = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Greybox\Terrain3DView.cs')

hollow = HOLLOW.read_text(encoding='utf-8')
old = '            GD.Print($"UNNAMED dressing: {placed} props placed, {skipped} skipped");'
new = ('            GD.Print($"UNNAMED dressing: {placed} props placed, {skipped} skipped");\n'
       '            SceneryProbe.Report(this, DrawnGround(terrain), "dressing");')
if 'SceneryProbe.Report' not in hollow and old in hollow:
    HOLLOW.write_text(hollow.replace(old, new, 1), encoding='utf-8')
    print('dressing probe wired')
else:
    print('dressing probe: already wired or anchor missing')

terrain = TERRAIN.read_text(encoding='utf-8')
rock_old = '        GD.Print($"UNNAMED terrain3d: {rocks} rock pieces dress the north ravine");'
rock_new = ('        GD.Print($"UNNAMED terrain3d: {rocks} rock pieces dress the north ravine");\n'
            '        SceneryProbe.Report(parent, (x, z) => SceneryHeight(ground, x, z), "ravine");')
if 'SceneryProbe.Report' not in terrain and rock_old in terrain:
    TERRAIN.write_text(terrain.replace(rock_old, rock_new, 1), encoding='utf-8')
    print('ravine probe wired')
else:
    print('ravine probe: already wired or anchor missing')
