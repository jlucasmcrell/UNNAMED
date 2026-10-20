$ErrorActionPreference='Stop'
$root='D:\Otherreach_Scratch\mh_playtest_20261019'
$guard='D:\Otherreach_Scratch\mh_slice_20261019\scripts\run_guarded.ps1'
$catalog=Get-Content -LiteralPath "$root\runtime\catalog.json" -Raw|ConvertFrom-Json
$groups=@('ada,mateo',($catalog.npcs[0..1] -join ','),($catalog.npcs[2..3] -join ','),($catalog.npcs[4..5] -join ','),($catalog.npcs[6..7] -join ','),($catalog.npcs[8..9] -join ','))
foreach($group in $groups){
 for($second=0;$second -lt 61;$second++){if(Test-Path -LiteralPath 'C:\Otherreach_Recovery\OWNER_PLAYING.flag'){throw 'Owner playing; deferred'};Start-Sleep -Seconds 1}
 if((Get-PSDrive C).Free/1GB -lt 190){throw 'C free space floor reached'}
 $env:OTHERREACH_MH_ANIM_PROBE='0';$env:OTHERREACH_MH_ANIM_CHARACTERS=$group
 $label=$group.Replace(',','_')
 & $guard -Executable 'C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe' -JobArguments @('D:\Otherreach_Scratch\mh_slice_20261019\OtherreachMHSlice\OtherreachMHSlice.uproject','-unattended','-nosplash','-nosound',"-ExecutePythonScript=$root\scripts\retarget_native_animations.py") -Log "$root\evidence\native_animation_export_$label.log" -Gpu -Job "native-animations-$label"
 $state=Get-Content -LiteralPath "$root\evidence\ue_native_animations.json" -Raw|ConvertFrom-Json
 foreach($id in $group.Split(',')){
  $row=$state.characters|Where-Object {$_.id -eq $id}
  if(-not $row.complete){Add-Content -LiteralPath 'C:\Otherreach_External_Assets\CODEX_PROGRESS.md' -Value "`r`nBLOCKED: mh-playtest native animation batch incomplete for $id. Inspect ue_native_animations.json; no library/gate accepted.";throw "Native animation batch incomplete $id"}
 }
 Add-Content -LiteralPath 'C:\Otherreach_External_Assets\CODEX_PROGRESS.md' -Value "`r`nFINISHED: mh-playtest Epic IK Retargeter batch/native342-joint GLTF animation export complete for $group. Full original Quaternius/KayKit state set; no skeleton edits or Godot retarget math. Standard bridge, native-rest contract and visual gates pending."
 Write-Output "UE_NATIVE_ANIMATION_CHARACTERS_EXPORTED $group"
}
