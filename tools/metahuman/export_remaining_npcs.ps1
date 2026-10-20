param([int]$StartIndex=3,[int]$EndIndex=9)
if(-not [IO.Path]::IsPathRooted($env:OTHERREACH_PIPELINE_ROOT)){throw 'Set an absolute OTHERREACH_PIPELINE_ROOT'}
$jobRoot=[IO.Path]::GetFullPath($env:OTHERREACH_PIPELINE_ROOT).TrimEnd('\','/')
$frozenRoot='D:\Otherreach_Scratch\mh_playtest_20261019'
if($jobRoot -ieq $frozenRoot -or $jobRoot.StartsWith($frozenRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Frozen f87 data cannot be a job output root'}
$ErrorActionPreference='Stop'
$root=$env:OTHERREACH_PIPELINE_ROOT
$guard="$PSScriptRoot\run_guarded.ps1"
$progress='C:\Otherreach_External_Assets\CODEX_PROGRESS.md'
$prep=Get-Content -LiteralPath "$root\evidence\npc_characters.json" -Raw | ConvertFrom-Json
for($index=$StartIndex;$index -le $EndIndex;$index++){
 $id=$prep.characters[$index].id
 if(Test-Path -LiteralPath 'C:\Otherreach_Recovery\OWNER_PLAYING.flag'){throw 'Owner playing; deferred'}
 foreach($drive in @('C','D')){ $free=(Get-PSDrive $drive).Free/1GB; if($drive -eq 'C' -and $free -lt 190){throw 'C free space below190GiB'}; Write-Output "$drive free GiB $([math]::Round($free,1))" }
 $env:OTHERREACH_MH_NPC_START=[string]$index
 $env:OTHERREACH_MH_NPC_COUNT='1'
 $env:OTHERREACH_MH_NPC_ASSEMBLE='1'
 for($attempt=1;$attempt -le 3;$attempt++){
  try {
 & $guard -Executable 'C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe' -JobArguments @('D:\Otherreach_Scratch\mh_slice_20261019\OtherreachMHSlice\OtherreachMHSlice.uproject','-unattended','-nosplash','-nosound',"-ExecutePythonScript=$PSScriptRoot\cloud_export_npcs.py") -Log "$root\evidence\npc_$($id)_guard.log" -Gpu -Job "npc-$id"
   break
  }catch{
   if($_.ToString() -notmatch 'gpu.queue' -or $attempt -eq 3){throw}
   Add-Content -LiteralPath $progress -Value "`r`nBLOCKED transient: mh-playtest GPU queue file sharing error before $id acquire; retrying the documented lock after5s. No UE job launched and no foreign lease touched."
   Start-Sleep -Seconds 5
  }
 }
 foreach($stage in @('npc_cloud_export','npc_assembly','npc_glb_export','npc_material_groom_export')){
  $state=Get-Content -LiteralPath "$root\evidence\$stage.json" -Raw | ConvertFrom-Json
  if(-not ($state.characters | Where-Object {$_.id -eq $id})){Add-Content -LiteralPath $progress -Value "`r`nBLOCKED: mh-playtest $id native stage $stage incomplete; inspect $root\evidence\$stage.json. No gate claimed."; throw "$id $stage incomplete"}
 }
 Add-Content -LiteralPath $progress -Value "`r`nFINISHED: mh-playtest $id native Full Rig/4K/Geometry/DCC/Materials, Optimized High assembly, native lower-LOD GLTF candidates, groom cards and eye materials exported. Native skeleton unchanged. Visual/packaged-game gates pending. GPU lease released;60s cooldown."
 Write-Output "NATIVE_NPC_COMPLETE $id"
 # Each character yields its lease. Owner cancellation remains responsive during cooldown.
 for($second=0;$second -lt 61;$second++){if(Test-Path -LiteralPath 'C:\Otherreach_Recovery\OWNER_PLAYING.flag'){throw 'Owner playing; deferred'};Start-Sleep -Seconds 1}
}
