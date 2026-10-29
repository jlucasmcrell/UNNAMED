param([Parameter(Mandatory)][string]$JobRoot)
$ErrorActionPreference='Stop'
$guard=Join-Path $PSScriptRoot 'run_cpu_tool.ps1'
$env:OTHERREACH_HUMGEN_ANIMATION_JOB=$JobRoot
$env:OTHERREACH_HUMGEN_SOURCE_SELECTION=Join-Path $JobRoot 'selection.json'
$ue='C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
foreach($actor in @('humgen_creator_female','humgen_creator_male')) {
 $env:OTHERREACH_HUMGEN_TARGET_ID=$actor
 $env:OTHERREACH_HUMGEN_TARGET_REPORT=Join-Path $JobRoot ('target_'+$actor+'.json')
 $script=(Join-Path $PSScriptRoot 'retarget_humgen_probe.py').Replace('\','/')
 & $guard -Executable $ue -JobArguments @((Join-Path $JobRoot 'project\OtherreachHumGenMotion.uproject'),'-run=pythonscript',('-script="'+$script+'"'),'-NullRHI','-unattended','-nosplash','-nosound','-nop4','-stdout','-FullStdOutLogOutput') -Log (Join-Path $JobRoot ($actor+'.log')) -Job ('gate-d-'+$actor) -LimitSeconds 3600
 $receipt=Get-Content -LiteralPath (Join-Path $JobRoot ('retarget_'+$actor+'.json')) -Raw | ConvertFrom-Json
 if($receipt.status -ne 'DONE'){throw ('Gate D retarget failed: '+$receipt.error)}
}
@{status='DONE';phase='initial inspected UAL/FAL retarget only'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $JobRoot 'initial_complete.json')
