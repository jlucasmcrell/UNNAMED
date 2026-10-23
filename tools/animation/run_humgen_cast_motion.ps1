param([Parameter(Mandatory)][string]$JobRoot,[Parameter(Mandatory)][string]$StagingReceipt,[string[]]$Only=@())
$ErrorActionPreference='Stop'
$guard=Join-Path $PSScriptRoot 'run_cpu_tool.ps1'
$ue='C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$godot='C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64.exe'
$scripts=$PSScriptRoot.Replace('\','/')
$env:OTHERREACH_HUMGEN_ANIMATION_JOB=$JobRoot
$env:OTHERREACH_HUMGEN_SOURCE_SELECTION=Join-Path $PSScriptRoot 'humgen_sources.json'
$selection=Get-Content -LiteralPath $env:OTHERREACH_HUMGEN_SOURCE_SELECTION -Raw | ConvertFrom-Json -AsHashtable
$retargeterHash=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'retarget_humgen_probe.py') -Algorithm SHA256).Hash.ToLowerInvariant()
$rows=@(Get-Content -LiteralPath $StagingReceipt -Raw | ConvertFrom-Json)
$state=[ordered]@{status='RUNNING';characters=@();current='';updated=''}
function Save-Progress {
 $state.updated=(Get-Date).ToString('o')
 $state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $JobRoot 'cast_batch.json') -Encoding utf8
}
function Run-Epic([string]$Script,[string]$Log) {
 & $guard -Executable $ue -JobArguments @((Join-Path $JobRoot 'project\OtherreachHumGenMotion.uproject'),'-run=pythonscript',('-script="'+$scripts+'/'+$Script+'"'),'-NullRHI','-unattended','-nosplash','-nosound','-nop4','-stdout','-FullStdOutLogOutput') -Log $Log -Job ('humgen-native-'+$env:OTHERREACH_HUMGEN_TARGET_ID) -LimitSeconds 1200
}
try {
 foreach($row in $rows){
  if($Only.Count -gt 0 -and $row.id -notin $Only){continue}
  $state.current=$row.id;Save-Progress
  $env:OTHERREACH_HUMGEN_TARGET_ID=$row.id
  $env:OTHERREACH_HUMGEN_TARGET_GLB=$row.staged
  if((Get-FileHash -LiteralPath $row.staged -Algorithm SHA256).Hash.ToLowerInvariant() -ne $row.sha256){throw ('Staged target changed: '+$row.id)}
  $env:OTHERREACH_HUMGEN_TARGET_REPORT=Join-Path $JobRoot ('target_'+$row.id+'.json')
  $target=$null
  if(Test-Path -LiteralPath $env:OTHERREACH_HUMGEN_TARGET_REPORT){$target=Get-Content -LiteralPath $env:OTHERREACH_HUMGEN_TARGET_REPORT -Raw | ConvertFrom-Json}
  if($null -eq $target -or $target.status -ne 'DONE' -or $target.body_sha256 -ne $row.sha256){
   Run-Epic 'probe_humgen_target.py' (Join-Path $JobRoot ('logs\import_'+$row.id+'.log'))
  }
  $target=Get-Content -LiteralPath $env:OTHERREACH_HUMGEN_TARGET_REPORT -Raw | ConvertFrom-Json
  if($target.status -ne 'DONE' -or $target.body_sha256 -ne $row.sha256){throw ('Native target import failed or changed: '+$row.id)}
  $retargetPath=Join-Path $JobRoot ('retarget_'+$row.id+'.json')
  $retarget=$null
  if(Test-Path -LiteralPath $retargetPath){$retarget=Get-Content -LiteralPath $retargetPath -Raw | ConvertFrom-Json}
  if($null -eq $retarget -or $retarget.status -ne 'DONE' -or $retarget.body_sha256 -ne $row.sha256 -or $retarget.clips.Count -ne $selection.Count -or $retarget.retargeter_script_sha256 -ne $retargeterHash){
   Run-Epic 'retarget_humgen_probe.py' (Join-Path $JobRoot ('logs\retarget_'+$row.id+'.log'))
   $retarget=Get-Content -LiteralPath $retargetPath -Raw | ConvertFrom-Json
  }
  if($retarget.status -ne 'DONE' -or $retarget.clips.Count -ne $selection.Count){throw ('Native retarget incomplete: '+$row.id+' '+$retarget.error)}
  $audit=Join-Path $JobRoot ('audit_'+$row.id+'.json')
  $auditArgs=@((Join-Path $PSScriptRoot 'audit_humgen_transport.py'),$row.staged)+@($retarget.clips | ForEach-Object path)+@('--report',$audit)
  & 'C:\Python314\python.exe' @auditArgs
  if($LASTEXITCODE -ne 0){throw ('Native rest/hierarchy audit failed: '+$row.id)}
  $output=Join-Path (Split-Path $JobRoot -Parent) ('runtime\humgen_characters\mocap_motion\'+$row.id+'_motion.res')
  $manifestPath=Join-Path $JobRoot ('library_'+$row.id+'.json')
  @{body=$row.staged;output=$output;clips=$retarget.clips} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
  & $guard -Executable $godot -JobArguments @('--headless','--path',(Join-Path (Split-Path $JobRoot -Parent) 'rig_gate'),'--script',($scripts+'/build_humgen_motion.gd'),'--',$manifestPath) -Log (Join-Path $JobRoot ('logs\library_'+$row.id+'.log')) -Job ('humgen-library-'+$row.id) -LimitSeconds 240 -FailIfStderrMatches 'SCRIPT ERROR|Assertion failed'
  if(-not(Test-Path -LiteralPath ($output+'.audit.json'))){throw ('Native library receipt missing: '+$row.id)}
  $state.characters+=@{id=$row.id;status='PASS';motion=$output;body_sha256=$row.sha256;clips=$retarget.clips.Count}
  Save-Progress
 }
 $state.status='DONE';$state.current='';Save-Progress
}catch{
 $state.status='FAILED';$state.error=$_.Exception.Message;Save-Progress
 throw
}
