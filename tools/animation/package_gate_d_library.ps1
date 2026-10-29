param([Parameter(Mandatory)][string]$Manifest,[Parameter(Mandatory)][string]$JobRoot)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'run_cpu_tool.ps1') -Executable 'C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64_console.exe' `
 -JobArguments @('--headless','--path',(Join-Path $JobRoot 'rig_gate'),'--script',(Join-Path $PSScriptRoot 'build_humgen_motion.gd'),'--',$Manifest) `
 -Log ($Manifest+'.build.log') -Job 'gate-d-native-library' -LimitSeconds 3600 -FailIfStderrMatches 'SCRIPT ERROR|Parse Error|Assertion failed'
