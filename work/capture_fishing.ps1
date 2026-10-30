# In-game capture of the fishing cast: the game's own action-shots beat, under a GPU lease (goal round 4).
param([string]$Out = 'F:\otherreach_captures\fishing_ingame')
$ErrorActionPreference = 'Continue'
$wt = 'D:\Otherreach_Scratch\ds_fish2_wt'
$lock = 'C:\Otherreach_Recovery\gpu_lock.ps1'
$log = Join-Path $wt 'work\fish_capture.log'
function Say($t) { $line = "$(Get-Date -Format 'HH:mm:ss') $t"; Write-Host $line; Add-Content -Path $log -Value $line }
New-Item -ItemType Directory -Force -Path $Out | Out-Null
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $lock acquire 'claude fauna fishing capture'
if ($LASTEXITCODE -ne 0) { Say 'gpu busy'; exit 1 }
Say 'lease acquired'
try {
    $exe = 'C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64.exe'
    $arguments = @('--rendering-driver', 'vulkan', '--path', (Join-Path $wt 'src\Presentation'),
        '--resolution', '1600x900', '--windowed', '--',
        '--content-root', (Join-Path $wt 'content'),
        '--bindings-overlay', (Join-Path $wt 'content\_art_bindings.json'),
        '--asset-root', 'D:\Otherreach_Shared\art_root',
        '--asset-fallback', (Join-Path $wt 'work\creator\game\world_fallback'),
        '--shot-actions', $Out,
        '--stand', '492.6,748.4',
        '--start-items', 'item.tool.fishing_rod:1,item.material.bait_worm:3',
        '--shot-beat', 'fish',
        '--visual', 'terrain=terrain3d,sky=sky3d,people=humgen,hour=10,tier=high,weather=fair')
    $env:OTHERREACH_CHARACTER_ROOT = (Join-Path $wt 'work\creator\game\humgen_characters')
    $env:OTHERREACH_LIGHTING_FAST = '1'
    Say "run: $($arguments -join ' ')"
    $game = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $Out 'game.log') -RedirectStandardError (Join-Path $Out 'game.stderr')
    $deadline = (Get-Date).AddMinutes(9)
    while (-not $game.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 5; $game.Refresh() }
    if (-not $game.HasExited) { Say 'killing at the lease limit'; $game.Kill() }
    Say "exit=$($game.ExitCode)"
}
finally {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $lock release 'claude fauna fishing capture' | Out-Null
    Say 'lease released'
}
Say "frames: $((Get-ChildItem $Out -Filter '*.png' -ErrorAction SilentlyContinue | Measure-Object).Count)"
