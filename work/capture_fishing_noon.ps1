# Two in-game casts under one lease: the creek at midday, and the millpond at midday, judged on light (goal round 14).
param([string]$Out = 'F:\otherreach_captures')
$ErrorActionPreference = 'Continue'
$wt = 'D:\Otherreach_Scratch\ds_fish2_wt'
$lock = 'C:\Otherreach_Recovery\gpu_lock.ps1'
$log = Join-Path $wt 'work\fish_capture_b.log'
function Say($t) { $line = "$(Get-Date -Format 'HH:mm:ss') $t"; Write-Host $line; Add-Content -Path $log -Value $line }
$exe = 'C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64.exe'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $lock acquire 'claude fauna fishing capture noon'
if ($LASTEXITCODE -ne 0) { Say 'gpu busy'; exit 1 }
Say 'lease acquired'
try {
    foreach ($run in @(
        @{ dir = Join-Path $Out 'fishing_creek_noon'; stand = '492.6,748.4' },
        @{ dir = Join-Path $Out 'fishing_pond_noon'; stand = '958.2,700.4' })) {
        New-Item -ItemType Directory -Force -Path $run.dir | Out-Null
        $arguments = @('--rendering-driver', 'vulkan', '--path', (Join-Path $wt 'src\Presentation'),
            '--resolution', '1920x1080', '--windowed', '--',
            '--content-root', (Join-Path $wt 'content'),
            '--bindings-overlay', (Join-Path $wt 'content\_art_bindings.json'),
            '--asset-root', 'D:\Otherreach_Scratch\art_merged',
            '--asset-fallback', (Join-Path $wt 'work\creator\game\world_fallback'),
            '--shot-actions', $run.dir,
            '--stand', $run.stand,
            '--start-items', 'item.tool.fishing_rod:1,item.material.bait_worm:3',
            '--shot-beat', 'fish',
            '--visual', 'terrain=terrain3d,sky=sky3d,people=humgen,hour=13,tier=high')
        $env:OTHERREACH_CHARACTER_ROOT = (Join-Path $wt 'work\creator\game\humgen_characters')
        $env:OTHERREACH_LIGHTING_FAST = '0'
        Say "run: $($run.dir)"
        $game = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $run.dir 'game.log') -RedirectStandardError (Join-Path $run.dir 'game.stderr')
        $deadline = (Get-Date).AddMinutes(4)
        while (-not $game.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 4; $game.Refresh() }
        if (-not $game.HasExited) { Say 'killing at the per-run limit'; $game.Kill() }
        Say "frames in $($run.dir): $((Get-ChildItem $run.dir -Filter '*.png' -ErrorAction SilentlyContinue | Measure-Object).Count)"
    }
}
finally {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $lock release 'claude fauna fishing capture noon' | Out-Null
    Say 'lease released'
}
