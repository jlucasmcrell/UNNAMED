param([Parameter(Mandatory)][string]$JobRoot,[Parameter(Mandatory)][string]$CreatorRoot)
$ErrorActionPreference='Stop'
$guard=Join-Path $PSScriptRoot 'run_cpu_tool.ps1'
$ue='C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$env:OTHERREACH_HUMGEN_ANIMATION_JOB=$JobRoot
$deadline=(Get-Date).AddHours(3)
while($true){
 $sourceLog=Join-Path (Split-Path $JobRoot -Parent) 'fbx_sources.log'
 $sourceReady=(Test-Path $sourceLog) -and (Select-String -LiteralPath $sourceLog -Pattern 'GATE_D_SOURCES_COMPLETE' -Quiet)
 $retargetReady=Test-Path (Join-Path $JobRoot 'initial_complete.json')
 if($sourceReady -and $retargetReady){break}
 if((Get-Date) -gt $deadline){throw 'Gate D source preparation deadline exceeded'}
 Start-Sleep -Seconds 10
}
function Epic([string]$Script,[string]$Name){
 $path=(Join-Path $PSScriptRoot $Script).Replace('\','/')
 & $guard -Executable $ue -JobArguments @((Join-Path $JobRoot 'project\OtherreachHumGenMotion.uproject'),'-run=pythonscript',('-script="'+$path+'"'),'-NullRHI','-unattended','-nosplash','-nosound','-nop4','-stdout','-FullStdOutLogOutput') -Log (Join-Path $JobRoot ($Name+'.log')) -Job ('gate-d-'+$Name) -LimitSeconds 5400
}
Epic 'import_gate_d_sources.py' 'import_owned_sources'
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
}
