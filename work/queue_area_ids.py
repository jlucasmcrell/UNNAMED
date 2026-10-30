"""Put the four new area spawn ids into the snapshot inserter's list, then run it (fauna in the Old Late road and the field)."""
import subprocess
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')
script = ROOT / 'work' / 'area_snapshot.py'
code = script.read_text(encoding='utf-8')
IDS = "'spawn.field.crows', 'spawn.field.hares', 'spawn.old_late.crows', 'spawn.old_late.hares',"
if 'spawn.old_late.crows' not in code:
    code = code.replace('NEW = [', 'NEW = [\n    ' + IDS, 1)
    script.write_text(code, encoding='utf-8')
print(subprocess.run(['C:\\Python314\\python.exe', '-X', 'utf8', str(script)], capture_output=True, text=True).stdout.strip())
