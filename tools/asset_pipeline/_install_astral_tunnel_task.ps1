# Keep the BEAST -> ASTRAL ComfyUI tunnel up without depending on an interactive session.
#
# A one-shot `ssh -N -L` dies on any connection reset. When it does, the ASTRAL builder fails
# every prompt in about three seconds and burns through its queue looking like it is still
# working. `_astral_tunnel.py` reconnects; this makes sure that loop starts again after a logon.
#
# A scheduled task would be the stronger option (it also covers a boot with nobody logged in),
# but Register-ScheduledTask needs elevation and this account cannot get it. The per-user Run key
# needs no elevation, so that is what this uses.
#
#     powershell -ExecutionPolicy Bypass -File _install_astral_tunnel_task.ps1
#     powershell -ExecutionPolicy Bypass -File _install_astral_tunnel_task.ps1 -Remove

param([switch]$Remove)

$ErrorActionPreference = 'Stop'

$python = 'C:\Python314\python.exe'
$script = 'W:\UNNAMED\tools\asset_pipeline\_astral_tunnel.py'
$log    = 'W:\UNNAMED\assets\astral_tunnel.log'
$key    = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$name   = 'ASTRAL_ComfyUI_tunnel'
$value  = "`"$python`" `"$script`" --log `"$log`""

if ($Remove) {
    Remove-ItemProperty -Path $key -Name $name -ErrorAction SilentlyContinue
    Write-Host "removed $name from the per-user Run key"
    return
}

if (-not (Test-Path $script)) { throw "missing $script" }
if (-not (Test-Path $python)) { throw "missing $python" }

New-ItemProperty -Path $key -Name $name -Value $value -PropertyType String -Force | Out-Null
Write-Host "registered $name under HKCU Run:"
Write-Host "  $value"
Write-Host ""
Write-Host "It starts at logon. Start it now as well with:"
Write-Host "  python _astral_tunnel.py --log '$log'"
