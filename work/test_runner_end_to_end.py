"""End-to-end test of the patched farm runner, isolated on BEAST: a fake farm root, two dummy jobs, one of which must time out.

The real runner is left alone. A copy of runner.ps1 is pointed at work\\farm_sim instead of the share, and given a dummy build whose exe is a
cmd script, so the whole loop runs - claim, mirror, start, poll, kill the tree, write TIMEOUT, move to the next job.
"""
import shutil
import subprocess
import time
from pathlib import Path

WT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')
SIM = WT / 'work' / 'farm_sim'
REAL = Path(r'C:\Otherreach_Farm\runner')

if SIM.exists():
    shutil.rmtree(SIM)
for sub in ('jobs', 'builds/test/game', 'results', 'status', 'runner', 'local'):
    (SIM / sub).mkdir(parents=True, exist_ok=True)

runner = (REAL / 'runner.ps1').read_text(encoding='utf-8')
runner = runner.replace("$farm = '\\\\BEAST\\OtherreachFarm'", f"$farm = '{SIM}'")
runner = runner.replace("$local = 'C:\\Otherreach_Test'", f"$local = '{SIM / 'local'}'")
runner = runner.replace("$ffmpeg = Join-Path $farm 'tools\\ffmpeg\\bin\\ffmpeg.exe'", "$ffmpeg = $null")
(SIM / 'runner' / 'runner.ps1').write_text(runner, encoding='utf-8')
shutil.copy2(REAL / 'jobprocess.ps1', SIM / 'runner' / 'jobprocess.ps1')

(SIM / 'builds' / 'test' / 'game' / 'slow.cmd').write_text('@ping -n 60 127.0.0.1 > nul\r\n', encoding='utf-8')
(SIM / 'builds' / 'test' / 'game' / 'hello.cmd').write_text('@echo hello from the dummy build\r\n@exit /b 3\r\n', encoding='utf-8')

def job(name, exe, timeout):
    import json
    (SIM / 'jobs' / f'{name}.json').write_text(json.dumps({
        "build": "test", "exe": f"game/{exe}", "args": [], "timeout_s": timeout, "record": False
    }, indent=2), encoding='utf-8')

job('a_fast', 'hello.cmd', 30)
job('b_slow', 'slow.cmd', 5)

proc = subprocess.Popen(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass',
                         '-File', str(SIM / 'runner' / 'runner.ps1')],
                        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
time.sleep(45)
proc.terminate()
try:
    proc.wait(timeout=10)
except subprocess.TimeoutExpired:
    proc.kill()

for name in ('a_fast', 'b_slow'):
    folder = SIM / 'results' / name
    files = sorted(p.name for p in folder.iterdir()) if folder.exists() else []
    print(f'{name}: {files}')
    marker = folder / 'TIMEOUT'
    if marker.exists():
        print('   ', marker.read_text(encoding='utf-8', errors='replace').strip())
    exit_file = folder / 'exit.txt'
    if exit_file.exists():
        print('    exit.txt =', exit_file.read_text(encoding='utf-8', errors='replace').strip())
    done = list((SIM / 'jobs').glob(f'{name}.done'))
    print('    marked done:', bool(done))
