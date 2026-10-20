param([string]$Character='ada')
$ErrorActionPreference='Stop'
$root='D:\Otherreach_Scratch\mh_playtest_20261019'
$env:OTHERREACH_MH_GATE_CHARACTER=$Character
$env:OTHERREACH_MH_GATE_CLIPS=''
$env:OTHERREACH_MH_GATE_SUFFIX='_shell_final'
$env:OTHERREACH_MH_ANIM_MANIFEST='ue_native_animations_rootfix.json'
$env:OTHERREACH_MH_GATE_UNCLOTHED='0'
$env:OTHERREACH_MH_GATE_REST='0'
$env:OTHERREACH_MH_BAKE_MASKS='0'
& 'D:\Otherreach_Scratch\mh_slice_20261019\scripts\run_guarded.ps1' -Executable 'C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64_console.exe' -JobArguments @('--path','C:\Otherreach_Build\playtest_mh_characters_wt\src\Presentation','--script',"$root\scripts\capture_animation_gate.gd") -Log "$root\evidence\animation_shell_final_$Character.log" -Gpu -Job "shell-trousers-full-animation-$Character"
$frames=Get-Content -LiteralPath "$root\evidence\animation_gate\$($Character)_shell_final\frames.json" -Raw | ConvertFrom-Json
if(@($frames.frames).Count -ne 114){throw 'Incomplete full animation capture'}
python "$root\scripts\make_review_sheets.py" "$root\evidence\animation_gate\$($Character)_shell_final"
