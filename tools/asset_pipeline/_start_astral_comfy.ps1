# Start ComfyUI_LTX25 on ASTRAL detached from any SSH session.
#
# Launching the .bat directly over ssh does not survive: the script ends with `pause`, and the
# whole process tree is torn down when the SSH session closes. A scheduled task runs under the
# Task Scheduler service instead, so the server keeps running after I disconnect.
$install = 'G:\ComfyUI_LTX25'
$bat = Join-Path $install 'run_LTX25_port8190.bat'
$taskName = 'ComfyUI_LTX25_autostart'

Write-Output ("launcher exists: " + (Test-Path $bat))

# Remove any previous instance of the task so this is repeatable.
schtasks /Delete /TN $taskName /F 2>&1 | Out-Null

# /IT would require an interactive session; run whether logged on or not instead.
$action = "cmd.exe /c `"$bat`""
schtasks /Create /TN $taskName /TR $action /SC ONCE /ST 00:00 /RL HIGHEST /F 2>&1 |
    ForEach-Object { Write-Output ("  create: " + $_) }

schtasks /Run /TN $taskName 2>&1 | ForEach-Object { Write-Output ("  run: " + $_) }

Write-Output "waiting for the server to bind 8190..."
for ($i = 1; $i -le 18; $i++) {
    Start-Sleep -Seconds 10
    $c = Get-NetTCPConnection -State Listen -LocalPort 8190 -ErrorAction SilentlyContinue
    if ($c) {
        Write-Output ("  UP after " + ($i * 10) + "s, pid=" + $c[0].OwningProcess)
        break
    }
    if ($i -eq 18) { Write-Output "  still not listening after 180s" }
}

Write-Output "--- final check ---"
$c = Get-NetTCPConnection -State Listen -LocalPort 8190 -ErrorAction SilentlyContinue
if ($c) { Write-Output ("  listening pid=" + $c[0].OwningProcess) } else { Write-Output "  nothing listening" }
