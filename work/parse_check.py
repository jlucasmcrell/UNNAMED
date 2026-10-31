"""Syntax-check the patched farm runner and its helper before anyone restarts the console."""
import subprocess
from pathlib import Path

CHECK = r"""
$files = @('C:\Otherreach_Farm\runner\runner.ps1', 'C:\Otherreach_Farm\runner\jobprocess.ps1')
foreach ($f in $files) {
    $tokens = $null
    $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$tokens, [ref]$errors)
    if ($errors.Count -eq 0) { Write-Output ("{0}: parses clean" -f (Split-Path $f -Leaf)) }
    else { foreach ($e in $errors | Select-Object -First 4) { Write-Output ("{0}: line {1}: {2}" -f (Split-Path $f -Leaf), $e.Extent.StartLineNumber, $e.Message) } }
}
"""

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\work\parse_check.ps1')
path.write_text(CHECK, encoding='utf-8')
done = subprocess.run(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(path)],
                      capture_output=True, text=True, timeout=120)
print(done.stdout.strip())
if done.returncode != 0:
    print('stderr:', done.stderr.strip()[:500])
