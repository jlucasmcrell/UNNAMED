# The actual exported game's binding parser checks the assembled primary/fallback roots without rendering.
param([Parameter(Mandatory)][string]$Package,[Parameter(Mandatory)][string]$Receipt)
$ErrorActionPreference='Stop'
$Package=[IO.Path]::GetFullPath($Package)
$Receipt=[IO.Path]::GetFullPath($Receipt)
New-Item -ItemType Directory -Force (Split-Path -Parent $Receipt) | Out-Null
if(Test-Path -LiteralPath $Receipt){Remove-Item -LiteralPath $Receipt}
$arguments=@('--headless','--audio-driver','Dummy','--','--cpu-diagnostic=character-dependencies',('"'+$Package+'"'),('"'+$Receipt+'"'))
$process=Start-Process -FilePath (Join-Path $Package 'Otherreach.exe') -ArgumentList $arguments -WorkingDirectory $Package `
 -PassThru -WindowStyle Hidden -RedirectStandardOutput ($Receipt+'.out.log') -RedirectStandardError ($Receipt+'.err.log')
if(-not $process.WaitForExit(300000)){$process.Kill();$process.WaitForExit();throw 'Owned package character preflight exceeded five minutes'}
$process.WaitForExit()
if($process.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $Receipt)){throw ('Bound package dependency gate failed: '+$Receipt)}
$result=Get-Content -LiteralPath $Receipt -Raw | ConvertFrom-Json
if($result.status -ne 'PASS' -or $result.packaged -ne $true -or $result.checked_scope -ne 'native_player_npc_inputs' -or $result.npc_definitions -lt 1){throw ('Native player/NPC package dependencies failed: '+$Receipt)}
