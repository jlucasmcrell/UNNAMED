# Native HumGen Gate A: prepared-cache READ mode, actual game studio/noon review, official bounded GPU lease.
param([Parameter(Mandatory)][string]$Label, [string]$Plan='', [string]$Hour = '12', [string]$Tier = 'ultra',
      [string]$Resolution = '2560x1440', [string]$Driver='vulkan', [int]$LimitSeconds = 480, [switch]$Record, [string]$Worktree = 'D:\Otherreach_Scratch\codex_f1_siann_wt')
$ErrorActionPreference = 'Stop'
$flag = 'C:\Otherreach_Recovery\OWNER_PLAYING.flag'
$runs = Join-Path $Worktree 'work\fidelity'
$out = Join-Path $runs "out\$Label"
New-Item -ItemType Directory -Path $out -Force | Out-Null
$game = Join-Path $runs 'game'
$wt = $Worktree
$ffmpeg = 'C:\Otherreach_Worker\tools\ffmpeg\bin\ffmpeg.exe'
$exe = 'C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64_console.exe'
if (Test-Path $flag) { throw 'Owner playing; not starting' }
$env:OTHERREACH_MH_REVIEW_OUT = $out
$env:OTHERREACH_CHARACTER_ROOT = Join-Path $game 'humgen_characters'
$env:OTHERREACH_RCD3_REVIEW = '1'
$env:OTHERREACH_RACE_LOOKS_PLAN = ''
$env:OTHERREACH_HUMGEN_FIDELITY = '1'
foreach ($k in 'OTHERREACH_RCD5_STEALTH', 'OTHERREACH_EFFECT_DELIVERY_REVIEW', 'OTHERREACH_RCD6_MPFB_MOTION', 'OTHERREACH_RCD6_NAMED_FACES', 'OTHERREACH_RCD6_VISIBILITY',
    'OTHERREACH_RCD8_WAYSTONE_REVIEW', 'OTHERREACH_RCD8_MOTION_REVIEW', 'OTHERREACH_RCD8_STARTUP_REVIEW', 'OTHERREACH_RCD6_STARTUP_REVIEW', 'OTHERREACH_RCD4_WORLD') { Set-Item "Env:$k" '0' }
$env:OTHERREACH_LIGHTING_FAST = '1'
$env:OTHERREACH_LIGHTING_SEED = [Convert]::ToUInt64('c6985a36ad0f2f75', 16).ToString()
$env:OTHERREACH_MH_REVIEW_PLAYER = 'ada'
$visual = "terrain=terrain3d,sky=sky3d,people=humgen,foldscar=proof,body=none,hour=$Hour,tier=$Tier,weather=fair"
$args_ = @('--rendering-driver', $Driver, '--path', "$wt\src\Presentation", '--resolution', $Resolution, '--windowed', '--', '--region', 'region.starting_frontier', '--content-root', "$wt\content",
    '--bindings-overlay', "$wt\content\_art_bindings.json", '--asset-root', (Join-Path $game 'primary'), '--asset-fallback', (Join-Path $game 'fallback'),
    '--scatter-rules', "$wt\content\_scatter_rules.json", '--profile', (Join-Path $out 'profile'), '--visual', $visual)
@{ executable = $exe; arguments = $args_; plan = $env:OTHERREACH_RACE_LOOKS_PLAN } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $out 'requested_launch.json')
$lease = "codex-f1 fidelity $Label"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\Otherreach_Recovery\gpu_lock.ps1' acquire $lease
if ($LASTEXITCODE -ne 0) { throw 'GPU acquire failed' }
$g = $null; $rec = $null
try {
    if (Test-Path $flag) { throw 'Owner playing after acquire' }
    $g = Start-Process -FilePath $exe -ArgumentList $args_ -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'game.log') -RedirectStandardError (Join-Path $out 'game.stderr')
    $null = $g.Handle
    $t0 = Get-Date
    while (-not (Test-Path (Join-Path $out 'complete.json'))) {
        if ($g.HasExited) { break }
        if ((Test-Path (Join-Path $out 'game.stderr')) -and (Select-String -LiteralPath (Join-Path $out 'game.stderr') -Pattern 'VkResult error -4|Vulkan.*device lost|Shader compilation failed|DXGI_ERROR_DEVICE_REMOVED|D3D12.*device.*lost|0x887a0005|Fatal error. System.AccessViolationException' -Quiet)) { throw 'Fatal renderer error; ending owned process tree' }
        if (Test-Path $flag) { throw 'Owner playing' }
        if (((Get-Date) - $t0).TotalSeconds -gt $LimitSeconds) { throw 'Time limit' }
        if ($Record -and -not $rec -and (Test-Path (Join-Path $out 'record_ready.json'))) {
            $mp4 = Join-Path $out "$Label.mp4"
            $fa = "-hide_banner -y -f lavfi -i `"gfxcapture=window_title='^Otherreach - $Label`$':max_framerate=60:capture_cursor=false:width=2560:height=1440:resize_mode=scale_aspect`" -vf hwdownload,format=bgra,format=nv12 -c:v h264_nvenc -preset p4 -tune hq -rc vbr -cq 19 -b:v 0 -g 30 -an -movflags +frag_keyframe+empty_moov+default_base_moof `"$mp4`""
            $rec = Start-Process -FilePath $ffmpeg -ArgumentList $fa -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $out 'ffmpeg.log') -RedirectStandardInput (Join-Path $runs 'q.txt')
            $null = $rec.Handle
            Start-Sleep -Seconds 2
            if ($rec.HasExited) { throw "Recorder failed: $(Get-Content (Join-Path $out 'ffmpeg.log') -Raw)" }
            Set-Content (Join-Path $out 'recording.started') (Get-Date -Format o)
        }
        if ($rec -and -not $rec.HasExited -and (Test-Path (Join-Path $out 'recording.finished'))) {
            Start-Sleep -Seconds 6
            Stop-Process -Id $rec.Id -ErrorAction SilentlyContinue
        }
        Start-Sleep -Milliseconds 300
    }
    if (-not (Test-Path (Join-Path $out 'complete.json'))) { throw 'Capture incomplete' }
}
finally {
    if ($rec -and -not $rec.HasExited) { Stop-Process -Id $rec.Id -ErrorAction SilentlyContinue; Start-Sleep 2 }
    if ($g) {
        $snapshot=Get-CimInstance Win32_Process
        $ownedIds=[Collections.Generic.List[int]]::new();$ownedIds.Add($g.Id)
        for($i=0;$i -lt $ownedIds.Count;$i++){foreach($child in $snapshot | Where-Object {$_.ParentProcessId -eq $ownedIds[$i]}){$ownedIds.Add([int]$child.ProcessId)}}
        for($i=$ownedIds.Count-1;$i -ge 0;$i--){Stop-Process -Id $ownedIds[$i] -Force -ErrorAction SilentlyContinue}
    }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\Otherreach_Recovery\gpu_lock.ps1' release $lease
}
