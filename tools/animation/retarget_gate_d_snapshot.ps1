param([Parameter(Mandatory)][string]$JobRoot,[Parameter(Mandatory)][string]$CreatorRoot)
$ErrorActionPreference='Stop'
$env:OTHERREACH_HUMGEN_ANIMATION_JOB=$JobRoot
$env:OTHERREACH_HUMGEN_EXTRA_SOURCES=Join-Path $JobRoot 'extra_sources.json'
$env:OTHERREACH_HUMGEN_SOURCE_SELECTION=Join-Path $JobRoot 'selection.json'
$guard=Join-Path $PSScriptRoot 'run_cpu_tool.ps1'
$ue='C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
foreach($actor in @('humgen_creator_female','humgen_creator_male')){
 $snapshot=Join-Path $JobRoot ('targets\'+$actor+'.glb')
 New-Item -ItemType Directory -Force (Split-Path $snapshot -Parent) | Out-Null
 Copy-Item -LiteralPath (Join-Path $CreatorRoot ('primary\rigged\'+$actor+'\'+$actor+'_rigged.glb')) -Destination $snapshot
 $env:OTHERREACH_HUMGEN_TARGET_ID=$actor
 $env:OTHERREACH_HUMGEN_TARGET_GLB=$snapshot
 $env:OTHERREACH_HUMGEN_TARGET_REPORT=Join-Path $JobRoot ('target_'+$actor+'.json')
 foreach($script in @('probe_humgen_target.py','retarget_humgen_probe.py')){
  & $guard -Executable $ue -JobArguments @((Join-Path $JobRoot 'project\OtherreachHumGenMotion.uproject'),'-run=pythonscript',('-script="'+(Join-Path $PSScriptRoot $script).Replace('\','/')+'"'),'-NullRHI','-unattended','-nosplash','-nosound','-nop4','-stdout','-FullStdOutLogOutput') -Log (Join-Path $JobRoot ($actor+'_'+$script+'.log')) -Job ('gate-d-snapshot-'+$actor) -LimitSeconds 3600
 }
 $receipt=Get-Content -LiteralPath (Join-Path $JobRoot ('retarget_'+$actor+'.json')) -Raw | ConvertFrom-Json
 if($receipt.status -ne 'DONE'){throw ('Retarget failed: '+$receipt.error)}
}
