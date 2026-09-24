# Report ASTRAL's ComfyUI state: listening port, processes, and recent log activity.
Write-Output "--- port 8190 ---"
$c = Get-NetTCPConnection -State Listen -LocalPort 8190 -ErrorAction SilentlyContinue
if ($c) { Write-Output ("  listening pid=" + $c[0].OwningProcess) } else { Write-Output "  nothing listening" }

Write-Output "--- python processes referencing ComfyUI or LTX25 ---"
$procs = Get-CimInstance Win32_Process -Filter "Name='python.exe'" |
    Where-Object { $_.CommandLine -match 'LTX25' -or $_.CommandLine -match 'ComfyUI.main.py' -or $_.CommandLine -match 'main\.py' }
if (-not $procs) { Write-Output "  none" }
foreach ($p in $procs) {
    $line = $p.CommandLine
    if ($line.Length -gt 120) { $line = $line.Substring(0, 120) }
    Write-Output ("  pid=" + $p.ProcessId + " :: " + $line)
}

Write-Output "--- log tail ---"
$log = 'G:\ComfyUI_LTX25\ComfyUI\user\comfyui_8190.log'
if (Test-Path $log) {
    $age = [math]::Round(((Get-Date) - (Get-Item $log).LastWriteTime).TotalSeconds)
    Write-Output ("  log idle " + $age + "s")
    Get-Content $log -Tail 6 | ForEach-Object { if ($_.Trim()) { Write-Output ("  " + $_.Trim().Substring(0, [Math]::Min(120, $_.Trim().Length))) } }
} else { Write-Output "  no log" }

Write-Output "--- available memory ---"
Write-Output ("  free GB: " + [math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB, 1))
