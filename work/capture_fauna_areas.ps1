# Fauna camera pass: stand and look at a place where the animals now live, for a few seconds, and keep the frame (region fauna at full scale).
# One lease, one batch of places. `--shot` steers the character and takes the picture, so this is the cheapest way to film a herd.
param([string]$Out = 'F:\otherreach_captures\fauna_areas', [string]$Batch = 'frontier')
$ErrorActionPreference = 'Continue'
$wt = 'D:\Otherreach_Scratch\ds_fish2_wt'
$lock = 'C:\Otherreach_Recovery\gpu_lock.ps1'
$log = Join-Path $wt 'work\fauna_areas.log'
function Say($t) { $line = "$(Get-Date -Format 'HH:mm:ss') $t"; Write-Host $line; Add-Content -Path $log -Value $line }
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$exe = 'C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64.exe'

$places = if ($Batch -eq 'frontier') {
    @(
        @{ name = 'charwood_deer';    spec = '616,722,90,-0.15,5'; region = 'region.starting_frontier' },
        @{ name = 'moor_elk';         spec = '934,694,270,-0.12,5'; region = 'region.starting_frontier' },
        @{ name = 'outfall_waterfowl'; spec = '1086,704,90,-0.22,5'; region = 'region.starting_frontier' }
    )
} else {
    @(
        @{ name = 'barrowworks_ruin'; spec = '66,84,0,-0.12,5'; region = 'region.barrowworks' },
        @{ name = 'gorge_mouth';      spec = '528,780,90,-0.12,5'; region = 'region.den_gorge_cave' },
        @{ name = 'sedge_pools';      spec = '4872,740,90,-0.18,5'; region = 'region.sedge_levels' }
    )
}

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $lock acquire "claude fauna $Batch"
if ($LASTEXITCODE -ne 0) { Say 'gpu busy'; exit 1 }
Say "lease acquired for $Batch"
try {
    foreach ($place in $places) {
        $dir = Join-Path $Out $place.name
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $arguments = @('--rendering-driver', 'vulkan', '--path', (Join-Path $wt 'src\Presentation'),
            '--resolution', '1600x900', '--windowed', '--',
            '--region', $place.region,
            '--content-root', (Join-Path $wt 'content'),
            '--bindings-overlay', (Join-Path $wt 'content\_art_bindings.json'),
            '--asset-root', 'D:\Otherreach_Shared\art_root',
            '--asset-fallback', (Join-Path $wt 'work\creator\game\world_fallback'),
            '--shot', $place.spec,
            '--visual', 'terrain=terrain3d,sky=sky3d,people=humgen,foldscar=proof,body=none,hour=9,tier=high,weather=fair')
        $env:OTHERREACH_CHARACTER_ROOT = (Join-Path $wt 'work\creator\game\humgen_characters')
        Say "place $($place.name) region $($place.region) spec $($place.spec)"
        $game = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Normal -PassThru `
            -RedirectStandardOutput (Join-Path $dir 'game.log') -RedirectStandardError (Join-Path $dir 'game.stderr')
        $deadline = (Get-Date).AddMinutes(2)
        while (-not $game.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 3; $game.Refresh() }
        if (-not $game.HasExited) { Say 'killing the run at its limit'; $game.Kill() }
        Say "$($place.name): $((Get-ChildItem $dir -Filter '*.png' -ErrorAction SilentlyContinue | Measure-Object).Count) frame(s)"
    }
}
finally {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $lock release "claude fauna $Batch" | Out-Null
    Say 'lease released'
}
