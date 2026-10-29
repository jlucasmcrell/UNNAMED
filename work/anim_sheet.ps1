# Gate D part 2: the animation sheet - every clip the game shows a person, plus the face expressions.
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Extra = ''
)
$ErrorActionPreference = 'Continue'
$gatec = 'D:\Otherreach_Scratch\agy_gatec_wt'
New-Item -ItemType Directory -Force -Path (Join-Path $gatec $Out) | Out-Null
$log = Join-Path $gatec ('work\gatec_logs\anim_' + ($Out -replace '[^a-z0-9]+', '_') + '.log')
function Say($t) { $line = "$(Get-Date -Format 'HH:mm:ss') $t"; Write-Host $line; Add-Content -Path $log -Value $line }
$lock = 'C:\Otherreach_Recovery\gpu_lock.ps1'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $lock acquire 'claude gateD anim sheet'
if ($LASTEXITCODE -ne 0) { throw 'GPU acquire failed' }
Say 'lease acquired'
try {
    $exe = 'C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64.exe'
    $out = Join-Path $gatec $Out
    $arguments = @('--rendering-driver', 'vulkan', '--log-file', (Join-Path $out 'engine.log'), '--path', (Join-Path $gatec 'src\Presentation'),
        '--resolution', '1600x1200', '--windowed', '--',
        '--anim-sheet', $out,
        '--content-root', (Join-Path $gatec 'content'),
        '--bindings-overlay', (Join-Path $gatec 'content\_art_bindings.json'),
        '--asset-root', 'D:\Otherreach_Scratch\art_merged',
        '--asset-fallback', (Join-Path $gatec 'work\creator\game\world_fallback'),
        '--visual', 'terrain=terrain3d,sky=sky3d,people=humgen,foldscar=proof,body=none,hour=11,tier=high,weather=fair')
    if ($Extra.Trim()) { $arguments += $Extra.Trim().Split(' ') }
    $env:OTHERREACH_CHARACTER_ROOT = (Join-Path $gatec 'work\creator\game\humgen_characters')
    $env:OTHERREACH_LIGHTING_FAST = '1'
    Say "run: $($arguments -join ' ')"
    $game = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $out 'game.log') -RedirectStandardError (Join-Path $out 'game.stderr')
    $null = $game.Handle
    Say "pid $($game.Id)"
    $deadline = (Get-Date).AddMinutes(9)
    while (-not $game.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 5; $game.Refresh() }
    if (-not $game.HasExited) { Say 'killing at the lease limit'; $game.Kill() }
    Say "exit=$($game.ExitCode)"
}
finally {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $lock release 'claude gateD anim sheet' | Out-Null
    Say 'lease released'
}
Say "shots: $((Get-ChildItem (Join-Path $gatec $Out) -Filter '*.png' -Recurse -ErrorAction SilentlyContinue | Measure-Object).Count)"
