param([Parameter(Mandatory)][string]$ManifestRoot,[Parameter(Mandatory)][string]$StagingReceipt,
 [Parameter(Mandatory)][string]$RateReceipt,[Parameter(Mandatory)][string]$OutputRoot,[string[]]$Only=@())
$ErrorActionPreference='Stop'
$guard=Join-Path $PSScriptRoot 'run_cpu_tool.ps1'
$godot='C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64.exe'
$rates=Get-Content -LiteralPath $RateReceipt -Raw | ConvertFrom-Json -AsHashtable
if($rates.status -ne 'DONE' -or -not $rates.source_rates){throw 'Missing native source playback-rate receipt'}
$null=New-Item -ItemType Directory -Path $OutputRoot -Force
$state=[ordered]@{status='RUNNING';source_rates=$RateReceipt;characters=@();current=''}
function Save-Progress {$state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputRoot 'batch.json') -Encoding utf8}
try {
 foreach($row in @(Get-Content -LiteralPath $StagingReceipt -Raw | ConvertFrom-Json)){
  if($Only.Count -gt 0 -and $row.id -notin $Only){continue}
  if((Get-FileHash -LiteralPath $row.staged).Hash.ToLowerInvariant() -ne $row.sha256){throw ('Body changed: '+$row.id)}
  $state.current=$row.id;Save-Progress
  $manifest=Get-Content -LiteralPath (Join-Path $ManifestRoot ('library_'+$row.id+'.json')) -Raw | ConvertFrom-Json -AsHashtable
  foreach($clip in $manifest.clips){
   if(-not $rates.source_rates.ContainsKey($clip.source_asset)){throw ('No original RateScale: '+$clip.source_asset)}
   $clip.source_rate_scale=$rates.source_rates[$clip.source_asset]
  }
  # Changes confined to garments can reuse Epic tracks only after the native builder
  # proves every bone name, parent and rest against this exact new GLB.
  $manifest.body=$row.staged
  $manifest.output=Join-Path $OutputRoot ('mocap_motion\'+$row.id+'_motion.res')
  $manifestPath=Join-Path $OutputRoot ('library_'+$row.id+'.json')
  $manifest | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $manifestPath -Encoding utf8
  & $guard -Executable $godot -JobArguments @('--headless','--path','D:\Otherreach_Scratch\humgen_rcd8_20261023\rig_gate','--script',(Join-Path $PSScriptRoot 'build_humgen_motion.gd'),'--',$manifestPath) -Log (Join-Path $OutputRoot ('library_'+$row.id+'.log')) -LimitSeconds 240 -FailIfStderrMatches 'SCRIPT ERROR|Assertion failed'
  $audit=Get-Content -LiteralPath ($manifest.output+'.audit.json') -Raw | ConvertFrom-Json
  if($audit.clips.Count -ne $manifest.clips.Count){throw ('Incomplete library: '+$row.id)}
  $state.characters+=@{id=$row.id;motion=$manifest.output;body_sha256=$row.sha256;source_clips=$audit.clips.Count}
  Save-Progress
 }
 $state.status='DONE';$state.current='';Save-Progress
}catch{$state.status='FAILED';$state.error=$_.Exception.Message;Save-Progress;throw}
