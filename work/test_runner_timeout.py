"""Test the new job-process wait against dummy children, without touching the farm's live runner.

1. a child that finishes quickly reports TimedOut=false and a real exit code, and its output reaches the log files;
2. a child that never finishes is killed at the deadline, reports TimedOut=true, writes the TIMEOUT marker, and returns promptly.
"""
import subprocess
from pathlib import Path

OUT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\work\runner_test')
OUT.mkdir(parents=True, exist_ok=True)

SCRIPT_TEMPLATE = r"""
. 'C:\Otherreach_Farm\runner\jobprocess.ps1'
$ok = Join-Path '@OUT@' 'quick'
New-Item -ItemType Directory -Force $ok | Out-Null
$p1 = Start-JobProcess -Exe 'cmd.exe' -Arguments @('/c', 'echo hello from the child & echo warning line 1>&2 & exit 7') `
    -WorkingDirectory '@OUT@' -StdOutPath (Join-Path $ok 'stdout.log') -StdErrPath (Join-Path $ok 'stderr.log')
$r1 = Wait-JobProcess -Process $p1 -TimeoutSeconds 20 -TimeoutMarkerPath (Join-Path $ok 'TIMEOUT')
Write-Output ("quick timedout={0} exit={1} marker={2}" -f $r1.TimedOut, $r1.ExitCode, (Test-Path (Join-Path $ok 'TIMEOUT')))

$slow = Join-Path '@OUT@' 'slow'
New-Item -ItemType Directory -Force $slow | Out-Null
$started = Get-Date
$p2 = Start-JobProcess -Exe 'cmd.exe' -Arguments @('/c', 'ping -n 120 127.0.0.1 > nul') `
    -WorkingDirectory '@OUT@' -StdOutPath (Join-Path $slow 'stdout.log') -StdErrPath (Join-Path $slow 'stderr.log')
$r2 = Wait-JobProcess -Process $p2 -TimeoutSeconds 5 -TimeoutMarkerPath (Join-Path $slow 'TIMEOUT')
$elapsed = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
Write-Output ("slow timedout={0} elapsed={1}s marker={2}" -f $r2.TimedOut, $elapsed, (Test-Path (Join-Path $slow 'TIMEOUT')))
"""

script_path = OUT / 'run_test.ps1'
script_path.write_text(SCRIPT_TEMPLATE.replace('@OUT@', str(OUT)), encoding='utf-8')

completed = subprocess.run(
    ['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(script_path)],
    capture_output=True, text=True, timeout=180)
print(completed.stdout.strip() or '(no stdout)')
if completed.returncode != 0:
    print('stderr:', completed.stderr.strip()[:800])

quick_log = OUT / 'quick' / 'stdout.log'
slow_marker = OUT / 'slow' / 'TIMEOUT'
print('quick stdout has the child output:', quick_log.exists() and 'hello from the child' in quick_log.read_text(encoding='utf-8', errors='replace'))
print('slow marker:', slow_marker.read_text(encoding='utf-8', errors='replace').strip() if slow_marker.exists() else 'MISSING')
