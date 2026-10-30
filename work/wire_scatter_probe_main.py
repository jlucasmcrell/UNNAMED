"""Run the instance probe once the scatter actually exists: it is built in Main after the scenery, not inside the terrain build."""
from pathlib import Path

PATH = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.cs')
text = PATH.read_text(encoding='utf-8')
OLD = '        AddChild(_scatter);'
NEW = ('        AddChild(_scatter);\n'
       '        // The boulder question needs a number, not a guess: these instance transforms are the only scenery nothing else reports on.\n'
       '        if (_ground is { } scatterGround)\n'
       '            SceneryProbe.ReportInstances(_scatter, (x, z) => Terrain3DView.SceneryHeight(scatterGround, x, z), "scatter");')
if OLD in text and 'ReportInstances' not in text:
    PATH.write_text(text.replace(OLD, NEW, 1), encoding='utf-8')
    print('scatter probe wired in Main')
else:
    print('already wired or anchor missing')
