# Fauna camera pass on BEAST: stand at the elk band and the deer ground and keep the frame (the last two stills the objective needs).
# Ready to run the moment the 3090 is free again; takes the gpu_lock itself and holds it for one short batch.
param([string]$Out = 'F:\otherreach_captures\fauna_beast')
$ErrorActionPreference = 'Continue'
$wt = 'D:\Otherreach_Scratch\ds_fish2_wt'
$lock = 'C:\Otherreach_Recovery\gpu_lock.ps1'
$log = Join-Path $wt 'work\fauna_beast.log'
$exe = 'C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64.exe'
function Say($t) { $line = "$(Get-Date -Format 'HH:mm:ss') $t"; Write-Host $line; Add-Content -Path $log -Value $line }

if (Test-Path 'C:\Otherreach_Recovery\OWNER_PLAYING.flag') { Say 'owner is playing; standing down'; exit 1 }
New-Item -ItemType Directory -Force -Path $Out | Out-Null

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $lock acquire 'claude fauna beast'
if ($LASTEXITCODE -ne 0) { Say 'gpu busy'; exit 1 }
Say 'lease acquired'

$places = @(
    @{ name = 'elk_band';    spec = '1010,644,90,-0.12,5' },
    @{ name = 'deer_ground'; spec = '616,722,90,-0.15,5' }
)
try {
    foreach ($place in $places) {
        $dir = Join-Path $Out $place.name
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $arguments = @('--rendering-driver', 'vulkan', '--fixed-fps', '60', '--capture-unfocused', '--path', (Join-Path $wt 'src\Presentation'),
            '--resolution', '1600x900', '--windowed', '--',
            '--region', 'region.starting_frontier',
            '--content-root', (Join-Path $wt 'content'),
            '--bindings-overlay', (Join-Path $wt 'content\_art_bindings.json'),
            '--asset-root', 'D:\Otherreach_Shared\art_root',
            '--asset-fallback', (Join-Path $wt 'work\creator\game\world_fallback'),
            '--shot-out', $dir,
            '--shot', $place.spec,
            '--visual', 'terrain=terrain3d,sky=sky3d,people=humgen,foldscar=proof,body=none,hour=9,tier=high,weather=fair')
        $env:OTHERREACH_CHARACTER_ROOT = (Join-Path $wt 'work\creator\game\humgen_characters')
        Say "place $($place.name) spec $($place.spec)"
        $game = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Normal -PassThru `
            -RedirectStandardOutput (Join-Path $dir 'game.log') -RedirectStandardError (Join-Path $dir 'game.stderr')
        $deadline = (Get-Date).AddMinutes(3)
        while (-not $game.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 3; $game.Refresh() }
        if (-not $game.HasExited) { Say 'killing the run at its limit'; $game.Kill() }
        Say "$($place.name): $((Get-ChildItem $dir -Filter '*.png' -ErrorAction SilentlyContinue | Measure-Object).Count) frame(s)"
    }
}
finally {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $lock release 'claude fauna beast' | Out-Null
    Say 'lease released'
}
