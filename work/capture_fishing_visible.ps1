# The frames came out black with the game's window hidden; a hidden window is never presented, so nothing is drawn into the 3D viewport
# while the 2D HUD still reads fine. Same capture, visible window (goal round 15).
param([string]$Out = 'F:\otherreach_captures\fishing_visible')
$ErrorActionPreference = 'Continue'
$wt = 'D:\Otherreach_Scratch\ds_fish2_wt'
$lock = 'C:\Otherreach_Recovery\gpu_lock.ps1'
$log = Join-Path $wt 'work\fish_capture_visible.log'
function Say($t) { $line = "$(Get-Date -Format 'HH:mm:ss') $t"; Write-Host $line; Add-Content -Path $log -Value $line }
New-Item -ItemType Directory -Force -Path $Out | Out-Null
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $lock acquire 'claude fauna fishing visible'
if ($LASTEXITCODE -ne 0) { Say 'gpu busy'; exit 1 }
Say 'lease acquired'
try {
    $exe = 'C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64.exe'
    $arguments = @('--rendering-driver', 'vulkan', '--path', (Join-Path $wt 'src\Presentation'),
        '--resolution', '1600x900', '--windowed', '--position', '40,40', '--',
        '--content-root', (Join-Path $wt 'content'),
        '--bindings-overlay', (Join-Path $wt 'content\_art_bindings.json'),
        '--asset-root', 'D:\Otherreach_Scratch\art_merged',
        '--asset-fallback', (Join-Path $wt 'work\creator\game\world_fallback'),
        '--shot-actions', $Out,
        '--stand', '492.6,748.4',
        '--start-items', 'item.tool.fishing_rod:1,item.material.bait_worm:3',
        '--shot-beat', 'fish',
        '--visual', 'terrain=terrain3d,sky=sky3d,people=humgen,hour=13,tier=high')
    $env:OTHERREACH_CHARACTER_ROOT = (Join-Path $wt 'work\creator\game\humgen_characters')
    $game = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Normal -PassThru `
        -RedirectStandardOutput (Join-Path $Out 'game.log') -RedirectStandardError (Join-Path $Out 'game.stderr')
    $deadline = (Get-Date).AddMinutes(6)
    while (-not $game.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 4; $game.Refresh() }
    if (-not $game.HasExited) { Say 'killing at the limit'; $game.Kill() }
    Say "frames: $((Get-ChildItem $Out -Filter '*.png' -ErrorAction SilentlyContinue | Measure-Object).Count)"
}
finally {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $lock release 'claude fauna fishing visible' | Out-Null
    Say 'lease released'
}
