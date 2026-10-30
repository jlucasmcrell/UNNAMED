"""Wire the instance probe next to the node probe, so one run names both the placed scenery and the scatter."""
from pathlib import Path

PATH = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Greybox\Terrain3DView.cs')
text = PATH.read_text(encoding='utf-8')
OLD = '        SceneryProbe.Report(parent, (x, z) => SceneryHeight(ground, x, z), "ravine");'
NEW = ('        SceneryProbe.Report(parent, (x, z) => SceneryHeight(ground, x, z), "ravine");\n'
       '        SceneryProbe.ReportInstances(parent, (x, z) => SceneryHeight(ground, x, z), "scatter");')
if OLD in text and 'ReportInstances' not in text:
    PATH.write_text(text.replace(OLD, NEW, 1), encoding='utf-8')
    print('instance probe wired')
else:
    print('already wired or anchor missing')
