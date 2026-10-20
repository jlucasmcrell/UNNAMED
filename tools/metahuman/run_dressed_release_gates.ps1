if(-not [IO.Path]::IsPathRooted($env:OTHERREACH_PIPELINE_ROOT)){throw 'Set an absolute OTHERREACH_PIPELINE_ROOT'}
$jobRoot=[IO.Path]::GetFullPath($env:OTHERREACH_PIPELINE_ROOT).TrimEnd('\','/')
$frozenRoot='D:\Otherreach_Scratch\mh_playtest_20261019'
if($jobRoot -ieq $frozenRoot -or $jobRoot.StartsWith($frozenRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Frozen f87 data cannot be a job output root'}
$ErrorActionPreference='Stop'
$root=$env:OTHERREACH_PIPELINE_ROOT
$godot='C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64_console.exe'
$project=(Join-Path (Split-Path (Split-Path $PSScriptRoot)) "src\Presentation")
$env:OTHERREACH_MH_CHARACTERS_ROOT="$root\runtime"
$env:OTHERREACH_MH_BAKE_MASKS='0'
function Render([string]$Script,[string]$Log,[switch]$Headless){
 $jobArgs=@('--path',$project,'--script',$Script)
 if($Headless){$jobArgs=@('--headless')+$jobArgs}
 $process=Start-Process -FilePath $godot -ArgumentList $jobArgs -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $Log -RedirectStandardError ($Log+'.stderr')
 if($process.ExitCode -ne 0){throw "Godot gate exit $($process.ExitCode): $Script"}
}
if(-not (Test-Path -LiteralPath "$root\evidence\devonlux_final_masks.log") -or -not (Select-String -LiteralPath "$root\evidence\devonlux_final_masks.log" -SimpleMatch "MH_POSED_MASKS_DONE characters=12")){Render "$PSScriptRoot\bake_posed_hidden_maps.gd" "$root\evidence\devonlux_final_masks.log"}
python "$PSScriptRoot\raster_hidden_face_maps.py"
if($LASTEXITCODE -ne 0){throw 'Mask raster failed'}
Render "$PSScriptRoot\native_wrist_uv.gd" "$root\evidence\native_wrist_uv.log" -Headless
Render "$PSScriptRoot\native_covered_legs_uv.gd" "$root\evidence\native_covered_legs_uv.log" -Headless
python "$PSScriptRoot\raster_native_hands_masks.py"
if($LASTEXITCODE -ne 0){throw 'Native hand/covered-leg mask raster failed'}
Render "$project\MHCharacters\Tests\verify_native_character.gd" "$root\evidence\devonlux_final_native_verify.log" -Headless
foreach($id in @('ada','mateo')){
 $env:OTHERREACH_MH_GATE_CHARACTER=$id
 $env:OTHERREACH_MH_GATE_CLIPS=''
 $env:OTHERREACH_MH_GATE_SUFFIX='_devonlux_final'
 $env:OTHERREACH_MH_ANIM_MANIFEST='ue_native_animations_extremities.json'
 $env:OTHERREACH_MH_GATE_UNCLOTHED='0'
 $env:OTHERREACH_MH_GATE_REST='0'
 Render "$PSScriptRoot\capture_animation_gate.gd" "$root\evidence\animation_devonlux_final_$id.log"
 $folder="$root\evidence\animation_gate\$($id)_devonlux_final"
 $frames=Get-Content -LiteralPath "$folder\frames.json" -Raw|ConvertFrom-Json
 if(@($frames.frames).Count -ne 114){throw 'Incomplete dressed capture'}
 python "$PSScriptRoot\make_review_sheets.py" $folder
 if($LASTEXITCODE -ne 0){throw 'Sheet indexing failed'}
}
Write-Output 'DRESSED_RELEASE_GATES_CAPTURED_228_FRAMES_REQUIRES_REVIEW'
