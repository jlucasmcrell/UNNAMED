# F1 Gate B: the native creator over the real art, on the official bounded GPU lease. Main.MetaHumanReview opens the creator
# (normal-launch path, the owner's default settings) and Main.Creator walks it (OTHERREACH_CREATOR_GATE_B=1).
# --asset-root is the world art root, --asset-fallback the world fallback merged with the creator root (the two native bases, the
# parts and the 83 preset texture sets), and OTHERREACH_CHARACTER_ROOT carries the exact-target motion libraries.
param([Parameter(Mandatory)][string]$Label, [string]$Hour = '12', [string]$Tier = 'high', [string]$Resolution = '1920x1080',
      [int]$LimitSeconds = 580, [string]$Worktree = 'D:\Otherreach_Scratch\codex_f1_siann_wt')
$ErrorActionPreference = 'Stop'
$flag = 'C:\Otherreach_Recovery\OWNER_PLAYING.flag'
$runs = Join-Path $Worktree 'work\creator'
$out = Join-Path $runs "out\$Label"
New-Item -ItemType Directory -Path $out -Force | Out-Null
$game = Join-Path $runs 'game'
$exe = 'C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64_console.exe'
if (Test-Path $flag) { throw 'Owner playing; not starting' }
$env:OTHERREACH_CHARACTER_ROOT = Join-Path $game 'humgen_characters'
$env:OTHERREACH_MH_REVIEW_OUT = $out
$env:OTHERREACH_RCD3_REVIEW = '1'
$env:OTHERREACH_CREATOR_GATE_B = '1'
foreach ($k in 'OTHERREACH_RCD5_STEALTH', 'OTHERREACH_EFFECT_DELIVERY_REVIEW', 'OTHERREACH_RCD6_MPFB_MOTION', 'OTHERREACH_RCD6_NAMED_FACES',
    'OTHERREACH_RCD6_VISIBILITY', 'OTHERREACH_RCD8_WAYSTONE_REVIEW', 'OTHERREACH_RCD8_MOTION_REVIEW', 'OTHERREACH_RCD8_STARTUP_REVIEW',
    'OTHERREACH_RCD6_STARTUP_REVIEW', 'OTHERREACH_RCD4_WORLD', 'OTHERREACH_HUMGEN_FIDELITY') { Set-Item "Env:$k" '0' }
$env:OTHERREACH_LIGHTING_FAST = '1'
$visual = "terrain=terrain3d,sky=sky3d,people=humgen,foldscar=proof,body=none,hour=$Hour,tier=$Tier,weather=fair"
$args_ = @('--rendering-driver', 'vulkan', '--log-file', (Join-Path $out 'engine.log'), '--path', "$Worktree\src\Presentation", '--resolution', $Resolution, '--windowed', '--',
    '--region', 'region.starting_frontier', '--content-root', "$Worktree\content", '--bindings-overlay', "$Worktree\content\_art_bindings.json",
    '--asset-root', (Join-Path $Worktree 'work\fidelity\game\primary'), '--asset-fallback', (Join-Path $game 'world_fallback'),
    '--scatter-rules', "$Worktree\content\_scatter_rules.json", '--profile', (Join-Path $out 'profile'), '--visual', $visual)
$lease = "claude f1 gateB $Label"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\Otherreach_Recovery\gpu_lock.ps1' acquire $lease
if ($LASTEXITCODE -ne 0) { throw 'GPU acquire failed' }
$g = $null
try {
    if (Test-Path $flag) { throw 'Owner playing after acquire' }
    $g = Start-Process -FilePath $exe -ArgumentList $args_ -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'game.log') -RedirectStandardError (Join-Path $out 'game.stderr')
    $null = $g.Handle
    $t0 = Get-Date
    while (-not $g.HasExited) {
        if ((Test-Path (Join-Path $out 'game.stderr')) -and (Select-String -LiteralPath (Join-Path $out 'game.stderr') -Pattern 'VkResult error -4|Vulkan.*device lost|Shader compilation failed|DXGI_ERROR_DEVICE_REMOVED|D3D12.*device.*lost|0x887a0005|Fatal error. System.AccessViolationException' -Quiet)) { throw 'Fatal renderer error; ending owned process tree' }
        if (Test-Path $flag) { throw 'Owner playing' }
        if (((Get-Date) - $t0).TotalSeconds -gt $LimitSeconds) { throw 'Time limit' }
        Start-Sleep -Milliseconds 400
    }
    Get-Content (Join-Path $out 'engine.log') -Tail 4
}
finally {
    if ($g -and -not $g.HasExited) {
        $snapshot = Get-CimInstance Win32_Process
        $ownedIds = [Collections.Generic.List[int]]::new(); $ownedIds.Add($g.Id)
        for ($i = 0; $i -lt $ownedIds.Count; $i++) { foreach ($child in $snapshot | Where-Object { $_.ParentProcessId -eq $ownedIds[$i] }) { $ownedIds.Add([int]$child.ProcessId) } }
        for ($i = $ownedIds.Count - 1; $i -ge 0; $i--) { Stop-Process -Id $ownedIds[$i] -Force -ErrorAction SilentlyContinue }
    }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\Otherreach_Recovery\gpu_lock.ps1' release $lease
}
