# F1 Gate C proof run: every NPC of a town, face at 2 m, plus wide shots - driven by the game's own content and look code.
param([Parameter(Mandatory)][string]$Label, [Parameter(Mandatory)][string]$Plan, [string]$Resolution = '1600x1200',
      [int]$LimitSeconds = 540, [string]$Worktree = 'D:\Otherreach_Scratch\agy_gatec_wt', [string]$AssetFallback='')
$ErrorActionPreference = 'Stop'
$flag = 'C:\Otherreach_Recovery\OWNER_PLAYING.flag'
$out = Join-Path $Worktree "work\creator\out\npc_looks\$Label"
New-Item -ItemType Directory -Path $out -Force | Out-Null
$exe = 'C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64.exe'
if (Test-Path $flag) { throw 'Owner playing; not starting' }
# The creator bases need their own exact-target motion payloads, and only this character root holds them.
$env:OTHERREACH_CHARACTER_ROOT = Join-Path $Worktree 'work\creator\game\humgen_characters'
$env:OTHERREACH_MH_REVIEW_OUT = $out
$env:OTHERREACH_RCD3_REVIEW = '1'
$env:OTHERREACH_NPC_LOOKS_PLAN = (Resolve-Path $Plan).Path
$env:OTHERREACH_NPC_OUTFIT_REVIEW = if((Get-Content -LiteralPath $Plan -Raw|ConvertFrom-Json).full_body){'1'}else{'0'}
foreach ($k in 'OTHERREACH_RCD5_STEALTH', 'OTHERREACH_EFFECT_DELIVERY_REVIEW', 'OTHERREACH_RCD6_MPFB_MOTION', 'OTHERREACH_RCD6_NAMED_FACES',
    'OTHERREACH_RCD6_VISIBILITY', 'OTHERREACH_RCD8_WAYSTONE_REVIEW', 'OTHERREACH_RCD8_MOTION_REVIEW', 'OTHERREACH_RCD8_STARTUP_REVIEW',
    'OTHERREACH_RCD6_STARTUP_REVIEW', 'OTHERREACH_RCD4_WORLD', 'OTHERREACH_HUMGEN_FIDELITY', 'OTHERREACH_CREATOR_GATE_B',
    'OTHERREACH_RACE_LOOKS_PLAN') { Set-Item "Env:$k" '0' }
$env:OTHERREACH_LIGHTING_FAST = '1'
if(-not $AssetFallback){$AssetFallback="$Worktree\work\creator\game\world_fallback"}
$visual = 'terrain=terrain3d,sky=sky3d,people=humgen,foldscar=proof,body=none,hour=11,tier=high,weather=fair'
$args_ = @('--rendering-driver', 'vulkan', '--log-file', (Join-Path $out 'engine.log'), '--path', "$Worktree\src\Presentation",
    '--resolution', $Resolution, '--windowed', '--', '--region', 'region.starting_frontier', '--content-root', "$Worktree\content",
    '--bindings-overlay', "$Worktree\content\_art_bindings.json", '--asset-root', 'D:\Otherreach_Scratch\art_merged',
    '--asset-fallback', $AssetFallback, '--scatter-rules', "$Worktree\content\_scatter_rules.json",
    '--profile', (Join-Path $out 'profile'), '--visual', $visual)
$lease = "codex Gate C $Label"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\Otherreach_Recovery\gpu_lock.ps1' acquire $lease
if ($LASTEXITCODE -ne 0) { throw 'GPU acquire failed' }
$g = $null
try {
    if (Test-Path $flag) { throw 'Owner playing after acquire' }
    $g = Start-Process -FilePath $exe -ArgumentList $args_ -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'game.log') -RedirectStandardError (Join-Path $out 'game.stderr')
    $null = $g.Handle
    $t0 = Get-Date
    while (-not $g.HasExited) {
        if(Test-Path $flag){throw 'Owner playing; ending own capture'}
        if (Test-Path (Join-Path $out 'complete.json')) { Start-Sleep -Seconds 4; break }
        if (((Get-Date) - $t0).TotalSeconds -gt $LimitSeconds) { throw 'Time limit' }
        Start-Sleep -Milliseconds 500
    }
    Write-Host "game exit $($g.ExitCode)"
}
finally {
    if ($g -and -not $g.HasExited) { Start-Sleep 3; if (-not $g.HasExited) { Stop-Process -Id $g.Id -Force -ErrorAction SilentlyContinue } }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\Otherreach_Recovery\gpu_lock.ps1' release $lease
}
