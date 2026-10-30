# In-game tracking capture: stand where the Charwood deer walk, read the ground, walk the bearing, read again (goal round 17).
param([string]$Out = 'F:\otherreach_captures\tracking_ingame')
$ErrorActionPreference = 'Continue'
$wt = 'D:\Otherreach_Scratch\ds_fish2_wt'
$lock = 'C:\Otherreach_Recovery\gpu_lock.ps1'
$log = Join-Path $wt 'work\track_capture.log'
function Say($t) { $line = "$(Get-Date -Format 'HH:mm:ss') $t"; Write-Host $line; Add-Content -Path $log -Value $line }
New-Item -ItemType Directory -Force -Path $Out | Out-Null
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $lock acquire 'claude fauna tracking capture'
if ($LASTEXITCODE -ne 0) { Say 'gpu busy'; exit 1 }
Say 'lease acquired'
try {
    $exe = 'C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64.exe'
    $visual = 'terrain=terrain3d,sky=sky3d,people=humgen,foldscar=proof,body=none,hour=8,tier=high,weather=fair'
    $arguments = @('--rendering-driver', 'vulkan', '--path', (Join-Path $wt 'src\Presentation'),
        '--resolution', '1600x900', '--windowed', '--',
        '--region', 'region.starting_frontier',
        '--content-root', (Join-Path $wt 'content'),
        '--bindings-overlay', (Join-Path $wt 'content\_art_bindings.json'),
        '--asset-root', 'D:\Otherreach_Shared\art_root',
        '--asset-fallback', (Join-Path $wt 'work\creator\game\world_fallback'),
        '--shot-actions', $Out,
        '--stand', '612,718',
        '--start-skills', 'skill.tracking:15,insight:12',
        '--shot-beat', 'track',
        '--visual', $visual)
    $env:OTHERREACH_CHARACTER_ROOT = (Join-Path $wt 'work\creator\game\humgen_characters')
    Say "tracking beat at the Charwood deer ground"
    $game = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Normal -PassThru `
        -RedirectStandardOutput (Join-Path $Out 'game.log') -RedirectStandardError (Join-Path $Out 'game.stderr')
    $deadline = (Get-Date).AddMinutes(7)
    while (-not $game.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 4; $game.Refresh() }
    if (-not $game.HasExited) { Say 'killing at the limit'; $game.Kill() }
    Say "frames: $((Get-ChildItem $Out -Filter '*.png' -ErrorAction SilentlyContinue | Measure-Object).Count)"
    Get-Content (Join-Path $Out 'report.md') -Tail 12 -ErrorAction SilentlyContinue | ForEach-Object { Say $_ }
}
finally {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $lock release 'claude fauna tracking capture' | Out-Null
    Say 'lease released'
}
