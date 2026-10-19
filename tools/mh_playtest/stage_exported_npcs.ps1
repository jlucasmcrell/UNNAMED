$ErrorActionPreference='Stop'
$root='D:\Otherreach_Scratch\mh_playtest_20261019'
$guard='D:\Otherreach_Scratch\mh_slice_20261019\scripts\run_guarded.ps1'
$progress='C:\Otherreach_External_Assets\CODEX_PROGRESS.md'
$blender='C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
$godot='C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64_console.exe'
$prep=(Get-Content -LiteralPath "$root\evidence\npc_characters.json" -Raw|ConvertFrom-Json).characters
while($true){
 if(Test-Path -LiteralPath 'C:\Otherreach_Recovery\OWNER_PLAYING.flag'){throw 'Owner playing; CPU staging deferred'}
 try{$finished=(Get-Content -LiteralPath "$root\evidence\npc_material_groom_export.json" -Raw|ConvertFrom-Json).characters.id}catch{Start-Sleep -Seconds 5;continue}
 $count=0;$changed=$false
 foreach($row in $prep){
  $id=$row.id;$checkpoint="$root\evidence\staged_$id.json"
  if(Test-Path -LiteralPath $checkpoint){$count++;continue}
  if($id -notin $finished){continue}
  foreach($job in @(@('clean_npc_gltf.py','clean'),@('build_npc_shells.py','shells'))){
   & $guard -Executable $blender -JobArguments @('--background','--factory-startup','--threads','4','--python-exit-code','2','--python',"$root\scripts\$($job[0])",'--',$id) -Log "$root\evidence\$($job[1])_$id.log" -Job "stage-$id-$($job[1])"
  }
  $clean=Get-Content -LiteralPath "$root\evidence\clean_$id.json" -Raw|ConvertFrom-Json
  if($clean.status -ne 'DONE'){throw "Native clean incomplete $id"}
  $shells=Get-Content -LiteralPath "$root\evidence\shells_$id.json" -Raw|ConvertFrom-Json
  if(@($shells).Count -ne 2){throw "Native shells incomplete $id"}
  python "$root\scripts\stage_npcs.py"
  if($LASTEXITCODE -ne 0){throw "Catalog staging failed $id"}
  Set-Content -LiteralPath $checkpoint -Value ('{"status":"STAGED","id":"'+$id+'","body_untouched":true}')
  Add-Content -LiteralPath $progress -Value "`r`nFINISHED: mh-playtest $id CPU stage: native LOD body/head with reference material index table; own-body leather boots/gloves with all influences; native DCC maps/grooms/eyes staged. Body/skeleton untouched. Mask and live game gates pending."
  Write-Output "CPU_STAGED_NATIVE_NPC $id";$count++;$changed=$true
 }
 if($changed){
  $env:OTHERREACH_MH_CHARACTERS_ROOT="$root\runtime";$env:OTHERREACH_MH_BAKE_MASKS='1'
  & $guard -Executable $godot -JobArguments @('--headless','--path','C:\Otherreach_Build\playtest_mh_characters_wt\src\Presentation','--script','res://MHCharacters/Tests/verify_native_character.gd','--quit-after','600') -Log "$root\evidence\roster_bake.log" -Job 'roster-mask-bake'
  $env:OTHERREACH_MH_BAKE_MASKS='0'
  python "$root\scripts\raster_hidden_face_maps.py"
  & $guard -Executable $godot -JobArguments @('--headless','--path','C:\Otherreach_Build\playtest_mh_characters_wt\src\Presentation','--script','res://MHCharacters/Tests/verify_native_character.gd','--quit-after','600') -Log "$root\evidence\roster_verify.log" -Job 'roster-verify'
  if(-not (Select-String -LiteralPath "$root\evidence\roster_verify.log" -SimpleMatch 'MH_NATIVE_CHARACTER_CHECK failures=0 []')){throw 'Native roster regression failed'}
  Write-Output "NATIVE_ROSTER_REGRESSION_PASS staged=$count"
 }
 if($count -eq $prep.Count){break}
 Start-Sleep -Seconds 5
}
