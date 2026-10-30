"""Queue the five full-scale spawn ids for the snapshot inserter and run it (fauna at full scale)."""
import subprocess
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')
script = ROOT / 'work' / 'snapshot_fauna.py'
code = script.read_text(encoding='utf-8')
IDS = "'spawn.cairnwold.moor_west_elk', 'spawn.cairnwold.millpond_deer', 'spawn.cairnwold.millpond_ducks', 'spawn.hollow.creek_trout', 'spawn.hollow.hollow_crows',"
if 'moor_west_elk' not in code:
    code = code.replace('NEW = [', 'NEW = [\n    ' + IDS, 1)
    script.write_text(code, encoding='utf-8')
print(subprocess.run(['C:\\Python314\\python.exe', '-X', 'utf8', str(script)], capture_output=True, text=True).stdout.strip())
