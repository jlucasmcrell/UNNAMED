# Restart the ComfyUI_LTX25 server on ASTRAL so it reloads extra_model_paths.yaml.
# Written as a file because inline PowerShell through ssh/cmd mangles $_.

$ErrorActionPreference = 'Continue'
$install = 'G:\ComfyUI_LTX25'

Write-Output "--- current ComfyUI processes ---"
$procs = Get-CimInstance Win32_Process -Filter "Name='python.exe'" |
    Where-Object { $_.CommandLine -match 'ComfyUI_LTX25' -or $_.CommandLine -match 'main\.py' }
foreach ($p in $procs) {
    $line = $p.CommandLine
    if ($line.Length -gt 130) { $line = $line.Substring(0, 130) }
    Write-Output ("  pid=" + $p.ProcessId + " :: " + $line)
}

if ($procs) {
    Write-Output "--- stopping ---"
    foreach ($p in $procs) {
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
        Write-Output ("  stopped " + $p.ProcessId)
    }
    Start-Sleep -Seconds 6
}

Write-Output "--- verifying port 8190 is free ---"
$busy = Get-NetTCPConnection -State Listen -LocalPort 8190 -ErrorAction SilentlyContinue
if ($busy) { Write-Output "  STILL LISTENING" } else { Write-Output "  free" }

Write-Output "--- relaunching ---"
$bat = Join-Path $install 'run_LTX25_port8190.bat'
Write-Output ("  launcher: " + $bat + " exists=" + (Test-Path $bat))
Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', $bat -WorkingDirectory $install -WindowStyle Hidden
Start-Sleep -Seconds 12

Write-Output "--- port after relaunch ---"
$up = Get-NetTCPConnection -State Listen -LocalPort 8190 -ErrorAction SilentlyContinue
if ($up) { Write-Output ("  listening pid=" + $up[0].OwningProcess) } else { Write-Output "  not up yet" }
