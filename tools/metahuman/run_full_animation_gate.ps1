param([string]$Character='ada')
if(-not [IO.Path]::IsPathRooted($env:OTHERREACH_PIPELINE_ROOT)){throw 'Set an absolute OTHERREACH_PIPELINE_ROOT'}
$jobRoot=[IO.Path]::GetFullPath($env:OTHERREACH_PIPELINE_ROOT).TrimEnd('\','/')
$frozenRoot='D:\Otherreach_Scratch\mh_playtest_20261019'
if($jobRoot -ieq $frozenRoot -or $jobRoot.StartsWith($frozenRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Frozen f87 data cannot be a job output root'}
$ErrorActionPreference='Stop'
$root=$env:OTHERREACH_PIPELINE_ROOT
$env:OTHERREACH_MH_GATE_CHARACTER=$Character
$env:OTHERREACH_MH_GATE_CLIPS=''
$env:OTHERREACH_MH_GATE_SUFFIX='_shell_final'
$env:OTHERREACH_MH_ANIM_MANIFEST='ue_native_animations_extremities.json'
$env:OTHERREACH_MH_GATE_UNCLOTHED='0'
$env:OTHERREACH_MH_GATE_REST='0'
$env:OTHERREACH_MH_BAKE_MASKS='0'
& "$PSScriptRoot\run_guarded.ps1" -Executable 'C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64_console.exe' -JobArguments @('--path',(Join-Path (Split-Path (Split-Path $PSScriptRoot)) "src\Presentation"),'--script',"$PSScriptRoot\capture_animation_gate.gd") -Log "$root\evidence\animation_shell_final_$Character.log" -Gpu -Job "shell-trousers-full-animation-$Character"
$frames=Get-Content -LiteralPath "$root\evidence\animation_gate\$($Character)_shell_final\frames.json" -Raw | ConvertFrom-Json
if(@($frames.frames).Count -ne 114){throw 'Incomplete full animation capture'}
python "$PSScriptRoot\make_review_sheets.py" "$root\evidence\animation_gate\$($Character)_shell_final"
