param([string]$Script='retarget_native_animations.py',[string]$Manifest='ue_native_animations_extremities.json',[string]$LabelPrefix='native')
if(-not [IO.Path]::IsPathRooted($env:OTHERREACH_PIPELINE_ROOT)){throw 'Set an absolute OTHERREACH_PIPELINE_ROOT'}
$jobRoot=[IO.Path]::GetFullPath($env:OTHERREACH_PIPELINE_ROOT).TrimEnd('\','/')
$frozenRoot='D:\Otherreach_Scratch\mh_playtest_20261019'
if($jobRoot -ieq $frozenRoot -or $jobRoot.StartsWith($frozenRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Frozen f87 data cannot be a job output root'}
$ErrorActionPreference='Stop'
$root=$env:OTHERREACH_PIPELINE_ROOT
$guard="$PSScriptRoot\run_guarded.ps1"
$catalog=Get-Content -LiteralPath "$root\runtime\catalog.json" -Raw|ConvertFrom-Json
$ids=@($catalog.characters.PSObject.Properties.Name)
$groups=for($i=0;$i -lt $ids.Count;$i+=2){($ids[$i..([math]::Min($i+1,$ids.Count-1))] -join ',')}
foreach($group in $groups){
 $prior=if(Test-Path -LiteralPath "$root\evidence\$Manifest"){Get-Content -LiteralPath "$root\evidence\$Manifest" -Raw|ConvertFrom-Json}else{[pscustomobject]@{characters=@()}}
 if(@($group.Split(',')|Where-Object {$id=$_;-not ($prior.characters|Where-Object {$_.id -eq $id -and $_.complete -and @($_.clips).Count -ge 26})}).Count -eq 0){Write-Output "UE_NATIVE_ANIMATION_ALREADY_EXPORTED $group";continue}
 if((Get-PSDrive C).Free/1GB -lt 190){throw 'C free space floor reached'}
 $env:OTHERREACH_MH_ANIM_PROBE='0';$env:OTHERREACH_MH_ANIM_CHARACTERS=$group
 $label=$group.Replace(',','_')
 & $guard -Executable 'C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe' -JobArguments @('D:\Otherreach_Scratch\mh_slice_20261019\OtherreachMHSlice\OtherreachMHSlice.uproject','-NullRHI','-unattended','-nosplash','-run=pythonscript',"-script=$PSScriptRoot\$Script") -Log "$root\evidence\$($LabelPrefix)_animation_export_$label.log" -Job "$LabelPrefix-animations-$label"
 $state=Get-Content -LiteralPath "$root\evidence\$Manifest" -Raw|ConvertFrom-Json
 foreach($id in $group.Split(',')){
  $row=$state.characters|Where-Object {$_.id -eq $id}
  if(-not $row.complete){Add-Content -LiteralPath 'C:\Otherreach_External_Assets\CODEX_PROGRESS.md' -Value "`r`nBLOCKED: mh-playtest native animation batch incomplete for $id. Inspect ue_native_animations_extremities.json; no library/gate accepted.";throw "Native animation batch incomplete $id"}
 }
 Add-Content -LiteralPath 'C:\Otherreach_External_Assets\CODEX_PROGRESS.md' -Value "`r`nFINISHED: mh-playtest Epic IK Retargeter batch/native342-joint GLTF animation export complete for $group. Full original Quaternius/KayKit state set; no skeleton edits or Godot retarget math. Standard bridge, native-rest contract and visual gates pending."
 Write-Output "UE_NATIVE_ANIMATION_CHARACTERS_EXPORTED $group"
}
