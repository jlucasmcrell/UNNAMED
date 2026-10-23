param([Parameter(Mandatory)][string]$JobRoot,[Parameter(Mandatory)][string]$StagingReceipt,[string[]]$Only=@())
$ErrorActionPreference='Stop'
$guard=Join-Path $PSScriptRoot 'run_cpu_tool.ps1'
$ue='C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$godot='C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64.exe'
$bow=Join-Path $JobRoot 'bow_candidate'
$null=New-Item -ItemType Directory -Path $bow -Force
foreach($file in @('sources.json','fal_migration.json')){Copy-Item -LiteralPath (Join-Path $JobRoot $file) -Destination (Join-Path $bow $file)}
$selection=Join-Path $bow 'selection.json'
@{bow_ready=@('cmu_bow','CMU79_86_BowReady_Anim');bow_draw=@('cmu_bow','CMU79_86_BowDraw_Anim');bow_release=@('cmu_bow','CMU79_86_BowRelease_Anim')} | ConvertTo-Json | Set-Content -LiteralPath $selection -Encoding utf8
$env:OTHERREACH_HUMGEN_ANIMATION_JOB=$bow
$env:OTHERREACH_HUMGEN_SOURCE_SELECTION=$selection
$env:OTHERREACH_HUMGEN_EXTRA_SOURCES=Join-Path $JobRoot 'bow_epic_source.json'
[Environment]::SetEnvironmentVariable('UE-LocalDataCachePath',(Join-Path $JobRoot 'ddc'),'Process')
$state=[ordered]@{status='RUNNING';characters=@();current=''}
function Save-Progress {$state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $bow 'batch.json') -Encoding utf8}
try {
 foreach($row in @(Get-Content -LiteralPath $StagingReceipt -Raw | ConvertFrom-Json)){
  if($Only.Count -gt 0 -and $row.id -notin $Only){continue}
  if((Get-FileHash -LiteralPath $row.staged).Hash.ToLowerInvariant() -ne $row.sha256){throw ('Body changed: '+$row.id)}
  $state.current=$row.id;Save-Progress
  $env:OTHERREACH_HUMGEN_TARGET_ID=$row.id
  $env:OTHERREACH_HUMGEN_TARGET_REPORT=Join-Path $JobRoot ('target_'+$row.id+'.json')
  & $guard -Executable $ue -JobArguments @((Join-Path $JobRoot 'project\OtherreachHumGenMotion.uproject'),'-run=pythonscript',('-script="'+(Join-Path $PSScriptRoot 'retarget_humgen_probe.py').Replace('\','/')+'"'),'-NullRHI','-unattended','-nosplash','-nosound','-nop4','-stdout','-FullStdOutLogOutput') -Log (Join-Path $bow ('retarget_'+$row.id+'.log')) -LimitSeconds 300
  $native=Get-Content -LiteralPath (Join-Path $bow ('retarget_'+$row.id+'.json')) -Raw | ConvertFrom-Json
  if($native.status -ne 'DONE' -or $native.clips.Count -ne 3 -or $native.body_sha256 -ne $row.sha256){throw ('Native bow export failed: '+$row.id+' '+$native.error)}
  $auditArgs=@((Join-Path $PSScriptRoot 'audit_humgen_transport.py'),$row.staged)+@($native.clips | ForEach-Object path)+@('--report',(Join-Path $bow ('audit_'+$row.id+'.json')))
  & 'C:\Python314\python.exe' @auditArgs
  if($LASTEXITCODE -ne 0){throw ('Native bow rig audit failed: '+$row.id)}
  # Build separately: never change the payload Claude is currently reviewing.
  $manifest=Get-Content -LiteralPath (Join-Path $JobRoot ('library_'+$row.id+'.json')) -Raw | ConvertFrom-Json
  if($manifest.clips.Count -ne 39){throw 'Expected the original 39-source native library'}
  foreach($clip in $native.clips){
   if($clip.state -eq 'bow_draw'){$clip | Add-Member -NotePropertyName action_phases -NotePropertyValue @((1.35/2.45),(1.75/2.45),1)}
  }
  $manifest.clips=@($manifest.clips)+@($native.clips)
  $manifest.output=Join-Path $bow ('mocap_motion\'+$row.id+'_motion.res')
  $manifestPath=Join-Path $bow ('library_'+$row.id+'.json')
  $manifest | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $manifestPath -Encoding utf8
  & $guard -Executable $godot -JobArguments @('--headless','--path',(Join-Path (Split-Path $JobRoot -Parent) 'rig_gate'),'--script',(Join-Path $PSScriptRoot 'build_humgen_motion.gd'),'--',$manifestPath) -Log (Join-Path $bow ('library_'+$row.id+'.log')) -LimitSeconds 240 -FailIfStderrMatches 'SCRIPT ERROR|Assertion failed'
  $receipt=Get-Content -LiteralPath ($manifest.output+'.audit.json') -Raw | ConvertFrom-Json
  if($receipt.clips.Count -ne 42){throw ('Candidate library incomplete: '+$row.id)}
  $state.characters+=@{id=$row.id;motion=$manifest.output;source_clips=42;runtime_states=49;body_sha256=$row.sha256}
  Save-Progress
 }
 $state.status='DONE';$state.current='';Save-Progress
}catch{$state.status='FAILED';$state.error=$_.Exception.Message;Save-Progress;throw}
