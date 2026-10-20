param([string]$Manifest='ue_native_animations_rootfix.json',[string]$StampPrefix='animation_rootfix_staged_')
$ErrorActionPreference='Stop'
$root='D:\Otherreach_Scratch\mh_playtest_20261019'
$guard='D:\Otherreach_Scratch\mh_slice_20261019\scripts\run_guarded.ps1'
$godot='C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64_console.exe'
$deadline=(Get-Date).AddHours(2)
while($true){
 if(Test-Path -LiteralPath 'C:\Otherreach_Recovery\OWNER_PLAYING.flag'){throw 'Owner playing; staging deferred'}
 try{$state=Get-Content -LiteralPath "$root\evidence\$Manifest" -Raw|ConvertFrom-Json}catch{Start-Sleep -Seconds 5;continue}
 $count=0
 foreach($row in $state.characters){
  $id=$row.id;$checkpoint="$root\evidence\$StampPrefix$id.json"
  if(Test-Path -LiteralPath $checkpoint){$count++;continue}
  if(-not $row.complete -or @($row.clips).Count -lt 26){continue}
  $env:OTHERREACH_MH_ANIM_MANIFEST=$Manifest
  & $guard -Executable 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -JobArguments @('--background','--factory-startup','--threads','4','--python-exit-code','2','--python',"$root\scripts\clean_native_animations.py",'--',$id) -Log "$root\evidence\$($StampPrefix)clean_$id.log" -Job "native-animation-$id-bridge"
  $clean=Get-Content -LiteralPath "$root\evidence\clean_animations_$id.json" -Raw|ConvertFrom-Json
  if($clean.status -ne 'DONE' -or @($clean.clips).Count -lt 26){throw "Native animation bridge incomplete $id"}
  $env:OTHERREACH_MH_ANIM_CHARACTERS=$id
  & $guard -Executable $godot -JobArguments @('--headless','--path','C:\Otherreach_Build\playtest_mh_characters_wt\src\Presentation','--script',"$root\scripts\build_native_animation_library.gd") -Log "$root\evidence\native_animation_library_$id.log" -Job "native-animation-$id-library"
  if(-not (Select-String -LiteralPath "$root\evidence\native_animation_library_$id.log" -SimpleMatch 'MH_NATIVE_ANIMATION_LIBRARIES_DONE characters=1')){throw "Native rest contract/library failed $id"}
  Set-Content -LiteralPath $checkpoint -Value ('{"status":"STAGED","id":"'+$id+'","native_rest_unchanged":true,"logical_clips":26}')
  Add-Content -LiteralPath 'C:\Otherreach_External_Assets\CODEX_PROGRESS.md' -Value "`r`nFINISHED: mh-playtest $id native AnimationLibrary staged (26 original clips plus state aliases), exact native342 bone names/parents/rest contract PASS, own body hash pinned, gait pace measured from planted native feet. No Godot retarget. Full visual/packaged-game proof still pending."
  Write-Output "NATIVE_ANIMATION_STAGED $id";$count++
 }
 if($count -eq 12){break}
 if((Get-Date) -gt $deadline){throw 'Native animation roster staging deadline reached'}
 Start-Sleep -Seconds 5
}
