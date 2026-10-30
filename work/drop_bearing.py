"""The bearing is reported, not stored: the walk follows the camera's forward (goal round 18)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.ActionShots.cs')
code = path.read_text(encoding='utf-8')
code = code.replace('                    _bearing = read.BearingMdeg;\n', '')
code = code.replace('''        /// <summary>The bearing the first read gave, and the way the beat walks.</summary>
        private int _bearing;
''', '')
path.write_text(code, encoding='utf-8')
print('the unused bearing field is gone')
