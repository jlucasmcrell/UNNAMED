$ErrorActionPreference='Stop'
$root='D:\Otherreach_Scratch\mh_playtest_20261019'
$guard='D:\Otherreach_Scratch\mh_slice_20261019\scripts\run_guarded.ps1'
$godot='C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64_console.exe'
$deadline=(Get-Date).AddMinutes(8)
while($true){
 if(Test-Path -LiteralPath 'C:\Otherreach_Recovery\OWNER_PLAYING.flag'){throw 'Owner playing; sample deferred'}
 $state=Get-Content -LiteralPath "$root\evidence\ue_native_animations.json" -Raw|ConvertFrom-Json
 if($state.status -eq 'DONE' -and @($state.characters[0].clips).Count -ge 3){break}
 if((Get-Date) -gt $deadline){throw 'Native UE sample not ready; inspect checkpoint'}
 Start-Sleep -Seconds 5
}
& $guard -Executable 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -JobArguments @('--background','--factory-startup','--threads','4','--python-exit-code','2','--python',"$root\scripts\clean_native_animations.py",'--','ada') -Log "$root\evidence\clean_animations_ada.log" -Job 'native-animation-Ada-bridge'
if((Get-Content -LiteralPath "$root\evidence\clean_animations_ada.json" -Raw|ConvertFrom-Json).status -ne 'DONE'){throw 'Native animation bridge incomplete'}
$env:OTHERREACH_MH_ANIM_CHARACTERS='ada'
& $guard -Executable $godot -JobArguments @('--headless','--path','C:\Otherreach_Build\playtest_mh_characters_wt\src\Presentation','--script',"$root\scripts\build_native_animation_library.gd") -Log "$root\evidence\native_animation_library_sample.log" -Job 'native-animation-Ada-library'
if(-not (Select-String -LiteralPath "$root\evidence\native_animation_library_sample.log" -SimpleMatch 'MH_NATIVE_ANIMATION_LIBRARIES_DONE characters=1')){throw 'Native animation rest contract/library failed'}
# Always allow the full own-lane cooldown before the real-renderer visual sample.
for($second=0;$second -lt 61;$second++){if(Test-Path -LiteralPath 'C:\Otherreach_Recovery\OWNER_PLAYING.flag'){throw 'Owner playing; deferred'};Start-Sleep -Seconds 1}
$env:OTHERREACH_MH_GATE_CHARACTER='ada';$env:OTHERREACH_MH_GATE_CLIPS='idle,walk,sword_attack_1'
$env:OTHERREACH_MH_BAKE_MASKS='0'
& $guard -Executable $godot -JobArguments @('--path','C:\Otherreach_Build\playtest_mh_characters_wt\src\Presentation','--script',"$root\scripts\capture_animation_gate.gd") -Log "$root\evidence\native_animation_sample_gate.log" -Gpu -Job 'native-animation-Ada-visual-sample'
if(-not (Test-Path -LiteralPath "$root\evidence\animation_gate\ada\frames.json")){throw 'Native animation visual capture incomplete'}
Write-Output 'NATIVE_ANIMATION_SAMPLE_CAPTURED_REQUIRES_REVIEW'
