# One plan, one <=9-minute GPU lease. Run only after the release package/gate's reserved GPU window.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Plan,
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][string]$Stand,
    [Parameter(Mandatory)][string]$AssetRoot,
    [Parameter(Mandatory)][string]$AssetFallback,
    [Parameter(Mandatory)][string]$CharacterRoot,
    [string]$Game='',
    [string]$Content='',
    [string]$Region='region.starting_frontier',
    [string]$Hour='16.5',
    [string]$Resolution='1920x1080',
    [ValidateRange(1,240)][int]$Fps=60,
    [ValidateRange(1,540)][int]$LeaseSeconds=540
)
$ErrorActionPreference='Stop'
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$planPath=(Resolve-Path -LiteralPath $Plan).Path
$outputPath=[IO.Path]::GetFullPath($Output)
if(Test-Path 'C:\Otherreach_External_Assets\OWNER_PLAYING.flag'){throw 'The owner is playing; capture deferred'}
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
if(-not $Content){$Content=Join-Path $repository 'content'}
$executable=if($Game){(Resolve-Path -LiteralPath $Game).Path}else{'C:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'}
$env:DOTNET_ROOT='C:\Program Files\dotnet';$env:DOTNET_ROOT_X64=$env:DOTNET_ROOT
$env:GODOT_AI_DISABLE_TELEMETRY='1'
$env:UNNAMED_SCATTER_RESIDENCY='0';$env:UNNAMED_LOD_STIPPLE='0' # Packaged PLAY defaults.
$env:OTHERREACH_CHARACTER_ROOT=(Resolve-Path -LiteralPath $CharacterRoot).Path
$arguments=@()
if(-not $Game){$arguments+=@('--path',('"'+(Join-Path $repository 'src/Presentation')+'"'))}
$arguments+=@('--resolution',$Resolution,'--position','6000,6000','--fixed-fps',"$Fps",'--',
    '--region',$Region,'--stand',$Stand,'--profile',('"'+(Join-Path $outputPath 'profile')+'"'),
    '--content-root',('"'+[IO.Path]::GetFullPath($Content)+'"'),
    '--asset-root',('"'+[IO.Path]::GetFullPath($AssetRoot)+'"'),
    '--asset-fallback',('"'+[IO.Path]::GetFullPath($AssetFallback)+'"'),
    '--bindings-overlay',('"'+(Join-Path $repository 'content/_art_bindings.json')+'"'),
    '--scatter-rules',('"'+(Join-Path $repository 'content/_scatter_rules.json')+'"'),
    '--visual',"tier=ultra,people=humgen,hud=production,weather=fair,hour=$Hour",'--capture-size',$Resolution,
    '--mkt-plan',('"'+$planPath+'"'),'--mkt-out',('"'+$outputPath+'"'))
$lock='C:\Otherreach_Recovery\gpu_lock.ps1'
$owner='CodexFilmClip film-'+[IO.Path]::GetFileNameWithoutExtension($planPath)+' max'+$LeaseSeconds+'sec'
& powershell -NoProfile -ExecutionPolicy Bypass -File $lock acquire $owner
if($LASTEXITCODE -ne 0){throw 'GPU lease acquisition failed'}
try {
    if(Test-Path 'C:\Otherreach_External_Assets\OWNER_PLAYING.flag'){throw 'The owner started playing; capture deferred'}
    $started=[DateTimeOffset]::UtcNow
    $process=Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $repository -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $outputPath 'run.out.log') -RedirectStandardError (Join-Path $outputPath 'run.err.log')
    Set-Content -LiteralPath (Join-Path $outputPath 'pid.txt') -Value $process.Id
    $timedOut=-not $process.WaitForExit($LeaseSeconds*1000)
    if($timedOut){$process.Kill($true);$process.WaitForExit()}
    $process.Refresh()
    [ordered]@{executable=$executable;arguments=$arguments;pid=$process.Id;started_utc=$started.ToString('o');
        seconds=([DateTimeOffset]::UtcNow-$started).TotalSeconds;lease_limit_seconds=$LeaseSeconds;
        timeout=$timedOut;exit=$process.ExitCode} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $outputPath 'launch.json')
    if($timedOut){throw 'Own film process reached its lease timeout; completed compatible shots can be resumed'}
    if($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $outputPath 'complete.json'))){throw 'Film did not complete; inspect its retained logs'}
} finally {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $lock release $owner
}
