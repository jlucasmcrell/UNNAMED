param([Parameter(Mandatory)][string]$JobRoot,[Parameter(Mandatory)][string]$CreatorRoot,[string]$RuntimeRoot='')
$ErrorActionPreference='Stop'
$guard=Join-Path $PSScriptRoot 'run_cpu_tool.ps1'
$ue='C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$env:OTHERREACH_HUMGEN_ANIMATION_JOB=$JobRoot
$deadline=(Get-Date).AddHours(3)
while($true){
 $sourceLog=Join-Path (Split-Path $JobRoot -Parent) 'fbx_sources_resume.log'
 $sourceReady=(Test-Path $sourceLog) -and (Select-String -LiteralPath $sourceLog -Pattern 'GATE_D_SOURCES_COMPLETE' -Quiet)
 $retargetReady=Test-Path (Join-Path $JobRoot 'initial_complete.json')
 if($sourceReady -and $retargetReady){break}
 if((Get-Date) -gt $deadline){throw 'Gate D source preparation deadline exceeded'}
 Start-Sleep -Seconds 10
}
& python (Join-Path $PSScriptRoot 'prepare_gate_d.py') 'D:\Otherreach_Scratch\codex_f1_siann_wt\work\creator\motion' $JobRoot
if($LASTEXITCODE -ne 0){throw 'Gate D selection preparation failed'}
function Epic([string]$Script,[string]$Name){
 $path=(Join-Path $PSScriptRoot $Script).Replace('\','/')
 for($attempt=1;$attempt -le 3;$attempt++){
  $suffix=if($attempt -eq 1){''}else{'_resume'+$attempt}
  $log=Join-Path $JobRoot ($Name+$suffix+'.log')
  try {
   & $guard -Executable $ue -JobArguments @((Join-Path $JobRoot 'project\OtherreachHumGenMotion.uproject'),'-run=pythonscript',('-script="'+$path+'"'),'-NullRHI','-unattended','-nosplash','-nosound','-nop4','-stdout','-FullStdOutLogOutput') -Log $log -Job ('gate-d-'+$Name) -LimitSeconds 14400
   return
  } catch {
   # Epic can retain thousands of source/retarget packages until process exit.
   # Resume the exact checked transports in a fresh process; never alter keys or
   # source quality to fit an offline batch into the workstation's commit limit.
   if($Script -ne 'retarget_humgen_probe.py' -or $attempt -eq 3 -or
      !(Select-String -LiteralPath $log -Pattern 'paging file is too small' -Quiet)){throw}
   Write-Output 'Native batch reached the Windows commit limit; resuming its exact-target checkpoint in a fresh process.'
  }
 }
}
Epic 'import_gate_d_sources.py' 'import_owned_sources'
$imported=Get-Content -LiteralPath (Join-Path $JobRoot 'extra_sources.json') -Raw | ConvertFrom-Json
if($imported.status -ne 'DONE' -or $imported.failures.Count -gt 0){throw ('Owned motion import is incomplete; review extra_sources.json ('+$imported.failures.Count+' failures)')}
$env:OTHERREACH_HUMGEN_EXTRA_SOURCES=Join-Path $JobRoot 'extra_sources.json'
$env:OTHERREACH_HUMGEN_SOURCE_SELECTION=Join-Path $JobRoot 'selection.json'
foreach($actor in @('humgen_creator_female','humgen_creator_male')){
 $snapshot=Join-Path $JobRoot ('targets\'+$actor+'.glb')
 New-Item -ItemType Directory -Force (Split-Path $snapshot -Parent) | Out-Null
 Copy-Item -LiteralPath (Join-Path $CreatorRoot ('primary\rigged\'+$actor+'\'+$actor+'_rigged.glb')) -Destination $snapshot
 $env:OTHERREACH_HUMGEN_TARGET_ID=$actor
 $env:OTHERREACH_HUMGEN_TARGET_GLB=$snapshot
 $env:OTHERREACH_HUMGEN_TARGET_REPORT=Join-Path $JobRoot ('target_'+$actor+'.json')
 Epic 'probe_humgen_target.py' ('target_'+$actor)
 $target=Get-Content -LiteralPath $env:OTHERREACH_HUMGEN_TARGET_REPORT -Raw | ConvertFrom-Json
 if($target.status -ne 'DONE'){throw ('Target probe failed: '+$actor)}
 Epic 'retarget_humgen_probe.py' ('final_'+$actor)
 $receipt=Get-Content -LiteralPath (Join-Path $JobRoot ('retarget_'+$actor+'.json')) -Raw | ConvertFrom-Json
 if($receipt.status -ne 'DONE'){throw ('Retarget failed: '+$receipt.error)}
 if($RuntimeRoot){
  $runtimeBody=Join-Path $RuntimeRoot ('primary\rigged\'+$actor+'\'+$actor+'_rigged.glb')
  if((Get-FileHash -LiteralPath $runtimeBody).Hash -ne (Get-FileHash -LiteralPath $snapshot).Hash){throw 'Creator body changed; restage private proof art before packaging'}
  & python (Join-Path $PSScriptRoot 'package_gate_d_motion.py') $JobRoot $actor $RuntimeRoot
  if($LASTEXITCODE -ne 0){throw ('Native package failed: '+$actor)}
 }
}
